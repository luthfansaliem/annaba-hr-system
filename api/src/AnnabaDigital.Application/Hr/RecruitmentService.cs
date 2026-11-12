using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record ManpowerRow(Guid Id, int Year, Guid? OrgUnitId, string? Unit, string JobTitle, int Headcount, int Filled, int OpenRequests, int Gap, decimal MonthlyBudget, string? Note);
public sealed record ManpowerRequest(int? Year, Guid? OrgUnitId, string? JobTitle, int? Headcount, decimal? MonthlyBudget, string? Note);
public sealed record RequisitionView(Guid Id, string Number, Guid? OrgUnitId, string? Unit, string JobTitle, int Openings, int Hired, string Type, decimal? SalaryMin, decimal? SalaryMax, bool Replacement, string Justification,
    string RequestedBy, bool Mine, string Status, string? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionNote, bool OverPlan, DateTimeOffset CreatedAt);
public sealed record RequisitionRequest(Guid? OrgUnitId, string? JobTitle, int? Openings, string? Type, decimal? SalaryMin, decimal? SalaryMax, bool? Replacement, string? Justification);
public sealed record DecideRequest(bool Approve, string? Note);
public sealed record PostingView(Guid Id, string PublicId, Guid? RequisitionId, string? RequisitionNumber, string Title, Guid? OrgUnitId, string? Unit, string? Location, string Type, string Description, string? Requirements,
    decimal? SalaryMin, decimal? SalaryMax, bool ShowSalary, IReadOnlyList<string> Stages, string Status, DateOnly? ClosesOn, DateTimeOffset CreatedAt, DateTimeOffset? PublishedAt, int Applicants, int Active, IReadOnlyDictionary<string, int> ByStage);
public sealed record PostingRequest(Guid? RequisitionId, string? Title, Guid? OrgUnitId, string? Location, string? Type, string? Description, string? Requirements, decimal? SalaryMin, decimal? SalaryMax, bool? ShowSalary, IReadOnlyList<string>? Stages, DateOnly? ClosesOn);
public sealed record CareerJob(string Id, string Title, string? Department, string? Location, string Type, string Description, string? Requirements, decimal? SalaryMin, decimal? SalaryMax, DateOnly? ClosesOn, DateTimeOffset? PublishedAt);
public sealed record CareerPage(string Company, IReadOnlyList<CareerJob> Jobs);
public sealed record ApplyRequest(string? FullName, string? Email, string? Phone, string? CoverLetter, string? CvName, string? CvBase64, bool? Consent, string? Source);
public sealed record ApplicationRow(Guid Id, Guid CandidateId, string Name, string Email, string? Phone, string Source, string Stage, string Status, int? Rating, bool HasCv, DateTimeOffset AppliedAt, DateTimeOffset UpdatedAt, string? TestSummary);
public sealed record ActivityView(Guid Id, string Kind, string Text, string? By, DateTimeOffset At);
public sealed record InterviewView(Guid Id, Guid ApplicationId, string Candidate, string Posting, DateTimeOffset At, int DurationMinutes, string? Location, IReadOnlyList<Guid> InterviewerIds, IReadOnlyList<string> Interviewers,
    string Status, int? Rating, string? Recommendation, string? Feedback);
public sealed record InviteView(Guid Id, string Template, string Kind, string Status, int? Score, int? PassScore, string? Result, DateTimeOffset SentAt, DateTimeOffset ExpiresAt, DateTimeOffset? DoneAt);
public sealed record OfferView(Guid Id, string JobTitle, decimal MonthlySalary, DateOnly StartDate, DateOnly ExpiresOn, string Type, DateOnly? ContractEnd, string? Terms, string Status, DateTimeOffset? SentAt, DateTimeOffset? RespondedAt, string? ResponseNote);
public sealed record ApplicationDetail(ApplicationRow Application, Guid PostingId, string Posting, IReadOnlyList<string> Stages, string? CoverLetter, string? RejectReason, Guid? HiredEmployeeId,
    IReadOnlyList<ActivityView> Activity, IReadOnlyList<InterviewView> Interviews, IReadOnlyList<InviteView> Tests, IReadOnlyList<OfferView> Offers);
public sealed record ApplicationUpdate(string? Stage, int? Rating, string? Note, bool? Reject, string? RejectReason, bool? NotifyCandidate);
public sealed record InterviewRequest(DateTimeOffset? At, int? DurationMinutes, string? Location, IReadOnlyList<Guid>? InterviewerIds);
public sealed record FeedbackRequest(int? Rating, string? Recommendation, string? Feedback);
public sealed record AssessmentOption(string Text, bool? Correct, string? Pole);
public sealed record AssessmentQuestion(string Id, string Text, IReadOnlyList<AssessmentOption> Options);
public sealed record TemplateView(Guid Id, string Name, string Kind, IReadOnlyList<AssessmentQuestion> Questions, int? PassScore, int TimeLimitMinutes, bool Archived);
public sealed record TemplateRequest(string? Name, string? Kind, IReadOnlyList<AssessmentQuestion>? Questions, int? PassScore, int? TimeLimitMinutes, bool? Archived);
public sealed record SendTestRequest(Guid? TemplateId, int? DaysValid);
public sealed record SentLink(Guid Id, string Link);
public sealed record PublicQuestion(string Id, string Text, IReadOnlyList<string> Options);
public sealed record PublicTest(string Company, string Candidate, string Name, string Kind, int TimeLimitMinutes, DateTimeOffset? StartedAt, bool Done, IReadOnlyList<PublicQuestion> Questions);
public sealed record TestAnswers(Dictionary<string, int>? Answers);
public sealed record OfferRequest(string? JobTitle, decimal? MonthlySalary, DateOnly? StartDate, DateOnly? ExpiresOn, string? Type, DateOnly? ContractEnd, string? Terms);
public sealed record PublicOffer(string Company, string Candidate, string JobTitle, decimal MonthlySalary, DateOnly StartDate, DateOnly ExpiresOn, string Type, DateOnly? ContractEnd, string? Terms, string Status);
public sealed record OfferResponse(bool Accept, string? Note);
public sealed record HireRequest(string? EmployeeNo, Guid? OrgUnitId, Guid? ManagerId, string? WorkEmail);
public sealed record RecruitmentSummary(int OpenRequisitions, int PendingRequisitions, int OpenPostings, int ActiveApplicants, int HiredThisYear, double? AvgDaysToHire, double? OfferAcceptance, IReadOnlyDictionary<string, int> Sources);
public sealed record InterviewerTask(Guid Id, string Candidate, string Posting, DateTimeOffset At, int DurationMinutes, string? Location, string Status, int? Rating, string? Recommendation, string? Feedback, Guid ApplicationId, bool HasCv);

