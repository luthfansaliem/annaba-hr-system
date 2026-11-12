using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Infrastructure.Persistence;

public class AnnabaDigitalDbContext(DbContextOptions<AnnabaDigitalDbContext> options) : DbContext(options), IAppDb
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> Credentials => Set<UserCredential>();
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<Avatar> Avatars => Set<Avatar>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<MfaFactor> MfaFactors => Set<MfaFactor>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<InvitationRole> InvitationRoles => Set<InvitationRole>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();
    public DbSet<RateLimitBucket> RateLimitBuckets => Set<RateLimitBucket>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<AccessReview> AccessReviews => Set<AccessReview>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<AccessReviewItem> AccessReviewItems => Set<AccessReviewItem>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AccessReviewSchedule> AccessReviewSchedules => Set<AccessReviewSchedule>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
    public DbSet<AccountClosureRequest> AccountClosureRequests => Set<AccountClosureRequest>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeEmployment> Employments => Set<EmployeeEmployment>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveLedgerEntry> LeaveLedger => Set<LeaveLedgerEntry>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<EmployeeTask> EmployeeTasks => Set<EmployeeTask>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();
    public DbSet<AttendancePeriod> AttendancePeriods => Set<AttendancePeriod>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<HrReminder> HrReminders => Set<HrReminder>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementReceipt> AnnouncementReceipts => Set<AnnouncementReceipt>();
    public DbSet<EmployeeChangeRequest> ChangeRequests => Set<EmployeeChangeRequest>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<EmployeeCompensation> Compensations => Set<EmployeeCompensation>();
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    public DbSet<PayRunItem> PayRunItems => Set<PayRunItem>();
    public DbSet<PayRunLine> PayRunLines => Set<PayRunLine>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<SalaryGrade> SalaryGrades => Set<SalaryGrade>();
    public DbSet<SalaryReview> SalaryReviews => Set<SalaryReview>();
    public DbSet<SalaryReviewItem> SalaryReviewItems => Set<SalaryReviewItem>();
    public DbSet<ClaimPolicy> ClaimPolicies => Set<ClaimPolicy>();
    public DbSet<CashAdvance> CashAdvances => Set<CashAdvance>();
    public DbSet<WageAdvance> WageAdvances => Set<WageAdvance>();
    public DbSet<CustomFieldDef> CustomFieldDefs => Set<CustomFieldDef>();
    public DbSet<LetterTemplate> LetterTemplates => Set<LetterTemplate>();
    public DbSet<IssuedLetter> IssuedLetters => Set<IssuedLetter>();
    public DbSet<HrForm> HrForms => Set<HrForm>();
    public DbSet<HrFormResponse> HrFormResponses => Set<HrFormResponse>();
    public DbSet<HelpdeskTicket> HelpdeskTickets => Set<HelpdeskTicket>();
    public DbSet<HelpdeskMessage> HelpdeskMessages => Set<HelpdeskMessage>();
    public DbSet<CompanyEvent> CompanyEvents => Set<CompanyEvent>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<HourlyLeave> HourlyLeaves => Set<HourlyLeave>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<Goal> Goals => Set<Goal>();
    public DbSet<GoalCheckIn> GoalCheckIns => Set<GoalCheckIn>();
    public DbSet<Competency> Competencies => Set<Competency>();
    public DbSet<ReviewCycle> ReviewCycles => Set<ReviewCycle>();
    public DbSet<ReviewAssignment> ReviewAssignments => Set<ReviewAssignment>();
    public DbSet<ReviewResult> ReviewResults => Set<ReviewResult>();
    public DbSet<DevelopmentPlan> DevelopmentPlans => Set<DevelopmentPlan>();
    public DbSet<SuccessionPlan> SuccessionPlans => Set<SuccessionPlan>();
    public DbSet<SuccessionCandidate> SuccessionCandidates => Set<SuccessionCandidate>();
    public DbSet<ManpowerPlan> ManpowerPlans => Set<ManpowerPlan>();
    public DbSet<JobRequisition> JobRequisitions => Set<JobRequisition>();
    public DbSet<JobPosting> JobPostings => Set<JobPosting>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ApplicationActivity> ApplicationActivities => Set<ApplicationActivity>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<AssessmentTemplate> AssessmentTemplates => Set<AssessmentTemplate>();
    public DbSet<AssessmentInvite> AssessmentInvites => Set<AssessmentInvite>();
    public DbSet<JobOffer> JobOffers => Set<JobOffer>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseLesson> CourseLessons => Set<CourseLesson>();
    public DbSet<CourseEnrollment> CourseEnrollments => Set<CourseEnrollment>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<OnCallDuty> OnCallDuties => Set<OnCallDuty>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TimesheetEntry> TimesheetEntries => Set<TimesheetEntry>();
    public DbSet<KioskDevice> KioskDevices => Set<KioskDevice>();
    public DbSet<FieldVisit> FieldVisits => Set<FieldVisit>();
    public DbSet<PayrollRuleVersion> PayrollRuleVersions => Set<PayrollRuleVersion>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<RecurringPayItem> RecurringPayItems => Set<RecurringPayItem>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<WorkLocation> WorkLocations => Set<WorkLocation>();
    public DbSet<ScimToken> ScimTokens => Set<ScimToken>();
    public DbSet<DocumentSeal> DocumentSeals => Set<DocumentSeal>();
    public DbSet<ApprovalRule> ApprovalRules => Set<ApprovalRule>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<HrPhoto> HrPhotos => Set<HrPhoto>();
    public DbSet<ScheduledEmploymentChange> ScheduledEmploymentChanges => Set<ScheduledEmploymentChange>();
    public DbSet<WeeklyPayout> WeeklyPayouts => Set<WeeklyPayout>();
    public DbSet<WeeklyPayoutItem> WeeklyPayoutItems => Set<WeeklyPayoutItem>();
    public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
    public DbSet<EmploymentLetter> EmploymentLetters => Set<EmploymentLetter>();
    public DbSet<RetroAdjustment> RetroAdjustments => Set<RetroAdjustment>();

    /// <summary>Set by the scoped factory registration; null for workers and design time (then nothing is filtered).</summary>
    public ITenantContext? Tenant { get; set; }
    private bool TenantScoped => Tenant?.Scoped == true;
    private Guid CurrentTenant => Tenant?.TenantId ?? Guid.Empty;

    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    private void Filter<T>(ModelBuilder b) where T : class, ITenantScoped => b.Entity<T>().HasQueryFilter(e => !TenantScoped || e.TenantId == CurrentTenant);

    /// <summary>In a tenant scope every new row belongs to that tenant, whatever the caller set. Unscoped writers (sign-in, registration) keep what they set, and audit/security rows take their person's tenant.</summary>
    private async Task StampTenantAsync(CancellationToken ct)
    {
        var added = ChangeTracker.Entries<ITenantScoped>().Where(e => e.State == EntityState.Added).ToList();
        if (added.Count == 0) return;
        if (TenantScoped) { foreach (var e in added) e.Entity.TenantId = CurrentTenant; return; }
        var loose = added.Where(e => e.Entity.TenantId == Domain.Entities.Tenant.DefaultId && e.Entity is AuditEvent or SecurityEvent).ToList();
        if (loose.Count == 0) return;
        Guid? Who(ITenantScoped t) => t is AuditEvent a ? a.ActorId : t is SecurityEvent x ? x.UserId : null;
        var ids = loose.Select(e => Who(e.Entity)).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        // People created in this same save (accepting an invitation) aren't in the database yet: take their organisation from the tracked row.
        var map = ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Added && ids.Contains(e.Entity.Id)).ToDictionary(e => e.Entity.Id, e => e.Entity.TenantId);
        var rest = ids.Where(i => !map.ContainsKey(i)).ToList();
        if (rest.Count > 0) foreach (var kv in await Users.IgnoreQueryFilters().Where(u => rest.Contains(u.Id)).Select(u => new { u.Id, u.TenantId }).ToListAsync(ct)) map[kv.Id] = kv.TenantId;
        foreach (var e in loose) if (Who(e.Entity) is { } id && map.TryGetValue(id, out var tid)) e.Entity.TenantId = tid;
    }

    /// <summary>Raised after a save that changed system settings, organisation settings or organisations (clears the short-lived caches).</summary>
    public Action? ReferenceDataChanged { get; set; }
    public event Action? PermissionDataChanged;

    private (bool Reference, bool Permissions) Touched()
    {
        var reference = false; var permissions = false;
        foreach (var e in ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (e.Entity is Domain.Entities.Tenant or SystemSetting or TenantSetting) reference = true;
            else if (e.Entity is UserRole or RolePermission or Role or User) permissions = true;
        }
        return (reference, permissions);
    }

    private void Raise((bool Reference, bool Permissions) t) { if (t.Reference) ReferenceDataChanged?.Invoke(); if (t.Permissions) PermissionDataChanged?.Invoke(); }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await StampTenantAsync(cancellationToken);
        var touched = Touched();
        var n = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        Raise(touched); return n;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantAsync(CancellationToken.None).GetAwaiter().GetResult();
        var touched = Touched();
        var n = base.SaveChanges(acceptAllChangesOnSuccess);
        Raise(touched); return n;
    }

    public void ResetTracking() => ChangeTracker.Clear();

    public Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct = default) => Database.ExecuteSqlAsync(sql, ct);

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null) return await work();
        await using IDbContextTransaction tx = await Database.BeginTransactionAsync(ct);
        try { var r = await work(); await tx.CommitAsync(ct); return r; }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<EmployeeChangeRequest>(e =>
        {
            e.ToTable("hr_change_requests"); e.HasKey(x => x.Id); e.Property(x => x.Field).HasMaxLength(30).IsRequired(); e.Property(x => x.ValueEnc).HasMaxLength(600).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300); e.Property(x => x.DecisionNote).HasMaxLength(300); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => new { x.TenantId, x.Status }); e.HasIndex(x => x.EmployeeId);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Announcement>(e =>
        {
            e.ToTable("hr_announcements"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Body).HasMaxLength(4000).IsRequired(); e.HasIndex(x => new { x.TenantId, x.PublishedAt });
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AnnouncementReceipt>(e =>
        {
            e.ToTable("hr_announcement_receipts"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.AnnouncementId, x.EmployeeId }).IsUnique();
            e.HasOne<Announcement>().WithMany().HasForeignKey(x => x.AnnouncementId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WebhookEndpoint>(e =>
        {
            e.ToTable("hr_webhook_endpoints"); e.HasKey(x => x.Id); e.Property(x => x.Url).HasMaxLength(500).IsRequired(); e.Property(x => x.SecretEnc).HasMaxLength(400).IsRequired(); e.Property(x => x.Events).HasMaxLength(400).IsRequired(); e.Property(x => x.Description).HasMaxLength(200);
        });
        b.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("hr_webhook_deliveries"); e.HasKey(x => x.Id); e.Property(x => x.Event).HasMaxLength(60).IsRequired(); e.Property(x => x.PayloadJson).HasColumnType("jsonb"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.LastError).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt }); e.HasIndex(x => x.EndpointId);
            e.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Shift>(e =>
        {
            e.ToTable("hr_shifts", t => { t.HasCheckConstraint("ck_hr_shifts_time", "end_time <> start_time"); t.HasCheckConstraint("ck_hr_shifts_days", "work_days BETWEEN 1 AND 127"); t.HasCheckConstraint("ck_hr_shifts_grace", "grace_minutes BETWEEN 0 AND 240"); });
            e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(30).IsRequired(); e.Property(x => x.Name).HasMaxLength(100).IsRequired(); e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        b.Entity<ShiftAssignment>(e =>
        {
            e.ToTable("hr_shift_assignments", t => t.HasCheckConstraint("ck_hr_shift_assign_range", "effective_to IS NULL OR effective_to >= effective_from"));
            e.HasKey(x => x.Id); e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("effective_to IS NULL");
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApprovalDelegation>(e =>
        {
            e.ToTable("hr_approval_delegations", t => { t.HasCheckConstraint("ck_hr_deleg_range", "end_date >= start_date"); t.HasCheckConstraint("ck_hr_deleg_self", "delegator_employee_id <> delegate_employee_id"); });
            e.HasKey(x => x.Id); e.HasIndex(x => x.DelegatorEmployeeId); e.HasIndex(x => x.DelegateEmployeeId);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.DelegatorEmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.DelegateEmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<HrReminder>(e =>
        {
            e.ToTable("hr_reminders"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(60).IsRequired();
            e.HasIndex(x => new { x.EmployeeId, x.Kind, x.DueOn, x.ThresholdDays }).IsUnique();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EmployeeTask>(e =>
        {
            e.ToTable("hr_employee_tasks"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => new { x.EmployeeId, x.Kind });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EmployeeDocument>(e =>
        {
            e.ToTable("hr_employee_documents", t => t.HasCheckConstraint("ck_hr_docs_size", "size_bytes > 0 AND size_bytes <= 5242880")); e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasMaxLength(20).IsRequired(); e.Property(x => x.FileName).HasMaxLength(200).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.EmployeeId); e.HasIndex(x => x.ExpiresOn); e.Property(x => x.ScanStatus).HasMaxLength(12).HasDefaultValue("not_scanned"); e.Property(x => x.Version).HasDefaultValue(1);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AttendanceCorrection>(e =>
        {
            e.ToTable("hr_attendance_corrections", t => t.HasCheckConstraint("ck_hr_att_corr_order", "clock_out > clock_in")); e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired(); e.Property(x => x.DecisionNote).HasMaxLength(300); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.EmployeeId, x.Date }); e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AttendancePeriod>(e => { e.ToTable("hr_attendance_periods"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique(); });
        b.Entity<OvertimeRequest>(e =>
        {
            e.ToTable("hr_overtime_requests", t => t.HasCheckConstraint("ck_hr_overtime_hours", "hours > 0 AND hours <= 4")); e.HasKey(x => x.Id);
            e.Property(x => x.Hours).HasPrecision(4, 2); e.Property(x => x.Reason).HasMaxLength(300).IsRequired(); e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.EmployeeId, x.Date }); e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EmployeeCompensation>(e =>
        {
            e.ToTable("hr_compensations", t => { t.HasCheckConstraint("ck_hr_comp_range", "effective_to IS NULL OR effective_to >= effective_from"); t.HasCheckConstraint("ck_hr_comp_ptkp", "ptkp_status IN ('TK0','TK1','TK2','TK3','K0','K1','K2','K3')"); });
            e.HasKey(x => x.Id); e.Property(x => x.BasicEnc).HasMaxLength(400).IsRequired(); e.Property(x => x.AllowanceEnc).HasMaxLength(400).IsRequired(); e.Property(x => x.OpeningEnc).HasMaxLength(600);
            e.Property(x => x.PayBasis).HasConversion<string>().HasMaxLength(10).HasDefaultValue(PayBasis.Monthly); e.Property(x => x.TaxScheme).HasConversion<string>().HasMaxLength(12).HasDefaultValue(TaxScheme.MonthlyTer);
            e.Property(x => x.PtkpStatus).HasMaxLength(4).IsRequired(); e.Property(x => x.JkkRate).HasPrecision(6, 5); e.Property(x => x.ChangeReason).HasMaxLength(300);
            e.Property(x => x.TaxTreatment).HasConversion<string>().HasMaxLength(10).HasDefaultValue(TaxTreatment.Gross); e.Property(x => x.TreatyRate).HasPrecision(5, 4);
            e.HasOne<SalaryGrade>().WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("effective_to IS NULL"); // exactly one open period per employee
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PayRun>(e =>
        {
            e.ToTable("hr_pay_runs", t => t.HasCheckConstraint("ck_hr_pay_runs_month", "month BETWEEN 1 AND 12"));
            e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.RulesVersion).HasMaxLength(40); e.Property(x => x.RulesJson).HasColumnType("jsonb");
            e.Property(x => x.Note).HasMaxLength(300); e.Property(x => x.Version).IsConcurrencyToken();
            foreach (var p in new[] { nameof(PayRun.TotalGross), nameof(PayRun.TotalTax), nameof(PayRun.TotalNet), nameof(PayRun.TotalEmployerCost) }) e.Property(p).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.Year, x.Month }).IsUnique(); // one run per entity per month; corrections are made as adjustments in a later run
            e.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PayRunItem>(e =>
        {
            e.ToTable("hr_pay_run_items"); e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeNo).HasMaxLength(30); e.Property(x => x.EmployeeName).HasMaxLength(200); e.Property(x => x.Ptkp).HasMaxLength(4); e.Property(x => x.TaxMethod).HasMaxLength(12); e.Property(x => x.Warnings).HasMaxLength(500);
            e.Property(x => x.ProrateFactor).HasPrecision(7, 6); e.Property(x => x.DailyRate).HasPrecision(18, 2); e.Property(x => x.DaysPaid).HasPrecision(6, 2); e.Property(x => x.TaxTreatment).HasConversion<string>().HasMaxLength(10).HasDefaultValue(TaxTreatment.Gross);
            foreach (var p in new[] { nameof(PayRunItem.Basic), nameof(PayRunItem.Allowance), nameof(PayRunItem.UnpaidDeduction), nameof(PayRunItem.TaxableExtras), nameof(PayRunItem.NonTaxableExtras), nameof(PayRunItem.OtherDeductions), nameof(PayRunItem.OvertimePay),
                nameof(PayRunItem.Gross), nameof(PayRunItem.TaxableGross), nameof(PayRunItem.KesEmployee), nameof(PayRunItem.JhtEmployee), nameof(PayRunItem.JpEmployee), nameof(PayRunItem.KesEmployer), nameof(PayRunItem.JhtEmployer),
                nameof(PayRunItem.JpEmployer), nameof(PayRunItem.JkkEmployer), nameof(PayRunItem.JkmEmployer), nameof(PayRunItem.Tax), nameof(PayRunItem.TaxAllowance), nameof(PayRunItem.TaxBorne), nameof(PayRunItem.Net), nameof(PayRunItem.EmployerCost) }) e.Property(p).HasPrecision(18, 2);
            e.HasIndex(x => new { x.PayRunId, x.EmployeeId }).IsUnique(); e.HasIndex(x => x.EmployeeId);
            e.HasOne<PayRun>().WithMany().HasForeignKey(x => x.PayRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PayRunLine>(e =>
        {
            e.ToTable("hr_pay_run_lines", t => t.HasCheckConstraint("ck_hr_pay_run_lines_amount", "amount > 0")); e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(100).IsRequired(); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => new { x.PayRunId, x.EmployeeId });
            e.HasOne<PayRun>().WithMany().HasForeignKey(x => x.PayRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RetroAdjustment>(e =>
        {
            e.ToTable("hr_retro_adjustments", t => t.HasCheckConstraint("ck_hr_retro_nonzero", "amount <> 0")); e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Note).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.EmployeeId, x.AppliedRunId });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PayrollRuleVersion>(e =>
        {
            e.ToTable("hr_payroll_rule_versions", t => t.HasCheckConstraint("ck_hr_rule_first_of_month", "extract(day from effective_from) = 1")); e.HasKey(x => x.Id);
            e.Property(x => x.Label).HasMaxLength(60).IsRequired(); e.Property(x => x.RulesJson).HasColumnType("jsonb"); e.Property(x => x.Reason).HasMaxLength(300).IsRequired(); e.Property(x => x.Changes).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.EffectiveFrom }).IsUnique();
        });
        b.Entity<WeeklyPayout>(e =>
        {
            e.ToTable("hr_weekly_payouts", t => { t.HasCheckConstraint("ck_hr_weekly_payouts_week", "extract(isodow from week_start) = 1 AND week_end = week_start + 6"); t.HasCheckConstraint("ck_hr_weekly_payouts_holdback", "holdback_pct BETWEEN 0 AND 50"); });
            e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Note).HasMaxLength(300); e.Property(x => x.HoldbackPct).HasPrecision(5, 2);
            e.Property(x => x.TotalGross).HasPrecision(18, 2); e.Property(x => x.TotalNet).HasPrecision(18, 2); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.LegalEntityId, x.WeekStart }).IsUnique();
            e.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WeeklyPayoutItem>(e =>
        {
            e.ToTable("hr_weekly_payout_items"); e.HasKey(x => x.Id); e.Property(x => x.EmployeeNo).HasMaxLength(30); e.Property(x => x.EmployeeName).HasMaxLength(200); e.Property(x => x.Days).HasPrecision(5, 2);
            foreach (var p in new[] { nameof(WeeklyPayoutItem.DailyRate), nameof(WeeklyPayoutItem.Gross), nameof(WeeklyPayoutItem.Holdback), nameof(WeeklyPayoutItem.Net) }) e.Property(p).HasPrecision(18, 2);
            e.HasIndex(x => new { x.PayoutId, x.EmployeeId }).IsUnique(); e.HasIndex(x => x.EmployeeId).HasFilter("settled_run_id IS NULL");
            e.HasOne<WeeklyPayout>().WithMany().HasForeignKey(x => x.PayoutId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RecurringPayItem>(e =>
        {
            e.ToTable("hr_recurring_pay_items", t => { t.HasCheckConstraint("ck_hr_recurring_amount", "amount > 0"); t.HasCheckConstraint("ck_hr_recurring_months", "extract(day from \"from\") = 1 AND (through IS NULL OR (extract(day from through) = 1 AND through >= \"from\"))"); });
            e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Label).HasMaxLength(100).IsRequired(); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => x.EmployeeId).HasFilter("ended_at IS NULL");
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Loan>(e =>
        {
            e.ToTable("hr_loans", t => { t.HasCheckConstraint("ck_hr_loans_amounts", "principal > 0 AND installment > 0 AND remaining >= 0"); });
            e.HasKey(x => x.Id); e.Property(x => x.Principal).HasPrecision(18, 2); e.Property(x => x.Installment).HasPrecision(18, 2); e.Property(x => x.Remaining).HasPrecision(18, 2);
            e.Property(x => x.Note).HasMaxLength(200); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.HasIndex(x => new { x.EmployeeId, x.Status }); e.Property(x => x.InterestRatePct).HasPrecision(6, 3); e.Property(x => x.TotalPayable).HasPrecision(18, 2);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SalaryGrade>(e =>
        {
            e.ToTable("hr_salary_grades", t => t.HasCheckConstraint("ck_hr_salary_grades_range", "min_pay >= 0 AND min_pay <= mid_pay AND mid_pay <= max_pay"));
            e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(20).IsRequired(); e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            foreach (var p in new[] { nameof(SalaryGrade.MinPay), nameof(SalaryGrade.MidPay), nameof(SalaryGrade.MaxPay) }) e.Property(p).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        b.Entity<SalaryReview>(e =>
        {
            e.ToTable("hr_salary_reviews"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(120).IsRequired(); e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken();
        });
        b.Entity<SalaryReviewItem>(e =>
        {
            e.ToTable("hr_salary_review_items"); e.HasKey(x => x.Id); e.Property(x => x.CurrentEnc).HasMaxLength(600).IsRequired(); e.Property(x => x.ProposedEnc).HasMaxLength(600).IsRequired();
            e.Property(x => x.Note).HasMaxLength(200); e.Property(x => x.ApplyError).HasMaxLength(300);
            e.HasIndex(x => new { x.ReviewId, x.EmployeeId }).IsUnique();
            e.HasOne<SalaryReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ClaimPolicy>(e =>
        {
            e.ToTable("hr_claim_policies", t => t.HasCheckConstraint("ck_hr_claim_policies_limits", "(annual_limit IS NULL OR annual_limit > 0) AND (per_claim_limit IS NULL OR per_claim_limit > 0)"));
            e.HasKey(x => x.Id); e.Property(x => x.Category).HasMaxLength(30).IsRequired(); e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.AnnualLimit).HasPrecision(18, 2); e.Property(x => x.PerClaimLimit).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Category, x.GradeId }).IsUnique().HasFilter("active").AreNullsDistinct(false);   // one active rule per category and grade
            e.HasOne<SalaryGrade>().WithMany().HasForeignKey(x => x.GradeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CashAdvance>(e =>
        {
            e.ToTable("hr_cash_advances", t => t.HasCheckConstraint("ck_hr_cash_advances_amounts", "amount > 0 AND (spent_amount IS NULL OR spent_amount >= 0)"));
            e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.SpentAmount).HasPrecision(18, 2); e.Property(x => x.Purpose).HasMaxLength(300).IsRequired();
            e.Property(x => x.DecisionNote).HasMaxLength(300); e.Property(x => x.SettlementNote).HasMaxLength(300); e.Property(x => x.ReceiptName).HasMaxLength(100); e.Property(x => x.ReceiptType).HasMaxLength(60);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => new { x.EmployeeId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Goal>(e =>
        {
            e.ToTable("hr_goals", t => t.HasCheckConstraint("ck_hr_goals_weight", "weight >= 0 AND weight <= 100")); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Description).HasMaxLength(1000); e.Property(x => x.Unit).HasMaxLength(20);
            foreach (var p in new[] { nameof(Goal.StartValue), nameof(Goal.TargetValue), nameof(Goal.CurrentValue) }) e.Property(p).HasPrecision(18, 4);
            e.Property(x => x.Weight).HasPrecision(5, 2); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => new { x.OwnerEmployeeId, x.Year }); e.HasIndex(x => x.ParentId);
            e.HasOne<Goal>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.OwnerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<GoalCheckIn>(e =>
        {
            e.ToTable("hr_goal_check_ins"); e.HasKey(x => x.Id); e.Property(x => x.Value).HasPrecision(18, 4); e.Property(x => x.Note).HasMaxLength(500); e.HasIndex(x => x.GoalId);
            e.HasOne<Goal>().WithMany().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Competency>(e =>
        {
            e.ToTable("hr_competencies"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(20).IsRequired(); e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Category).HasMaxLength(20); e.Property(x => x.Description).HasMaxLength(1000); e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        b.Entity<ReviewCycle>(e =>
        {
            e.ToTable("hr_review_cycles", t => t.HasCheckConstraint("ck_hr_review_cycles_weights", "goals_weight + competency_weight = 100")); e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.Property(x => x.CompetencyIds).HasMaxLength(4000); e.Property(x => x.GoalsWeight).HasPrecision(5, 2); e.Property(x => x.CompetencyWeight).HasPrecision(5, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken();
        });
        b.Entity<ReviewAssignment>(e =>
        {
            e.ToTable("hr_review_assignments"); e.HasKey(x => x.Id); e.Property(x => x.AnswersJson).HasColumnType("jsonb"); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.HasIndex(x => new { x.CycleId, x.EmployeeId, x.ReviewerEmployeeId, x.Kind }).IsUnique(); e.HasIndex(x => x.ReviewerEmployeeId);
            e.HasOne<ReviewCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReviewResult>(e =>
        {
            e.ToTable("hr_review_results", t => t.HasCheckConstraint("ck_hr_review_results_ranges", "(calibrated_rating IS NULL OR calibrated_rating BETWEEN 1 AND 5) AND (potential IS NULL OR potential BETWEEN 1 AND 3)")); e.HasKey(x => x.Id);
            foreach (var p in new[] { nameof(ReviewResult.GoalScore), nameof(ReviewResult.CompetencyScore), nameof(ReviewResult.FinalScore) }) e.Property(p).HasPrecision(5, 2);
            e.Property(x => x.CalibrationNote).HasMaxLength(500); e.HasIndex(x => new { x.CycleId, x.EmployeeId }).IsUnique();
            e.HasOne<ReviewCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DevelopmentPlan>(e =>
        {
            e.ToTable("hr_development_plans"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Aim).HasMaxLength(1000);
            e.Property(x => x.ActionsJson).HasColumnType("jsonb"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); e.HasIndex(x => x.EmployeeId);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SuccessionPlan>(e => { e.ToTable("hr_succession_plans"); e.HasKey(x => x.Id); e.Property(x => x.Position).HasMaxLength(150).IsRequired(); e.Property(x => x.Note).HasMaxLength(500); });
        b.Entity<SuccessionCandidate>(e =>
        {
            e.ToTable("hr_succession_candidates"); e.HasKey(x => x.Id); e.Property(x => x.Readiness).HasConversion<string>().HasMaxLength(16); e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.PlanId, x.EmployeeId }).IsUnique(); e.HasOne<SuccessionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        // stage 17: recruitment
        b.Entity<ManpowerPlan>(e => { e.ToTable("hr_manpower_plans"); e.HasKey(x => x.Id); e.Property(x => x.JobTitle).HasMaxLength(150).IsRequired(); e.Property(x => x.MonthlyBudget).HasPrecision(18, 2); e.Property(x => x.Note).HasMaxLength(300); e.HasIndex(x => new { x.TenantId, x.Year }); });
        b.Entity<JobRequisition>(e =>
        {
            e.ToTable("hr_job_requisitions"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(30).IsRequired(); e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique(); e.Property(x => x.JobTitle).HasMaxLength(150).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.SalaryMin).HasPrecision(18, 2); e.Property(x => x.SalaryMax).HasPrecision(18, 2);
            e.Property(x => x.Justification).HasMaxLength(1000).IsRequired(); e.Property(x => x.DecisionNote).HasMaxLength(300); e.Property(x => x.Version).IsConcurrencyToken();
        });
        b.Entity<JobPosting>(e =>
        {
            e.ToTable("hr_job_postings"); e.HasKey(x => x.Id); e.Property(x => x.PublicId).HasMaxLength(16).IsRequired(); e.HasIndex(x => x.PublicId).IsUnique(); e.Property(x => x.Title).HasMaxLength(150).IsRequired(); e.Property(x => x.Location).HasMaxLength(150);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Description).HasMaxLength(8000).IsRequired(); e.Property(x => x.Requirements).HasMaxLength(4000);
            e.Property(x => x.SalaryMin).HasPrecision(18, 2); e.Property(x => x.SalaryMax).HasPrecision(18, 2); e.Property(x => x.Stages).HasMaxLength(400).IsRequired();
            e.HasOne<JobRequisition>().WithMany().HasForeignKey(x => x.RequisitionId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Candidate>(e =>
        {
            e.ToTable("hr_candidates"); e.HasKey(x => x.Id); e.Property(x => x.FullName).HasMaxLength(200).IsRequired(); e.Property(x => x.Email).HasMaxLength(254).IsRequired(); e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(30); e.Property(x => x.Source).HasMaxLength(30); e.Property(x => x.CvName).HasMaxLength(200);
        });
        b.Entity<JobApplication>(e =>
        {
            e.ToTable("hr_job_applications"); e.HasKey(x => x.Id); e.Property(x => x.Stage).HasMaxLength(30); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); e.Property(x => x.CoverLetter).HasMaxLength(4000); e.Property(x => x.RejectReason).HasMaxLength(300);
            e.HasIndex(x => new { x.PostingId, x.CandidateId }).IsUnique(); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<JobPosting>().WithMany().HasForeignKey(x => x.PostingId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Candidate>().WithMany().HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ApplicationActivity>(e => { e.ToTable("hr_application_activities"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(20); e.Property(x => x.Text).HasMaxLength(2000).IsRequired(); e.HasIndex(x => x.ApplicationId); e.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Interview>(e =>
        {
            e.ToTable("hr_interviews"); e.HasKey(x => x.Id); e.Property(x => x.Location).HasMaxLength(300); e.Property(x => x.Interviewers).HasMaxLength(800); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Recommendation).HasMaxLength(10); e.Property(x => x.Feedback).HasMaxLength(2000); e.HasIndex(x => x.ApplicationId); e.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AssessmentTemplate>(e => { e.ToTable("hr_assessment_templates"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(12); e.Property(x => x.QuestionsJson).HasColumnType("jsonb"); });
        b.Entity<AssessmentInvite>(e =>
        {
            e.ToTable("hr_assessment_invites"); e.HasKey(x => x.Id); e.Property(x => x.TokenHash).HasMaxLength(100).IsRequired(); e.HasIndex(x => x.TokenHash).IsUnique(); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Result).HasMaxLength(20); e.Property(x => x.AnswersJson).HasColumnType("jsonb"); e.HasIndex(x => x.ApplicationId);
            e.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade); e.HasOne<AssessmentTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<JobOffer>(e =>
        {
            e.ToTable("hr_job_offers"); e.HasKey(x => x.Id); e.Property(x => x.JobTitle).HasMaxLength(150).IsRequired(); e.Property(x => x.MonthlySalary).HasPrecision(18, 2); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Terms).HasMaxLength(4000); e.Property(x => x.TokenHash).HasMaxLength(100); e.HasIndex(x => x.TokenHash).IsUnique(); e.Property(x => x.ResponseNote).HasMaxLength(500);
            e.HasIndex(x => x.ApplicationId); e.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Cascade);
        });
        // stage 18: learning
        b.Entity<Course>(e =>
        {
            e.ToTable("hr_courses"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.Category).HasMaxLength(30);
            e.Property(x => x.QuizJson).HasColumnType("jsonb"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<CourseLesson>(e =>
        {
            e.ToTable("hr_course_lessons"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Body).HasMaxLength(20000);
            e.Property(x => x.Url).HasMaxLength(1000); e.Property(x => x.FileName).HasMaxLength(200); e.HasIndex(x => new { x.CourseId, x.Order }); e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CourseEnrollment>(e =>
        {
            e.ToTable("hr_course_enrollments"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.LessonsDone).HasMaxLength(4000); e.Property(x => x.CertificateNo).HasMaxLength(30);
            e.HasIndex(x => new { x.CourseId, x.EmployeeId }).IsUnique(); e.HasIndex(x => x.EmployeeId);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PushSubscription>(e =>
        {
            e.ToTable("push_subscriptions"); e.HasKey(x => x.Id); e.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired(); e.Property(x => x.P256dh).HasMaxLength(200).IsRequired(); e.Property(x => x.Auth).HasMaxLength(100).IsRequired();
            e.Property(x => x.Device).HasMaxLength(200); e.HasIndex(x => x.Endpoint).IsUnique(); e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<HourlyLeave>(e =>
        {
            e.ToTable("hr_hourly_leaves", t => t.HasCheckConstraint("ck_hr_hourly_leaves_range", "\"to\" > \"from\"")); e.HasKey(x => x.Id); e.Ignore(x => x.Hours);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired(); e.Property(x => x.DecisionNote).HasMaxLength(300); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.EmployeeId, x.Date }); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RosterEntry>(e =>
        {
            e.ToTable("hr_roster_entries"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OnCallDuty>(e =>
        {
            e.ToTable("hr_on_call_duties", t => t.HasCheckConstraint("ck_hr_on_call_allowance", "allowance >= 0")); e.HasKey(x => x.Id); e.Property(x => x.Allowance).HasPrecision(18, 2); e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique(); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Project>(e =>
        {
            e.ToTable("hr_projects"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(20).IsRequired(); e.Property(x => x.Name).HasMaxLength(120).IsRequired(); e.Property(x => x.Client).HasMaxLength(120);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        b.Entity<TimesheetEntry>(e =>
        {
            e.ToTable("hr_timesheet_entries", t => t.HasCheckConstraint("ck_hr_timesheet_hours", "hours > 0 AND hours <= 24")); e.HasKey(x => x.Id); e.Property(x => x.Hours).HasPrecision(5, 2);
            e.Property(x => x.Note).HasMaxLength(300); e.Property(x => x.DecisionNote).HasMaxLength(300); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.HasIndex(x => new { x.EmployeeId, x.Date });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict); e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<KioskDevice>(e =>
        {
            e.ToTable("hr_kiosk_devices"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(100).IsRequired(); e.Property(x => x.TokenHash).HasMaxLength(100).IsRequired(); e.HasIndex(x => x.TokenHash).IsUnique();
        });
        b.Entity<FieldVisit>(e =>
        {
            e.ToTable("hr_field_visits"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(10); e.Property(x => x.Place).HasMaxLength(150).IsRequired(); e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.EmployeeId, x.Date }); e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CustomFieldDef>(e =>
        {
            e.ToTable("hr_custom_fields"); e.HasKey(x => x.Id); e.Property(x => x.Key).HasMaxLength(40).IsRequired(); e.Property(x => x.Label).HasMaxLength(100).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10); e.Property(x => x.Options).HasMaxLength(1000); e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        });
        b.Entity<LetterTemplate>(e =>
        {
            e.ToTable("hr_letter_templates"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(100).IsRequired(); e.Property(x => x.Prefix).HasMaxLength(20).IsRequired();
            e.Property(x => x.Title).HasMaxLength(150).IsRequired(); e.Property(x => x.Body).HasMaxLength(20000).IsRequired();
        });
        b.Entity<IssuedLetter>(e =>
        {
            e.ToTable("hr_issued_letters"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(60).IsRequired(); e.Property(x => x.Title).HasMaxLength(150).IsRequired(); e.Property(x => x.Body).HasMaxLength(30000).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique(); e.HasIndex(x => x.EmployeeId);
            e.HasOne<LetterTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<HrForm>(e =>
        {
            e.ToTable("hr_forms"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(150).IsRequired(); e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.QuestionsJson).HasColumnType("jsonb"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        });
        b.Entity<HrFormResponse>(e =>
        {
            e.ToTable("hr_form_responses"); e.HasKey(x => x.Id); e.Property(x => x.AnswersJson).HasColumnType("jsonb"); e.HasIndex(x => new { x.FormId, x.EmployeeId }).IsUnique();
            e.HasOne<HrForm>().WithMany().HasForeignKey(x => x.FormId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<HelpdeskTicket>(e =>
        {
            e.ToTable("hr_helpdesk_tickets"); e.HasKey(x => x.Id); e.Property(x => x.Category).HasMaxLength(30); e.Property(x => x.Subject).HasMaxLength(150).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique(); e.HasIndex(x => new { x.EmployeeId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<HelpdeskMessage>(e =>
        {
            e.ToTable("hr_helpdesk_messages"); e.HasKey(x => x.Id); e.Property(x => x.Body).HasMaxLength(4000).IsRequired(); e.HasIndex(x => x.TicketId);
            e.HasOne<HelpdeskTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CompanyEvent>(e =>
        {
            e.ToTable("hr_company_events", t => t.HasCheckConstraint("ck_hr_company_events_range", "end_date >= start_date")); e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(150).IsRequired(); e.Property(x => x.Location).HasMaxLength(150); e.Property(x => x.Description).HasMaxLength(1000); e.HasIndex(x => x.StartDate);
        });
        b.Entity<Branch>(e =>
        {
            e.ToTable("hr_branches"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(20).IsRequired(); e.Property(x => x.Name).HasMaxLength(120).IsRequired(); e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Address).HasMaxLength(300); e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WageAdvance>(e =>
        {
            e.ToTable("hr_wage_advances", t => t.HasCheckConstraint("ck_hr_wage_advances_amount", "amount > 0 AND month BETWEEN 1 AND 12"));
            e.HasKey(x => x.Id); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Reason).HasMaxLength(200); e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => new { x.EmployeeId, x.Year, x.Month });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LeaveType>(e =>
        {
            e.ToTable("hr_leave_types", t => { t.HasCheckConstraint("ck_hr_leave_types_quota", "annual_quota IS NULL OR annual_quota >= 0"); t.HasCheckConstraint("ck_hr_leave_types_expiry_month", "carry_over_expiry_month IS NULL OR carry_over_expiry_month BETWEEN 1 AND 12"); });
            e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(30).IsRequired(); e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.AnnualQuota).HasPrecision(6, 2); e.Property(x => x.CarryOverMax).HasPrecision(6, 2); e.Property(x => x.HrApprovalFromDays).HasPrecision(5, 1);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        b.Entity<Holiday>(e =>
        {
            e.ToTable("hr_holidays"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.HasIndex(x => new { x.TenantId, x.Date }).IsUnique();
        });
        b.Entity<LeaveLedgerEntry>(e =>
        {
            e.ToTable("hr_leave_ledger"); e.HasKey(x => x.Id); e.Property(x => x.Days).HasPrecision(6, 2); e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.Year });
            e.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.Year }, "ix_hr_leave_ledger_one_expiry").IsUnique().HasFilter("kind = 'Expire'"); // carried-over days expire at most once a year
            e.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.Year }).IsUnique().HasFilter("kind = 'Grant'"); // the yearly entitlement is granted exactly once, even when two requests ask for the balance at the same moment
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LeaveRequest>(e =>
        {
            e.ToTable("hr_leave_requests", t =>
            {
                t.HasCheckConstraint("ck_hr_leave_requests_range", "end_date >= start_date");
                t.HasCheckConstraint("ck_hr_leave_requests_days", "days > 0");
            });
            e.HasKey(x => x.Id); e.Property(x => x.Days).HasPrecision(6, 2); e.Property(x => x.Reason).HasMaxLength(500); e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.EmployeeId, x.StartDate }); e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AttendanceLog>(e =>
        {
            e.ToTable("hr_attendance_logs", t => t.HasCheckConstraint("ck_hr_attendance_order", "clock_out IS NULL OR clock_out >= clock_in"));
            e.HasKey(x => x.Id); e.Property(x => x.Ip).HasMaxLength(64); e.Property(x => x.Source).HasMaxLength(12).HasDefaultValue("clock"); e.Property(x => x.WorkMode).HasConversion<string>().HasMaxLength(10).HasDefaultValue(WorkMode.Office);
            e.HasIndex(x => new { x.EmployeeId, x.Date });
            e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("clock_out IS NULL"); // at most one open session per person
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OrgUnit>(e =>
        {
            e.ToTable("hr_org_units"); e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired(); e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UserNotification>(e =>
        {
            e.ToTable("hr_notifications"); e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40).IsRequired(); e.Property(x => x.Title).HasMaxLength(200).IsRequired(); e.Property(x => x.Body).HasMaxLength(500); e.Property(x => x.Link).HasMaxLength(200);
            e.HasIndex(x => new { x.UserId, x.CreatedAt }); e.HasIndex(x => x.CreatedAt).HasFilter("pushed_at IS NULL");   // the push worker looks for the newest not yet pushed
            e.HasIndex(x => x.UserId).HasFilter("read_at IS NULL"); // the unread badge
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<NotificationPreference>(e =>
        {
            e.ToTable("hr_notification_preferences"); e.HasKey(x => x.UserId); e.Property(x => x.InAppOff).HasMaxLength(200); e.Property(x => x.EmailOff).HasMaxLength(200);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkLocation>(e =>
        {
            e.ToTable("hr_work_locations", t => t.HasCheckConstraint("ck_hr_work_locations_geo", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 AND radius_meters BETWEEN 20 AND 5000"));
            e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(100).IsRequired(); e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });
        b.Entity<ExpenseClaim>(e =>
        {
            e.ToTable("hr_expense_claims", t => { t.HasCheckConstraint("ck_hr_claims_amount", "amount > 0"); t.HasCheckConstraint("ck_hr_claims_receipt", "receipt IS NULL OR octet_length(receipt) <= 5242880"); });
            e.HasKey(x => x.Id); e.Property(x => x.Category).HasMaxLength(20).IsRequired(); e.Property(x => x.Description).HasMaxLength(300).IsRequired(); e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.ReceiptName).HasMaxLength(60); e.Property(x => x.ReceiptType).HasMaxLength(60); e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12); e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.EmployeeId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EmploymentLetter>(e =>
        {
            e.ToTable("hr_employment_letters"); e.HasKey(x => x.Id); e.Property(x => x.Purpose).HasMaxLength(200).IsRequired(); e.Property(x => x.Number).HasMaxLength(30); e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.Property(x => x.SnapshotJson).HasColumnType("jsonb"); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique().HasFilter("number IS NOT NULL"); e.HasIndex(x => new { x.EmployeeId, x.Status });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ScimToken>(e =>
        {
            e.ToTable("scim_tokens"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(80).IsRequired(); e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => x.TenantId);
            e.HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ScheduledEmploymentChange>(e =>
        {
            e.ToTable("hr_scheduled_employment_changes"); e.HasKey(x => x.Id); e.Property(x => x.RequestJson).HasColumnType("jsonb"); e.Property(x => x.Summary).HasMaxLength(300); e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => x.EffectiveFrom).HasFilter("applied_at IS NULL AND cancelled_at IS NULL");
            e.HasIndex(x => new { x.EmployeeId, x.EffectiveFrom }).IsUnique().HasFilter("applied_at IS NULL AND cancelled_at IS NULL");
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<HrPhoto>(e =>
        {
            e.ToTable("hr_photos", t => t.HasCheckConstraint("ck_hr_photos_size", "octet_length(content) BETWEEN 1 AND 1048576"));
            e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(12).IsRequired(); e.Property(x => x.ContentType).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.Kind, x.OwnerId }).IsUnique(); e.HasIndex(x => new { x.Kind, x.CreatedAt });
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ReportSchedule>(e =>
        {
            e.ToTable("hr_report_schedules", t => t.HasCheckConstraint("ck_hr_report_schedules_day", "(frequency = 'weekly' AND day BETWEEN 1 AND 7) OR (frequency = 'monthly' AND day BETWEEN 1 AND 28)"));
            e.HasKey(x => x.Id); e.Property(x => x.Report).HasMaxLength(30).IsRequired(); e.Property(x => x.Frequency).HasMaxLength(10).IsRequired();
            e.HasIndex(x => new { x.UserId, x.Report, x.Frequency, x.Day }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ApprovalRule>(e =>
        {
            e.ToTable("hr_approval_rules"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(12).IsRequired(); e.Property(x => x.Steps).HasMaxLength(40).IsRequired(); e.Property(x => x.MinAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.TenantId, x.Kind, x.LeaveTypeId, x.MinAmount }).IsUnique();
            e.HasOne<LeaveType>().WithMany().HasForeignKey(x => x.LeaveTypeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ApprovalStep>(e =>
        {
            e.ToTable("hr_approval_steps"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(12).IsRequired(); e.Property(x => x.Approver).HasMaxLength(12).IsRequired();
            e.HasIndex(x => new { x.Kind, x.RequestId, x.StepNo }).IsUnique();   // two people cannot both take the same step
        });
        b.Entity<DocumentSeal>(e =>
        {
            e.ToTable("hr_document_seals"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(12).IsRequired(); e.Property(x => x.Code).HasMaxLength(12).IsRequired();
            e.Property(x => x.Company).HasMaxLength(200); e.Property(x => x.Reference).HasMaxLength(60); e.Property(x => x.Holder).HasMaxLength(200);
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.Kind, x.RefId }).IsUnique();
        });
        b.Entity<LegalEntity>(e =>
        {
            e.ToTable("hr_legal_entities", t => t.HasCheckConstraint("ck_hr_legal_entities_tax_id", "tax_id IS NULL OR tax_id ~ '^[0-9]{15,16}$'"));
            e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(200).IsRequired(); e.Property(x => x.TaxId).HasMaxLength(16); e.Property(x => x.Address).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => x.TenantId).IsUnique().HasFilter("is_default"); // exactly one default per tenant
        });
        b.Entity<Employee>(e =>
        {
            e.ToTable("hr_employees", t =>
            {
                t.HasCheckConstraint("ck_hr_employees_name_len", "length(trim(full_name)) >= 2");
                t.HasCheckConstraint("ck_hr_employees_term_after_hire", "termination_date IS NULL OR termination_date >= hire_date");
                e.Property(x => x.CustomFieldsJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeNo).HasMaxLength(30).IsRequired(); e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.WorkEmail).HasMaxLength(254); e.Property(x => x.Phone).HasMaxLength(40); e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.EmergencyContactName).HasMaxLength(200); e.Property(x => x.EmergencyContactPhone).HasMaxLength(40);
            e.Property(x => x.BankName).HasMaxLength(80); e.Property(x => x.TerminationReason).HasMaxLength(300);
            e.Property(x => x.BpjsKesNoEnc).HasMaxLength(400); e.Property(x => x.BpjsTkNoEnc).HasMaxLength(400); e.Property(x => x.Religion).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.NationalIdEnc).HasMaxLength(400); e.Property(x => x.TaxIdEnc).HasMaxLength(400); e.Property(x => x.BankAccountEnc).HasMaxLength(400);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Gender).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.MaritalStatus).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.EmployeeNo }).IsUnique();
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasIndex(x => x.LegalEntityId);
            e.HasOne<LegalEntity>().WithMany().HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Employments).WithOne().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EmployeeEmployment>(e =>
        {
            e.ToTable("hr_employments", t => t.HasCheckConstraint("ck_hr_employments_range", "effective_to IS NULL OR effective_to >= effective_from"));
            e.HasKey(x => x.Id);
            e.Property(x => x.JobTitle).HasMaxLength(150).IsRequired(); e.Property(x => x.Grade).HasMaxLength(30); e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.ChangeReason).HasMaxLength(300);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("effective_to IS NULL"); // exactly one open period per employee
            e.HasIndex(x => x.OrgUnitId); e.HasIndex(x => x.ManagerId);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<User>(e =>
        {
            e.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_email_lower", "email = lower(email)");
                t.HasCheckConstraint("ck_users_name_len", "length(trim(name)) >= 2");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.ExternalId).HasMaxLength(200); e.HasIndex(x => new { x.TenantId, x.ExternalId }).IsUnique().HasFilter("external_id IS NOT NULL");
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Credential).WithOne().HasForeignKey<UserCredential>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Profile).WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.UserRoles).WithOne(r => r.User).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MfaFactors).WithOne().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserCredential>(e => { e.ToTable("user_credentials"); e.HasKey(x => x.UserId); e.Property(x => x.PasswordHash).IsRequired(); });
        b.Entity<UserProfile>(e =>
        {
            e.ToTable("user_profiles", t => t.HasCheckConstraint("ck_profile_language", "language in ('en','id')"));
            e.HasKey(x => x.UserId);
        });
        b.Entity<Avatar>(e =>
        {
            e.ToTable("avatars", t =>
            {
                t.HasCheckConstraint("ck_avatar_type", "content_type in ('image/png','image/jpeg','image/webp')");
                t.HasCheckConstraint("ck_avatar_size", "octet_length(bytes) <= 262144");
            });
            e.HasKey(x => x.Id); e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExternalIdentity>(e =>
        {
            e.ToTable("external_identities"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EmailVerificationToken>(e =>
        {
            e.ToTable("email_verification_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MfaFactor>(e =>
        {
            e.ToTable("mfa_factors"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.Type }).IsUnique();
        });
        b.Entity<MfaRecoveryCode>(e =>
        {
            e.ToTable("mfa_recovery_codes"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions", t => t.HasCheckConstraint("ck_permission_key", "key ~ '^[a-z_]+\\.[a-z_]+$'"));
            e.HasKey(x => x.Key);
        });
        b.Entity<Role>(e =>
        {
            e.ToTable("roles"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique(); e.Property(x => x.Version).HasDefaultValue(1);
            e.HasIndex("TenantId", "Name").IsUnique().HasFilter("archived_at IS NULL");
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions"); e.HasKey(x => new { x.RoleId, x.PermissionKey });
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionKey).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles"); e.HasKey(x => new { x.UserId, x.RoleId }); e.HasIndex(x => x.RoleId);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Session>(e =>
        {
            e.ToTable("sessions"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId).HasFilter("revoked_at IS NULL"); e.HasIndex(x => x.AbsoluteExpiresAt);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Invitation>(e =>
        {
            e.ToTable("invitations", t => t.HasCheckConstraint("ck_invitation_email_lower", "email = lower(email)"));
            e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("status = 'Pending'");
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.InvitationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InvitationRole>(e => { e.ToTable("invitation_roles"); e.HasKey(x => new { x.InvitationId, x.RoleId }); });
        b.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events"); e.HasKey(x => x.Id); e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MetaJson).HasColumnType("jsonb"); e.Property(x => x.DiffJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Ts).IsDescending(); e.HasIndex(x => new { x.TenantId, x.Ts }).IsDescending(false, true); /* each organisation's audit list, newest first */ e.HasIndex(x => new { x.ActorId, x.Ts }); e.HasIndex(x => new { x.Action, x.Ts }); e.HasIndex(x => new { x.TargetType, x.TargetId });
        });
        b.Entity<SecurityEvent>(e =>
        {
            e.ToTable("security_events"); e.HasKey(x => x.Id); e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MetaJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Ts).IsDescending(); e.HasIndex(x => new { x.TenantId, x.Ts }).IsDescending(false, true); e.HasIndex(x => x.AcknowledgedAt).HasFilter("acknowledged_at IS NULL"); e.HasIndex(x => new { x.UserId, x.Ts });
        });
        b.Entity<SystemSetting>(e => { e.ToTable("system_settings"); e.HasKey(x => x.Key); e.Property(x => x.ValueJson).HasColumnType("jsonb"); });
        b.Entity<RateLimitBucket>(e => { e.ToTable("rate_limit_buckets"); e.HasKey(x => x.Key); });
        b.Entity<OutboxEmail>(e =>
        {
            e.ToTable("outbox_emails"); e.HasKey(x => x.Id);
            e.Property(x => x.ToAddress).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.LastError).HasMaxLength(200);
            e.HasIndex(x => x.NextAttemptAt).HasFilter("sent_at IS NULL AND failed_at IS NULL"); // the dispatcher's claim query
            e.HasIndex(x => new { x.TenantId, x.CreatedAt }); // each organisation's Email Delivery page
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessRequest>(e =>
        {
            e.ToTable("access_requests"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RequesterId, x.RoleId }).IsUnique().HasFilter("status = 'Pending'"); // one open request per person and role
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<AccessReview>(e =>
        {
            e.ToTable("access_reviews"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.TenantId).IsUnique().HasFilter("status = 'Open'"); // one open review at a time, per organisation
            e.HasMany(x => x.Items).WithOne(i => i.Review).HasForeignKey(i => i.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AccountClosureRequest>(e =>
        {
            e.ToTable("account_closure_requests"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("status IN ('Pending', 'Approved')"); // one open request per person
            e.HasIndex(x => x.ExecuteAfter).HasFilter("status = 'Approved'"); // the closing worker's due query
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TenantSetting>(e =>
        {
            e.ToTable("tenant_settings"); e.HasKey(x => new { x.TenantId, x.Key });
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.ValueJson).HasColumnType("jsonb");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessReviewSchedule>(e =>
        {
            e.ToTable("access_review_schedules"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId).IsUnique(); // one schedule per organisation
            e.HasIndex(x => x.NextRunAt).HasFilter("enabled"); // the scheduler's due query
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessReviewItem>(e =>
        {
            e.ToTable("access_review_items"); e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ReviewId, x.Decision });
            e.HasIndex(x => new { x.ReviewId, x.UserId, x.RoleId }).IsUnique();
        });
        b.Entity<UserIdentity>(e =>
        {
            e.ToTable("user_identities"); e.HasKey(x => x.Id);
            e.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(255).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Issuer }).IsUnique(); // one identity per person per provider
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants"); e.HasKey(x => x.Id);
            e.Property(x => x.Slug).HasMaxLength(40).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Slug).IsUnique();
        });
        foreach (var t in new[] { typeof(User), typeof(Role), typeof(Invitation), typeof(Session), typeof(AuditEvent), typeof(SecurityEvent), typeof(AccessRequest), typeof(AccessReview), typeof(AccessReviewItem) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<Goal>(b); Filter<GoalCheckIn>(b); Filter<Competency>(b); Filter<ReviewCycle>(b); Filter<ReviewAssignment>(b); Filter<ReviewResult>(b); Filter<DevelopmentPlan>(b); Filter<SuccessionPlan>(b); Filter<SuccessionCandidate>(b);
        Filter<ManpowerPlan>(b); Filter<JobRequisition>(b); Filter<JobPosting>(b); Filter<Candidate>(b); Filter<JobApplication>(b); Filter<ApplicationActivity>(b); Filter<Interview>(b); Filter<AssessmentTemplate>(b); Filter<AssessmentInvite>(b); Filter<JobOffer>(b);
        Filter<Course>(b); Filter<CourseLesson>(b); Filter<CourseEnrollment>(b);
        Filter<PushSubscription>(b); Filter<HourlyLeave>(b); Filter<RosterEntry>(b); Filter<OnCallDuty>(b); Filter<Project>(b); Filter<TimesheetEntry>(b); Filter<KioskDevice>(b); Filter<FieldVisit>(b);
        Filter<CustomFieldDef>(b); Filter<LetterTemplate>(b); Filter<IssuedLetter>(b); Filter<HrForm>(b); Filter<HrFormResponse>(b); Filter<HelpdeskTicket>(b); Filter<HelpdeskMessage>(b); Filter<CompanyEvent>(b); Filter<Branch>(b);
        Filter<SalaryGrade>(b); Filter<SalaryReview>(b); Filter<SalaryReviewItem>(b); Filter<ClaimPolicy>(b); Filter<CashAdvance>(b); Filter<WageAdvance>(b);
        Filter<User>(b); Filter<Role>(b); Filter<Invitation>(b); Filter<Session>(b); Filter<AuditEvent>(b); Filter<SecurityEvent>(b); Filter<AccessRequest>(b); Filter<AccessReview>(b); Filter<AccessReviewItem>(b); Filter<OutboxEmail>(b); Filter<AccessReviewSchedule>(b); Filter<TenantSetting>(b); Filter<AccountClosureRequest>(b); Filter<OrgUnit>(b); Filter<Employee>(b); Filter<EmployeeEmployment>(b); Filter<LeaveType>(b); Filter<Holiday>(b); Filter<LeaveLedgerEntry>(b); Filter<LeaveRequest>(b); Filter<AttendanceLog>(b); Filter<EmployeeCompensation>(b); Filter<PayRun>(b); Filter<PayRunItem>(b); Filter<PayRunLine>(b); Filter<Loan>(b); Filter<PayrollRuleVersion>(b); Filter<LegalEntity>(b); Filter<UserNotification>(b); Filter<RecurringPayItem>(b); Filter<NotificationPreference>(b); Filter<WorkLocation>(b); Filter<ScimToken>(b); Filter<DocumentSeal>(b); Filter<ApprovalRule>(b); Filter<ApprovalStep>(b); Filter<ReportSchedule>(b); Filter<HrPhoto>(b); Filter<ScheduledEmploymentChange>(b); Filter<WeeklyPayout>(b); Filter<WeeklyPayoutItem>(b); Filter<ExpenseClaim>(b); Filter<EmploymentLetter>(b); Filter<RetroAdjustment>(b); Filter<EmployeeTask>(b); Filter<EmployeeDocument>(b); Filter<AttendanceCorrection>(b); Filter<AttendancePeriod>(b); Filter<OvertimeRequest>(b); Filter<HrReminder>(b); Filter<ApprovalDelegation>(b); Filter<Shift>(b); Filter<WebhookEndpoint>(b); Filter<WebhookDelivery>(b); Filter<Announcement>(b); Filter<AnnouncementReceipt>(b); Filter<EmployeeChangeRequest>(b); Filter<ShiftAssignment>(b);
        foreach (var t in new[] { typeof(User), typeof(Role), typeof(Invitation), typeof(Session), typeof(AuditEvent), typeof(SecurityEvent), typeof(AccessRequest), typeof(AccessReview), typeof(AccessReviewItem) })
            b.Entity(t).HasIndex("TenantId");
    }
}
