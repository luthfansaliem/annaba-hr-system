using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Hr;

public sealed record CourseView(Guid Id, string Title, string? Description, string Category, bool Mandatory, bool SelfEnroll, int PassScore, bool Certificate, int? ValidMonths, string Status,
    int Lessons, int Minutes, int QuizQuestions, int Enrolled, int Completed, int Overdue, DateTimeOffset UpdatedAt);
public sealed record CourseRequest(string? Title, string? Description, string? Category, bool? Mandatory, bool? SelfEnroll, int? PassScore, bool? Certificate, int? ValidMonths, IReadOnlyList<AssessmentQuestion>? Quiz);
public sealed record LessonView(Guid Id, int Order, string Title, string Kind, string? Body, string? Url, string? FileName, int Minutes, bool Done);
public sealed record LessonRequest(string? Title, string? Kind, string? Body, string? Url, string? FileName, string? FileBase64, int? Minutes, int? Order);
public sealed record CourseEditor(CourseView Course, IReadOnlyList<LessonView> Lessons, IReadOnlyList<AssessmentQuestion> Quiz);
public sealed record AssignRequest(IReadOnlyList<Guid>? EmployeeIds, Guid? OrgUnitId, bool? Everyone, DateOnly? DueDate);
public sealed record EnrollmentRow(Guid Id, Guid EmployeeId, string EmployeeNo, string EmployeeName, string? Department, string Status, int Progress, int? QuizScore, int QuizAttempts, DateOnly? DueDate, bool Overdue,
    DateTimeOffset? CompletedAt, string? CertificateNo, DateOnly? ExpiresOn, bool Assigned);
public sealed record MyCourse(Guid CourseId, Guid? EnrollmentId, string Title, string? Description, string Category, bool Mandatory, int Lessons, int Minutes, string Status, int Progress, DateOnly? DueDate, bool Overdue,
    int? QuizScore, string? CertificateNo, DateOnly? ExpiresOn);
public sealed record QuizQuestionView(string Id, string Text, IReadOnlyList<string> Options);
public sealed record CoursePlayer(MyCourse Course, IReadOnlyList<LessonView> Lessons, IReadOnlyList<QuizQuestionView> Quiz, int PassScore, bool QuizOpen);
public sealed record QuizSubmit(Dictionary<string, int>? Answers);
public sealed record QuizResult(int Score, bool Passed, int Attempts, MyCourse Course);
public sealed record LearningSummary(int Published, int Enrolled, int Completed, double? CompletionRate, int Overdue, int CertificatesThisYear, int ExpiringSoon);

