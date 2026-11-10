using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record LetterView(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string Purpose, string Status, string? Number, string? DecisionNote, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, bool CanDecide, bool CanCancel);
public sealed record RequestLetterRequest(string? Purpose);

/// <summary>
/// ESS-003. Proof of employment ("surat keterangan kerja"): an employee asks for one and says what it is for (a bank, a visa, a landlord), HR approves, and the letter becomes a numbered PDF
/// the employee can download. The facts on it (name, job, department, hire date, company) are frozen at approval, so a letter never changes after it was issued. It states no salary.
/// </summary>
public sealed class LetterService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, NotificationService notes)
{
    public const int MaxPending = 3;
    private sealed record Facts(string Name, string EmployeeNo, string JobTitle, string? Department, string Type, DateOnly HireDate, string Company, string? CompanyTaxId, string? CompanyAddress, string Signer, DateOnly IssuedOn);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Employees.FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    private async Task<IReadOnlyList<LetterView>> ViewsAsync(IReadOnlyList<EmploymentLetter> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var ids = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var emps = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeNo, e.FullName, e.UserId }).ToDictionaryAsync(e => e.Id, ct);
        var canManage = (await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesUpdate);
        return [.. rows.Select(l => { var e = emps[l.EmployeeId]; var own = e.UserId == actor.UserId;
            return new LetterView(l.Id, l.EmployeeId, e.EmployeeNo, e.FullName, l.Purpose, l.Status.ToString().ToLowerInvariant(), l.Number, l.DecisionNote, l.CreatedAt, l.DecidedAt, l.Status == RequestStatus.Pending && !own && canManage, own && l.Status == RequestStatus.Pending); })];
    }

    public async Task<IReadOnlyList<LetterView>> ListAsync(bool mine, string? status, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.EmploymentLetters.AsNoTracking().AsQueryable();
        if (mine) { var me = await MeAsync(actor, ct); q = q.Where(l => l.EmployeeId == me.Id); }
        else if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesUpdate)) throw DomainException.Denied(Permissions.EmployeesUpdate);
        if (!string.IsNullOrWhiteSpace(status) && status != "all") { if (!Enum.TryParse<RequestStatus>(status, true, out var st)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected or cancelled." }); q = q.Where(l => l.Status == st); }
        return await ViewsAsync(await q.OrderByDescending(l => l.Status == RequestStatus.Pending).ThenByDescending(l => l.CreatedAt).Take(200).ToListAsync(ct), actor, ct);
    }

    public async Task<LetterView> RequestAsync(RequestLetterRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct); var purpose = r.Purpose?.Trim();
        if (string.IsNullOrEmpty(purpose) || purpose.Length is < 3 or > 200) throw DomainException.Validation("Please check the request.", new Dictionary<string, string> { ["purpose"] = "Say what the letter is for (3–200 characters)." });
        if (await db.EmploymentLetters.CountAsync(l => l.EmployeeId == me.Id && l.Status == RequestStatus.Pending, ct) >= MaxPending) throw DomainException.Invariant($"You already have {MaxPending} letter requests waiting.");
        var letter = new EmploymentLetter { EmployeeId = me.Id, Purpose = purpose, CreatedAt = clock.UtcNow };
        db.EmploymentLetters.Add(letter);
        audit.Record("hr.letter.request", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        foreach (var uid in await notes.UsersWithPermissionAsync(Permissions.EmployeesUpdate, ct)) if (uid != actor.UserId) await notes.NotifyOnceAsync(uid, "letter.requested", "An employment letter was requested", me.FullName, "/hr/approvals", ct);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([letter], actor, ct))[0];
    }

    public async Task<LetterView> DecideAsync(Guid id, bool approve, string? note, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesUpdate)) throw DomainException.Denied(Permissions.EmployeesUpdate);
        var reason = note?.Trim(); if (reason is { Length: > 300 }) throw DomainException.Validation("Note is too long.", new Dictionary<string, string> { ["note"] = "Keep it under 300 characters." });
        if (!approve && string.IsNullOrEmpty(reason)) throw DomainException.Validation("Please give a reason.", new Dictionary<string, string> { ["note"] = "Tell the employee why." });
        return await db.InTransactionAsync(async () =>
        {
            var letter = await db.EmploymentLetters.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw DomainException.NotFound("Letter");
            var emp = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == letter.EmployeeId, ct);
            if (emp.UserId == actor.UserId) throw DomainException.Invariant("You cannot decide your own request.");
            if (letter.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {letter.Status.ToString().ToLowerInvariant()}.");
            if (approve)
            {
                if (emp.Status != EmployeeStatus.Active) throw DomainException.Invariant("This employee's employment has ended.");
                var job = await db.Employments.AsNoTracking().Where(m => m.EmployeeId == emp.Id && m.EffectiveTo == null).FirstOrDefaultAsync(ct) ?? throw DomainException.Invariant("This employee has no current job record.");
                var dept = job.OrgUnitId is { } ou ? await db.OrgUnits.AsNoTracking().Where(u => u.Id == ou).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
                var company = await db.LegalEntities.AsNoTracking().FirstAsync(c => c.Id == emp.LegalEntityId, ct);
                // Number: SKK/<year>/<running number>, unique per organisation and year (the row lock on the company makes two approvals at once take turns).
                await db.ExecuteSqlAsync($"SELECT id FROM hr_legal_entities WHERE id = {company.Id} FOR UPDATE", ct);
                var year = Today.Year; var prefix = $"SKK/{year}/";
                var last = await db.EmploymentLetters.AsNoTracking().Where(l => l.Number != null && l.Number.StartsWith(prefix)).Select(l => l.Number!).ToListAsync(ct);
                var next = last.Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0).DefaultIfEmpty(0).Max() + 1;
                letter.Number = $"{prefix}{next:D4}";
                letter.SnapshotJson = JsonSerializer.Serialize(new Facts(emp.FullName, emp.EmployeeNo, job.JobTitle, dept, job.Type.ToString(), emp.HireDate, company.Name, company.TaxId, company.Address, actor.Name, Today));
            }
            letter.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected; letter.DecidedBy = actor.UserId; letter.DecidedAt = clock.UtcNow; letter.DecisionNote = reason;
            audit.Record(approve ? "hr.letter.approve" : "hr.letter.reject", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["employeeNo"] = emp.EmployeeNo, ["number"] = letter.Number ?? "" });
            notes.Notify(emp.UserId, "letter.decided", approve ? "Your employment letter is ready" : "Your employment letter was not issued", approve ? letter.Number : reason, "/hr/my-letters");
            await db.SaveChangesAsync(ct);
            return (await ViewsAsync([letter], actor, ct))[0];
        }, ct);
    }

    public async Task<LetterView> CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var letter = await db.EmploymentLetters.FirstOrDefaultAsync(l => l.Id == id && l.EmployeeId == me.Id, ct) ?? throw DomainException.NotFound("Letter");
        if (letter.Status != RequestStatus.Pending) throw DomainException.Conflict($"This request is already {letter.Status.ToString().ToLowerInvariant()}.");
        letter.Status = RequestStatus.Cancelled;
        audit.Record("hr.letter.cancel", "employee", me.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([letter], actor, ct))[0];
    }

    /// <summary>The issued letter as a PDF, for the employee it is about and for HR.</summary>
    public async Task<ExportFile> PdfAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var letter = await db.EmploymentLetters.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id && l.Status == RequestStatus.Approved, ct) ?? throw DomainException.NotFound("Letter");
        var owner = await db.Employees.AsNoTracking().Where(e => e.Id == letter.EmployeeId).Select(e => e.UserId).FirstAsync(ct);
        if (owner != actor.UserId && !(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesUpdate)) throw DomainException.NotFound("Letter");
        var f = JsonSerializer.Deserialize<Facts>(letter.SnapshotJson!)!; var id_ = new CultureInfo("id-ID");
        string Type(string t) => t switch { "Permanent" => "karyawan tetap (PKWTT)", "Contract" => "karyawan kontrak (PKWT)", "Probation" => "karyawan dalam masa percobaan", "Intern" => "peserta magang", _ => "karyawan" };
        var pdf = new SimplePdf(); double y = 70;
        pdf.Text(f.Company, 60, y, 15, true); y += 16;
        if (!string.IsNullOrEmpty(f.CompanyAddress)) { foreach (var line in Wrap(f.CompanyAddress, 95)) { pdf.Text(line, 60, y, 9); y += 11; } }
        if (!string.IsNullOrEmpty(f.CompanyTaxId)) { pdf.Text($"NPWP {f.CompanyTaxId}", 60, y, 9); y += 11; }
        y += 4; pdf.Line(60, y, 535, 1); y += 34;
        pdf.Text("SURAT KETERANGAN KERJA", 205, y, 13, true); y += 15; pdf.Text($"Nomor: {letter.Number}", 235, y, 10); y += 34;
        pdf.Text("Yang bertanda tangan di bawah ini menerangkan bahwa:", 60, y, 11); y += 24;
        void Row(string k, string v) { pdf.Text(k, 80, y, 11); pdf.Text(": " + v, 210, y, 11); y += 17; }
        Row("Nama", f.Name); Row("Nomor karyawan", f.EmployeeNo); Row("Jabatan", f.JobTitle); if (!string.IsNullOrEmpty(f.Department)) Row("Departemen", f.Department!); Row("Bekerja sejak", f.HireDate.ToString("d MMMM yyyy", id_));
        y += 10;
        foreach (var line in Wrap($"adalah benar {Type(f.Type)} pada {f.Company} dan sampai dengan tanggal surat ini diterbitkan masih aktif bekerja.", 88)) { pdf.Text(line, 60, y, 11); y += 16; }
        y += 8;
        foreach (var line in Wrap($"Surat keterangan ini dibuat atas permintaan yang bersangkutan untuk keperluan: {letter.Purpose}.", 88)) { pdf.Text(line, 60, y, 11); y += 16; }
        y += 8; pdf.Text("Demikian surat keterangan ini dibuat untuk dipergunakan sebagaimana mestinya.", 60, y, 11); y += 50;
        pdf.Text(f.IssuedOn.ToString("d MMMM yyyy", id_), 360, y, 11); y += 16; pdf.Text(f.Company, 360, y, 11); y += 70;
        pdf.Text(f.Signer, 360, y, 11, true); y += 14; pdf.Text("Human Resources", 360, y, 10);
        pdf.Text($"Dokumen ini diterbitkan secara elektronik oleh sistem HR ({letter.Number}). Keasliannya dapat dikonfirmasi ke bagian HR perusahaan.", 60, 800, 7.5);
        audit.Record("hr.letter.download", "employee", letter.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = letter.Number ?? "" });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"surat-keterangan-kerja-{letter.Number!.Replace('/', '-')}.pdf", "application/pdf", pdf.Build());
    }

    internal static IEnumerable<string> Wrap(string text, int width)
    {
        var line = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { yield return line.ToString(); line.Clear(); }
            if (line.Length > 0) line.Append(' '); line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
