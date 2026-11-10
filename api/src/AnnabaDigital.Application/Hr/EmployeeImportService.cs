using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

/// <summary>
/// EMP-006. Loads employees from a CSV for the move from spreadsheets. It is all-or-nothing: every row is checked first and, if even one has a problem, nothing is written
/// and each problem is reported with its line number. A dry run stops after the check. Managers are named by employee number and may appear later in the same file.
/// Protected columns (national_id, tax_id, bank_name, bank_account) are accepted only from someone who may enter them and recently re-entered their password.
/// </summary>
public sealed class EmployeeImportService(IAppDb db, IClock clock, EmployeeService employees, AuthorizationService authz, AuditWriter audit, LegalEntityService entities)
{
    public const int MaxRows = 1000;
    public const int MaxChars = 1_000_000;
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));

    private sealed class Row
    {
        public int Line; public Employee Emp = null!; public EmployeeEmployment Job = null!; public string? ManagerNo; public string? Error;
    }

    public async Task<ImportEmployeesResult> ImportAsync(ImportEmployeesRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var csv = req.Csv ?? "";
        if (csv.Length > MaxChars) throw DomainException.Validation("The file is too large.", new Dictionary<string, string> { ["csv"] = $"Use at most {MaxChars / 1000} KB." });
        var table = UserImportService.ParseCsv(csv);
        if (table.Count == 0) throw DomainException.Validation("The file is empty.", new Dictionary<string, string> { ["csv"] = "Add a header row and at least one employee." });
        var header = table[0].Select(h => new string(h.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        int cNo = Col("employeeno", "employeenumber", "nik karyawan", "nip"), cName = Col("fullname", "name"), cHire = Col("hiredate", "joindate"), cTitle = Col("jobtitle", "title", "position"),
            cType = Col("type", "employmenttype"), cEnd = Col("contractend", "contractenddate"), cUnit = Col("department", "orgunit"), cMgr = Col("managerno", "manageremployeeno", "manager"),
            cMail = Col("workemail", "email"), cPhone = Col("phone"), cBirth = Col("birthdate", "dateofbirth"), cGender = Col("gender"), cMarital = Col("maritalstatus"), cGrade = Col("grade"), cLoc = Col("location"),
            cNik = Col("nationalid", "nik"), cTax = Col("taxid", "npwp"), cBank = Col("bankname"), cAcct = Col("bankaccount", "accountno"), cCompany = Col("company", "legalentity", "entity"), cKes = Col("bpjskesno", "bpjskesehatan"), cTk = Col("bpjstkno", "bpjsketenagakerjaan"), cRel = Col("religion", "agama");
        if (cName < 0 || cHire < 0 || cTitle < 0) throw DomainException.Validation("Missing columns.", new Dictionary<string, string> { ["csv"] = "The first row must contain at least full_name, hire_date and job_title." });
        var data = table.Skip(1).Select((r, i) => (Line: i + 2, Row: r)).Where(x => x.Row.Any(c => c.Trim().Length > 0)).ToList();
        if (data.Count == 0) throw DomainException.Validation("No employees found.", new Dictionary<string, string> { ["csv"] = "Add at least one row under the header." });
        if (data.Count > MaxRows) throw DomainException.Validation("Too many rows.", new Dictionary<string, string> { ["csv"] = $"Import at most {MaxRows} employees at a time." });

        string? Cell(List<string> r, int c) => c >= 0 && c < r.Count && r[c].Trim().Length > 0 ? r[c].Trim() : null;
        var hasProtected = new[] { cNik, cTax, cBank, cAcct, cKes, cTk }.Any(c => c >= 0) && data.Any(x => new[] { cNik, cTax, cBank, cAcct, cKes, cTk }.Any(c => Cell(x.Row, c) is not null));
        if (hasProtected) await employees.RequireSensitiveWriteAsync(actor, ct); // permission + recent password check, once, up front

        var units = await db.OrgUnits.AsNoTracking().Where(u => u.ArchivedAt == null).ToListAsync(ct);
        var defaultEntity = await entities.DefaultIdAsync(ct); var companies = await db.LegalEntities.AsNoTracking().Where(x => x.ArchivedAt == null).ToListAsync(ct);
        var existingNos = (await db.Employees.AsNoTracking().Select(e => new { e.EmployeeNo, e.Id, e.Status }).ToListAsync(ct)).ToDictionary(e => e.EmployeeNo, e => e, StringComparer.OrdinalIgnoreCase);
        var nextNo = existingNos.Keys.Where(k => k.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase)).Select(k => int.TryParse(k[4..], out var v) ? v : 0).DefaultIfEmpty(0).Max();
        var now = clock.UtcNow; var seenNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var rows = new List<Row>();

        foreach (var (line, r) in data)
        {
            var errors = new List<string>(); var row = new Row { Line = line };
            var name = EmployeeService.Clean(Cell(r, cName), 200); if (name is null || name.Length < 2) errors.Add("Full name is required.");
            var hire = Date(Cell(r, cHire)); if (hire is null) errors.Add("Hire date is missing or not a date (use yyyy-mm-dd or dd/mm/yyyy)."); else if (hire > Today.AddYears(1)) errors.Add("Hire date is too far ahead.");
            var title = EmployeeService.Clean(Cell(r, cTitle), 150); if (title is null) errors.Add("Job title is required.");
            var type = EmploymentType.Permanent; var typeErr = new Dictionary<string, string>(); if (Cell(r, cType) is { } ts) type = EmployeeService.ParseEnum<EmploymentType>(ts, "type", typeErr) ?? EmploymentType.Permanent; errors.AddRange(typeErr.Values);
            var end = Date(Cell(r, cEnd)); if (Cell(r, cEnd) is not null && end is null) errors.Add("Contract end is not a date.");
            if (type != EmploymentType.Permanent && end is null && !typeErr.Any()) errors.Add("A fixed-term position needs a contract end date.");
            if (end is { } ce && hire is { } hd && ce < hd) errors.Add("Contract end is before the hire date.");
            var gErr = new Dictionary<string, string>(); var gender = EmployeeService.ParseEnum<Gender>(Cell(r, cGender), "gender", gErr); var mErr = new Dictionary<string, string>(); var marital = EmployeeService.ParseEnum<MaritalStatus>(Cell(r, cMarital), "maritalStatus", mErr);
            errors.AddRange(gErr.Values); errors.AddRange(mErr.Values);
            var rErr = new Dictionary<string, string>(); var religion = EmployeeService.ParseEnum<Religion>(Cell(r, cRel), "religion", rErr); errors.AddRange(rErr.Values);
            var eErr = new Dictionary<string, string>(); var mail = EmployeeService.Email(Cell(r, cMail), eErr); errors.AddRange(eErr.Values);
            var birth = Date(Cell(r, cBirth)); if (Cell(r, cBirth) is not null && (birth is null || birth > Today.AddYears(-15) || birth < Today.AddYears(-100))) errors.Add("Birth date is not valid.");
            Guid? unitId = null; if (Cell(r, cUnit) is { } un) { var u = units.FirstOrDefault(x => x.Code.Equals(un, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(un, StringComparison.OrdinalIgnoreCase)); if (u is null) errors.Add($"Department '{un}' does not exist."); else unitId = u.Id; }

            var entityId = defaultEntity; if (Cell(r, cCompany) is { } cn) { var co = companies.FirstOrDefault(x => x.Name.Equals(cn, StringComparison.OrdinalIgnoreCase)); if (co is null) errors.Add($"Company '{cn}' does not exist."); else entityId = co.Id; }

            var no = EmployeeService.Clean(Cell(r, cNo), 30)?.ToUpperInvariant() ?? $"EMP-{++nextNo:D4}";
            if (existingNos.ContainsKey(no)) errors.Add($"Employee number '{no}' already exists.");
            else if (!seenNos.Add(no)) errors.Add($"Employee number '{no}' appears twice in this file.");

            var emp = new Employee
            {
                EmployeeNo = no, FullName = name ?? "", WorkEmail = mail, Phone = EmployeeService.Clean(Cell(r, cPhone), 40), BirthDate = birth, Gender = gender, MaritalStatus = marital,
                BankName = EmployeeService.Clean(Cell(r, cBank), 80), HireDate = hire ?? default, LegalEntityId = entityId, Religion = religion, CreatedAt = now, UpdatedAt = now,
            };
            emp.NationalIdEnc = employees.Seal(emp, "national_id", Cell(r, cNik)); emp.TaxIdEnc = employees.Seal(emp, "tax_id", Cell(r, cTax)); emp.BankAccountEnc = employees.Seal(emp, "bank_account", Cell(r, cAcct));
            emp.BpjsKesNoEnc = employees.Seal(emp, "bpjs_kes_no", Cell(r, cKes)); emp.BpjsTkNoEnc = employees.Seal(emp, "bpjs_tk_no", Cell(r, cTk));
            row.Emp = emp; row.ManagerNo = Cell(r, cMgr)?.ToUpperInvariant();
            row.Job = new EmployeeEmployment { EmployeeId = emp.Id, EffectiveFrom = hire ?? default, JobTitle = title ?? "", Grade = EmployeeService.Clean(Cell(r, cGrade), 30), Location = EmployeeService.Clean(Cell(r, cLoc), 100), Type = type, ContractEnd = type == EmploymentType.Permanent ? null : end, OrgUnitId = unitId, ChangeReason = "Imported", CreatedAt = now };
            row.Error = errors.Count == 0 ? null : string.Join(" ", errors);
            rows.Add(row);
        }

        // Managers: by employee number, either already in the system (and active) or elsewhere in this file; no loops, no self-management.
        var inFile = rows.ToDictionary(r => r.Emp.EmployeeNo, r => r, StringComparer.OrdinalIgnoreCase);
        var existingMgr = (await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null && m.ManagerId != null).Select(m => new { m.EmployeeId, m.ManagerId }).ToListAsync(ct)).ToDictionary(m => m.EmployeeId, m => m.ManagerId);
        var idByNo = existingNos.Where(e => e.Value.Status == EmployeeStatus.Active).ToDictionary(e => e.Key, e => e.Value.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows.Where(x => x.ManagerNo is not null))
        {
            if (inFile.TryGetValue(r.ManagerNo!, out var m)) { if (m == r) Fail(r, "An employee cannot be their own manager."); else r.Job.ManagerId = m.Emp.Id; }
            else if (idByNo.TryGetValue(r.ManagerNo!, out var id)) r.Job.ManagerId = id;
            else Fail(r, $"Manager '{r.ManagerNo}' was not found among active employees or in this file.");
        }
        foreach (var r in rows.Where(x => x.Job.ManagerId is not null && x.Error is null))
        {
            var byId = rows.ToDictionary(x => x.Emp.Id); var steps = 0;
            for (var cur = r.Job.ManagerId; cur is { } c && steps++ < 2000; cur = byId.TryGetValue(c, out var next) ? next.Job.ManagerId : existingMgr.GetValueOrDefault(c))
                if (c == r.Emp.Id) { Fail(r, "The manager column creates a reporting loop."); break; }
        }

        var failed = rows.Count(r => r.Error is not null);
        var report = rows.Select(r => new ImportEmployeeRow(r.Line, r.Emp.EmployeeNo, r.Emp.FullName, r.Error is null ? "ok" : "error", r.Error)).ToList();
        if (req.DryRun || failed > 0) return new ImportEmployeesResult(req.DryRun, rows.Count, 0, failed, report);

        await db.InTransactionAsync(async () =>
        {
            foreach (var r in rows) { r.Emp.Employments.Add(r.Job); db.Employees.Add(r.Emp); }
            audit.Record("hr.employee.import", "employee", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = rows.Count.ToString(), ["protectedColumns"] = hasProtected.ToString() });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return new ImportEmployeesResult(false, rows.Count, rows.Count, 0, report);

        static void Fail(Row r, string msg) => r.Error = r.Error is null ? msg : r.Error + " " + msg;
    }

    private static DateOnly? Date(string? s)
    {
        if (s is null) return null;
        return DateOnly.TryParseExact(s, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
}
