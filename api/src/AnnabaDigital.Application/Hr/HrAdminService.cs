using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record CustomFieldView(Guid Id, string Key, string Label, string Type, string[] Options, bool Required, bool VisibleToEmployee, bool EditableByEmployee, int SortOrder, bool Archived);
public sealed record CustomFieldRequest(string? Key, string? Label, string? Type, string[]? Options, bool? Required, bool? VisibleToEmployee, bool? EditableByEmployee, int? SortOrder, bool? Archived);
public sealed record CustomFieldValue(string Key, string Label, string Type, string[] Options, bool Required, bool Editable, string? Value);

public sealed record LetterTemplateView(Guid Id, string Name, string Prefix, string Title, string Body, bool VisibleToEmployee, bool Archived, int Issued);
public sealed record LetterTemplateRequest(string? Name, string? Prefix, string? Title, string? Body, bool? VisibleToEmployee, bool? Archived);
public sealed record IssueLetterRequest(Guid? TemplateId, Guid? EmployeeId);
public sealed record IssuedLetterView(Guid Id, string Number, string Title, string Body, Guid EmployeeId, string EmployeeName, bool VisibleToEmployee, DateTimeOffset IssuedAt);

public sealed record FormQuestion(string Id, string Type, string Label, string[]? Options, bool Required);
public sealed record HrFormView(Guid Id, string Title, string? Description, IReadOnlyList<FormQuestion> Questions, Guid? OrgUnitId, string? Audience, bool Anonymous, string Status, DateOnly? ClosesOn, int Responses, int AudienceSize, bool Answered);
public sealed record HrFormRequest(string? Title, string? Description, IReadOnlyList<FormQuestion>? Questions, Guid? OrgUnitId, bool? Anonymous, DateOnly? ClosesOn);
public sealed record FormAnswerRequest(Dictionary<string, JsonElement>? Answers);
public sealed record FormQuestionResult(string Id, string Label, string Type, int Answered, IReadOnlyList<(string Option, int Count)> Counts, decimal? Average, IReadOnlyList<string> Texts);
public sealed record FormResults(HrFormView Form, IReadOnlyList<FormQuestionResult> Questions);

public sealed record TicketView(Guid Id, int Number, Guid EmployeeId, string EmployeeName, string Category, string Subject, string Status, string? AssignedTo, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Messages);
public sealed record TicketMessageView(Guid Id, string Author, bool FromHr, string Body, bool Internal, DateTimeOffset CreatedAt);
public sealed record TicketDetail(TicketView Ticket, IReadOnlyList<TicketMessageView> Messages, bool CanManage);
public sealed record NewTicketRequest(string? Category, string? Subject, string? Body);
public sealed record TicketReplyRequest(string? Body, bool? Internal);
public sealed record TicketStatusRequest(string? Status, bool? AssignToMe);

public sealed record CompanyEventView(Guid Id, string Title, DateOnly StartDate, DateOnly EndDate, string? Location, string? Description, Guid? OrgUnitId);
public sealed record CompanyEventRequest(string? Title, DateOnly? StartDate, DateOnly? EndDate, string? Location, string? Description, Guid? OrgUnitId);
public sealed record CalendarItem(string Kind, DateOnly Date, DateOnly EndDate, string Title, string? Detail, Guid? Id);

public sealed record DirectoryEntry(Guid EmployeeId, string EmployeeNo, string FullName, string? JobTitle, string? Department, string? Branch, string? Manager, string? WorkEmail, bool HasPhoto);

public sealed record BranchView(Guid Id, Guid LegalEntityId, string Company, string Code, string Name, string Kind, string? Address, bool Archived, int Employees);
public sealed record BranchRequest(Guid? LegalEntityId, string? Code, string? Name, string? Kind, string? Address, bool? Archived);
public sealed record SetBranchRequest(Guid? BranchId);