/// <summary>
/// Stage 17, recruitment (Talenta: manpower planning, ATS, assessments, career page). HR (hrconfig.manage) runs it; managers raise hiring requests and interview;
/// candidates use the public career page and private links for tests and offers. Candidate data is kept for recruitment only, with consent, and can be erased.
/// </summary>
public sealed partial class RecruitmentService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, ManagerScope managers, NotificationService notes, ITokenService tokens, IRateLimiter limiter,
    ITenantContext tenant, IEmailQueue email, IAppLinks links, EmployeeService employees)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Poles = ["E", "I", "S", "N", "T", "F", "J", "P"];
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
    private static string Snake(string s) => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    private static string? Clip(string? s, int max) => s?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, max)] : null;
    private async Task<bool> IsHrAsync(SessionPrincipal actor, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.HrConfigManage);
    private string Origin => links.Origin.TrimEnd('/');
    [GeneratedRegex("^[a-z][a-z0-9_]{1,29}$")] private static partial Regex StageRx();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRx();

    private void Mail(string to, string subject, string text)
    {
        var html = "<p>" + string.Join("</p><p>", text.Split("\n\n").Select(p => WebUtility.HtmlEncode(p).Replace("\n", "<br>"))) + "</p>";
        email.Enqueue(new EmailMessage(to, subject, text, html));
    }

    // ============================================================ manpower planning
    public async Task<IReadOnlyList<ManpowerRow>> ManpowerAsync(int year, CancellationToken ct = default)
    {
        var plans = await db.ManpowerPlans.AsNoTracking().Where(p => p.Year == year).OrderBy(p => p.JobTitle).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var staff = await (from m in db.Employments.AsNoTracking() join e in db.Employees.AsNoTracking() on m.EmployeeId equals e.Id where m.EffectiveTo == null && e.Status == EmployeeStatus.Active select new { m.OrgUnitId, m.JobTitle }).ToListAsync(ct);
        var reqs = await db.JobRequisitions.AsNoTracking().Where(r => r.Status == RequisitionStatus.Approved || r.Status == RequisitionStatus.Pending).Select(r => new { r.OrgUnitId, r.JobTitle, Left = r.Openings - r.Hired }).ToListAsync(ct);
        bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        return [.. plans.Select(p =>
        {
            var filled = staff.Count(s => s.OrgUnitId == p.OrgUnitId && Same(s.JobTitle, p.JobTitle)); var open = reqs.Where(r => r.OrgUnitId == p.OrgUnitId && Same(r.JobTitle, p.JobTitle)).Sum(r => r.Left);
            return new ManpowerRow(p.Id, p.Year, p.OrgUnitId, p.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, p.JobTitle, p.Headcount, filled, open, p.Headcount - filled - open, p.MonthlyBudget, p.Note);
        })];
    }

    public async Task<IReadOnlyList<ManpowerRow>> SaveManpowerAsync(Guid? id, ManpowerRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = id is { } pid ? await db.ManpowerPlans.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Plan") : new ManpowerPlan();
        var errors = new Dictionary<string, string>();
        var year = r.Year ?? (id is null ? Today.Year : p.Year); if (year < Today.Year - 1 || year > Today.Year + 3) errors["year"] = "Plan for last year up to three years ahead.";
        var title = Clip(r.JobTitle, 150) ?? p.JobTitle; if (title.Length < 2) errors["jobTitle"] = "Enter the job title.";
        var hc = r.Headcount ?? p.Headcount; if (hc is < 0 or > 10000) errors["headcount"] = "Enter 0–10,000.";
        var budget = r.MonthlyBudget ?? p.MonthlyBudget; if (budget < 0) errors["monthlyBudget"] = "The budget cannot be negative.";
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the plan.", errors);
        p.Year = year; p.JobTitle = title; p.Headcount = hc; p.MonthlyBudget = budget; p.OrgUnitId = r.OrgUnitId ?? p.OrgUnitId; p.Note = r.Note is null ? p.Note : Clip(r.Note, 300); p.UpdatedAt = clock.UtcNow;
        if (id is null) db.ManpowerPlans.Add(p);
        audit.Record(id is null ? "hr.manpower.create" : "hr.manpower.update", "manpower_plan", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["headcount"] = hc.ToString() });
        await db.SaveChangesAsync(ct);
        return await ManpowerAsync(year, ct);
    }

    public async Task DeleteManpowerAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.ManpowerPlans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Plan");
        db.ManpowerPlans.Remove(p); audit.Record("hr.manpower.delete", "manpower_plan", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }

    // ============================================================ hiring requests
    private async Task<IReadOnlyList<RequisitionView>> RequisitionViewsAsync(IReadOnlyList<JobRequisition> rows, SessionPrincipal actor, CancellationToken ct)
    {
        var users = rows.Select(r => r.RequestedBy).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var years = rows.Select(r => r.CreatedAt.Year).Distinct().ToList();
        var plans = await ManpowerForYearsAsync(years, ct);
        return [.. rows.Select(r =>
        {
            var plan = plans.FirstOrDefault(p => p.Year == r.CreatedAt.Year && p.OrgUnitId == r.OrgUnitId && string.Equals(p.JobTitle.Trim(), r.JobTitle.Trim(), StringComparison.OrdinalIgnoreCase));
            var over = plan is not null && plan.Gap < 0 || plan is null && plans.Any(p => p.Year == r.CreatedAt.Year);
            return new RequisitionView(r.Id, r.Number, r.OrgUnitId, r.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, r.JobTitle, r.Openings, r.Hired, Snake(r.Type.ToString()), r.SalaryMin, r.SalaryMax, r.Replacement, r.Justification,
                names.GetValueOrDefault(r.RequestedBy) ?? "", r.RequestedBy == actor.UserId, Snake(r.Status.ToString()), r.DecidedBy is { } d ? names.GetValueOrDefault(d) : null, r.DecidedAt, r.DecisionNote, over && !r.Replacement, r.CreatedAt);
        })];
    }
    private async Task<List<ManpowerRow>> ManpowerForYearsAsync(IEnumerable<int> years, CancellationToken ct) { var all = new List<ManpowerRow>(); foreach (var y in years) all.AddRange(await ManpowerAsync(y, ct)); return all; }

    public async Task<IReadOnlyList<RequisitionView>> RequisitionsAsync(string? scope, SessionPrincipal actor, CancellationToken ct = default)
    {
        var q = db.JobRequisitions.AsNoTracking();
        if (scope == "all") { if (!await IsHrAsync(actor, ct)) throw DomainException.Denied(Permissions.HrConfigManage); } else q = q.Where(r => r.RequestedBy == actor.UserId);
        return await RequisitionViewsAsync(await q.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct), actor, ct);
    }

    public async Task<RequisitionView> RequestAsync(RequisitionRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await IsHrAsync(actor, ct))
        {
            var acting = await managers.ActingIdsAsync(actor.UserId, ct);
            if (acting.Count == 0 || !await db.Employments.AnyAsync(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value), ct)) throw DomainException.DeniedBecause("Hiring requests are raised by managers and HR.");
        }
        var errors = new Dictionary<string, string>();
        var title = Clip(r.JobTitle, 150); if (title is null || title.Length < 2) errors["jobTitle"] = "Enter the job title.";
        var openings = r.Openings ?? 1; if (openings is < 1 or > 100) errors["openings"] = "Enter 1–100.";
        var type = EmploymentType.Permanent; if (r.Type is { } ty && !Enum.TryParse(ty, true, out type)) errors["type"] = "Choose the employment type.";
        if (r.SalaryMin is < 0 || r.SalaryMax is < 0 || r.SalaryMin > r.SalaryMax) errors["salaryMax"] = "Check the salary range.";
        var why = Clip(r.Justification, 1000); if (why is null || why.Length < 5) errors["justification"] = "Say why the position is needed.";
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the hiring request.", errors);
        var year = Today.Year; var n = await db.JobRequisitions.CountAsync(x => x.Number.StartsWith($"REQ/{year}/"), ct) + 1;
        var req = new JobRequisition { Number = $"REQ/{year}/{n:D4}", OrgUnitId = r.OrgUnitId, JobTitle = title!, Openings = openings, Type = type, SalaryMin = r.SalaryMin, SalaryMax = r.SalaryMax, Replacement = r.Replacement ?? false, Justification = why!, RequestedBy = actor.UserId, CreatedAt = clock.UtcNow };
        db.JobRequisitions.Add(req);
        audit.Record("hr.requisition.create", "requisition", req.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = req.Number, ["openings"] = openings.ToString() });
        await db.SaveChangesAsync(ct);
        return (await RequisitionViewsAsync([req], actor, ct))[0];
    }

    public async Task<RequisitionView> DecideRequisitionAsync(Guid id, DecideRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var req = await db.JobRequisitions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Hiring request");
        if (req.RequestedBy == actor.UserId) throw DomainException.DeniedBecause("Someone else must decide your own hiring request.");
        if (req.Status != RequisitionStatus.Pending) throw DomainException.Conflict("This request is already decided.");
        if (!r.Approve && Clip(r.Note, 300) is null) throw DomainException.Validation("Say why the request is rejected.", new Dictionary<string, string> { ["note"] = "Required when rejecting." });
        req.Status = r.Approve ? RequisitionStatus.Approved : RequisitionStatus.Rejected; req.DecidedBy = actor.UserId; req.DecidedAt = clock.UtcNow; req.DecisionNote = Clip(r.Note, 300); req.Version++;
        notes.Notify(req.RequestedBy, "requisition.decided", r.Approve ? $"Hiring request {req.Number} approved" : $"Hiring request {req.Number} rejected", req.DecisionNote, "/hr/requisitions");
        audit.Record(r.Approve ? "hr.requisition.approve" : "hr.requisition.reject", "requisition", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = req.Number });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("Someone else changed this request just now."); }
        return (await RequisitionViewsAsync([req], actor, ct))[0];
    }

    public async Task<RequisitionView> CancelRequisitionAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var req = await db.JobRequisitions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Hiring request");
        if (req.RequestedBy != actor.UserId && !await IsHrAsync(actor, ct)) throw DomainException.NotFound("Hiring request");
        if (req.Status is not (RequisitionStatus.Pending or RequisitionStatus.Approved)) throw DomainException.Conflict("Only a pending or approved request can be cancelled.");
        req.Status = RequisitionStatus.Cancelled; req.Version++;
        foreach (var p in await db.JobPostings.Where(p => p.RequisitionId == id && p.Status == PostingStatus.Open).ToListAsync(ct)) p.Status = PostingStatus.Closed;
        audit.Record("hr.requisition.cancel", "requisition", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await RequisitionViewsAsync([req], actor, ct))[0];
    }

    // ============================================================ postings
    private static IReadOnlyList<string> StageList(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries);
    private async Task<IReadOnlyList<PostingView>> PostingViewsAsync(IReadOnlyList<JobPosting> rows, CancellationToken ct)
    {
        var ids = rows.Select(p => p.Id).ToList();
        var apps = await db.JobApplications.AsNoTracking().Where(a => ids.Contains(a.PostingId)).Select(a => new { a.PostingId, a.Stage, a.Status }).ToListAsync(ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var reqIds = rows.Where(p => p.RequisitionId != null).Select(p => p.RequisitionId!.Value).ToList();
        var numbers = await db.JobRequisitions.AsNoTracking().Where(r => reqIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);
        return [.. rows.Select(p =>
        {
            var mine = apps.Where(a => a.PostingId == p.Id).ToList();
            var by = new Dictionary<string, int> { ["applied"] = 0 }; foreach (var s in StageList(p.Stages)) by[s] = 0; by["hired"] = 0;
            foreach (var a in mine.Where(a => a.Status is ApplicationStatus.Active or ApplicationStatus.Hired)) by[by.ContainsKey(a.Stage) ? a.Stage : "applied"]++;
            return new PostingView(p.Id, p.PublicId, p.RequisitionId, p.RequisitionId is { } r ? numbers.GetValueOrDefault(r) : null, p.Title, p.OrgUnitId, p.OrgUnitId is { } u ? units.GetValueOrDefault(u) : null, p.Location, Snake(p.Type.ToString()),
                p.Description, p.Requirements, p.SalaryMin, p.SalaryMax, p.ShowSalary, StageList(p.Stages), Snake(p.Status.ToString()), p.ClosesOn, p.CreatedAt, p.PublishedAt, mine.Count, mine.Count(a => a.Status == ApplicationStatus.Active), by);
        })];
    }

    public async Task<IReadOnlyList<PostingView>> PostingsAsync(CancellationToken ct = default) => await PostingViewsAsync(await db.JobPostings.AsNoTracking().OrderByDescending(p => p.CreatedAt).Take(300).ToListAsync(ct), ct);

    public async Task<PostingView> SavePostingAsync(Guid? id, PostingRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = id is { } pid ? await db.JobPostings.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Job posting") : new JobPosting { PublicId = tokens.NewToken()[..12].Replace('-', 'x').Replace('_', 'y'), CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        JobRequisition? req = null;
        if (r.RequisitionId is { } rid)
        {
            req = await db.JobRequisitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == rid, ct);
            if (req is null || req.Status != RequisitionStatus.Approved) errors["requisitionId"] = "Choose an approved hiring request.";
        }
        var title = Clip(r.Title, 150) ?? (id is null ? req?.JobTitle : p.Title); if (title is null || title.Length < 2) errors["title"] = "Enter the job title.";
        var desc = Clip(r.Description, 8000) ?? (id is null ? null : p.Description); if (desc is null || desc.Length < 20) errors["description"] = "Describe the job (at least 20 characters).";
        var type = id is null ? req?.Type ?? EmploymentType.Permanent : p.Type; if (r.Type is { } ty && !Enum.TryParse(ty, true, out type)) errors["type"] = "Choose the employment type.";
        var stages = r.Stages?.Select(s => s.Trim().ToLowerInvariant().Replace(' ', '_')).ToList() ?? [.. StageList(p.Stages)];
        if (stages.Count is < 1 or > 8 || stages.Any(s => !StageRx().IsMatch(s) || s is "applied" or "hired") || stages.Distinct().Count() != stages.Count) errors["stages"] = "1–8 different stages, short lowercase names (not applied or hired).";
        var min = r.SalaryMin ?? (id is null ? req?.SalaryMin : p.SalaryMin); var max = r.SalaryMax ?? (id is null ? req?.SalaryMax : p.SalaryMax); if (min < 0 || max < 0 || min > max) errors["salaryMax"] = "Check the salary range.";
        if (r.ClosesOn is { } c && c < Today) errors["closesOn"] = "The closing date has passed.";
        if (r.OrgUnitId is { } u && !await db.OrgUnits.AnyAsync(x => x.Id == u, ct)) errors["orgUnitId"] = "Choose a department.";
        if (id is not null && r.Stages is not null && await db.JobApplications.AnyAsync(a => a.PostingId == p.Id && a.Status == ApplicationStatus.Active && a.Stage != "applied" && !stages.Contains(a.Stage), ct))
            errors["stages"] = "Some applicants are in a stage you removed. Move them first.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the job posting.", errors);
        p.RequisitionId = r.RequisitionId ?? p.RequisitionId; p.Title = title!; p.Description = desc!; p.Type = type; p.Stages = string.Join(',', stages); p.SalaryMin = min; p.SalaryMax = max;
        p.OrgUnitId = r.OrgUnitId ?? p.OrgUnitId ?? req?.OrgUnitId; p.Location = r.Location is null ? p.Location : Clip(r.Location, 150); p.Requirements = r.Requirements is null ? p.Requirements : Clip(r.Requirements, 4000);
        p.ShowSalary = r.ShowSalary ?? p.ShowSalary; p.ClosesOn = r.ClosesOn ?? p.ClosesOn;
        if (id is null) db.JobPostings.Add(p);
        audit.Record(id is null ? "hr.posting.create" : "hr.posting.update", "job_posting", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = p.Title });
        await db.SaveChangesAsync(ct);
        return (await PostingViewsAsync([p], ct))[0];
    }

    public async Task<PostingView> SetPostingStatusAsync(Guid id, bool open, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.JobPostings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Job posting");
        if (open)
        {
            if (p.ClosesOn is { } c && c < Today) throw DomainException.Invariant("Move the closing date before opening the posting again.");
            if (p.RequisitionId is { } rid && !await db.JobRequisitions.AnyAsync(r => r.Id == rid && r.Status == RequisitionStatus.Approved, ct)) throw DomainException.Invariant("The hiring request behind this posting is no longer open.");
            p.Status = PostingStatus.Open; p.PublishedAt ??= clock.UtcNow;
        }
        else p.Status = PostingStatus.Closed;
        audit.Record(open ? "hr.posting.open" : "hr.posting.close", "job_posting", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await PostingViewsAsync([p], ct))[0];
    }

    // ============================================================ public career page
    /// <summary>The organisation's short name used in the public career page address.</summary>
    public async Task<object> CareerLinkAsync(CancellationToken ct = default) => new { slug = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.TenantId).Select(t => t.Slug).FirstOrDefaultAsync(ct) ?? "" };
    private async Task<Tenant> TenantBySlugAsync(string slug, CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Slug == slug.Trim().ToLowerInvariant() && x.Status == TenantStatus.Active, ct) ?? throw DomainException.NotFound("Career page");
        tenant.Use(t.Id, false);   // anonymous request: from here on it reads inside that organisation
        return t;
    }
    private IQueryable<JobPosting> OpenPostings() { var today = Today; return db.JobPostings.AsNoTracking().Where(p => p.Status == PostingStatus.Open && (p.ClosesOn == null || p.ClosesOn >= today)); }
    private async Task<CareerJob> CareerJobOf(JobPosting p, CancellationToken ct)
    {
        var unit = p.OrgUnitId is { } u ? await db.OrgUnits.AsNoTracking().Where(x => x.Id == u).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new CareerJob(p.PublicId, p.Title, unit, p.Location, Snake(p.Type.ToString()), p.Description, p.Requirements, p.ShowSalary ? p.SalaryMin : null, p.ShowSalary ? p.SalaryMax : null, p.ClosesOn, p.PublishedAt);
    }

    public async Task<CareerPage> CareersAsync(string slug, CancellationToken ct = default)
    {
        var t = await TenantBySlugAsync(slug, ct); var jobs = new List<CareerJob>();
        foreach (var p in await OpenPostings().OrderByDescending(p => p.PublishedAt).Take(200).ToListAsync(ct)) jobs.Add(await CareerJobOf(p, ct));
        return new CareerPage(t.Name, jobs);
    }

    public async Task<CareerJob> CareerJobAsync(string slug, string publicId, CancellationToken ct = default)
    {
        await TenantBySlugAsync(slug, ct);
        return await CareerJobOf(await OpenPostings().FirstOrDefaultAsync(p => p.PublicId == publicId, ct) ?? throw DomainException.NotFound("Job"), ct);
    }

    private static (byte[]? Bytes, string? Error) ReadCv(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return (null, null);
        if (b64.Length > DocumentService.MaxBytes * 4 / 3 + 16) return (null, "The CV is larger than 4 MB.");
        byte[] bytes; try { bytes = Convert.FromBase64String(b64); } catch (FormatException) { return (null, "The CV could not be read."); }
        return DocumentService.Sniff(bytes) is { ContentType: "application/pdf" } && bytes.Length <= DocumentService.MaxBytes ? (bytes, null) : (null, "Send the CV as a PDF up to 4 MB.");
    }

    private async Task<JobApplication> AddApplicationAsync(JobPosting p, ApplyRequest r, bool fromCareerPage, Guid? byUser, ClientInfo client, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var name = Clip(r.FullName, 200); if (name is null || name.Length < 2) errors["fullName"] = "Enter your full name.";
        var mail = r.Email?.Trim().ToLowerInvariant(); if (mail is null || mail.Length > 254 || !EmailRx().IsMatch(mail)) errors["email"] = "Enter a valid email address.";
        var phone = Clip(r.Phone, 30);
        var (cv, cvError) = ReadCv(r.CvBase64); if (cvError is not null) errors["cv"] = cvError; else if (cv is null && fromCareerPage) errors["cv"] = "Attach your CV (PDF).";
        if (fromCareerPage && r.Consent != true) errors["consent"] = "Please agree to how we use your data.";
        var source = fromCareerPage ? "career_page" : (Clip(r.Source, 30)?.ToLowerInvariant() ?? "added_by_hr");
        if (errors.Count > 0) throw DomainException.Validation("Please check the application.", errors);
        var cand = await db.Candidates.FirstOrDefaultAsync(c => c.Email == mail, ct);
        if (cand is null) { cand = new Candidate { Email = mail!, CreatedAt = clock.UtcNow, Source = source }; db.Candidates.Add(cand); }
        else if (await db.JobApplications.AnyAsync(a => a.PostingId == p.Id && a.CandidateId == cand.Id, ct)) throw DomainException.Conflict(fromCareerPage ? "You have already applied for this job." : "This person already applied for this job.");
        cand.FullName = name!; cand.Phone = phone ?? cand.Phone; if (cv is not null) { cand.Cv = cv; cand.CvName = Clip(r.CvName, 200) ?? "cv.pdf"; }
        if (fromCareerPage) cand.ConsentAt = clock.UtcNow;
        var app = new JobApplication { PostingId = p.Id, CandidateId = cand.Id, CoverLetter = Clip(r.CoverLetter, 4000), AppliedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
        db.JobApplications.Add(app);
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = app.Id, Kind = "applied", Text = fromCareerPage ? "Applied on the career page." : $"Added by HR (source: {source}).", ByUserId = byUser, At = clock.UtcNow });
        audit.Record("hr.application.create", "job_posting", p.Id.ToString(), Outcome.Success, client, byUser, null, new Dictionary<string, string> { ["source"] = source });
        await db.SaveChangesAsync(ct);
        return app;
    }

    public async Task ApplyAsync(string slug, string publicId, ApplyRequest r, ClientInfo client, CancellationToken ct = default)
    {
        var t = await TenantBySlugAsync(slug, ct);
        var hit = await limiter.HitAsync($"careers:apply:{client.Ip ?? "?"}", 10, TimeSpan.FromHours(1), ct); if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var p = await OpenPostings().FirstOrDefaultAsync(x => x.PublicId == publicId, ct) ?? throw DomainException.NotFound("Job");
        await AddApplicationAsync(p, r, true, null, client, ct);
        if (r.Email?.Trim() is { Length: > 0 } to) Mail(to, $"We received your application: {p.Title}", $"Dear {r.FullName?.Trim()},\n\nThank you for applying for {p.Title} at {t.Name}. We will review your application and contact you about the next steps.\n\n{t.Name}");
    }

    // ============================================================ pipeline
    private async Task<IReadOnlyList<ApplicationRow>> RowsAsync(IQueryable<JobApplication> q, CancellationToken ct)
    {
        var rows = await (from a in q join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id orderby a.AppliedAt descending
                          select new { a, c.FullName, c.Email, c.Phone, c.Source, HasCv = c.Cv != null }).Take(1000).ToListAsync(ct);
        var ids = rows.Select(r => r.a.Id).ToList();
        var tests = await (from i in db.AssessmentInvites.AsNoTracking() join tpl in db.AssessmentTemplates.AsNoTracking() on i.TemplateId equals tpl.Id where ids.Contains(i.ApplicationId) && i.Status == InviteStatus.Done
                           select new { i.ApplicationId, i.Score, i.Result, tpl.Kind }).ToListAsync(ct);
        return [.. rows.Select(r => new ApplicationRow(r.a.Id, r.a.CandidateId, r.FullName, r.Email, r.Phone, r.Source, r.a.Stage, Snake(r.a.Status.ToString()), r.a.Rating, r.HasCv, r.a.AppliedAt, r.a.UpdatedAt,
            tests.Where(x => x.ApplicationId == r.a.Id).Select(x => x.Kind == AssessmentKind.Test ? $"{x.Score}%" : x.Result).FirstOrDefault()))];
    }

    public async Task<IReadOnlyList<ApplicationRow>> ApplicationsAsync(Guid postingId, CancellationToken ct = default)
    {
        if (!await db.JobPostings.AnyAsync(p => p.Id == postingId, ct)) throw DomainException.NotFound("Job posting");
        return await RowsAsync(db.JobApplications.AsNoTracking().Where(a => a.PostingId == postingId), ct);
    }

    public async Task<ApplicationRow> AddCandidateAsync(Guid postingId, ApplyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == postingId, ct) ?? throw DomainException.NotFound("Job posting");
        if (p.Status == PostingStatus.Closed) throw DomainException.Invariant("This posting is closed.");
        var app = await AddApplicationAsync(p, r, false, actor.UserId, client, ct);
        return (await RowsAsync(db.JobApplications.AsNoTracking().Where(a => a.Id == app.Id), ct))[0];
    }

    private async Task<IReadOnlyList<InterviewView>> InterviewViewsAsync(IReadOnlyList<Interview> rows, CancellationToken ct)
    {
        var appIds = rows.Select(i => i.ApplicationId).Distinct().ToList();
        var who = await (from a in db.JobApplications.AsNoTracking() join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id join p in db.JobPostings.AsNoTracking() on a.PostingId equals p.Id where appIds.Contains(a.Id)
                         select new { a.Id, c.FullName, p.Title }).ToDictionaryAsync(x => x.Id, ct);
        var empIds = rows.SelectMany(i => Ids(i.Interviewers)).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        return [.. rows.Select(i => new InterviewView(i.Id, i.ApplicationId, who.GetValueOrDefault(i.ApplicationId)?.FullName ?? "", who.GetValueOrDefault(i.ApplicationId)?.Title ?? "", i.At, i.DurationMinutes, i.Location,
            Ids(i.Interviewers), [.. Ids(i.Interviewers).Select(x => names.GetValueOrDefault(x) ?? "")], Snake(i.Status.ToString()), i.Rating, i.Recommendation, i.Feedback))];
    }
    private static IReadOnlyList<Guid> Ids(string csv) => [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)];

    public async Task<ApplicationDetail> ApplicationAsync(Guid id, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Application");
        var p = await db.JobPostings.AsNoTracking().FirstAsync(x => x.Id == a.PostingId, ct);
        var acts = await db.ApplicationActivities.AsNoTracking().Where(x => x.ApplicationId == id).OrderByDescending(x => x.At).Take(200).ToListAsync(ct);
        var users = acts.Where(x => x.ByUserId != null).Select(x => x.ByUserId!.Value).Distinct().ToList(); var names = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var interviews = await InterviewViewsAsync(await db.Interviews.AsNoTracking().Where(x => x.ApplicationId == id).OrderBy(x => x.At).ToListAsync(ct), ct);
        var tests = await (from i in db.AssessmentInvites.AsNoTracking() join tpl in db.AssessmentTemplates.AsNoTracking() on i.TemplateId equals tpl.Id where i.ApplicationId == id orderby i.SentAt descending
                           select new InviteView(i.Id, tpl.Name, tpl.Kind.ToString().ToLower(), i.Status.ToString().ToLower(), i.Score, tpl.PassScore, i.Result, i.SentAt, i.ExpiresAt, i.DoneAt)).ToListAsync(ct);
        var offers = (await db.JobOffers.AsNoTracking().Where(x => x.ApplicationId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(OfferViewOf).ToList();
        return new ApplicationDetail((await RowsAsync(db.JobApplications.AsNoTracking().Where(x => x.Id == id), ct))[0], p.Id, p.Title, ["applied", .. StageList(p.Stages), "hired"], a.CoverLetter, a.RejectReason, a.HiredEmployeeId,
            [.. acts.Select(x => new ActivityView(x.Id, x.Kind, x.Text, x.ByUserId is { } u ? names.GetValueOrDefault(u) : null, x.At))], interviews, tests, offers);
    }
    private static OfferView OfferViewOf(JobOffer o) => new(o.Id, o.JobTitle, o.MonthlySalary, o.StartDate, o.ExpiresOn, Snake(o.Type.ToString()), o.ContractEnd, o.Terms, o.Status.ToString().ToLower(), o.SentAt, o.RespondedAt, o.ResponseNote);

    public async Task<ApplicationDetail> UpdateApplicationAsync(Guid id, ApplicationUpdate r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Application");
        var p = await db.JobPostings.AsNoTracking().FirstAsync(x => x.Id == a.PostingId, ct);
        void Log(string kind, string text) => db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = id, Kind = kind, Text = text, ByUserId = actor.UserId, At = clock.UtcNow });
        if (r.Stage is { } stage)
        {
            if (a.Status != ApplicationStatus.Active) throw DomainException.Conflict("Only an active application moves between stages.");
            if (stage != "applied" && !StageList(p.Stages).Contains(stage)) throw DomainException.Validation("Unknown stage.", new Dictionary<string, string> { ["stage"] = "Choose one of the posting's stages. Hiring is done from an accepted offer." });
            if (stage != a.Stage) { Log("stage", $"Moved from {a.Stage} to {stage}."); a.Stage = stage; }
        }
        if (r.Rating is { } rating) { if (rating is < 1 or > 5) throw DomainException.Validation("Rate 1–5.", new Dictionary<string, string> { ["rating"] = "Rate 1–5." }); a.Rating = rating; }
        if (Clip(r.Note, 2000) is { } note) Log("note", note);
        if (r.Reject == true)
        {
            if (a.Status != ApplicationStatus.Active) throw DomainException.Conflict("This application is already closed.");
            a.Status = ApplicationStatus.Rejected; a.RejectReason = Clip(r.RejectReason, 300); Log("rejected", $"Rejected{(a.RejectReason is { } why ? $": {why}" : ".")}");
            foreach (var o in await db.JobOffers.Where(o => o.ApplicationId == id && (o.Status == OfferStatus.Draft || o.Status == OfferStatus.Sent)).ToListAsync(ct)) o.Status = OfferStatus.Withdrawn;
            if (r.NotifyCandidate == true)
            {
                var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct); var company = await CompanyNameAsync(ct);
                Mail(c.Email, $"Your application for {p.Title}", $"Dear {c.FullName},\n\nThank you for your interest in {p.Title} at {company}. After careful consideration we will not continue with your application at this time. We wish you every success.\n\n{company}");
            }
        }
        a.UpdatedAt = clock.UtcNow; a.Version++;
        audit.Record("hr.application.update", "job_posting", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["stage"] = a.Stage, ["status"] = a.Status.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ResetTracking(); throw DomainException.Conflict("Someone else changed this application just now."); }
        return await ApplicationAsync(id, ct);
    }

    private async Task<string> CompanyNameAsync(CancellationToken ct) => await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";

    public async Task<ExportFile> CvAsync(Guid applicationId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, ct) ?? throw DomainException.NotFound("Application");
        // interviewers may open the CV of someone they interview; otherwise HR
        if (!await IsHrAsync(actor, ct))
        {
            var me = await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
            var interviewers = await db.Interviews.AsNoTracking().Where(i => i.ApplicationId == applicationId && i.Status != InterviewStatus.Cancelled).Select(i => i.Interviewers).ToListAsync(ct);
            if (me is null || !interviewers.Any(s => s.Contains(me.Value.ToString()))) throw DomainException.NotFound("Application");
        }
        var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct); if (c.Cv is null) throw DomainException.NotFound("CV");
        audit.Record("hr.candidate.cv_download", "candidate", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        return new ExportFile(c.CvName ?? "cv.pdf", "application/pdf", c.Cv);
    }

    /// <summary>Erases a candidate (right to erasure, UU PDP): their record, CV and every application. Not for someone already hired.</summary>
    public async Task EraseCandidateAsync(Guid candidateId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.Candidates.FirstOrDefaultAsync(x => x.Id == candidateId, ct) ?? throw DomainException.NotFound("Candidate");
        if (await db.JobApplications.AnyAsync(a => a.CandidateId == candidateId && a.Status == ApplicationStatus.Hired, ct)) throw DomainException.Invariant("This person was hired; their data now lives in the employee record.");
        var appIds = await db.JobApplications.Where(a => a.CandidateId == candidateId).Select(a => a.Id).ToListAsync(ct);
        db.AssessmentInvites.RemoveRange(await db.AssessmentInvites.Where(i => appIds.Contains(i.ApplicationId)).ToListAsync(ct));
        db.Candidates.Remove(c);
        audit.Record("hr.candidate.erase", "candidate", candidateId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["applications"] = appIds.Count.ToString() });
        await db.SaveChangesAsync(ct);
    }

    // ============================================================ interviews
    public async Task<InterviewView> ScheduleAsync(Guid applicationId, InterviewRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, ct) ?? throw DomainException.NotFound("Application");
        if (a.Status != ApplicationStatus.Active) throw DomainException.Conflict("Only an active application can get an interview.");
        var errors = new Dictionary<string, string>();
        if (r.At is not { } at || at < clock.UtcNow) errors["at"] = "Choose a time in the future.";
        var dur = r.DurationMinutes ?? 60; if (dur is < 10 or > 480) errors["durationMinutes"] = "10–480 minutes.";
        var ids = (r.InterviewerIds ?? []).Distinct().ToList();
        if (ids.Count is < 1 or > 10) errors["interviewerIds"] = "Choose 1–10 interviewers.";
        else if (await db.Employees.CountAsync(e => ids.Contains(e.Id) && e.Status == EmployeeStatus.Active, ct) != ids.Count) errors["interviewerIds"] = "Choose active employees.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the interview.", errors);
        var i = new Interview { ApplicationId = applicationId, At = r.At!.Value.ToUniversalTime(), DurationMinutes = dur, Location = Clip(r.Location, 300), Interviewers = string.Join(',', ids), CreatedBy = actor.UserId };
        db.Interviews.Add(i);
        var local = r.At!.Value.ToOffset(TimeSpan.FromHours(7));
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = applicationId, Kind = "interview", Text = $"Interview scheduled for {local:yyyy-MM-dd HH:mm} WIB.", ByUserId = actor.UserId, At = clock.UtcNow });
        var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct); var p = await db.JobPostings.AsNoTracking().FirstAsync(x => x.Id == a.PostingId, ct);
        foreach (var uid in await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(ct))
            notes.Notify(uid, "interview.scheduled", $"Interview: {c.FullName} ({p.Title})", $"{local:dd MMM yyyy HH:mm} WIB", "/hr/interviews");
        var company = await CompanyNameAsync(ct);
        Mail(c.Email, $"Interview invitation: {p.Title}", $"Dear {c.FullName},\n\nWe would like to invite you to an interview for {p.Title} at {company}.\n\nWhen: {local:dddd, dd MMMM yyyy HH:mm} WIB ({dur} minutes)\nWhere: {i.Location ?? "we will send the details"}\n\n{company}");
        audit.Record("hr.interview.schedule", "job_posting", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await InterviewViewsAsync([i], ct))[0];
    }

    private async Task<Guid?> MyEmployeeIdAsync(SessionPrincipal actor, CancellationToken ct) => await db.Employees.AsNoTracking().Where(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<InterviewerTask>> MyInterviewsAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MyEmployeeIdAsync(actor, ct); if (me is null) return [];
        var key = me.Value.ToString();
        var rows = await db.Interviews.AsNoTracking().Where(i => i.Interviewers.Contains(key)).OrderByDescending(i => i.At).Take(200).ToListAsync(ct);
        var views = await InterviewViewsAsync(rows, ct);
        var cvs = await (from a in db.JobApplications.AsNoTracking() join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id where rows.Select(r => r.ApplicationId).Contains(a.Id) select new { a.Id, Has = c.Cv != null }).ToDictionaryAsync(x => x.Id, x => x.Has, ct);
        return [.. views.Select(v => new InterviewerTask(v.Id, v.Candidate, v.Posting, v.At, v.DurationMinutes, v.Location, v.Status, v.Rating, v.Recommendation, v.Feedback, v.ApplicationId, cvs.GetValueOrDefault(v.ApplicationId)))];
    }

    public async Task<InterviewView> FeedbackAsync(Guid interviewId, FeedbackRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var i = await db.Interviews.FirstOrDefaultAsync(x => x.Id == interviewId, ct) ?? throw DomainException.NotFound("Interview");
        var me = await MyEmployeeIdAsync(actor, ct);
        if (!(me is { } m && Ids(i.Interviewers).Contains(m)) && !await IsHrAsync(actor, ct)) throw DomainException.NotFound("Interview");
        if (i.Status == InterviewStatus.Cancelled) throw DomainException.Conflict("This interview was cancelled.");
        var errors = new Dictionary<string, string>();
        if (r.Rating is not (>= 1 and <= 5)) errors["rating"] = "Rate 1–5.";
        var rec = r.Recommendation?.Trim().ToLowerInvariant(); if (rec is not ("hire" or "maybe" or "no_hire")) errors["recommendation"] = "Choose hire, maybe or no_hire.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the feedback.", errors);
        i.Rating = r.Rating; i.Recommendation = rec; i.Feedback = Clip(r.Feedback, 2000); i.Status = InterviewStatus.Done;
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = i.ApplicationId, Kind = "feedback", Text = $"Interview feedback: {r.Rating}/5, {rec?.Replace('_', ' ')}.{(i.Feedback is { } f ? " " + f : "")}", ByUserId = actor.UserId, At = clock.UtcNow });
        audit.Record("hr.interview.feedback", "interview", interviewId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await InterviewViewsAsync([i], ct))[0];
    }

    public async Task CancelInterviewAsync(Guid interviewId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var i = await db.Interviews.FirstOrDefaultAsync(x => x.Id == interviewId, ct) ?? throw DomainException.NotFound("Interview");
        if (i.Status != InterviewStatus.Scheduled) throw DomainException.Conflict("Only a scheduled interview can be cancelled.");
        i.Status = InterviewStatus.Cancelled;
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = i.ApplicationId, Kind = "interview", Text = "Interview cancelled.", ByUserId = actor.UserId, At = clock.UtcNow });
        audit.Record("hr.interview.cancel", "interview", interviewId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
    }

    // ============================================================ assessments
    private static List<AssessmentQuestion> Questions(string json) { try { return JsonSerializer.Deserialize<List<AssessmentQuestion>>(json, Json) ?? []; } catch (JsonException) { return []; } }
    private static TemplateView TemplateOf(AssessmentTemplate t) => new(t.Id, t.Name, t.Kind.ToString().ToLower(), Questions(t.QuestionsJson), t.PassScore, t.TimeLimitMinutes, t.Archived);

    public async Task<IReadOnlyList<TemplateView>> TemplatesAsync(CancellationToken ct = default) => [.. (await db.AssessmentTemplates.AsNoTracking().OrderBy(t => t.Archived).ThenBy(t => t.Name).ToListAsync(ct)).Select(TemplateOf)];

    public async Task<TemplateView> SaveTemplateAsync(Guid? id, TemplateRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = id is { } tid ? await db.AssessmentTemplates.FirstOrDefaultAsync(x => x.Id == tid, ct) ?? throw DomainException.NotFound("Test") : new AssessmentTemplate { CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var name = Clip(r.Name, 150) ?? t.Name; if (name.Length < 2) errors["name"] = "Name the test.";
        var kind = id is null ? AssessmentKind.Test : t.Kind; if (id is null && r.Kind is { } k && !Enum.TryParse(k, true, out kind)) errors["kind"] = "Choose test or personality.";
        var qs = r.Questions?.Select((q, i) => q with { Id = string.IsNullOrWhiteSpace(q.Id) ? $"q{i + 1}" : q.Id.Trim()[..Math.Min(q.Id.Trim().Length, 20)], Text = q.Text?.Trim() ?? "", Options = q.Options ?? [] }).ToList() ?? Questions(t.QuestionsJson);
        if (qs.Count is < 1 or > 80) errors["questions"] = "Add 1–80 questions.";
        else if (qs.Select(q => q.Id).Distinct().Count() != qs.Count) errors["questions"] = "Question ids must be different.";
        else
            for (var i = 0; i < qs.Count; i++)
            {
                var q = qs[i];
                if (q.Text.Length is < 2 or > 500 || q.Options.Any(o => string.IsNullOrWhiteSpace(o.Text) || o.Text.Length > 300)) { errors["questions"] = $"Question {i + 1}: write the question and every option (up to 300 characters)."; break; }
                if (kind == AssessmentKind.Test && (q.Options.Count is < 2 or > 6 || q.Options.Count(o => o.Correct == true) != 1)) { errors["questions"] = $"Question {i + 1}: give 2–6 options with exactly one correct answer."; break; }
                if (kind == AssessmentKind.Personality && (q.Options.Count != 2 || q.Options.Any(o => o.Pole is null || !Poles.Contains(o.Pole)) || Array.IndexOf(Poles, q.Options[0].Pole) / 2 != Array.IndexOf(Poles, q.Options[1].Pole) / 2 || q.Options[0].Pole == q.Options[1].Pole))
                { errors["questions"] = $"Question {i + 1}: give two statements for the two poles of one dimension (E/I, S/N, T/F or J/P)."; break; }
            }
        var pass = r.PassScore ?? t.PassScore; if (pass is < 0 or > 100) errors["passScore"] = "Enter 0–100%.";
        var limit = r.TimeLimitMinutes ?? t.TimeLimitMinutes; if (limit is < 0 or > 240) errors["timeLimitMinutes"] = "0 (no limit) to 240 minutes.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the test.", errors);
        t.Name = name; t.Kind = kind; t.QuestionsJson = JsonSerializer.Serialize(qs, Json); t.PassScore = kind == AssessmentKind.Test ? pass : null; t.TimeLimitMinutes = limit; t.Archived = r.Archived ?? t.Archived;
        if (id is null) db.AssessmentTemplates.Add(t);
        audit.Record(id is null ? "hr.assessment.create" : "hr.assessment.update", "assessment", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        await db.SaveChangesAsync(ct);
        return TemplateOf(t);
    }

    /// <summary>A ready-made personality profile (16 statements, four per dimension) so HR need not write one.</summary>
    public async Task<TemplateView> AddSampleProfileAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        (string E, string I)[] ei = [("I get energy from working with a group.", "I get energy from working on my own."), ("I think out loud and talk ideas through.", "I think things through before I speak."), ("I enjoy meeting new people at events.", "I prefer a few deep conversations."), ("I like a busy, lively workplace.", "I like a quiet place to focus.")];
        (string S, string N)[] sn = [("I trust facts and experience.", "I trust ideas and possibilities."), ("I like clear, step-by-step instructions.", "I like to see the big picture first."), ("I notice details others miss.", "I notice patterns and connections."), ("I improve what already works.", "I like to try new ways.")];
        (string T, string F)[] tf = [("I decide by logic and fairness of rules.", "I decide by the effect on people."), ("Honest feedback matters more than tact.", "Tactful feedback matters more than bluntness."), ("I value being competent.", "I value being caring."), ("In a dispute I look for what is right.", "In a dispute I look for harmony.")];
        (string J, string P)[] jp = [("I like to plan ahead and keep to it.", "I like to keep options open."), ("I finish work well before the deadline.", "I do my best work close to the deadline."), ("I like things settled and decided.", "I like to adapt as things change."), ("A to-do list keeps me on track.", "I work best by following what comes up.")];
        var qs = new List<AssessmentQuestion>(); var n = 0;
        void Add(IEnumerable<(string, string)> pairs, string a, string b) { foreach (var (x, y) in pairs) qs.Add(new AssessmentQuestion($"q{++n}", "Which describes you better?", [new(x, null, a), new(y, null, b)])); }
        Add(ei, "E", "I"); Add(sn, "S", "N"); Add(tf, "T", "F"); Add(jp, "J", "P");
        return await SaveTemplateAsync(null, new TemplateRequest("Personality profile (4 preferences)", "personality", qs, null, 15, false), actor, client, ct);
    }

    public async Task<SentLink> SendTestAsync(Guid applicationId, SendTestRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, ct) ?? throw DomainException.NotFound("Application");
        if (a.Status != ApplicationStatus.Active) throw DomainException.Conflict("Only an active application can get a test.");
        var t = await db.AssessmentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.TemplateId && !x.Archived, ct) ?? throw DomainException.Validation("Choose a test.", new Dictionary<string, string> { ["templateId"] = "Choose an active test." });
        if (await db.AssessmentInvites.AnyAsync(i => i.ApplicationId == applicationId && i.TemplateId == t.Id && i.Status != InviteStatus.Cancelled, ct)) throw DomainException.Conflict("This test was already sent to this applicant.");
        var days = r.DaysValid ?? 7; if (days is < 1 or > 30) throw DomainException.Validation("Choose 1–30 days.", new Dictionary<string, string> { ["daysValid"] = "1–30 days." });
        var token = tokens.NewToken();
        var inv = new AssessmentInvite { ApplicationId = applicationId, TemplateId = t.Id, TokenHash = tokens.Hash(token), SentAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddDays(days) };
        db.AssessmentInvites.Add(inv);
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = applicationId, Kind = "test", Text = $"Test sent: {t.Name}.", ByUserId = actor.UserId, At = clock.UtcNow });
        var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct); var company = await CompanyNameAsync(ct);
        var link = $"{Origin}/careers/test#{token}";
        Mail(c.Email, $"{company}: {t.Name}", $"Dear {c.FullName},\n\nAs the next step of your application, please complete this {(t.Kind == AssessmentKind.Test ? "test" : "questionnaire")} before {inv.ExpiresAt.ToOffset(TimeSpan.FromHours(7)):dd MMMM yyyy}:\n\n{link}\n\n{(t.TimeLimitMinutes > 0 ? $"You have {t.TimeLimitMinutes} minutes once you start. " : "")}The link is personal; please do not share it.\n\n{company}");
        audit.Record("hr.assessment.send", "assessment", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new SentLink(inv.Id, link);
    }

    private async Task<AssessmentInvite> InviteByTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw DomainException.NotFound("Test");
        var hash = tokens.Hash(token.Trim());
        var inv = await db.AssessmentInvites.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.TokenHash == hash, ct) ?? throw DomainException.NotFound("Test");
        tenant.Use(inv.TenantId, false);
        return inv;
    }

    public async Task<PublicTest> PublicTestAsync(string? token, bool start, ClientInfo client, CancellationToken ct = default)
    {
        var hit = await limiter.HitAsync($"careers:test:{client.Ip ?? "?"}", 60, TimeSpan.FromMinutes(10), ct); if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var inv = await InviteByTokenAsync(token, ct);
        if (inv.Status == InviteStatus.Cancelled) throw DomainException.NotFound("Test");
        if (inv.Status == InviteStatus.Sent && inv.ExpiresAt < clock.UtcNow) throw DomainException.Expired("This link has expired. Ask the recruiter for a new one.");
        var t = await db.AssessmentTemplates.AsNoTracking().FirstAsync(x => x.Id == inv.TemplateId, ct);
        var a = await db.JobApplications.AsNoTracking().FirstAsync(x => x.Id == inv.ApplicationId, ct); var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct);
        if (start && inv.Status == InviteStatus.Sent && inv.StartedAt is null) { inv.StartedAt = clock.UtcNow; await db.SaveChangesAsync(ct); }
        var done = inv.Status == InviteStatus.Done;
        return new PublicTest(await CompanyNameAsync(ct), c.FullName, t.Name, t.Kind.ToString().ToLower(), t.TimeLimitMinutes, inv.StartedAt, done,
            done || inv.StartedAt is null ? [] : [.. Questions(t.QuestionsJson).Select(q => new PublicQuestion(q.Id, q.Text, [.. q.Options.Select(o => o.Text)]))]);
    }

    /// <summary>Scores the answers: a test in percent correct, a personality profile as the four leanings (a tie goes to the first pole of the pair).</summary>
    public static (int? Score, string? Result) Score(AssessmentKind kind, IReadOnlyList<AssessmentQuestion> qs, IReadOnlyDictionary<string, int> answers)
    {
        if (kind == AssessmentKind.Test)
        {
            var right = qs.Count(q => answers.TryGetValue(q.Id, out var i) && i >= 0 && i < q.Options.Count && q.Options[i].Correct == true);
            return ((int)Math.Round(right * 100.0 / Math.Max(1, qs.Count), MidpointRounding.AwayFromZero), null);
        }
        var counts = Poles.ToDictionary(p => p, _ => 0);
        foreach (var q in qs) if (answers.TryGetValue(q.Id, out var i) && i is 0 or 1 && q.Options[i].Pole is { } pole) counts[pole]++;
        return (null, string.Concat(Enumerable.Range(0, 4).Select(d => counts[Poles[d * 2]] >= counts[Poles[d * 2 + 1]] ? Poles[d * 2] : Poles[d * 2 + 1])));
    }

    public async Task SubmitTestAsync(string? token, TestAnswers r, ClientInfo client, CancellationToken ct = default)
    {
        var hit = await limiter.HitAsync($"careers:test:{client.Ip ?? "?"}", 60, TimeSpan.FromMinutes(10), ct); if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var inv = await InviteByTokenAsync(token, ct);
        if (inv.Status != InviteStatus.Sent) throw DomainException.Conflict("This test is already completed.");
        if (inv.ExpiresAt < clock.UtcNow) throw DomainException.Expired("This link has expired. Ask the recruiter for a new one.");
        if (inv.StartedAt is null) throw DomainException.Conflict("Start the test first.");
        var t = await db.AssessmentTemplates.AsNoTracking().FirstAsync(x => x.Id == inv.TemplateId, ct); var qs = Questions(t.QuestionsJson);
        var answers = (r.Answers ?? []).Where(kv => qs.Any(q => q.Id == kv.Key)).ToDictionary();
        if (answers.Count < qs.Count) throw DomainException.Validation("Answer every question.", new Dictionary<string, string> { ["answers"] = $"{qs.Count - answers.Count} question(s) left." });
        var late = t.TimeLimitMinutes > 0 && clock.UtcNow > inv.StartedAt.Value.AddMinutes(t.TimeLimitMinutes + 2);
        var (score, result) = Score(t.Kind, qs, answers);
        inv.Score = score; inv.Result = result; inv.AnswersJson = JsonSerializer.Serialize(answers, Json); inv.Status = InviteStatus.Done; inv.DoneAt = clock.UtcNow;
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = inv.ApplicationId, Kind = "test", Text = $"{t.Name} completed: {(score is { } s ? $"{s}%" + (t.PassScore is { } pass ? s >= pass ? " (passed)" : " (below the pass mark)" : "") : result)}{(late ? ", after the time limit" : "")}.", At = clock.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    // ============================================================ offers
    public async Task<OfferView> CreateOfferAsync(Guid applicationId, OfferRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.JobApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, ct) ?? throw DomainException.NotFound("Application");
        if (a.Status != ApplicationStatus.Active) throw DomainException.Conflict("Only an active application can get an offer.");
        if (await db.JobOffers.AnyAsync(o => o.ApplicationId == applicationId && (o.Status == OfferStatus.Draft || o.Status == OfferStatus.Sent || o.Status == OfferStatus.Accepted), ct)) throw DomainException.Conflict("This applicant already has an open offer.");
        var p = await db.JobPostings.AsNoTracking().FirstAsync(x => x.Id == a.PostingId, ct);
        var errors = new Dictionary<string, string>();
        var title = Clip(r.JobTitle, 150) ?? p.Title;
        if (r.MonthlySalary is not > 0) errors["monthlySalary"] = "Enter the monthly salary.";
        if (r.StartDate is not { } start || start < Today) errors["startDate"] = "Choose a start date from today.";
        var expires = r.ExpiresOn ?? Today.AddDays(7); if (expires < Today || r.StartDate is { } s0 && expires > s0) errors["expiresOn"] = "The offer must be answered before the start date.";
        var type = p.Type; if (r.Type is { } ty && !Enum.TryParse(ty, true, out type)) errors["type"] = "Choose the employment type.";
        if (type is EmploymentType.Contract or EmploymentType.Probation or EmploymentType.Intern && (r.ContractEnd is null || r.StartDate is { } s1 && r.ContractEnd < s1)) errors["contractEnd"] = "A fixed-term offer needs an end date after the start.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the offer.", errors);
        var o = new JobOffer { ApplicationId = applicationId, JobTitle = title, MonthlySalary = PayrollRules.Rupiah(r.MonthlySalary!.Value), StartDate = r.StartDate!.Value, ExpiresOn = expires, Type = type, ContractEnd = r.ContractEnd, Terms = Clip(r.Terms, 4000), CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        db.JobOffers.Add(o);
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = applicationId, Kind = "offer", Text = "Offer drafted.", ByUserId = actor.UserId, At = clock.UtcNow });
        audit.Record("hr.offer.create", "job_posting", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return OfferViewOf(o);
    }

    public async Task<SentLink> SendOfferAsync(Guid offerId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var o = await db.JobOffers.FirstOrDefaultAsync(x => x.Id == offerId, ct) ?? throw DomainException.NotFound("Offer");
        if (o.Status is not (OfferStatus.Draft or OfferStatus.Sent)) throw DomainException.Conflict("This offer was already answered or withdrawn.");
        if (o.ExpiresOn < Today) throw DomainException.Invariant("The offer has expired. Create a new one.");
        var token = tokens.NewToken(); o.TokenHash = tokens.Hash(token); o.Status = OfferStatus.Sent; o.SentAt = clock.UtcNow;
        var a = await db.JobApplications.FirstAsync(x => x.Id == o.ApplicationId, ct); a.Stage = StageList((await db.JobPostings.AsNoTracking().FirstAsync(x => x.Id == a.PostingId, ct)).Stages).Contains("offer") ? "offer" : a.Stage;
        var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct); var company = await CompanyNameAsync(ct);
        var link = $"{Origin}/careers/offer#{token}";
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = o.ApplicationId, Kind = "offer", Text = "Offer sent to the candidate.", ByUserId = actor.UserId, At = clock.UtcNow });
        Mail(c.Email, $"Job offer from {company}: {o.JobTitle}", $"Dear {c.FullName},\n\nWe are pleased to offer you the position of {o.JobTitle} at {company}. Please read the offer and accept or decline it before {o.ExpiresOn:dd MMMM yyyy}:\n\n{link}\n\nThe link is personal; please do not share it.\n\n{company}");
        audit.Record("hr.offer.send", "offer", offerId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return new SentLink(o.Id, link);
    }

    public async Task<OfferView> WithdrawOfferAsync(Guid offerId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var o = await db.JobOffers.FirstOrDefaultAsync(x => x.Id == offerId, ct) ?? throw DomainException.NotFound("Offer");
        if (o.Status is not (OfferStatus.Draft or OfferStatus.Sent)) throw DomainException.Conflict("Only an unanswered offer can be withdrawn.");
        o.Status = OfferStatus.Withdrawn; o.TokenHash = null;
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = o.ApplicationId, Kind = "offer", Text = "Offer withdrawn.", ByUserId = actor.UserId, At = clock.UtcNow });
        audit.Record("hr.offer.withdraw", "offer", offerId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        return OfferViewOf(o);
    }

    private async Task<(JobOffer Offer, Candidate Candidate)> OfferByTokenAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw DomainException.NotFound("Offer");
        var hash = tokens.Hash(token.Trim());
        var o = await db.JobOffers.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TokenHash == hash, ct) ?? throw DomainException.NotFound("Offer");
        tenant.Use(o.TenantId, false);
        var a = await db.JobApplications.AsNoTracking().FirstAsync(x => x.Id == o.ApplicationId, ct);
        return (o, await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct));
    }

    public async Task<PublicOffer> PublicOfferAsync(string? token, ClientInfo client, CancellationToken ct = default)
    {
        var hit = await limiter.HitAsync($"careers:offer:{client.Ip ?? "?"}", 30, TimeSpan.FromMinutes(10), ct); if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var (o, c) = await OfferByTokenAsync(token, ct);
        return new PublicOffer(await CompanyNameAsync(ct), c.FullName, o.JobTitle, o.MonthlySalary, o.StartDate, o.ExpiresOn, Snake(o.Type.ToString()), o.ContractEnd, o.Terms, o.Status.ToString().ToLower());
    }

    public async Task<PublicOffer> RespondOfferAsync(string? token, OfferResponse r, ClientInfo client, CancellationToken ct = default)
    {
        var hit = await limiter.HitAsync($"careers:offer:{client.Ip ?? "?"}", 30, TimeSpan.FromMinutes(10), ct); if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var (o, c) = await OfferByTokenAsync(token, ct);
        if (o.Status != OfferStatus.Sent) throw DomainException.Conflict("This offer was already answered.");
        if (o.ExpiresOn < Today) throw DomainException.Expired("This offer has expired.");
        o.Status = r.Accept ? OfferStatus.Accepted : OfferStatus.Declined; o.RespondedAt = clock.UtcNow; o.ResponseNote = Clip(r.Note, 500);
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = o.ApplicationId, Kind = "offer", Text = r.Accept ? "The candidate accepted the offer." : $"The candidate declined the offer.{(o.ResponseNote is { } n ? " " + n : "")}", At = clock.UtcNow });
        var p = await (from a in db.JobApplications join po in db.JobPostings on a.PostingId equals po.Id where a.Id == o.ApplicationId select po).AsNoTracking().FirstAsync(ct);
        foreach (var uid in new[] { p.CreatedBy, o.CreatedBy }.Distinct()) notes.Notify(uid, "offer.answered", r.Accept ? $"{c.FullName} accepted the offer ({o.JobTitle})" : $"{c.FullName} declined the offer ({o.JobTitle})", null, $"/hr/recruitment/{p.Id}");
        audit.Record(r.Accept ? "hr.offer.accept" : "hr.offer.decline", "offer", o.Id.ToString(), Outcome.Success, client, null, null);
        await db.SaveChangesAsync(ct);
        return new PublicOffer(await CompanyNameAsync(ct), c.FullName, o.JobTitle, o.MonthlySalary, o.StartDate, o.ExpiresOn, Snake(o.Type.ToString()), o.ContractEnd, o.Terms, o.Status.ToString().ToLower());
    }

    public async Task<ExportFile> OfferPdfAsync(Guid offerId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var o = await db.JobOffers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == offerId, ct) ?? throw DomainException.NotFound("Offer");
        var a = await db.JobApplications.AsNoTracking().FirstAsync(x => x.Id == o.ApplicationId, ct); var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct);
        var company = await CompanyNameAsync(ct); var inv = System.Globalization.CultureInfo.InvariantCulture;
        var pdf = new SimplePdf(); double y = 60;
        pdf.Text(company, 50, y, 14, true); y += 18; pdf.Line(50, y, 545); y += 28;
        pdf.Text("Offer of Employment", 50, y, 13, true); y += 24;
        foreach (var line in new[] { $"Dear {c.FullName},", "", $"We are pleased to offer you the position of {o.JobTitle}.", "",
            $"Monthly salary: IDR {o.MonthlySalary.ToString("#,0", inv)} (gross)", $"Employment type: {o.Type}{(o.ContractEnd is { } end ? $" until {end:dd MMMM yyyy}" : "")}", $"Start date: {o.StartDate:dd MMMM yyyy}",
            $"Please answer this offer by {o.ExpiresOn:dd MMMM yyyy}.", "" }.Concat(SimplePdf.Wrap(o.Terms ?? "")))
        { if (y > 760) { pdf.NewPage(); y = 60; } pdf.Text(line, 50, y, 10); y += 14; }
        y += 20; pdf.Text($"Status: {o.Status}", 50, y, 9);
        audit.Record("hr.offer.pdf", "offer", offerId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        return new ExportFile($"offer-{c.FullName.Replace(' ', '-').ToLowerInvariant()}.pdf", "application/pdf", pdf.Build());
    }

    // ============================================================ hire
    /// <summary>Turns an accepted offer into an employee record (with the onboarding checklist that every new employee gets). The salary is then set in Compensation by payroll.</summary>
    public async Task<ApplicationDetail> HireAsync(Guid applicationId, HireRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!(await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.EmployeesCreate)) throw DomainException.Denied(Permissions.EmployeesCreate);
        var a = await db.JobApplications.FirstOrDefaultAsync(x => x.Id == applicationId, ct) ?? throw DomainException.NotFound("Application");
        if (a.Status != ApplicationStatus.Active) throw DomainException.Conflict("This application is already closed.");
        var o = await db.JobOffers.AsNoTracking().Where(x => x.ApplicationId == applicationId && x.Status == OfferStatus.Accepted).OrderByDescending(x => x.RespondedAt).FirstOrDefaultAsync(ct)
            ?? throw DomainException.Invariant("Hire after the candidate accepts an offer.");
        var c = await db.Candidates.AsNoTracking().FirstAsync(x => x.Id == a.CandidateId, ct); var p = await db.JobPostings.AsNoTracking().FirstAsync(x => x.Id == a.PostingId, ct);
        var emp = await employees.CreateAsync(new CreateEmployeeRequest(r.EmployeeNo, c.FullName, r.WorkEmail, c.Phone, null, null, null, null, null, null, o.StartDate, null, null, null, null, o.JobTitle, null, p.Location,
            Snake(o.Type.ToString()), o.ContractEnd, r.OrgUnitId ?? p.OrgUnitId, r.ManagerId, null), actor, client, ct);
        a.Status = ApplicationStatus.Hired; a.Stage = "hired"; a.HiredEmployeeId = emp.Id; a.UpdatedAt = clock.UtcNow; a.Version++;
        if (p.RequisitionId is { } rid && await db.JobRequisitions.FirstOrDefaultAsync(x => x.Id == rid, ct) is { } req)
        {
            req.Hired++; req.Version++;
            if (req.Hired >= req.Openings) { req.Status = RequisitionStatus.Filled; foreach (var po in await db.JobPostings.Where(x => x.RequisitionId == rid && x.Status == PostingStatus.Open).ToListAsync(ct)) po.Status = PostingStatus.Closed; }
        }
        db.ApplicationActivities.Add(new ApplicationActivity { ApplicationId = applicationId, Kind = "hired", Text = $"Hired as {emp.EmployeeNo}, starting {o.StartDate:yyyy-MM-dd}.", ByUserId = actor.UserId, At = clock.UtcNow });
        audit.Record("hr.application.hire", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["posting"] = p.Id.ToString() });
        await db.SaveChangesAsync(ct);
        return await ApplicationAsync(applicationId, ct);
    }

    // ============================================================ analytics
    public async Task<RecruitmentSummary> SummaryAsync(CancellationToken ct = default)
    {
        var yearStart = new DateTimeOffset(Today.Year, 1, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();   // timestamptz parameters must be UTC
        var reqs = await db.JobRequisitions.AsNoTracking().GroupBy(r => r.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var today = Today;
        var openPostings = await db.JobPostings.CountAsync(p => p.Status == PostingStatus.Open && (p.ClosesOn == null || p.ClosesOn >= today), ct);
        var active = await db.JobApplications.CountAsync(a => a.Status == ApplicationStatus.Active, ct);
        var hired = await db.JobApplications.AsNoTracking().Where(a => a.Status == ApplicationStatus.Hired && a.UpdatedAt >= yearStart).Select(a => new { a.AppliedAt, a.UpdatedAt }).ToListAsync(ct);
        var offers = await db.JobOffers.AsNoTracking().Where(o => o.Status == OfferStatus.Accepted || o.Status == OfferStatus.Declined).GroupBy(o => o.Status).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var answered = offers.Sum(x => x.N); var accepted = offers.Where(x => x.Key == OfferStatus.Accepted).Sum(x => x.N);
        var sources = await (from a in db.JobApplications.AsNoTracking() join c in db.Candidates.AsNoTracking() on a.CandidateId equals c.Id where a.AppliedAt >= yearStart group a by c.Source into g select new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return new RecruitmentSummary(reqs.Where(x => x.Key == RequisitionStatus.Approved).Sum(x => x.N), reqs.Where(x => x.Key == RequisitionStatus.Pending).Sum(x => x.N), openPostings, active, hired.Count,
            hired.Count == 0 ? null : Math.Round(hired.Average(h => (h.UpdatedAt - h.AppliedAt).TotalDays), 1), answered == 0 ? null : Math.Round(accepted * 100.0 / answered, 1), sources);
    }
}
