using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Infrastructure.Email;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>status: ok, warn (works, but fix before or soon after go-live), fail (do not go live), manual (the system cannot check this; a person must confirm it).</summary>
public sealed record ReadinessCheck(string Key, string Area, string Title, string Status, string Detail);
public sealed record ReadinessReport(DateTimeOffset CheckedAt, int Ok, int Warn, int Fail, int Manual, IReadOnlyList<ReadinessCheck> Checks);

/// <summary>
/// The go-live checklist (PRD §11) as something the system answers itself wherever it can: access hygiene, configuration of this deployment, payroll set-up and stuck queues,
/// for the caller's organisation. What only people can confirm (the tax consultant's sign-off, the penetration test, the restore drill, the legal documents) is listed as "manual"
/// so the page is the whole list, not half of it. Needs settings.manage.
/// </summary>
public static class ReadinessEndpoints
{
    private static readonly string[] Privileged = [Permissions.SettingsManage, Permissions.UsersAssignRole, Permissions.PermissionsAssign, Permissions.EmployeesReadSensitive, Permissions.PayrollConfigure, Permissions.PayrollRun, Permissions.PayrollApprove, Permissions.PayrollLock, Permissions.LeaveManage, Permissions.AttendanceManage, Permissions.ClaimsManage];

    public static void MapReadiness(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/hr/readiness", async (IAppDb db, IClock clock, SettingsProvider settings, IFileScanner scanner, IOptions<EmailOptions> email, IOptions<ApiOptions> api, CancellationToken ct) =>
        {
            var checks = new List<ReadinessCheck>(); var s = await settings.GetAsync(ct); var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
            void Add(string key, string area, string title, string status, string detail) => checks.Add(new ReadinessCheck(key, area, title, status, detail));
            IQueryable<User> Holding(params string[] perms) => db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.Role!.ArchivedAt == null && r.Role.Permissions.Any(p => perms.Contains(p.PermissionKey))));

            // ---- access
            var admins = await db.Users.AsNoTracking().CountAsync(u => u.Status == UserStatus.Active && u.UserRoles.Any(r => r.Role!.Key == SystemRoles.SuperAdmin), ct);
            Add("super_admins", "Access", "At least two super admins", admins >= 2 ? "ok" : "fail", admins >= 2 ? $"{admins} active super admins." : $"Only {admins}: if that person is unavailable nobody can administer the system. Add a second one.");
            var noMfa = await Holding(Privileged).Where(u => !db.MfaFactors.Any(f => f.UserId == u.Id && f.ConfirmedAt != null)).Select(u => u.Email).OrderBy(e => e).Take(50).ToListAsync(ct);
            Add("privileged_mfa", "Access", "Two-step verification for everyone with HR, payroll or admin rights", noMfa.Count == 0 ? "ok" : s.MfaPolicy == "required" ? "warn" : "fail",
                noMfa.Count == 0 ? "Everyone with sensitive permissions has two-step verification." : $"{noMfa.Count} privileged account(s) without it: {string.Join(", ", noMfa.Take(5))}{(noMfa.Count > 5 ? "…" : "")}." + (s.MfaPolicy == "required" ? " The policy will force them to enrol at their next sign-in." : " Set the policy to required, or have them enrol."));
            Add("mfa_policy", "Access", "Two-step verification is required by policy", s.MfaPolicy == "required" ? "ok" : "warn", $"Current policy: {s.MfaPolicy}.");
            var makers = await Holding(Permissions.PayrollRun).Select(u => u.Id).ToListAsync(ct); var checkers = await Holding(Permissions.PayrollApprove).Where(u => u.UserRoles.Any(r => r.Role!.Permissions.Any(p => p.PermissionKey == Permissions.PayrollLock))).Select(u => u.Id).ToListAsync(ct);
            var twoPeople = makers.Any(m => checkers.Any(c => c != m));
            Add("maker_checker", "Access", "Two different people can prepare and approve a pay run", twoPeople ? "ok" : "fail", twoPeople ? $"{makers.Count} can prepare, {checkers.Count} can approve and lock." : "Payroll needs one person who calculates and a different person who approves and locks. Assign the Payroll Officer and Payroll Approver roles to two people.");

            // ---- this deployment
            var https = api.Value.Origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            Add("https", "Deployment", "The application is served over HTTPS", https ? "ok" : "fail", https ? api.Value.Origin : $"Origin is {api.Value.Origin}: cookies are not marked Secure.");
            Add("proxy_key", "Deployment", "The API only answers to the web application", string.IsNullOrEmpty(api.Value.ProxyKey) ? "fail" : "ok", string.IsNullOrEmpty(api.Value.ProxyKey) ? "Api:ProxyKey is not set: the API can be called directly." : "The proxy key is set.");
            var console = string.Equals(email.Value.Provider, "console", StringComparison.OrdinalIgnoreCase);
            Add("email", "Deployment", "E-mail is really sent", console ? "fail" : "ok", console ? "Email:Provider is 'console': invitations, password resets and payslip notices are only written to the log." : $"Provider: {email.Value.Provider}.");
            Add("antivirus", "Deployment", "Uploaded files are virus-scanned", scanner.Enabled ? "ok" : "warn", scanner.Enabled ? "Every upload goes through the scanner; a file that cannot be scanned is refused." : "Antivirus:Enabled is off: documents and receipts are only checked for their type. Point it at a ClamAV daemon.");
            var metricsOn = api.Value.MetricsToken is { Length: >= 24 };
            Add("monitoring", "Deployment", "Metrics can be scraped for monitoring and alerts", metricsOn ? "ok" : "warn", metricsOn ? "GET /health/metrics is enabled; alert rules are in ops/prometheus-alerts.yml." : "Api:MetricsToken is not set, so there is nothing for the monitoring system to read.");

