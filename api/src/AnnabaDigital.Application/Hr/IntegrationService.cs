using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ApiKeyView(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, IReadOnlyList<string> AllowedIps, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, bool Active);
public sealed record CreateApiKeyRequest(string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? AllowedIps, int? ExpiresInDays);
public sealed record CreatedApiKey(ApiKeyView Key, string Secret);
public sealed record BankLayoutView(Guid Id, string Name, string Delimiter, bool Header, IReadOnlyList<string> Columns, int AmountDecimals, string DateFormat, string? SourceAccount, string? Note);
public sealed record BankLayoutRequest(string? Name, string? Delimiter, bool? Header, IReadOnlyList<string>? Columns, int? AmountDecimals, string? DateFormat, string? SourceAccount, string? Note);
public sealed record JournalAccountView(string Line, string Label, string Code, string Name);
public sealed record JournalAccountRequest(string? Line, string? Code, string? Name);
public sealed record BankTransfer(string EmployeeNo, string Name, string? Bank, string AccountNo, decimal Amount, string Reference, string? Email);
public sealed record ApiCaller(Guid KeyId, string Name, IReadOnlySet<string> Scopes);
public sealed record PublicEmployee(Guid Id, string EmployeeNo, string Name, string? WorkEmail, string Status, DateOnly HireDate, DateOnly? TerminationDate, string? JobTitle, Guid? OrgUnitId, string? Department, Guid? ManagerId, string? EmploymentType, DateTimeOffset UpdatedAt);
public sealed record PublicLeave(Guid Id, Guid EmployeeId, string EmployeeNo, string LeaveType, DateOnly StartDate, DateOnly EndDate, decimal Days, string Status);
public sealed record PublicAttendance(Guid Id, Guid EmployeeId, string EmployeeNo, DateOnly Date, DateTimeOffset ClockIn, DateTimeOffset? ClockOut, int BreakMinutes, string WorkMode);
public sealed record PublicPage<T>(IReadOnlyList<T> Data, int Page, int PageSize, int Total);