/// <summary>
/// Stage 12, HR administration (Talenta: custom fields, document templates, forms and surveys, HR helpdesk, company calendar, employee directory, branches / SBU).
/// Setting things up needs hrconfig.manage; issuing letters and editing employee values needs the employee permissions; employees see and answer their own parts.
/// </summary>
public sealed partial class HrAdminService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, NotificationService notes, DocumentSealService seals, IAppLinks links)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
    [GeneratedRegex("^[a-z][a-z0-9_]{1,39}$")] private static partial Regex KeyPattern();
    [GeneratedRegex(@"\{\{\s*([a-z0-9_.]+)\s*\}\}")] private static partial Regex Placeholder();

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");
    private async Task<bool> HasAsync(SessionPrincipal actor, string perm, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(perm);
    private static Dictionary<string, string?> Values(string json) { try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json) ?? []; } catch (JsonException) { return []; } }

    // ============================================================ custom fields
    private static CustomFieldView View(CustomFieldDef d) => new(d.Id, d.Key, d.Label, d.Type.ToString().ToLowerInvariant(), Opts(d), d.Required, d.VisibleToEmployee, d.EditableByEmployee, d.SortOrder, d.Archived);
    private static string[] Opts(CustomFieldDef d) => string.IsNullOrEmpty(d.Options) ? [] : d.Options.Split('|');

    public async Task<IReadOnlyList<CustomFieldView>> FieldsAsync(CancellationToken ct = default)
        => [.. (await db.CustomFieldDefs.AsNoTracking().OrderBy(d => d.Archived).ThenBy(d => d.SortOrder).ThenBy(d => d.Label).ToListAsync(ct)).Select(View)];

    public async Task<CustomFieldView> SaveFieldAsync(Guid? id, CustomFieldRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var d = id is { } fid ? await db.CustomFieldDefs.FirstOrDefaultAsync(x => x.Id == fid, ct) ?? throw DomainException.NotFound("Field") : new CustomFieldDef { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var key = id is null ? (r.Key ?? "").Trim().ToLowerInvariant() : d.Key;   // the key never changes: values are stored under it
        if (id is null && !KeyPattern().IsMatch(key)) errors["key"] = "Use 2–40 lowercase letters, digits or underscores, starting with a letter.";
        var label = (r.Label ?? d.Label).Trim(); if (label.Length is < 1 or > 100) errors["label"] = "Enter a label (up to 100 characters).";
        var type = d.Type; if (r.Type is { } ts && !Enum.TryParse(ts.Replace("-", ""), true, out type)) errors["type"] = "Choose text, number, date, choice or yesno.";
        var options = r.Options?.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToArray() ?? Opts(d);
        if (type == CustomFieldType.Choice && (options.Length < 2 || options.Any(o => o.Length > 60 || o.Contains('|')))) errors["options"] = "Give at least two answers (up to 60 characters each).";
        if (errors.Count == 0 && id is null && await db.CustomFieldDefs.AnyAsync(x => x.Key == key, ct)) errors["key"] = "Another field already uses this key.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the field.", errors);
        d.Key = key; d.Label = label; d.Type = type; d.Options = type == CustomFieldType.Choice ? string.Join('|', options) : null; d.Required = r.Required ?? d.Required;
        d.VisibleToEmployee = r.VisibleToEmployee ?? d.VisibleToEmployee; d.EditableByEmployee = d.VisibleToEmployee && (r.EditableByEmployee ?? d.EditableByEmployee); d.SortOrder = r.SortOrder ?? d.SortOrder; d.Archived = r.Archived ?? d.Archived;
        if (id is null) db.CustomFieldDefs.Add(d);
        audit.Record(id is null ? "hr.custom_field.create" : "hr.custom_field.update", "custom_field", d.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["key"] = key });
        await db.SaveChangesAsync(ct);
        return View(d);
    }

    private static string? Check(CustomFieldDef d, string? raw, Dictionary<string, string> errors)
    {
        var v = raw?.Trim(); if (string.IsNullOrEmpty(v)) { if (d.Required) errors[d.Key] = $"{d.Label} is required."; return null; }
        switch (d.Type)
        {
            case CustomFieldType.Text: if (v.Length > 500) errors[d.Key] = "Keep it under 500 characters."; return v;
            case CustomFieldType.Number: if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) errors[d.Key] = "Enter a number."; else return n.ToString(CultureInfo.InvariantCulture); return null;
            case CustomFieldType.Date: if (!DateOnly.TryParseExact(v, "yyyy-MM-dd", out var dt)) errors[d.Key] = "Enter a date as YYYY-MM-DD."; else return dt.ToString("yyyy-MM-dd"); return null;
            case CustomFieldType.YesNo: if (v is not ("yes" or "no")) errors[d.Key] = "Answer yes or no."; return v;
            default: if (!Opts(d).Contains(v)) errors[d.Key] = "Choose one of the answers."; return v;
        }
    }

    public async Task<IReadOnlyList<CustomFieldValue>> EmployeeFieldsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        var vals = Values(e.CustomFieldsJson);
        return [.. (await db.CustomFieldDefs.AsNoTracking().Where(d => !d.Archived).OrderBy(d => d.SortOrder).ThenBy(d => d.Label).ToListAsync(ct)).Select(d => new CustomFieldValue(d.Key, d.Label, d.Type.ToString().ToLowerInvariant(), Opts(d), d.Required, true, vals.GetValueOrDefault(d.Key)))];
    }

    public async Task<IReadOnlyList<CustomFieldValue>> SetEmployeeFieldsAsync(Guid employeeId, Dictionary<string, string?> values, bool self, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        var defs = await db.CustomFieldDefs.AsNoTracking().Where(d => !d.Archived).ToListAsync(ct); var current = Values(e.CustomFieldsJson); var errors = new Dictionary<string, string>();
        foreach (var key in values.Keys.Where(k => !defs.Any(d => d.Key == k))) errors[key] = "Unknown field.";
        foreach (var d in defs.Where(d => values.ContainsKey(d.Key)))
        {
            if (self && !d.EditableByEmployee) { errors[d.Key] = "Only HR can change this field."; continue; }
            current[d.Key] = Check(d, values[d.Key], errors);
        }
        if (errors.Count > 0) throw DomainException.Validation("Please check the fields.", errors);
        e.CustomFieldsJson = JsonSerializer.Serialize(current.Where(kv => kv.Value is not null).ToDictionary(), Json);
        audit.Record(self ? "hr.custom_field.self_update" : "hr.custom_field.values", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["fields"] = string.Join(",", values.Keys) });
        await db.SaveChangesAsync(ct);
        return self ? await MyFieldsAsync(actor, ct) : await EmployeeFieldsAsync(employeeId, ct);
    }

    public async Task<IReadOnlyList<CustomFieldValue>> MyFieldsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var vals = Values(me.CustomFieldsJson);
        return [.. (await db.CustomFieldDefs.AsNoTracking().Where(d => !d.Archived && d.VisibleToEmployee).OrderBy(d => d.SortOrder).ThenBy(d => d.Label).ToListAsync(ct))
            .Select(d => new CustomFieldValue(d.Key, d.Label, d.Type.ToString().ToLowerInvariant(), Opts(d), d.Required, d.EditableByEmployee, vals.GetValueOrDefault(d.Key)))];
    }

    public async Task<IReadOnlyList<CustomFieldValue>> SetMyFieldsAsync(Dictionary<string, string?> values, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
        => await SetEmployeeFieldsAsync((await MeAsync(actor, ct)).Id, values, true, actor, client, ct);

    // ============================================================ letter templates and issued letters
    public async Task<IReadOnlyList<LetterTemplateView>> TemplatesAsync(CancellationToken ct = default)
    {
        var counts = await db.IssuedLetters.AsNoTracking().GroupBy(l => l.TemplateId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. (await db.LetterTemplates.AsNoTracking().OrderBy(t => t.Archived).ThenBy(t => t.Name).ToListAsync(ct)).Select(t => new LetterTemplateView(t.Id, t.Name, t.Prefix, t.Title, t.Body, t.VisibleToEmployee, t.Archived, counts.GetValueOrDefault(t.Id)))];
    }

    public static readonly string[] Placeholders = ["name", "employee_no", "job_title", "department", "branch", "manager", "hire_date", "company", "company_address", "today", "number"];

    public async Task<LetterTemplateView> SaveTemplateAsync(Guid? id, LetterTemplateRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = id is { } tid ? await db.LetterTemplates.FirstOrDefaultAsync(x => x.Id == tid, ct) ?? throw DomainException.NotFound("Template") : new LetterTemplate { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var name = (r.Name ?? t.Name).Trim(); if (name.Length is < 1 or > 100) errors["name"] = "Enter a name (up to 100 characters).";
        var prefix = (r.Prefix ?? t.Prefix).Trim().ToUpperInvariant(); if (!Regex.IsMatch(prefix, "^[A-Z0-9-]{1,20}$")) errors["prefix"] = "Use 1–20 capital letters, digits or dashes, e.g. SP or HR-SK.";
        var title = (r.Title ?? t.Title).Trim(); if (title.Length is < 1 or > 150) errors["title"] = "Enter the letter's title (up to 150 characters).";
        var body = (r.Body ?? t.Body).Replace("\r\n", "\n").Trim(); if (body.Length is < 1 or > 20000) errors["body"] = "Write the letter (up to 20,000 characters).";
        var cfKeys = await db.CustomFieldDefs.AsNoTracking().Select(d => d.Key).ToListAsync(ct);
        var unknown = Placeholder().Matches(body).Select(m => m.Groups[1].Value).Where(k => !Placeholders.Contains(k) && !(k.StartsWith("cf.") && cfKeys.Contains(k[3..]))).Distinct().ToList();
        if (unknown.Count > 0) errors["body"] = "Unknown placeholder(s): " + string.Join(", ", unknown.Select(u => "{{" + u + "}}")) + ".";
        if (errors.Count > 0) throw DomainException.Validation("Please check the template.", errors);
        t.Name = name; t.Prefix = prefix; t.Title = title; t.Body = body; t.VisibleToEmployee = r.VisibleToEmployee ?? t.VisibleToEmployee; t.Archived = r.Archived ?? t.Archived;
        if (id is null) db.LetterTemplates.Add(t);
        audit.Record(id is null ? "hr.letter_template.create" : "hr.letter_template.update", "letter_template", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return (await TemplatesAsync(ct)).First(x => x.Id == t.Id);
    }

    private async Task<string> RenderAsync(string text, Employee e, string number, CancellationToken ct)
    {
        var job = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == e.Id && m.EffectiveTo == null).Select(m => new { m.JobTitle, m.OrgUnitId, m.ManagerId }).FirstOrDefaultAsync(ct);
        var dept = job?.OrgUnitId is { } u ? await db.OrgUnits.AsNoTracking().Where(x => x.Id == u).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var mgr = job?.ManagerId is { } m ? await db.Employees.AsNoTracking().Where(x => x.Id == m).Select(x => x.FullName).FirstOrDefaultAsync(ct) : null;
        var branch = e.BranchId is { } b ? await db.Branches.AsNoTracking().Where(x => x.Id == b).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var co = await db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == e.LegalEntityId, ct);
        var cf = Values(e.CustomFieldsJson); var id = CultureInfo.GetCultureInfo("id-ID");
        var map = new Dictionary<string, string?>
        {
            ["name"] = e.FullName, ["employee_no"] = e.EmployeeNo, ["job_title"] = job?.JobTitle, ["department"] = dept, ["branch"] = branch, ["manager"] = mgr,
            ["hire_date"] = e.HireDate.ToString("d MMMM yyyy", id), ["company"] = co?.Name, ["company_address"] = co?.Address, ["today"] = Today.ToString("d MMMM yyyy", id), ["number"] = number,
        };
        return Placeholder().Replace(text, m => { var k = m.Groups[1].Value; return (k.StartsWith("cf.") ? cf.GetValueOrDefault(k[3..]) : map.GetValueOrDefault(k)) ?? "-"; });
    }

    public async Task<IssuedLetterView> PreviewAsync(IssueLetterRequest r, CancellationToken ct = default)
    {
        var t = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.TemplateId, ct) ?? throw DomainException.NotFound("Template");
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.EmployeeId, ct) ?? throw DomainException.NotFound("Employee");
        var number = $"{t.Prefix}/{Today.Year}/....";
        return new IssuedLetterView(Guid.Empty, number, await RenderAsync(t.Title, e, number, ct), await RenderAsync(t.Body, e, number, ct), e.Id, e.FullName, t.VisibleToEmployee, clock.UtcNow);
    }

    public async Task<IssuedLetterView> IssueAsync(IssueLetterRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.LetterTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.TemplateId && !x.Archived, ct) ?? throw DomainException.NotFound("Template");
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.EmployeeId, ct) ?? throw DomainException.NotFound("Employee");
        return await db.InTransactionAsync(async () =>
        {
            // numbers run per prefix per year; the advisory lock keeps two letters issued at the same moment from taking one number
            await db.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext({"letter:" + t.Prefix + ":" + Today.Year}))", ct);
            var start = $"{t.Prefix}/{Today.Year}/";
            var count = await db.IssuedLetters.CountAsync(l => l.Number.StartsWith(start), ct);
            var number = $"{start}{count + 1:D4}";
            var l = new IssuedLetter { TemplateId = t.Id, EmployeeId = e.Id, Number = number, Title = await RenderAsync(t.Title, e, number, ct), Body = await RenderAsync(t.Body, e, number, ct), VisibleToEmployee = t.VisibleToEmployee, IssuedBy = actor.UserId, IssuedAt = clock.UtcNow };
            db.IssuedLetters.Add(l);
            audit.Record("hr.letter.issue", "employee", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = number, ["template"] = t.Name });
            if (t.VisibleToEmployee && e.UserId is { } uid) notes.Notify(uid, "letter.issued", $"A letter was issued to you: {l.Title}", number, "/hr/my-letters");
            await db.SaveChangesAsync(ct);
            return new IssuedLetterView(l.Id, l.Number, l.Title, l.Body, e.Id, e.FullName, l.VisibleToEmployee, l.IssuedAt);
        }, ct);
    }

    public async Task<IReadOnlyList<IssuedLetterView>> IssuedAsync(Guid? employeeId, bool mine, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.IssuedLetters.AsNoTracking().AsQueryable();
        if (mine) { var me = await MeAsync(actor, ct); q = q.Where(l => l.EmployeeId == me.Id && l.VisibleToEmployee); }
        else if (employeeId is { } eid) q = q.Where(l => l.EmployeeId == eid);
        var rows = await q.OrderByDescending(l => l.IssuedAt).Take(500).ToListAsync(ct); var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        return [.. rows.Select(l => new IssuedLetterView(l.Id, l.Number, l.Title, l.Body, l.EmployeeId, names.GetValueOrDefault(l.EmployeeId) ?? "", l.VisibleToEmployee, l.IssuedAt))];
    }

    /// <summary>The issued letter as a PDF with its verification code: for HR (employees.read), or for the employee when the letter is visible to them.</summary>
    public async Task<ExportFile> LetterPdfAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.IssuedLetters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Letter");
        var e = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == l.EmployeeId, ct); var own = e.UserId == actor.UserId;
        if (!(own && l.VisibleToEmployee) && !await HasAsync(actor, Permissions.EmployeesRead, ct)) throw DomainException.NotFound("Letter");
        var co = await db.LegalEntities.AsNoTracking().FirstOrDefaultAsync(x => x.Id == e.LegalEntityId, ct);
        var pdf = new SimplePdf(); double y = 60;
        pdf.Text(co?.Name ?? "", 50, y, 14, true); y += 14; if (co?.Address is { Length: > 0 } addr) { pdf.Text(addr, 50, y, 8); y += 10; }
        pdf.Line(50, y, 545); y += 26;
        pdf.Text(l.Title, 50, y, 13, true); y += 16; pdf.Text($"No. {l.Number}", 50, y, 9); y += 24;
        foreach (var line in SimplePdf.Wrap(l.Body))
        {
            if (y > 760) { pdf.NewPage(); y = 60; }
            pdf.Text(line, 50, y, 10); y += 14;
        }
        var seal = await seals.IssueAsync("hr_letter", l.Id, co?.Name ?? "", l.Number, DocumentSealService.Mask(e.FullName), ct); await db.SaveChangesAsync(ct);
        pdf.Text($"Verification code: {DocumentSealService.Format(seal)}  -  check it at {links.Origin.TrimEnd('/')}/verify", 50, 800, 8);
        audit.Record("hr.letter.download", "employee", e.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = l.Number });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"{l.Number.Replace('/', '-')}.pdf", "application/pdf", pdf.Build());
    }

    // ============================================================ forms and surveys
    private static readonly string[] QuestionTypes = ["text", "choice", "multi", "rating", "yesno"];
    private static IReadOnlyList<FormQuestion> Questions(string json) { try { return JsonSerializer.Deserialize<List<FormQuestion>>(json, Json) ?? []; } catch (JsonException) { return []; } }

    private async Task<int> AudienceSizeAsync(Guid? unit, CancellationToken ct)
        => unit is { } u ? await db.Employees.CountAsync(e => e.Status == EmployeeStatus.Active && e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u), ct) : await db.Employees.CountAsync(e => e.Status == EmployeeStatus.Active, ct);

    private async Task<HrFormView> FormViewAsync(HrForm f, Guid? me, CancellationToken ct)
    {
        var unit = f.OrgUnitId is { } u ? await db.OrgUnits.AsNoTracking().Where(x => x.Id == u).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new HrFormView(f.Id, f.Title, f.Description, Questions(f.QuestionsJson), f.OrgUnitId, unit, f.Anonymous, f.Status.ToString().ToLowerInvariant(), f.ClosesOn,
            await db.HrFormResponses.CountAsync(r => r.FormId == f.Id, ct), await AudienceSizeAsync(f.OrgUnitId, ct), me is { } m && await db.HrFormResponses.AnyAsync(r => r.FormId == f.Id && r.EmployeeId == m, ct));
    }

    public async Task<IReadOnlyList<HrFormView>> FormsAsync(CancellationToken ct = default)
    {
        var list = new List<HrFormView>();
        foreach (var f in await db.HrForms.AsNoTracking().OrderByDescending(f => f.CreatedAt).ToListAsync(ct)) list.Add(await FormViewAsync(f, null, ct));
        return list;
    }

    public async Task<HrFormView> SaveFormAsync(Guid? id, HrFormRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var f = id is { } fid ? await db.HrForms.FirstOrDefaultAsync(x => x.Id == fid, ct) ?? throw DomainException.NotFound("Form") : new HrForm { CreatedAt = clock.UtcNow, CreatedBy = actor.UserId };
        if (f.Status != HrFormStatus.Draft && r.Questions is not null) throw DomainException.Invariant("Questions cannot change once the form is open. Close it and make a new one.");
        var errors = new Dictionary<string, string>();
        var title = (r.Title ?? f.Title).Trim(); if (title.Length is < 1 or > 150) errors["title"] = "Enter a title (up to 150 characters).";
        var qs = r.Questions ?? Questions(f.QuestionsJson);
        if (qs.Count is < 1 or > 40) errors["questions"] = "Add between 1 and 40 questions.";
        else
        {
            var ids = new HashSet<string>();
            foreach (var (q, i) in qs.Select((q, i) => (q, i)))
            {
                if (string.IsNullOrWhiteSpace(q.Id) || !ids.Add(q.Id) || q.Id.Length > 20) errors[$"questions.{i}"] = "Each question needs its own short id.";
                else if (!QuestionTypes.Contains(q.Type)) errors[$"questions.{i}"] = "Choose text, choice, multi, rating or yesno.";
                else if (string.IsNullOrWhiteSpace(q.Label) || q.Label.Length > 300) errors[$"questions.{i}"] = "Write the question (up to 300 characters).";
                else if (q.Type is "choice" or "multi" && (q.Options is null || q.Options.Count(o => !string.IsNullOrWhiteSpace(o)) < 2)) errors[$"questions.{i}"] = "Give at least two answers to choose from.";
            }
        }
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (r.ClosesOn is { } c && c < Today) errors["closesOn"] = "Choose today or a later date.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the form.", errors);
        f.Title = title; f.Description = r.Description?.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 1000)] : f.Description;
        if (f.Status == HrFormStatus.Draft) { f.QuestionsJson = JsonSerializer.Serialize(qs.Select(q => q with { Label = q.Label.Trim(), Options = q.Options?.Select(o => o.Trim()).Where(o => o.Length > 0).ToArray() }), Json); f.OrgUnitId = r.OrgUnitId ?? f.OrgUnitId; f.Anonymous = r.Anonymous ?? f.Anonymous; }
        f.ClosesOn = r.ClosesOn ?? f.ClosesOn;
        if (id is null) db.HrForms.Add(f);
        audit.Record(id is null ? "hr.form.create" : "hr.form.update", "hr_form", f.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = title });
        await db.SaveChangesAsync(ct);
        return await FormViewAsync(f, null, ct);
    }

    public async Task<HrFormView> SetFormStatusAsync(Guid id, bool open, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var f = await db.HrForms.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Form");
        if (open && f.Status != HrFormStatus.Draft) throw DomainException.Conflict("Only a draft can be opened.");
        if (!open && f.Status != HrFormStatus.Open) throw DomainException.Conflict("Only an open form can be closed.");
        f.Status = open ? HrFormStatus.Open : HrFormStatus.Closed; if (open) f.OpenedAt = clock.UtcNow;
        if (open)
        {
            var q = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && e.UserId != null);
            if (f.OrgUnitId is { } u) q = q.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u));
            foreach (var uid in await q.Select(e => e.UserId!.Value).ToListAsync(ct)) notes.Notify(uid, "form.opened", $"Please fill in: {f.Title}", f.Anonymous ? "Your answers are anonymous." : null, "/hr/my-forms");
        }
        audit.Record(open ? "hr.form.open" : "hr.form.close", "hr_form", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await FormViewAsync(f, null, ct);
    }

    private static bool IsOpen(HrForm f, DateOnly today) => f.Status == HrFormStatus.Open && (f.ClosesOn is null || f.ClosesOn >= today);

    public async Task<IReadOnlyList<HrFormView>> MyFormsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var unit = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == me.Id && m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefaultAsync(ct);
        var forms = await db.HrForms.AsNoTracking().Where(f => f.Status != HrFormStatus.Draft && (f.OrgUnitId == null || f.OrgUnitId == unit)).OrderByDescending(f => f.OpenedAt).Take(50).ToListAsync(ct);
        var list = new List<HrFormView>();
        foreach (var f in forms) { var v = await FormViewAsync(f, me.Id, ct); if (IsOpen(f, Today) || v.Answered) list.Add(v with { Status = IsOpen(f, Today) ? "open" : "closed" }); }
        return list;
    }

    public async Task<HrFormView> AnswerAsync(Guid id, FormAnswerRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var f = await db.HrForms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Form");
        var unit = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == me.Id && m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefaultAsync(ct);
        if (f.OrgUnitId is { } u && u != unit) throw DomainException.NotFound("Form");
        if (!IsOpen(f, Today)) throw DomainException.Conflict("This form is closed.");
        if (await db.HrFormResponses.AnyAsync(x => x.FormId == id && x.EmployeeId == me.Id, ct)) throw DomainException.Conflict("You already answered this form.");
        var answers = r.Answers ?? []; var clean = new Dictionary<string, object>(); var errors = new Dictionary<string, string>();
        foreach (var q in Questions(f.QuestionsJson))
        {
            if (!answers.TryGetValue(q.Id, out var a) || a.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined || a.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(a.GetString()) || a.ValueKind == JsonValueKind.Array && a.GetArrayLength() == 0)
            { if (q.Required) errors[q.Id] = "Please answer this question."; continue; }
            switch (q.Type)
            {
                case "text": if (a.ValueKind != JsonValueKind.String || a.GetString()!.Length > 2000) errors[q.Id] = "Keep the answer under 2,000 characters."; else clean[q.Id] = a.GetString()!.Trim(); break;
                case "rating": if (a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out var n) || n is < 1 or > 5) errors[q.Id] = "Choose 1 to 5."; else clean[q.Id] = n; break;
                case "yesno": if (a.ValueKind != JsonValueKind.String || a.GetString() is not ("yes" or "no")) errors[q.Id] = "Answer yes or no."; else clean[q.Id] = a.GetString()!; break;
                case "choice": if (a.ValueKind != JsonValueKind.String || !(q.Options ?? []).Contains(a.GetString())) errors[q.Id] = "Choose one of the answers."; else clean[q.Id] = a.GetString()!; break;
                default:
                    var picked = a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Distinct().ToList() : [];
                    if (picked.Count == 0 || picked.Any(p => !(q.Options ?? []).Contains(p))) errors[q.Id] = "Choose from the answers."; else clean[q.Id] = picked; break;
            }
        }
        if (errors.Count > 0) throw DomainException.Validation("Please check your answers.", errors);
        db.HrFormResponses.Add(new HrFormResponse { FormId = id, EmployeeId = me.Id, AnswersJson = JsonSerializer.Serialize(clean, Json), SubmittedAt = clock.UtcNow });
        audit.Record("hr.form.answer", "hr_form", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("You already answered this form."); }
        return await FormViewAsync(f, me.Id, ct);
    }

    /// <summary>Results in aggregate. For an anonymous form the text answers are listed without names and in random order, and nothing links an answer to a person.</summary>
    public async Task<FormResults> ResultsAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.HrForms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Form");
        var rows = await db.HrFormResponses.AsNoTracking().Where(r => r.FormId == id).Select(r => new { r.EmployeeId, r.AnswersJson }).ToListAsync(ct);
        var names = f.Anonymous ? [] : await db.Employees.AsNoTracking().Where(e => rows.Select(r => r.EmployeeId).Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var parsed = rows.Select(r => (r.EmployeeId, Answers: JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(r.AnswersJson, Json) ?? [])).ToList();
        if (f.Anonymous) parsed = [.. parsed.OrderBy(_ => Random.Shared.Next())];
        var results = Questions(f.QuestionsJson).Select(q =>
        {
            var got = parsed.Where(p => p.Answers.ContainsKey(q.Id)).ToList();
            IReadOnlyList<(string, int)> counts = q.Type switch
            {
                "choice" or "yesno" => [.. (q.Type == "yesno" ? ["yes", "no"] : q.Options ?? []).Select(o => (o, got.Count(p => p.Answers[q.Id].ValueKind == JsonValueKind.String && p.Answers[q.Id].GetString() == o)))],
                "multi" => [.. (q.Options ?? []).Select(o => (o, got.Count(p => p.Answers[q.Id].ValueKind == JsonValueKind.Array && p.Answers[q.Id].EnumerateArray().Any(x => x.GetString() == o))))],
                "rating" => [.. Enumerable.Range(1, 5).Select(n => (n.ToString(), got.Count(p => p.Answers[q.Id].TryGetInt32(out var v) && v == n)))],
                _ => [],
            };
            decimal? avg = q.Type == "rating" && got.Count > 0 ? Math.Round((decimal)got.Average(p => p.Answers[q.Id].GetInt32()), 2) : null;
            IReadOnlyList<string> texts = q.Type == "text" ? [.. got.Select(p => (f.Anonymous ? "" : (names.GetValueOrDefault(p.EmployeeId) ?? "") + ": ") + p.Answers[q.Id].GetString())] : [];
            return new FormQuestionResult(q.Id, q.Label, q.Type, got.Count, counts, avg, texts);
        }).ToList();
        return new FormResults(await FormViewAsync(f, null, ct), results);
    }

    // ============================================================ HR helpdesk
    public static readonly string[] TicketCategories = ["payroll", "leave", "attendance", "documents", "benefits", "data", "other"];

    private async Task<IReadOnlyList<TicketView>> TicketViewsAsync(IReadOnlyList<HelpdeskTicket> rows, CancellationToken ct)
    {
        var emps = rows.Select(r => r.EmployeeId).Distinct().ToList(); var assignees = rows.Where(r => r.AssignedTo != null).Select(r => r.AssignedTo!.Value).Distinct().ToList(); var ids = rows.Select(r => r.Id).ToList();
        var names = await db.Employees.AsNoTracking().Where(e => emps.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var users = await db.Users.AsNoTracking().Where(u => assignees.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var counts = await db.HelpdeskMessages.AsNoTracking().Where(m => ids.Contains(m.TicketId) && !m.Internal).GroupBy(m => m.TicketId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. rows.Select(t => new TicketView(t.Id, t.Number, t.EmployeeId, names.GetValueOrDefault(t.EmployeeId) ?? "", t.Category, t.Subject, StatusName(t.Status), t.AssignedTo is { } a ? users.GetValueOrDefault(a) : null, t.CreatedAt, t.UpdatedAt, counts.GetValueOrDefault(t.Id)))];
    }
    private static string StatusName(TicketStatus s) => s == TicketStatus.InProgress ? "in_progress" : s.ToString().ToLowerInvariant();

    public async Task<TicketView> NewTicketAsync(NewTicketRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var errors = new Dictionary<string, string>();
        var cat = (r.Category ?? "other").Trim().ToLowerInvariant(); if (!TicketCategories.Contains(cat)) errors["category"] = "Choose a topic.";
        var subject = r.Subject?.Trim(); if (string.IsNullOrEmpty(subject) || subject.Length > 150) errors["subject"] = "Give a short subject (up to 150 characters).";
        var body = r.Body?.Trim(); if (string.IsNullOrEmpty(body) || body.Length > 4000) errors["body"] = "Describe your question (up to 4,000 characters).";
        if (errors.Count > 0) throw DomainException.Validation("Please check your question.", errors);
        if (await db.HelpdeskTickets.CountAsync(t => t.EmployeeId == me.Id && (t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress), ct) >= 10) throw DomainException.Invariant("You already have 10 open questions. Wait for an answer first.");
        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtext('helpdesk-number'))", ct);
            var number = (await db.HelpdeskTickets.MaxAsync(t => (int?)t.Number, ct) ?? 0) + 1; var now = clock.UtcNow;
            var t = new HelpdeskTicket { Number = number, EmployeeId = me.Id, Category = cat, Subject = subject!, CreatedAt = now, UpdatedAt = now };
            db.HelpdeskTickets.Add(t); db.HelpdeskMessages.Add(new HelpdeskMessage { TicketId = t.Id, AuthorUserId = actor.UserId, Body = body!, CreatedAt = now });
            audit.Record("hr.helpdesk.create", "helpdesk_ticket", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["category"] = cat });
            foreach (var uid in await notes.UsersWithPermissionAsync(Permissions.HrConfigManage, ct)) if (uid != actor.UserId) notes.Notify(uid, "helpdesk.new", $"New question #{number}: {subject}", me.FullName, $"/hr/helpdesk/{t.Id}");
            await db.SaveChangesAsync(ct);
            return (await TicketViewsAsync([t], ct))[0];
        }, ct);
    }

    public async Task<IReadOnlyList<TicketView>> TicketsAsync(bool mine, string? status, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.HelpdeskTickets.AsNoTracking().AsQueryable();
        if (mine) { var me = await MeAsync(actor, ct); q = q.Where(t => t.EmployeeId == me.Id); }
        else if (!await HasAsync(actor, Permissions.HrConfigManage, ct)) throw DomainException.Denied(Permissions.HrConfigManage);
        if (status is "open") q = q.Where(t => t.Status == TicketStatus.Open || t.Status == TicketStatus.InProgress);
        else if (status is "closed") q = q.Where(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed);
        return await TicketViewsAsync(await q.OrderBy(t => t.Status == TicketStatus.Resolved || t.Status == TicketStatus.Closed).ThenByDescending(t => t.UpdatedAt).Take(500).ToListAsync(ct), ct);
    }

    private async Task<(HelpdeskTicket Ticket, bool Manage)> TicketForAsync(Guid id, SessionPrincipal actor, CancellationToken ct)
    {
        var t = await db.HelpdeskTickets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Question");
        var manage = await HasAsync(actor, Permissions.HrConfigManage, ct);
        var own = await db.Employees.AnyAsync(e => e.Id == t.EmployeeId && e.UserId == actor.UserId, ct);
        if (!own && !manage) throw DomainException.NotFound("Question");
        return (t, manage && !own);
    }

    public async Task<TicketDetail> TicketAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        var (t, manage) = await TicketForAsync(id, actor, ct);
        var msgs = await db.HelpdeskMessages.AsNoTracking().Where(m => m.TicketId == id && (manage || !m.Internal)).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var authors = msgs.Select(m => m.AuthorUserId).Distinct().ToList(); var users = await db.Users.AsNoTracking().Where(u => authors.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var employeeUser = await db.Employees.AsNoTracking().Where(e => e.Id == t.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct);
        return new TicketDetail((await TicketViewsAsync([t], ct))[0], [.. msgs.Select(m => new TicketMessageView(m.Id, users.GetValueOrDefault(m.AuthorUserId) ?? "", m.AuthorUserId != employeeUser, m.Body, m.Internal, m.CreatedAt))], manage);
    }

    public async Task<TicketDetail> ReplyAsync(Guid id, TicketReplyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (t, manage) = await TicketForAsync(id, actor, ct);
        var body = r.Body?.Trim(); if (string.IsNullOrEmpty(body) || body.Length > 4000) throw DomainException.Validation("Please write a message.", new Dictionary<string, string> { ["body"] = "Up to 4,000 characters." });
        if (t.Status == TicketStatus.Closed) throw DomainException.Conflict("This question is closed. Ask a new one.");
        var isInternal = manage && r.Internal == true;
        db.HelpdeskMessages.Add(new HelpdeskMessage { TicketId = id, AuthorUserId = actor.UserId, Body = body, Internal = isInternal, CreatedAt = clock.UtcNow });
        if (!isInternal)
        {
            if (manage) { if (t.Status == TicketStatus.Open) t.Status = TicketStatus.InProgress; t.AssignedTo ??= actor.UserId; var uid = await db.Employees.AsNoTracking().Where(e => e.Id == t.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct); notes.Notify(uid, "helpdesk.reply", $"HR answered your question #{t.Number}", t.Subject, $"/hr/helpdesk/{id}"); }
            else { if (t.Status == TicketStatus.Resolved) t.Status = TicketStatus.Open; if (t.AssignedTo is { } a) notes.Notify(a, "helpdesk.reply", $"New message on question #{t.Number}", t.Subject, $"/hr/helpdesk/{id}"); }
        }
        t.UpdatedAt = clock.UtcNow; t.Version++;
        audit.Record(isInternal ? "hr.helpdesk.note" : "hr.helpdesk.reply", "helpdesk_ticket", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await TicketAsync(id, actor, ct);
    }

    public async Task<TicketDetail> SetTicketStatusAsync(Guid id, TicketStatusRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (t, manage) = await TicketForAsync(id, actor, ct);
        var want = (r.Status ?? "").Replace("_", ""); TicketStatus status = t.Status;
        if (want.Length > 0 && !Enum.TryParse(want, true, out status)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use open, in_progress, resolved or closed." });
        if (!manage && status != TicketStatus.Closed) throw DomainException.DeniedBecause("You can only close your own question.");
        if (r.AssignToMe == true) { if (!manage) throw DomainException.Denied(Permissions.HrConfigManage); t.AssignedTo = actor.UserId; }
        if (status != t.Status)
        {
            t.Status = status; if (status == TicketStatus.Resolved) t.ResolvedAt = clock.UtcNow;
            if (manage && status is TicketStatus.Resolved or TicketStatus.Closed) { var uid = await db.Employees.AsNoTracking().Where(e => e.Id == t.EmployeeId).Select(e => e.UserId).FirstOrDefaultAsync(ct); notes.Notify(uid, "helpdesk.reply", $"Your question #{t.Number} was marked {StatusName(status).Replace('_', ' ')}", t.Subject, $"/hr/helpdesk/{id}"); }
        }
        t.UpdatedAt = clock.UtcNow; t.Version++;
        audit.Record("hr.helpdesk.status", "helpdesk_ticket", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["status"] = StatusName(t.Status) });
        await db.SaveChangesAsync(ct);
        return await TicketAsync(id, actor, ct);
    }

    // ============================================================ company calendar
    public async Task<CompanyEventView> SaveEventAsync(Guid? id, CompanyEventRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var ev = id is { } eid ? await db.CompanyEvents.FirstOrDefaultAsync(x => x.Id == eid, ct) ?? throw DomainException.NotFound("Event") : new CompanyEvent { CreatedAt = clock.UtcNow, CreatedBy = actor.UserId };
        var errors = new Dictionary<string, string>();
        var title = (r.Title ?? ev.Title).Trim(); if (title.Length is < 1 or > 150) errors["title"] = "Enter a title (up to 150 characters).";
        var start = r.StartDate ?? (id is null ? null : ev.StartDate); var end = r.EndDate ?? start;
        if (start is null) errors["startDate"] = "Choose the date."; else if (end < start) errors["endDate"] = "The end cannot be before the start.";
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the event.", errors);
        ev.Title = title; ev.StartDate = start!.Value; ev.EndDate = end!.Value; ev.Location = r.Location?.Trim() is { Length: > 0 } l ? l[..Math.Min(l.Length, 150)] : null; ev.Description = r.Description?.Trim() is { Length: > 0 } d ? d[..Math.Min(d.Length, 1000)] : null; ev.OrgUnitId = r.OrgUnitId;
        if (id is null) db.CompanyEvents.Add(ev);
        audit.Record(id is null ? "hr.event.create" : "hr.event.update", "company_event", ev.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = title });
        await db.SaveChangesAsync(ct);
        return new CompanyEventView(ev.Id, ev.Title, ev.StartDate, ev.EndDate, ev.Location, ev.Description, ev.OrgUnitId);
    }

    public async Task DeleteEventAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var ev = await db.CompanyEvents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Event");
        db.CompanyEvents.Remove(ev); audit.Record("hr.event.delete", "company_event", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// One month of the company calendar for the caller: company events for everyone or their department, public holidays, colleagues' birthdays (day and month only, never the year)
    /// and work anniversaries, and approved leave of people in the caller's own department.
    /// </summary>
    public async Task<IReadOnlyList<CalendarItem>> CalendarAsync(int year, int month, SessionPrincipal actor, CancellationToken ct = default)
    {
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw DomainException.Validation("Unknown month.", new Dictionary<string, string> { ["month"] = "Use a year and a month 1–12." });
        var me = await MeAsync(actor, ct); var first = new DateOnly(year, month, 1); var last = first.AddMonths(1).AddDays(-1);
        var unit = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == me.Id && m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefaultAsync(ct);
        var items = new List<CalendarItem>();
        items.AddRange((await db.CompanyEvents.AsNoTracking().Where(e => e.StartDate <= last && e.EndDate >= first && (e.OrgUnitId == null || e.OrgUnitId == unit)).ToListAsync(ct))
            .Select(e => new CalendarItem("event", e.StartDate, e.EndDate, e.Title, e.Location, e.Id)));
        items.AddRange((await db.Holidays.AsNoTracking().Where(h => h.Date >= first && h.Date <= last).ToListAsync(ct)).Select(h => new CalendarItem("holiday", h.Date, h.Date, h.Name, null, null)));
        var people = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).Select(e => new { e.FullName, e.BirthDate, e.HireDate }).ToListAsync(ct);
        foreach (var p in people)
        {
            if (p.BirthDate is { } b && b.Month == month && b.Day <= DateTime.DaysInMonth(year, month)) { var d = new DateOnly(year, month, b.Day); items.Add(new CalendarItem("birthday", d, d, p.FullName, null, null)); }
            if (p.HireDate.Month == month && p.HireDate.Year < year && p.HireDate.Day <= DateTime.DaysInMonth(year, month)) { var d = new DateOnly(year, month, p.HireDate.Day); items.Add(new CalendarItem("anniversary", d, d, p.FullName, $"{year - p.HireDate.Year}", null)); }
        }
        if (unit is { } u)
        {
            var leave = await (from l in db.LeaveRequests.AsNoTracking() join e in db.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                               where l.Status == LeaveStatus.Approved && l.StartDate <= last && l.EndDate >= first && e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u)
                               select new { e.FullName, l.StartDate, l.EndDate }).ToListAsync(ct);
            items.AddRange(leave.Select(l => new CalendarItem("leave", l.StartDate, l.EndDate, l.FullName, null, null)));
        }
        return [.. items.OrderBy(i => i.Date).ThenBy(i => i.Kind)];
    }

    // ============================================================ employee directory
    public async Task<PagedResult<DirectoryEntry>> DirectoryAsync(string? q, Guid? orgUnitId, Guid? branchId, int page, int pageSize, SessionPrincipal actor, CancellationToken ct = default)
    {
        // colleagues may look each other up; anyone else needs the employee permission
        if (!await db.Employees.AnyAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) && !await HasAsync(actor, Permissions.EmployeesRead, ct)) throw DomainException.Invariant("Your account is not linked to an employee record. Ask HR to link it.");
        var query = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToLower(); query = query.Where(e => e.FullName.ToLower().Contains(term) || e.EmployeeNo.ToLower().Contains(term) || e.Employments.Any(m => m.EffectiveTo == null && m.JobTitle.ToLower().Contains(term))); }
        if (orgUnitId is { } u) query = query.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u));
        if (branchId is { } b) query = query.Where(e => e.BranchId == b);
        var size = Math.Clamp(pageSize, 1, 100); var pg = Math.Max(1, page); var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(e => e.FullName).Skip((pg - 1) * size).Take(size)
            .Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.WorkEmail, e.BranchId, Job = e.Employments.Where(m => m.EffectiveTo == null).Select(m => new { m.JobTitle, m.OrgUnitId, m.ManagerId }).FirstOrDefault(), HasPhoto = db.HrPhotos.Any(p => p.Kind == "employee" && p.EmployeeId == e.Id) }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct); var branches = await db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var mgrIds = rows.Where(r => r.Job?.ManagerId != null).Select(r => r.Job!.ManagerId!.Value).Distinct().ToList();
        var mgrs = await db.Employees.AsNoTracking().Where(e => mgrIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        return new PagedResult<DirectoryEntry>([.. rows.Select(r => new DirectoryEntry(r.Id, r.EmployeeNo, r.FullName, r.Job?.JobTitle, r.Job?.OrgUnitId is { } ou ? units.GetValueOrDefault(ou) : null,
            r.BranchId is { } br ? branches.GetValueOrDefault(br) : null, r.Job?.ManagerId is { } mg ? mgrs.GetValueOrDefault(mg) : null, r.WorkEmail, r.HasPhoto))], pg, size, total);
    }

    // ============================================================ branches and business units
    public async Task<IReadOnlyList<BranchView>> BranchesAsync(CancellationToken ct = default)
    {
        var counts = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active && e.BranchId != null).GroupBy(e => e.BranchId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var cos = await db.LegalEntities.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return [.. (await db.Branches.AsNoTracking().OrderBy(b => b.Archived).ThenBy(b => b.Name).ToListAsync(ct)).Select(b => new BranchView(b.Id, b.LegalEntityId, cos.GetValueOrDefault(b.LegalEntityId) ?? "", b.Code, b.Name, b.Kind, b.Address, b.Archived, counts.GetValueOrDefault(b.Id)))];
    }

    public async Task<BranchView> SaveBranchAsync(Guid? id, BranchRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var b = id is { } bid ? await db.Branches.FirstOrDefaultAsync(x => x.Id == bid, ct) ?? throw DomainException.NotFound("Branch") : new Branch { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var entity = r.LegalEntityId ?? (id is null ? await db.LegalEntities.Where(x => x.IsDefault).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) : b.LegalEntityId);
        if (entity is null || !await db.LegalEntities.AnyAsync(x => x.Id == entity, ct)) errors["legalEntityId"] = "Choose a company.";
        else if (id is not null && entity != b.LegalEntityId && await db.Employees.AnyAsync(e => e.BranchId == b.Id, ct)) errors["legalEntityId"] = "Move the employees out first: a branch with people cannot change company.";
        var code = (r.Code ?? b.Code).Trim().ToUpperInvariant(); if (!Regex.IsMatch(code, "^[A-Z0-9-]{1,20}$")) errors["code"] = "Use 1–20 capital letters, digits or dashes.";
        var name = (r.Name ?? b.Name).Trim(); if (name.Length is < 1 or > 120) errors["name"] = "Enter a name (up to 120 characters).";
        var kind = (r.Kind ?? b.Kind).Trim().ToLowerInvariant(); if (kind is not ("branch" or "unit")) errors["kind"] = "Choose branch or business unit.";
        if (errors.Count == 0 && await db.Branches.AnyAsync(x => x.Code == code && x.Id != b.Id, ct)) errors["code"] = "Another branch already uses this code.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the branch.", errors);
        b.LegalEntityId = entity!.Value; b.Code = code; b.Name = name; b.Kind = kind; b.Address = r.Address is null ? b.Address : r.Address.Trim() is { Length: > 0 } a ? a[..Math.Min(a.Length, 300)] : null; b.Archived = r.Archived ?? b.Archived;
        if (id is null) db.Branches.Add(b);
        audit.Record(id is null ? "hr.branch.create" : "hr.branch.update", "branch", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code });
        await db.SaveChangesAsync(ct);
        return (await BranchesAsync(ct)).First(x => x.Id == b.Id);
    }

    public async Task<SetBranchRequest> EmployeeBranchAsync(Guid employeeId, CancellationToken ct = default)
        => new(await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => new { e.BranchId }).FirstOrDefaultAsync(ct) is { } x ? x.BranchId : throw DomainException.NotFound("Employee"));

    public async Task SetEmployeeBranchAsync(Guid employeeId, SetBranchRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.Employees.FirstOrDefaultAsync(x => x.Id == employeeId, ct) ?? throw DomainException.NotFound("Employee");
        if (r.BranchId is { } bid)
        {
            var b = await db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == bid && !x.Archived, ct) ?? throw DomainException.Validation("Please check the branch.", new Dictionary<string, string> { ["branchId"] = "Choose an active branch." });
            if (b.LegalEntityId != e.LegalEntityId) throw DomainException.Validation("Please check the branch.", new Dictionary<string, string> { ["branchId"] = "That branch belongs to another company." });
        }
        e.BranchId = r.BranchId;
        audit.Record("hr.employee.branch", "employee", employeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["branch"] = r.BranchId?.ToString() ?? "none" });
        await db.SaveChangesAsync(ct);
    }
}