            // ---- HR and payroll set-up
            var active = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active); var headcount = await active.CountAsync(ct);
            var noComp = await active.CountAsync(e => !db.Compensations.Any(c => c.EmployeeId == e.Id && c.EffectiveTo == null), ct);
            Add("compensation", "Payroll", "Every active employee has a compensation record", headcount == 0 ? "warn" : noComp == 0 ? "ok" : "warn", headcount == 0 ? "No employees yet." : noComp == 0 ? $"All {headcount} employees." : $"{noComp} of {headcount} employees have none and will be left out of pay runs.");
            var noBank = await active.CountAsync(e => e.BankAccountEnc == null, ct);
            Add("bank_accounts", "Payroll", "Every active employee has a bank account", noBank == 0 ? "ok" : "warn", noBank == 0 ? "All set." : $"{noBank} employee(s) have no bank account and will be missing from the bank file.");
            var noLogin = await active.CountAsync(e => e.UserId == null, ct);
            Add("linked_accounts", "HR", "Employees can sign in to self-service", noLogin == 0 ? "ok" : "warn", noLogin == 0 ? "Every employee is linked to a sign-in account." : $"{noLogin} employee(s) have no sign-in account: no leave requests, clock-in or payslips for them.");
            var noNpwp = await db.LegalEntities.AsNoTracking().Where(c => c.ArchivedAt == null && c.TaxId == null).Select(c => c.Name).ToListAsync(ct);
            Add("company_tax_id", "Payroll", "Every company has its tax number (NPWP)", noNpwp.Count == 0 ? "ok" : "warn", noNpwp.Count == 0 ? "All set." : $"Missing for: {string.Join(", ", noNpwp)}. It is printed on payslips and the annual tax file.");
            var holidays = await db.Holidays.AsNoTracking().CountAsync(h => h.Date.Year == today.Year, ct);
            Add("holidays", "HR", $"Public holidays for {today.Year} are entered", holidays > 0 ? "ok" : "warn", holidays > 0 ? $"{holidays} day(s)." : "None: leave is counted and overtime is paid as if there were no holidays.");
            var ruleVersions = await db.PayrollRuleVersions.AsNoTracking().CountAsync(ct);
            Add("payroll_rules", "Payroll", "Tax and BPJS rules were confirmed by a tax consultant", "manual", ruleVersions == 0 ? "The built-in rules are in use and have not been changed. They were written from public regulations and are NOT validated: have a consultant confirm them (Payroll Rules page) before the first real run." : $"{ruleVersions} rule version(s) entered by your organisation. Keep the consultant's written confirmation on file.");

            // ---- queues
            var deadHooks = await db.WebhookDeliveries.AsNoTracking().CountAsync(d => d.Status == DeliveryStatus.Dead, ct); var failedMail = await db.OutboxEmails.AsNoTracking().CountAsync(m => m.FailedAt != null, ct);
            Add("queues", "Operations", "No e-mails or webhook deliveries were given up on", deadHooks + failedMail == 0 ? "ok" : "warn", deadHooks + failedMail == 0 ? "Nothing failed for good." : $"{failedMail} e-mail(s) and {deadHooks} webhook delivery(ies) failed for good.");

            // ---- what only people can confirm
            Add("parallel_run", "Payroll", "Two parallel payroll cycles matched the old system", "manual", "See docs/27-hr-cutover-plan.md.");
            Add("pentest", "Security", "External penetration test done, no high or critical findings open", "manual", "Internal threat model: docs/25-hr-threat-model.md.");
            Add("backup", "Operations", "Backups run and a restore was rehearsed on the production infrastructure", "manual", "scripts/restore-drill.sh; target RPO 15 minutes.");
            Add("privacy", "Legal", "Privacy documents approved and a data-protection contact appointed (UU PDP)", "manual", "Worksheet: docs/26-hr-pdp-compliance.md.");
            Add("training", "People", "HR, payroll, managers and employees were trained", "manual", "User guide: docs/30-hr-user-guide.md.");

            return TypedResults.Ok(new ReadinessReport(clock.UtcNow, checks.Count(c => c.Status == "ok"), checks.Count(c => c.Status == "warn"), checks.Count(c => c.Status == "fail"), checks.Count(c => c.Status == "manual"), checks));
        }).RequirePermission(Permissions.SettingsManage);
    }
}