/// <summary>
/// Stage 20, integrations: read-only API keys for other systems (scoped, optionally IP-restricted, expiring, audited), bank transfer file layouts for each bank's bulk upload,
/// and the company's chart-of-account codes for the payroll journal (generic and Xero formats).
/// </summary>
public sealed class IntegrationService(IAppDb db, IClock clock, AuditWriter audit, ITokenService tokens, IRateLimiter limiter, ITenantContext tenant, SettingsProvider settings)
{
    /// <summary>What a key may read. Never payroll: pay and tax data leave the system only as files a person downloads.</summary>
    public static readonly IReadOnlyDictionary<string, string> Scopes = new Dictionary<string, string>
    {
        ["employees.read"] = "Employees and departments (no salary, ID numbers or bank data)",
        ["leave.read_all"] = "Leave requests",
        ["attendance.read_all"] = "Attendance (clock-in and clock-out times)",
    };
    public static readonly IReadOnlyDictionary<string, string> BankTokens = new Dictionary<string, string>
    {
        ["account_no"] = "Account number", ["name"] = "Account holder", ["amount"] = "Amount", ["bank"] = "Bank name", ["employee_no"] = "Employee number", ["reference"] = "Reference / remark",
        ["email"] = "Email", ["currency"] = "Currency (IDR)", ["date"] = "Transfer date", ["source_account"] = "Company account", ["transfer_type"] = "Transfer type (BCA = in-house, other = online)", ["blank"] = "Empty column",
    };
    public static readonly IReadOnlyList<(string Line, string Label, string Code, string Name)> JournalLines =
    [
        ("salaries_expense", "Salaries and wages expense", "6-1001", "Salaries and wages expense"), ("employer_bpjs_expense", "Employer BPJS expense", "6-1002", "Employer BPJS expense"),
        ("tax_borne_expense", "Income tax borne by the company", "6-1003", "Income tax borne by the company"), ("salaries_payable", "Salaries payable", "2-1001", "Salaries payable"),
        ("tax_payable", "PPh 21/26 payable", "2-1002", "PPh 21/26 payable"), ("bpjs_payable", "BPJS payable", "2-1003", "BPJS payable"), ("other_deductions_payable", "Other payroll deductions payable", "2-1004", "Other payroll deductions payable"),
    ];
    private static string? Clip(string? s, int max) => s?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, max)] : null;

    // ============================================================ API keys
    private static ApiKeyView View(ApiKey k, DateTimeOffset now) => new(k.Id, k.Name, k.Prefix, k.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries), (k.AllowedIps ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
        k.CreatedAt, k.ExpiresAt, k.LastUsedAt, k.RevokedAt is null && (k.ExpiresAt is null || k.ExpiresAt > now));

    public async Task<IReadOnlyList<ApiKeyView>> KeysAsync(CancellationToken ct = default)
        => [.. (await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).ToListAsync(ct)).Select(k => View(k, clock.UtcNow))];

    public async Task<CreatedApiKey> CreateKeyAsync(CreateApiKeyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, (await settings.GetAsync(ct)) with { RequireReauthForCritical = true }, clock);
        var errors = new Dictionary<string, string>();
        var name = Clip(r.Name, 100); if (name is null || name.Length < 2) errors["name"] = "Name the system that uses the key.";
        var scopes = (r.Scopes ?? []).Distinct().ToList(); if (scopes.Count == 0 || scopes.Any(s => !Scopes.ContainsKey(s))) errors["scopes"] = "Choose what the key may read.";
        var ips = (r.AllowedIps ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        if (ips.Count > 20 || ips.Any(ip => !IPAddress.TryParse(ip, out _))) errors["allowedIps"] = "Give up to 20 IP addresses, or leave empty.";
        var days = r.ExpiresInDays; if (days is < 1 or > 730) errors["expiresInDays"] = "1–730 days, or never.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the key.", errors);
        if (await db.ApiKeys.CountAsync(k => k.RevokedAt == null, ct) >= 10) throw DomainException.Invariant("At most 10 active API keys. Revoke one first.");
        var secret = "ak_" + tokens.NewToken();
        var k = new ApiKey { Name = name!, Prefix = secret[..10], KeyHash = tokens.Hash(secret), Scopes = string.Join(',', scopes), AllowedIps = ips.Count == 0 ? null : string.Join(',', ips), CreatedBy = actor.UserId, CreatedAt = clock.UtcNow, ExpiresAt = days is { } d ? clock.UtcNow.AddDays(d) : null };
        db.ApiKeys.Add(k);
        audit.Record("integration.api_key.create", "api_key", k.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = k.Name, ["scopes"] = k.Scopes });
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(View(k, clock.UtcNow), secret);
    }

    public async Task RevokeKeyAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var k = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("API key");
        if (k.RevokedAt is null) { k.RevokedAt = clock.UtcNow; audit.Record("integration.api_key.revoke", "api_key", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = k.Name }); await db.SaveChangesAsync(ct); }
    }

    /// <summary>Checks the bearer key, its expiry and IP list, rate-limits it (600 calls per 10 minutes), and switches the request into the key's organisation.</summary>
    public async Task<ApiCaller?> AuthenticateAsync(string? authorization, ClientInfo client, CancellationToken ct = default)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ak_", StringComparison.Ordinal)) return null;
        var presented = authorization[7..].Trim(); if (presented.Length is < 20 or > 200) return null;
        var hash = tokens.Hash(presented);
        var k = await db.ApiKeys.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.KeyHash == hash && x.RevokedAt == null, ct);
        if (k is null || k.ExpiresAt is { } exp && exp <= clock.UtcNow) return null;
        if (k.AllowedIps is { Length: > 0 } list && !list.Split(',').Contains(client.Ip ?? "")) return null;
        if (!await db.Tenants.AsNoTracking().AnyAsync(x => x.Id == k.TenantId && x.Status == TenantStatus.Active, ct)) return null;
        var hit = await limiter.HitAsync($"apikey:{k.Id}", 600, TimeSpan.FromMinutes(10), ct); if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        tenant.Use(k.TenantId, false);
        if (k.LastUsedAt is null || clock.UtcNow - k.LastUsedAt > TimeSpan.FromMinutes(1)) { k.LastUsedAt = clock.UtcNow; await db.SaveChangesAsync(ct); }
        return new ApiCaller(k.Id, k.Name, k.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet());
    }

    private void Need(ApiCaller caller, string scope, string path, ClientInfo client)
    {
        if (!caller.Scopes.Contains(scope)) throw DomainException.DeniedBecause($"This key may not read this data (needs {scope}).");
        audit.Record("integration.api.read", "api_key", caller.KeyId.ToString(), Outcome.Success, client, null, caller.Name, new Dictionary<string, string> { ["path"] = path });
    }
    private static (int Page, int Size) Paging(int? page, int? size) => (Math.Max(1, page ?? 1), Math.Clamp(size ?? 100, 1, 500));

    public async Task<PublicPage<PublicEmployee>> EmployeesAsync(ApiCaller caller, DateTimeOffset? updatedSince, int? page, int? pageSize, ClientInfo client, CancellationToken ct = default)
    {
        Need(caller, "employees.read", "/employees", client); var (p, size) = Paging(page, pageSize);
        var q = db.Employees.AsNoTracking().Where(e => e.AnonymizedAt == null); if (updatedSince is { } u) { var since = u.ToUniversalTime(); q = q.Where(e => e.UpdatedAt >= since); }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(e => e.EmployeeNo).Skip((p - 1) * size).Take(size).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.WorkEmail, e.Status, e.HireDate, e.TerminationDate, e.UpdatedAt,
            Job = e.Employments.OrderByDescending(m => m.EffectiveTo == null).ThenByDescending(m => m.EffectiveFrom).Select(m => new { m.JobTitle, m.OrgUnitId, m.ManagerId, m.Type }).FirstOrDefault() }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        await db.SaveChangesAsync(ct);
        return new PublicPage<PublicEmployee>([.. rows.Select(e => new PublicEmployee(e.Id, e.EmployeeNo, e.FullName, e.WorkEmail, e.Status.ToString().ToLowerInvariant(), e.HireDate, e.TerminationDate, e.Job?.JobTitle, e.Job?.OrgUnitId,
            e.Job?.OrgUnitId is { } o ? units.GetValueOrDefault(o) : null, e.Job?.ManagerId, e.Job?.Type.ToString().ToLowerInvariant(), e.UpdatedAt))], p, size, total);
    }

    public async Task<IReadOnlyList<object>> OrgUnitsAsync(ApiCaller caller, ClientInfo client, CancellationToken ct = default)
    {
        Need(caller, "employees.read", "/org-units", client); await db.SaveChangesAsync(ct);
        return [.. (await db.OrgUnits.AsNoTracking().OrderBy(o => o.Code).ToListAsync(ct)).Select(o => (object)new { o.Id, o.Code, o.Name, o.ParentId, Archived = o.ArchivedAt != null })];
    }

    public async Task<PublicPage<PublicLeave>> LeaveAsync(ApiCaller caller, DateOnly? from, DateOnly? to, int? page, int? pageSize, ClientInfo client, CancellationToken ct = default)
    {
        Need(caller, "leave.read_all", "/leave-requests", client); var (p, size) = Paging(page, pageSize);
        var f = from ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddMonths(-1); var t = to ?? f.AddMonths(3);
        var q = from l in db.LeaveRequests.AsNoTracking() join e in db.Employees.AsNoTracking() on l.EmployeeId equals e.Id join ty in db.LeaveTypes.AsNoTracking() on l.LeaveTypeId equals ty.Id
                where l.EndDate >= f && l.StartDate <= t select new { l, e.EmployeeNo, ty.Name };
        var total = await q.CountAsync(ct); var rows = await q.OrderBy(x => x.l.StartDate).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        await db.SaveChangesAsync(ct);
        return new PublicPage<PublicLeave>([.. rows.Select(x => new PublicLeave(x.l.Id, x.l.EmployeeId, x.EmployeeNo, x.Name, x.l.StartDate, x.l.EndDate, x.l.Days, x.l.Status.ToString().ToLowerInvariant()))], p, size, total);
    }

    public async Task<PublicPage<PublicAttendance>> AttendanceAsync(ApiCaller caller, DateOnly? from, DateOnly? to, int? page, int? pageSize, ClientInfo client, CancellationToken ct = default)
    {
        Need(caller, "attendance.read_all", "/attendance", client); var (p, size) = Paging(page, pageSize);
        var t = to ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7)); var f = from ?? t.AddDays(-7);
        if (t.DayNumber - f.DayNumber > 92) throw DomainException.Validation("Ask for at most 92 days at a time.", new Dictionary<string, string> { ["from"] = "At most 92 days." });
        var q = from a in db.AttendanceLogs.AsNoTracking() join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id where a.Date >= f && a.Date <= t select new { a, e.EmployeeNo };
        var total = await q.CountAsync(ct); var rows = await q.OrderBy(x => x.a.Date).ThenBy(x => x.EmployeeNo).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        await db.SaveChangesAsync(ct);
        return new PublicPage<PublicAttendance>([.. rows.Select(x => new PublicAttendance(x.a.Id, x.a.EmployeeId, x.EmployeeNo, x.a.Date, x.a.ClockIn, x.a.ClockOut, x.a.BreakMinutes, x.a.WorkMode.ToString().ToLowerInvariant()))], p, size, total);
    }

    // ============================================================ bank file layouts
    private static BankLayoutView LayoutView(BankFileLayout l) => new(l.Id, l.Name, l.Delimiter, l.Header, l.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries), l.AmountDecimals, l.DateFormat, l.SourceAccount, l.Note);
    public async Task<IReadOnlyList<BankLayoutView>> LayoutsAsync(CancellationToken ct = default) => [.. (await db.BankFileLayouts.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct)).Select(LayoutView)];

    public async Task<BankLayoutView> SaveLayoutAsync(Guid? id, BankLayoutRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = id is { } lid ? await db.BankFileLayouts.FirstOrDefaultAsync(x => x.Id == lid, ct) ?? throw DomainException.NotFound("Layout") : new BankFileLayout();
        var errors = new Dictionary<string, string>();
        var name = Clip(r.Name, 100) ?? l.Name; if (name.Length < 2) errors["name"] = "Name the layout, e.g. the bank.";
        var delim = r.Delimiter ?? l.Delimiter; if (delim is not ("," or ";" or "|" or "\t")) errors["delimiter"] = "Use comma, semicolon, pipe or tab.";
        var cols = r.Columns?.Select(c => c.Trim()).ToList() ?? [.. l.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries)];
        if (cols.Count is < 2 or > 20 || cols.Any(c => !BankTokens.ContainsKey(c)) || !cols.Contains("account_no") || !cols.Contains("amount")) errors["columns"] = "Choose the columns in order; the account number and amount are required.";
        var dec = r.AmountDecimals ?? l.AmountDecimals; if (dec is not (0 or 2)) errors["amountDecimals"] = "0 or 2 decimals.";
        var df = Clip(r.DateFormat, 20) ?? l.DateFormat; try { _ = new DateOnly(2027, 1, 31).ToString(df, CultureInfo.InvariantCulture); } catch (FormatException) { errors["dateFormat"] = "Use a date pattern such as yyyyMMdd or dd/MM/yyyy."; }
        var src = r.SourceAccount is null ? l.SourceAccount : Clip(r.SourceAccount, 40); if (src is not null && !src.All(c => char.IsDigit(c) || c == '-')) errors["sourceAccount"] = "Digits only.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the layout.", errors);
        l.Name = name; l.Delimiter = delim; l.Header = r.Header ?? l.Header; l.Columns = string.Join(',', cols); l.AmountDecimals = dec; l.DateFormat = df; l.SourceAccount = src; l.Note = r.Note is null ? l.Note : Clip(r.Note, 500); l.UpdatedAt = clock.UtcNow;
        if (id is null) db.BankFileLayouts.Add(l);
        audit.Record(id is null ? "hr.bank_layout.create" : "hr.bank_layout.update", "bank_layout", l.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return LayoutView(l);
    }

    public async Task DeleteLayoutAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.BankFileLayouts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Layout");
        db.BankFileLayouts.Remove(l); audit.Record("hr.bank_layout.delete", "bank_layout", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }

    /// <summary>Writes the transfers in the layout. Amounts with a dot as decimal separator; text values are quoted only when they contain the separator, a quote or a line break.</summary>
    public static byte[] Render(BankFileLayout? l, IReadOnlyList<BankTransfer> rows, DateOnly date)
    {
        l ??= new BankFileLayout { Name = "Generic CSV", Columns = "employee_no,name,bank,account_no,amount,reference", Delimiter = ",", Header = true, AmountDecimals = 2 };
        var cols = l.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries); var d = l.Delimiter;
        string Esc(string s) { if (s.Length > 0 && "=+-@".Contains(s[0]) && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) s = "'" + s; return s.Contains(d) || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s; }
        string Value(string col, BankTransfer t) => col switch
        {
            "account_no" => new string(t.AccountNo.Where(char.IsDigit).ToArray()), "name" => t.Name, "amount" => t.Amount.ToString(l.AmountDecimals == 0 ? "0" : "0.00", CultureInfo.InvariantCulture), "bank" => t.Bank ?? "",
            "employee_no" => t.EmployeeNo, "reference" => t.Reference, "email" => t.Email ?? "", "currency" => "IDR", "date" => date.ToString(l.DateFormat, CultureInfo.InvariantCulture), "source_account" => l.SourceAccount ?? "",
            "transfer_type" => (t.Bank ?? "").Contains("BCA", StringComparison.OrdinalIgnoreCase) ? "BCA" : "LLG", _ => "",
        };
        var sb = new StringBuilder();
        if (l.Header) sb.Append(string.Join(d, cols.Select(c => c == "blank" ? "" : c))).Append("\r\n");
        foreach (var t in rows) sb.Append(string.Join(d, cols.Select(c => Esc(Value(c, t))))).Append("\r\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // ============================================================ journal accounts
    public async Task<IReadOnlyList<JournalAccountView>> JournalAccountsAsync(CancellationToken ct = default)
    {
        var saved = await db.JournalAccounts.AsNoTracking().ToDictionaryAsync(a => a.Line, ct);
        return [.. JournalLines.Select(x => saved.TryGetValue(x.Line, out var a) ? new JournalAccountView(x.Line, x.Label, a.Code, a.Name) : new JournalAccountView(x.Line, x.Label, x.Code, x.Name))];
    }

    public async Task<IReadOnlyList<JournalAccountView>> SaveJournalAccountsAsync(IReadOnlyList<JournalAccountRequest> r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        foreach (var x in r) if (x.Line is null || JournalLines.All(l => l.Line != x.Line) || Clip(x.Code, 30) is null || Clip(x.Name, 120) is null) { errors["accounts"] = "Give every line an account code and name."; break; }
        if (errors.Count > 0) throw DomainException.Validation("Please check the accounts.", errors);
        var saved = await db.JournalAccounts.ToDictionaryAsync(a => a.Line, ct);
        foreach (var x in r)
        {
            if (!saved.TryGetValue(x.Line!, out var a)) { a = new JournalAccount { Line = x.Line! }; db.JournalAccounts.Add(a); saved[a.Line] = a; }
            a.Code = Clip(x.Code, 30)!; a.Name = Clip(x.Name, 120)!;
        }
        audit.Record("hr.journal_accounts.update", "payroll", "-", Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await JournalAccountsAsync(ct);
    }
}