/// <summary>
/// Stage 18, learning (Talenta LMS): HR (hrconfig.manage) builds courses (lessons of text, video, link or PDF; a quiz with a pass mark), assigns them with a due date, and follows progress;
/// people take their courses, pass the quiz and download a certificate with a verification code. Managers see their direct reports' progress.
/// </summary>
public sealed class LearningService(IAppDb db, IClock clock, AuthorizationService authz, AuditWriter audit, ManagerScope managers, NotificationService notes, DocumentSealService seals, IAppLinks links, ITenantContext tenant)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddHours(7));
    private static string? Clip(string? s, int max) => s?.Trim() is { Length: > 0 } t ? t[..Math.Min(t.Length, max)] : null;
    private static string Snake(string s) => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    private static List<AssessmentQuestion> Quiz(string json) { try { return JsonSerializer.Deserialize<List<AssessmentQuestion>>(json, Json) ?? []; } catch (JsonException) { return []; } }
    private async Task<bool> IsHrAsync(SessionPrincipal actor, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(actor.UserId, ct)).Contains(Permissions.HrConfigManage);
    private static HashSet<Guid> Done(string csv) => [.. csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse)];

    private async Task<Employee> MeAsync(SessionPrincipal actor, CancellationToken ct) =>
        await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == actor.UserId && e.Status == EmployeeStatus.Active, ct) ?? throw DomainException.Invariant("Your account is not linked to an active employee record. Ask HR to link it.");

    // ============================================================ HR: courses
    private async Task<IReadOnlyList<CourseView>> ViewsAsync(IReadOnlyList<Course> rows, CancellationToken ct)
    {
        var ids = rows.Select(c => c.Id).ToList(); var today = Today;
        var lessons = await db.CourseLessons.AsNoTracking().Where(l => ids.Contains(l.CourseId)).GroupBy(l => l.CourseId).Select(g => new { g.Key, N = g.Count(), M = g.Sum(x => x.Minutes) }).ToDictionaryAsync(x => x.Key, ct);
        var enr = await db.CourseEnrollments.AsNoTracking().Where(e => ids.Contains(e.CourseId)).GroupBy(e => e.CourseId)
            .Select(g => new { g.Key, N = g.Count(), C = g.Count(x => x.Status == EnrollmentStatus.Completed), O = g.Count(x => x.Status != EnrollmentStatus.Completed && x.DueDate != null && x.DueDate < today) }).ToDictionaryAsync(x => x.Key, ct);
        return [.. rows.Select(c => new CourseView(c.Id, c.Title, c.Description, c.Category, c.Mandatory, c.SelfEnroll, c.PassScore, c.Certificate, c.ValidMonths, Snake(c.Status.ToString()),
            lessons.GetValueOrDefault(c.Id)?.N ?? 0, lessons.GetValueOrDefault(c.Id)?.M ?? 0, Quiz(c.QuizJson).Count, enr.GetValueOrDefault(c.Id)?.N ?? 0, enr.GetValueOrDefault(c.Id)?.C ?? 0, enr.GetValueOrDefault(c.Id)?.O ?? 0, c.UpdatedAt))];
    }

    public async Task<IReadOnlyList<CourseView>> CoursesAsync(CancellationToken ct = default) => await ViewsAsync(await db.Courses.AsNoTracking().OrderBy(c => c.Status).ThenBy(c => c.Title).ToListAsync(ct), ct);

    private static LessonView LessonOf(CourseLesson l, bool done = false) => new(l.Id, l.Order, l.Title, l.Kind.ToString().ToLower(), l.Body, l.Url, l.FileName, l.Minutes, done);

    public async Task<CourseEditor> EditorAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Course");
        var lessons = await db.CourseLessons.AsNoTracking().Where(l => l.CourseId == id).OrderBy(l => l.Order).ToListAsync(ct);
        return new CourseEditor((await ViewsAsync([c], ct))[0], [.. lessons.Select(l => LessonOf(l))], Quiz(c.QuizJson));
    }

    public async Task<CourseEditor> SaveCourseAsync(Guid? id, CourseRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = id is { } cid ? await db.Courses.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw DomainException.NotFound("Course") : new Course { CreatedBy = actor.UserId, CreatedAt = clock.UtcNow };
        var errors = new Dictionary<string, string>();
        var title = Clip(r.Title, 200) ?? c.Title; if (title.Length < 2) errors["title"] = "Name the course.";
        var cat = (Clip(r.Category, 30) ?? c.Category).ToLowerInvariant(); if (cat is not ("general" or "compliance" or "safety" or "technical" or "leadership" or "onboarding" or "product")) errors["category"] = "Choose a category.";
        var pass = r.PassScore ?? c.PassScore; if (pass is < 0 or > 100) errors["passScore"] = "Enter 0–100%.";
        var valid = r.ValidMonths ?? c.ValidMonths; if (valid is < 1 or > 120) errors["validMonths"] = "1–120 months, or leave empty.";
        var quiz = r.Quiz?.Select((q, i) => q with { Id = string.IsNullOrWhiteSpace(q.Id) ? $"q{i + 1}" : q.Id.Trim()[..Math.Min(q.Id.Trim().Length, 20)], Text = q.Text?.Trim() ?? "", Options = q.Options ?? [] }).ToList() ?? Quiz(c.QuizJson);
        if (quiz.Count > 50) errors["quiz"] = "Up to 50 questions.";
        for (var i = 0; i < quiz.Count && !errors.ContainsKey("quiz"); i++)
            if (quiz[i].Text.Length is < 2 or > 500 || quiz[i].Options.Count is < 2 or > 6 || quiz[i].Options.Count(o => o.Correct == true) != 1 || quiz[i].Options.Any(o => string.IsNullOrWhiteSpace(o.Text) || o.Text.Length > 300))
                errors["quiz"] = $"Question {i + 1}: write it with 2–6 options and exactly one correct answer.";
        if (quiz.Select(q => q.Id).Distinct().Count() != quiz.Count) errors["quiz"] = "Question ids must be different.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the course.", errors);
        c.Title = title; c.Category = cat; c.Description = r.Description is null ? c.Description : Clip(r.Description, 2000); c.Mandatory = r.Mandatory ?? c.Mandatory; c.SelfEnroll = r.SelfEnroll ?? c.SelfEnroll;
        c.PassScore = pass; c.Certificate = r.Certificate ?? c.Certificate; c.ValidMonths = r.ValidMonths is null && id is not null ? c.ValidMonths : valid; c.QuizJson = JsonSerializer.Serialize(quiz, Json); c.UpdatedAt = clock.UtcNow;
        if (id is null) db.Courses.Add(c);
        audit.Record(id is null ? "hr.course.create" : "hr.course.update", "course", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["title"] = title });
        await db.SaveChangesAsync(ct);
        return await EditorAsync(c.Id, ct);
    }

    public async Task<CourseEditor> SetStatusAsync(Guid id, string action, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.Courses.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Course");
        switch (action)
        {
            case "publish":
                if (!await db.CourseLessons.AnyAsync(l => l.CourseId == id, ct)) throw DomainException.Invariant("Add at least one lesson before publishing.");
                c.Status = CourseStatus.Published; break;
            case "archive": c.Status = CourseStatus.Archived; break;
            default: throw DomainException.Validation("Unknown action.", new Dictionary<string, string> { ["action"] = "Use publish or archive." });
        }
        c.UpdatedAt = clock.UtcNow;
        audit.Record($"hr.course.{action}", "course", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await EditorAsync(id, ct);
    }

    public async Task<CourseEditor> SaveLessonAsync(Guid courseId, Guid? lessonId, LessonRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.Courses.AnyAsync(x => x.Id == courseId, ct)) throw DomainException.NotFound("Course");
        var l = lessonId is { } lid ? await db.CourseLessons.FirstOrDefaultAsync(x => x.Id == lid && x.CourseId == courseId, ct) ?? throw DomainException.NotFound("Lesson")
            : new CourseLesson { CourseId = courseId, Order = (await db.CourseLessons.Where(x => x.CourseId == courseId).MaxAsync(x => (int?)x.Order, ct) ?? 0) + 1 };
        var errors = new Dictionary<string, string>();
        var title = Clip(r.Title, 200) ?? l.Title; if (title.Length < 2) errors["title"] = "Name the lesson.";
        var kind = l.Kind; if (r.Kind is { } k && !Enum.TryParse(k, true, out kind)) errors["kind"] = "Choose text, video, link or file.";
        var body = r.Body is null ? l.Body : Clip(r.Body, 20000); var url = r.Url is null ? l.Url : Clip(r.Url, 1000);
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)) errors["url"] = "Use an https:// address.";
        byte[]? file = null;
        if (!string.IsNullOrWhiteSpace(r.FileBase64))
        {
            try { file = Convert.FromBase64String(r.FileBase64); } catch (FormatException) { errors["file"] = "The file could not be read."; }
            if (file is not null && (file.Length > 10 * 1024 * 1024 || DocumentService.Sniff(file) is not { ContentType: "application/pdf" })) errors["file"] = "Attach a PDF up to 10 MB.";
        }
        if (kind == LessonKind.Text && body is null) errors["body"] = "Write the lesson.";
        if (kind is LessonKind.Video or LessonKind.Link && url is null) errors["url"] = "Give the address of the video or page.";
        if (kind == LessonKind.File && file is null && l.File is null) errors["file"] = "Attach the PDF.";
        var minutes = r.Minutes ?? l.Minutes; if (minutes is < 0 or > 600) errors["minutes"] = "0–600 minutes.";
        if (errors.Count > 0) throw DomainException.Validation("Please check the lesson.", errors);
        l.Title = title; l.Kind = kind; l.Body = body; l.Url = url; l.Minutes = minutes; if (r.Order is { } o) l.Order = o;
        if (file is not null) { l.File = file; l.FileName = Clip(r.FileName, 200) ?? "lesson.pdf"; }
        if (lessonId is null) db.CourseLessons.Add(l);
        await db.Courses.Where(x => x.Id == courseId).ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, clock.UtcNow), ct);
        audit.Record(lessonId is null ? "hr.course.lesson_add" : "hr.course.lesson_update", "course", courseId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await EditorAsync(courseId, ct);
    }

    public async Task<CourseEditor> DeleteLessonAsync(Guid courseId, Guid lessonId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.CourseLessons.FirstOrDefaultAsync(x => x.Id == lessonId && x.CourseId == courseId, ct) ?? throw DomainException.NotFound("Lesson");
        db.CourseLessons.Remove(l); audit.Record("hr.course.lesson_delete", "course", courseId.ToString(), Outcome.Success, client, actor.UserId, actor.Email); await db.SaveChangesAsync(ct);
        return await EditorAsync(courseId, ct);
    }

    /// <summary>Assigns a published course to chosen people, a department, or everyone active; people already enrolled keep their progress (a later due date is not moved earlier).</summary>
    public async Task<int> AssignAsync(Guid courseId, AssignRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId, ct) ?? throw DomainException.NotFound("Course");
        if (c.Status != CourseStatus.Published) throw DomainException.Invariant("Publish the course before assigning it.");
        if (r.DueDate is { } due && due < Today) throw DomainException.Validation("The due date has passed.", new Dictionary<string, string> { ["dueDate"] = "Choose today or later." });
        var q = db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (r.Everyone == true) { }
        else if (r.OrgUnitId is { } u) q = q.Where(e => e.Employments.Any(m => m.EffectiveTo == null && m.OrgUnitId == u));
        else if (r.EmployeeIds is { Count: > 0 } ids) q = q.Where(e => ids.Contains(e.Id));
        else throw DomainException.Validation("Choose who takes the course.", new Dictionary<string, string> { ["employeeIds"] = "Choose people, a department or everyone." });
        var people = await q.Select(e => new { e.Id, e.UserId }).ToListAsync(ct); var pids = people.Select(p => p.Id).ToList();
        var existing = await db.CourseEnrollments.Where(e => e.CourseId == courseId && pids.Contains(e.EmployeeId)).ToDictionaryAsync(e => e.EmployeeId, ct);
        var added = 0;
        foreach (var p in people)
        {
            if (existing.TryGetValue(p.Id, out var en)) { en.AssignedBy ??= actor.UserId; if (r.DueDate is { } d && (en.DueDate is null || d > en.DueDate)) en.DueDate = d; continue; }
            db.CourseEnrollments.Add(new CourseEnrollment { CourseId = courseId, EmployeeId = p.Id, AssignedBy = actor.UserId, DueDate = r.DueDate, EnrolledAt = clock.UtcNow }); added++;
            notes.Notify(p.UserId, "course.assigned", $"New course: {c.Title}", r.DueDate is { } dd ? $"Please complete it by {dd:dd MMM yyyy}." : null, $"/hr/learning/{courseId}");
        }
        audit.Record("hr.course.assign", "course", courseId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["added"] = added.ToString() });
        await db.SaveChangesAsync(ct);
        return added;
    }

    private async Task<IReadOnlyList<EnrollmentRow>> RowsAsync(IQueryable<CourseEnrollment> q, CancellationToken ct)
    {
        var rows = await (from e in q join p in db.Employees.AsNoTracking() on e.EmployeeId equals p.Id orderby p.FullName select new { e, p.EmployeeNo, p.FullName, Unit = p.Employments.Where(m => m.EffectiveTo == null).Select(m => m.OrgUnitId).FirstOrDefault() }).Take(2000).ToListAsync(ct);
        var courseIds = rows.Select(r => r.e.CourseId).Distinct().ToList();
        var lessonCount = await db.CourseLessons.AsNoTracking().Where(l => courseIds.Contains(l.CourseId)).GroupBy(l => l.CourseId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct); var today = Today;
        return [.. rows.Select(r => new EnrollmentRow(r.e.Id, r.e.EmployeeId, r.EmployeeNo, r.FullName, r.Unit is { } u ? units.GetValueOrDefault(u) : null, Snake(r.e.Status.ToString()), ProgressOf(r.e, lessonCount.GetValueOrDefault(r.e.CourseId)),
            r.e.QuizScore, r.e.QuizAttempts, r.e.DueDate, r.e.Status != EnrollmentStatus.Completed && r.e.DueDate < today, r.e.CompletedAt, r.e.CertificateNo, r.e.ExpiresOn, r.e.AssignedBy != null))];
    }
    private static int ProgressOf(CourseEnrollment e, int lessons) => e.Status == EnrollmentStatus.Completed ? 100 : lessons == 0 ? 0 : (int)Math.Round(Done(e.LessonsDone).Count * 100.0 / lessons);

    public async Task<IReadOnlyList<EnrollmentRow>> EnrollmentsAsync(Guid courseId, CancellationToken ct = default)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId, ct)) throw DomainException.NotFound("Course");
        return await RowsAsync(db.CourseEnrollments.AsNoTracking().Where(e => e.CourseId == courseId), ct);
    }

    /// <summary>A manager's direct reports and their courses.</summary>
    public async Task<IReadOnlyList<object>> TeamAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var acting = await managers.ActingIdsAsync(actor.UserId, ct); if (acting.Count == 0) return [];
        var reports = await db.Employments.AsNoTracking().Where(m => m.EffectiveTo == null && m.ManagerId != null && acting.Contains(m.ManagerId.Value)).Select(m => m.EmployeeId).ToListAsync(ct);
        var rows = await RowsAsync(db.CourseEnrollments.AsNoTracking().Where(e => reports.Contains(e.EmployeeId)), ct);
        var titles = await (from e in db.CourseEnrollments.AsNoTracking() join c in db.Courses.AsNoTracking() on e.CourseId equals c.Id where reports.Contains(e.EmployeeId) select new { e.Id, c.Title }).ToDictionaryAsync(x => x.Id, x => x.Title, ct);
        return [.. rows.Select(r => (object)new { r.Id, Course = titles.GetValueOrDefault(r.Id) ?? "", r.EmployeeId, r.EmployeeName, r.Status, r.Progress, r.DueDate, r.Overdue, r.CompletedAt, r.QuizScore })];
    }

    // ============================================================ people: my courses
    private async Task<MyCourse> MineOf(Course c, CourseEnrollment? e, CancellationToken ct)
    {
        var lessons = await db.CourseLessons.AsNoTracking().Where(l => l.CourseId == c.Id).Select(l => l.Minutes).ToListAsync(ct);
        return new MyCourse(c.Id, e?.Id, c.Title, c.Description, c.Category, c.Mandatory, lessons.Count, lessons.Sum(), e is null ? "available" : Snake(e.Status.ToString()), e is null ? 0 : ProgressOf(e, lessons.Count),
            e?.DueDate, e is not null && e.Status != EnrollmentStatus.Completed && e.DueDate < Today, e?.QuizScore, e?.CertificateNo, e?.ExpiresOn);
    }

    /// <summary>My courses (assigned or started) and, separately, published courses open to everyone that I have not started.</summary>
    public async Task<object> MyCoursesAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var me = await MeAsync(actor, ct);
        var mine = await (from e in db.CourseEnrollments.AsNoTracking() join c in db.Courses.AsNoTracking() on e.CourseId equals c.Id where e.EmployeeId == me.Id && c.Status != CourseStatus.Draft select new { e, c }).ToListAsync(ct);
        var taken = mine.Select(x => x.c.Id).ToList();
        var catalog = await db.Courses.AsNoTracking().Where(c => c.Status == CourseStatus.Published && c.SelfEnroll && !taken.Contains(c.Id)).OrderBy(c => c.Title).ToListAsync(ct);
        var list = new List<MyCourse>(); foreach (var x in mine.OrderBy(x => x.e.Status == EnrollmentStatus.Completed).ThenBy(x => x.e.DueDate ?? DateOnly.MaxValue)) list.Add(await MineOf(x.c, x.e, ct));
        var cat = new List<MyCourse>(); foreach (var c in catalog) cat.Add(await MineOf(c, null, ct));
        return new { mine = list, catalog = cat };
    }

    private async Task<(Course Course, CourseEnrollment? Enrollment, Employee Me)> ForMeAsync(Guid courseId, SessionPrincipal actor, bool tracked, CancellationToken ct)
    {
        var me = await MeAsync(actor, ct);
        var c = await db.Courses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == courseId && x.Status != CourseStatus.Draft, ct) ?? throw DomainException.NotFound("Course");
        var q = tracked ? db.CourseEnrollments : db.CourseEnrollments.AsNoTracking();
        var e = await q.FirstOrDefaultAsync(x => x.CourseId == courseId && x.EmployeeId == me.Id, ct);
        if (e is null && (c.Status != CourseStatus.Published || !c.SelfEnroll)) throw DomainException.NotFound("Course");
        return (c, e, me);
    }

    public async Task<CoursePlayer> PlayerAsync(Guid courseId, SessionPrincipal actor, CancellationToken ct = default)
    {
        var (c, e, _) = await ForMeAsync(courseId, actor, false, ct);
        var done = e is null ? [] : Done(e.LessonsDone);
        var lessons = await db.CourseLessons.AsNoTracking().Where(l => l.CourseId == courseId).OrderBy(l => l.Order).ToListAsync(ct);
        var quiz = Quiz(c.QuizJson);
        return new CoursePlayer(await MineOf(c, e, ct), [.. lessons.Select(l => LessonOf(l, done.Contains(l.Id)))], e is null ? [] : [.. quiz.Select(q => new QuizQuestionView(q.Id, q.Text, [.. q.Options.Select(o => o.Text)]))], c.PassScore,
            e is not null && e.Status != EnrollmentStatus.Completed && lessons.All(l => done.Contains(l.Id)) && quiz.Count > 0);
    }

    public async Task<CoursePlayer> EnrollAsync(Guid courseId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (c, e, me) = await ForMeAsync(courseId, actor, true, ct);
        if (e is not null) return await PlayerAsync(courseId, actor, ct);
        if (c.Status != CourseStatus.Published) throw DomainException.Invariant("This course is no longer offered.");
        db.CourseEnrollments.Add(new CourseEnrollment { CourseId = courseId, EmployeeId = me.Id, EnrolledAt = clock.UtcNow });
        audit.Record("hr.course.enroll", "course", courseId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await PlayerAsync(courseId, actor, ct);
    }

    public async Task<CoursePlayer> LessonDoneAsync(Guid courseId, Guid lessonId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (c, e, _) = await ForMeAsync(courseId, actor, true, ct);
        if (e is null) throw DomainException.Invariant("Start the course first.");
        if (!await db.CourseLessons.AnyAsync(l => l.Id == lessonId && l.CourseId == courseId, ct)) throw DomainException.NotFound("Lesson");
        var done = Done(e.LessonsDone); done.Add(lessonId); e.LessonsDone = string.Join(',', done);
        e.StartedAt ??= clock.UtcNow; if (e.Status == EnrollmentStatus.NotStarted) e.Status = EnrollmentStatus.InProgress;
        var all = await db.CourseLessons.Where(l => l.CourseId == courseId).Select(l => l.Id).ToListAsync(ct);
        if (all.All(done.Contains) && Quiz(c.QuizJson).Count == 0 && e.Status != EnrollmentStatus.Completed) Complete(c, e);
        await db.SaveChangesAsync(ct);
        return await PlayerAsync(courseId, actor, ct);
    }

    private void Complete(Course c, CourseEnrollment e)
    {
        e.Status = EnrollmentStatus.Completed; e.CompletedAt = clock.UtcNow;
        if (c.Certificate) { e.CertificateNo = $"CERT/{Today.Year}/{e.Id.ToString("N")[..8].ToUpperInvariant()}"; e.ExpiresOn = c.ValidMonths is { } m ? Today.AddMonths(m) : null; }
    }

    public async Task<QuizResult> SubmitQuizAsync(Guid courseId, QuizSubmit r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var (c, e, _) = await ForMeAsync(courseId, actor, true, ct);
        if (e is null) throw DomainException.Invariant("Start the course first.");
        if (e.Status == EnrollmentStatus.Completed) throw DomainException.Conflict("You have already completed this course.");
        var quiz = Quiz(c.QuizJson); if (quiz.Count == 0) throw DomainException.Invariant("This course has no quiz.");
        var lessons = await db.CourseLessons.Where(l => l.CourseId == courseId).Select(l => l.Id).ToListAsync(ct); var done = Done(e.LessonsDone);
        if (!lessons.All(done.Contains)) throw DomainException.Invariant("Finish every lesson before the quiz.");
        if (e.QuizAttempts >= 5) throw DomainException.Invariant("You have used all 5 attempts. Ask HR to reset the course for you.");
        var answers = (r.Answers ?? []).Where(kv => quiz.Any(q => q.Id == kv.Key)).ToDictionary();
        if (answers.Count < quiz.Count) throw DomainException.Validation("Answer every question.", new Dictionary<string, string> { ["answers"] = $"{quiz.Count - answers.Count} question(s) left." });
        var score = RecruitmentService.Score(AssessmentKind.Test, quiz, answers).Score!.Value;
        e.QuizAttempts++; e.QuizScore = Math.Max(e.QuizScore ?? 0, score);
        var passed = score >= c.PassScore; if (passed) Complete(c, e);
        audit.Record("hr.course.quiz", "course", courseId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["score"] = score.ToString(), ["passed"] = passed.ToString() });
        await db.SaveChangesAsync(ct);
        return new QuizResult(score, passed, e.QuizAttempts, await MineOf(c, e, ct));
    }

    public async Task<EnrollmentRow> ResetAsync(Guid enrollmentId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = await db.CourseEnrollments.FirstOrDefaultAsync(x => x.Id == enrollmentId, ct) ?? throw DomainException.NotFound("Enrollment");
        e.QuizAttempts = 0; if (e.Status == EnrollmentStatus.Completed) { e.Status = EnrollmentStatus.InProgress; e.CompletedAt = null; e.CertificateNo = null; e.ExpiresOn = null; e.QuizScore = null; }
        audit.Record("hr.course.reset", "employee", e.EmployeeId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return (await RowsAsync(db.CourseEnrollments.AsNoTracking().Where(x => x.Id == enrollmentId), ct))[0];
    }

    public async Task<ExportFile> LessonFileAsync(Guid courseId, Guid lessonId, SessionPrincipal actor, CancellationToken ct = default)
    {
        var hr = await IsHrAsync(actor, ct);
        if (!hr) { var (_, e, _) = await ForMeAsync(courseId, actor, false, ct); if (e is null) throw DomainException.Invariant("Start the course first."); }
        var l = await db.CourseLessons.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lessonId && x.CourseId == courseId, ct) ?? throw DomainException.NotFound("Lesson");
        if (l.File is null) throw DomainException.NotFound("File");
        return new ExportFile(l.FileName ?? "lesson.pdf", "application/pdf", l.File);
    }

    /// <summary>The certificate as a PDF with a verification code (checked on the public /verify page). The person themself, or HR.</summary>
    public async Task<ExportFile> CertificateAsync(Guid enrollmentId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var hr = await IsHrAsync(actor, ct);
        var e = await db.CourseEnrollments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == enrollmentId, ct) ?? throw DomainException.NotFound("Certificate");
        var emp = await db.Employees.AsNoTracking().FirstAsync(x => x.Id == e.EmployeeId, ct);
        if (!hr && emp.UserId != actor.UserId) throw DomainException.NotFound("Certificate");
        if (e.CertificateNo is null) throw DomainException.Invariant("There is no certificate for this course yet.");
        var c = await db.Courses.AsNoTracking().FirstAsync(x => x.Id == e.CourseId, ct);
        var company = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.TenantId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";
        var seal = await seals.IssueAsync("certificate", e.Id, company, e.CertificateNo, DocumentSealService.Mask(emp.FullName), ct); await db.SaveChangesAsync(ct);
        var pdf = new SimplePdf(); double y = 140;
        pdf.Text(company, 50, 70, 12, true); pdf.Line(50, 82, 545, 1);
        pdf.Text("CERTIFICATE OF COMPLETION", 50, y, 22, true); y += 50;
        pdf.Text("This certifies that", 50, y, 11); y += 30;
        pdf.Text(emp.FullName, 50, y, 20, true); y += 36;
        pdf.Text("has successfully completed the course", 50, y, 11); y += 28;
        foreach (var line in SimplePdf.Wrap(c.Title, 60)) { pdf.Text(line, 50, y, 15, true); y += 22; }
        y += 16;
        pdf.Text($"Completed on {DateOnly.FromDateTime(e.CompletedAt!.Value.UtcDateTime.AddHours(7)):dd MMMM yyyy}{(e.QuizScore is { } s ? $" with a quiz score of {s}%" : "")}.", 50, y, 11); y += 18;
        if (e.ExpiresOn is { } exp) { pdf.Text($"Valid until {exp:dd MMMM yyyy}.", 50, y, 11); y += 18; }
        pdf.Text($"Certificate no. {e.CertificateNo}", 50, y + 20, 10);
        pdf.Text($"Verification code: {DocumentSealService.Format(seal)}  -  check it at {links.Origin.TrimEnd('/')}/verify", 50, 800, 8);
        audit.Record("hr.course.certificate", "employee", emp.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["certificate"] = e.CertificateNo });
        await db.SaveChangesAsync(ct);
        return new ExportFile($"certificate-{e.CertificateNo.Replace('/', '-').ToLowerInvariant()}.pdf", "application/pdf", pdf.Build());
    }

    public async Task<LearningSummary> SummaryAsync(CancellationToken ct = default)
    {
        var today = Today; var yearStart = new DateTimeOffset(today.Year, 1, 1, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime(); var soon = today.AddDays(60);
        var published = await db.Courses.CountAsync(c => c.Status == CourseStatus.Published, ct);
        var e = await db.CourseEnrollments.AsNoTracking().GroupBy(_ => 1).Select(g => new { N = g.Count(), C = g.Count(x => x.Status == EnrollmentStatus.Completed), O = g.Count(x => x.Status != EnrollmentStatus.Completed && x.DueDate != null && x.DueDate < today),
            Y = g.Count(x => x.CertificateNo != null && x.CompletedAt >= yearStart), X = g.Count(x => x.ExpiresOn != null && x.ExpiresOn >= today && x.ExpiresOn <= soon) }).FirstOrDefaultAsync(ct);
        return new LearningSummary(published, e?.N ?? 0, e?.C ?? 0, e is { N: > 0 } ? Math.Round(e.C * 100.0 / e.N, 1) : null, e?.O ?? 0, e?.Y ?? 0, e?.X ?? 0);
    }
}
