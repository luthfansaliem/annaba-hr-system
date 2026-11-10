/** Hand-written mirror of the .NET API DTOs (camelCase JSON). Keep in sync with api/src/AnnabaDigital.Application. */
export type Me = { id: string; email: string; name: string; roles: string[]; permissions: string[]; locked: boolean; mfaEnrolled: boolean; avatarVersion?: number | null; tenantName?: string; actingTenant?: string | null; isPlatform?: boolean; language?: "en" | "id" };
export type LoginResponse = { status: "ok" | "mfa_required" | "verify_email" | "suspended" | "deactivated" | "locked" | "mfa_enrollment_required" | "password_reset_required"; next: string | null; user: Me | null };
export type Paged<T> = { data: T[]; page: number; pageSize: number; total: number };
export type RoleRef = { id: string; key: string; name: string };
export type StatusCount = { key: string; count: number };

export type UserListItem = { id: string; name: string; email: string; status: string; locked: boolean; verified: boolean; roles: RoleRef[]; lastLoginAt: string | null; createdAt: string; mfaEnabled: boolean; avatarVersion?: number | null; manageable?: boolean };
export type UserDetail = UserListItem & { phone: string | null; department: string | null; jobTitle: string | null; effectivePermissions: string[]; activeSessions: number; version: number };
export type UserStats = { total: number; active: number; pending: number; suspended: number; deactivated: number; locked: number; pendingVerification: number; growthLast12Months: number[]; byStatus: StatusCount[]; byRole: StatusCount[]; recentRegistrations: UserListItem[] };
export type SessionInfo = { id: string; device: string; browser: string; ip: string | null; createdAt: string; lastActiveAt: string; expiresAt: string };
export type ActivityItem = { id: number; ts: string; action: string; actor: string | null; target: string; outcome: string };
export type BulkResult = { results: { id: string; ok: boolean; error: string | null }[] };

export type InvitationItem = { id: string; email: string; status: "pending" | "accepted" | "expired" | "revoked"; roles: RoleRef[]; invitedBy: string | null; createdAt: string; expiresAt: string; acceptedAt: string | null };
export type InvitationLookup = { state: "valid" | "expired" | "used" | "revoked" | "invalid"; email: string | null; roles: string[] | null };

export type RoleListItem = { id: string; key: string; name: string; description: string; isSystem: boolean; isProtected: boolean; archived: boolean; permissionCount: number; userCount: number; version: number; permissions: string[] };
export type RoleDetail = { id: string; key: string; name: string; description: string; isSystem: boolean; isProtected: boolean; archived: boolean; permissions: string[]; userCount: number; version: number; updatedAt: string };
export type RoleUser = { id: string; name: string; email: string; status: string };
export type PermissionItem = { key: string; module: string; description: string; critical: boolean; grantedTo: string[] };
export type MatrixView = { version: string; permissions: { key: string; module: string; description: string; critical: boolean }[]; roles: { id: string; key: string; name: string; isSystem: boolean; isProtected: boolean; permissions: string[] }[]; grantable: string[] };

export type MySession = { id: string; current: boolean; device: string; browser: string; ip: string | null; createdAt: string; lastActiveAt: string; expiresAt: string; locked: boolean; remember: boolean };
export type AllSession = { id: string; userId: string; userName: string; userEmail: string; device: string; browser: string; ip: string | null; createdAt: string; lastActiveAt: string; expiresAt: string; locked: boolean };
export type AuditItem = { id: number; ts: string; actorId: string | null; actor: string; action: string; targetType: string; targetId: string | null; outcome: string };
export type AuditDetail = AuditItem & { ip: string | null; userAgent: string | null; requestId: string | null; meta: Record<string, string> | null; diff: { Field: string; Before: string; After: string }[] | null };
export type SecurityEvent = { id: string; ts: string; type: string; severity: "low" | "medium" | "high"; userId: string | null; userName: string | null; detail: string; acknowledged: boolean; acknowledgedBy: string | null; acknowledgedAt: string | null };

export type AdminDashboard = {
  kpis: { totalUsers?: number; activeUsers?: number; suspendedUsers?: number; pendingInvitations?: number; totalRoles?: number; activeSessions?: number };
  userGrowth?: number[]; userStatus?: StatusCount[]; roleDistribution?: StatusCount[]; loginActivity?: { days: string[]; success: number[]; failed: number[] };
  recentRegistrations?: UserListItem[]; recentLogins?: { ts: string; actor: string; outcome: string }[]; recentRoleChanges?: { ts: string; action: string; target: string; actor: string }[];
  recentSecurityEvents?: SecurityEvent[]; pendingInvitations?: { id: string; email: string; roles: string[]; expiresAt: string }[]; quickActions: string[]; visible: string[];
};
export type UserDashboard = {
  name: string; email: string; status: string; emailVerified: boolean; lastLoginAt: string | null; roles: RoleRef[]; permissionCount: number; permissions: string[]; activeSessions: number;
  mfaEnabled: boolean; mfaPolicy: string; passwordChangedAt: string | null; profileCompletion: { percent: number; missing: string[] }; recentActivity: { ts: string; action: string; outcome: string }[]; quickLinks: string[];
};

export type Profile = { name: string; email: string; emailVerified: boolean; phone: string | null; department: string | null; jobTitle: string | null; hasAvatar: boolean; avatarVersion: number | null; mfaEnabled: boolean; completion: { percent: number; missing: string[] }; version: number };
export type Preferences = { language: "en" | "id"; timezone: string; notifyEmail: boolean; notifySecurity: boolean; notifyProduct: boolean; autoLockMinutes: number };
export type MfaStatus = { enrolled: boolean; confirmedAt: string | null; recoveryCodesRemaining: number; required: boolean };

export type SystemSettings = {
  registrationMode: "open" | "invitation-only"; passwordMinLength: number; requireBreachCheck: boolean; sessionIdleMinutes: number; sessionAbsoluteHours: number; rememberIdleDays: number; rememberAbsoluteDays: number;
  lockoutAttempts: number; lockoutMinutes: number; mfaPolicy: "off" | "optional" | "required"; invitationExpiryDays: number; auditRetentionDays: number; emailFrom: string; requireReauthForCritical: boolean;
  emailProvider: string; emailApiKeySet: boolean; emailApiKeySource: "database" | "configuration" | "none";
};
export type PublicSettings = { registrationOpen: boolean; passwordMinLength: number; mfaPolicy: string; emailEnabled: boolean; ssoEnabled: boolean; ssoName: string | null };
export type EmailDelivery = { id: string; to: string; subject: string; status: "queued" | "sent" | "failed"; attempts: number; createdAt: string; sentAt: string | null; failedAt: string | null; nextAttemptAt: string | null; lastError: string | null };
export type EmailDeliveryPage = { summary: { provider: string; configured: boolean; queued: number; sent24h: number; failed7d: number }; data: EmailDelivery[]; page: number; pageSize: number; total: number };

export type ImportRowResult = { line: number; email: string; status: "ok" | "error"; message: string | null };
export type ImportUsersResult = { dryRun: boolean; total: number; created: number; failed: number; rows: ImportRowResult[] };

export type RequestableRole = { id: string; key: string; name: string; description: string; permissionCount: number };
export type AccessRequestItem = {
  id: string; requesterId: string; requesterName: string; requesterEmail: string; role: RoleRef; reason: string;
  status: "pending" | "approved" | "rejected" | "cancelled"; createdAt: string; decidedByName: string | null; decidedAt: string | null; decisionNote: string | null;
};

export type AccessReviewSummary = {
  id: string; name: string; status: "open" | "closed"; createdAt: string; dueAt: string; closedAt: string | null; createdByName: string | null;
  total: number; kept: number; revoked: number; pending: number; overdue: boolean;
};
export type ReviewSchedule = { enabled: boolean; everyDays: number; dueInDays: number; nextRunAt: string | null; lastRunAt: string | null };
export type AccessReviewItem = {
  id: string; userId: string; userName: string; userEmail: string; role: RoleRef; decision: "pending" | "keep" | "revoke";
  reviewerName: string | null; decidedAt: string | null; note: string | null;
};

export type TenantItem = { id: string; slug: string; name: string; status: "active" | "suspended"; isPlatform: boolean; users: number; createdAt: string };

export type TenantPolicyItem = { key: string; kind: "int" | "bool" | "enum"; strict: string; min: number; max: number; options: string[] | null; platform: number | boolean | string; own: number | boolean | string | null; effective: number | boolean | string };
export type TenantPolicy = { tenantName: string; items: TenantPolicyItem[] };

export type ClosureView = { id: string; status: "pending" | "approved" | "rejected" | "cancelled" | "completed"; reason: string; requestedAt: string; executeAfter: string | null; decisionNote: string | null };
export type ClosureItem = { id: string; userId: string; userName: string; userEmail: string; status: ClosureView["status"]; reason: string; requestedAt: string; decidedByName: string | null; decidedAt: string | null; decisionNote: string | null; executeAfter: string | null };

// ---- HR (docs/23-prd-hr-system.md)
export type EmployeeListItem = {
  id: string; employeeNo: string; fullName: string; workEmail: string | null; status: "active" | "terminated"; jobTitle: string | null; orgUnitId: string | null; orgUnit: string | null;
  type: string | null; managerId: string | null; managerName: string | null; hireDate: string; contractEnd: string | null;
};
export type EmploymentView = {
  id: string; effectiveFrom: string; effectiveTo: string | null; jobTitle: string; grade: string | null; location: string | null; type: string; contractEnd: string | null;
  orgUnitId: string | null; orgUnit: string | null; managerId: string | null; managerName: string | null; changeReason: string | null;
};
export type SensitiveView = { visible: boolean; nationalId: string | null; taxId: string | null; bankAccount: string | null; hasNationalId: boolean; hasTaxId: boolean; hasBankAccount: boolean; bpjsKesNo?: string | null; bpjsTkNo?: string | null; hasBpjsKesNo?: boolean; hasBpjsTkNo?: boolean };
export type EmployeeRef = { id: string; employeeNo: string; fullName: string };
export type EmployeeDetail = {
  id: string; employeeNo: string; fullName: string; workEmail: string | null; phone: string | null; birthDate: string | null; gender: string | null; maritalStatus: string | null;
  address: string | null; emergencyContactName: string | null; emergencyContactPhone: string | null; bankName: string | null; status: "active" | "terminated"; hireDate: string;
  terminationDate: string | null; terminationReason: string | null; userId: string | null; version: number; current: EmploymentView | null; history: EmploymentView[];
  sensitive: SensitiveView; directReports: EmployeeRef[]; legalEntityId: string; legalEntityName: string | null; religion?: string | null;
};
export type OrgUnitItem = { id: string; code: string; name: string; parentId: string | null; parentName: string | null; archived: boolean; employees: number };

export type LeaveType = { id: string; code: string; name: string; annualQuota: number | null; carryOverMax: number; maxDaysPerRequest: number | null; paid: boolean; allowHalfDay: boolean; archived: boolean; hrApprovalFromDays?: number | null };
export type Holiday = { id: string; date: string; name: string; collective: boolean };
export type LeaveBalance = { typeId: string; code: string; name: string; year: number; granted: number; used: number; adjusted: number; pending: number; available: number };
export type LeaveRequestItem = {
  id: string; employeeId: string; employeeNo: string; employeeName: string; leaveTypeId: string; leaveType: string; startDate: string; endDate: string; halfDay: boolean; days: number;
  reason: string | null; status: "pending" | "approved" | "rejected" | "cancelled"; decidedAt: string | null; decisionNote: string | null; createdAt: string; canDecide: boolean; canCancel: boolean; needsHrApproval?: boolean; awaitingHr?: boolean;
};
export type AttendanceLog = { id: string; date: string; clockIn: string; clockOut: string | null; hours: number | null; late: boolean };
export type MyAttendance = { open: AttendanceLog | null; logs: AttendanceLog[]; daysPresent: number; lateDays: number; hours: number };
export type AttendanceReportRow = { employeeId: string; employeeNo: string; employeeName: string; daysPresent: number; lateDays: number; hours: number; openSessions: number };

// ---- Payroll
export type CompensationRow = { employeeId: string; employeeNo: string; employeeName: string; jobTitle: string | null; hasCompensation: boolean; effectiveFrom: string | null; ptkpStatus: string | null };
export type CompensationView = {
  employeeId: string; employeeNo: string; employeeName: string; hasCompensation: boolean; effectiveFrom: string | null; basic: number | null; allowance: number | null; ptkpStatus: string | null;
  hasNpwp: boolean; kesEnrolled: boolean; tkEnrolled: boolean; jkkRate: number; openingYear: number | null; openingMonths: number; openingGross: number; openingPension: number; openingTax: number; retroPending?: number; payBasis?: "monthly" | "daily";
};
export type PayRunSummary = {
  id: string; year: number; month: number; status: "draft" | "calculated" | "approved" | "locked" | "paid"; employeeCount: number; totalGross: number; totalTax: number; totalNet: number; totalEmployerCost: number;
  rulesVersion: string; createdAt: string; calculatedAt: string | null; approvedAt: string | null; lockedAt: string | null; paidAt: string | null; note: string | null; version: number;
  canCalculate: boolean; canApprove: boolean; canLock: boolean; canMarkPaid: boolean; deductAbsence: boolean; legalEntityId: string; legalEntityName: string | null;
};
export type RecurringPayItem = { id: string; employeeId: string; employeeNo: string; employeeName: string; kind: "earning" | "deduction"; label: string; amount: number; taxable: boolean; from: string; through: string | null; ended: boolean; createdAt: string };
export type NotificationItem = { id: string; kind: string; title: string; body: string | null; link: string | null; createdAt: string; read: boolean };
export type NotificationList = { unread: number; items: NotificationItem[] };
export type LegalEntityItem = { id: string; name: string; taxId: string | null; address: string | null; isDefault: boolean; archived: boolean; activeEmployees: number; payRuns: number };
export type PayItem = {
  id: string; employeeId: string; employeeNo: string; employeeName: string; ptkp: string; basic: number; allowance: number; prorateFactor: number; unpaidDeduction: number; taxableExtras: number; nonTaxableExtras: number;
  otherDeductions: number; overtimePay: number; gross: number; kesEmployee: number; jhtEmployee: number; jpEmployee: number; kesEmployer: number; jhtEmployer: number; jpEmployer: number; jkkEmployer: number; jkmEmployer: number;
  tax: number; taxMethod: string; net: number; employerCost: number; warnings: string | null; dailyRate?: number | null; daysPaid?: number | null;
};
export type PayLine = { id: string; employeeId: string; employeeName: string; kind: "earning" | "deduction"; label: string; amount: number; taxable: boolean; auto?: boolean };
export type PayRunDetail = { run: PayRunSummary; items: PayItem[]; lines: PayLine[]; missing: string[]; attendanceClosed: boolean };
export type PayslipRow = { itemId: string; payRunId: string; year: number; month: number; status: string; gross: number; net: number };
export type Payslip = { payRunId: string; year: number; month: number; status: string; paidAt: string | null; item: PayItem; lines: PayLine[] };

export type ImportEmployeeRow = { line: number; employeeNo: string | null; name: string | null; status: "ok" | "error"; error: string | null };
export type ImportEmployeesResult = { dryRun: boolean; total: number; created: number; failed: number; rows: ImportEmployeeRow[] };
export type UserOption = { id: string; name: string; email: string };

// ---- checklists, documents, time requests
export type TaskItem = { id: string; kind: "onboarding" | "offboarding"; title: string; dueDate: string | null; done: boolean; doneAt: string | null; overdue: boolean };
export type ExpiryItem = { kind: "contract" | "document"; employeeId: string; employeeNo: string; employeeName: string; label: string; date: string; daysLeft: number };
export type DocumentItem = { id: string; category: string; fileName: string; contentType: string; sizeBytes: number; expiresOn: string | null; uploadedAt: string };
export type TimeRequest = {
  id: string; kind: "correction" | "overtime"; employeeId: string; employeeNo: string; employeeName: string; date: string; clockIn: string | null; clockOut: string | null; hours: number | null;
  reason: string; status: "pending" | "approved" | "rejected" | "cancelled"; decisionNote: string | null; createdAt: string; canDecide: boolean; canCancel: boolean;
};
export type AttendancePeriod = { year: number; month: number; closedAt: string };

export type DelegationItem = { id: string; delegatorEmployeeId: string; delegator: string; delegateEmployeeId: string; delegate: string; startDate: string; endDate: string; active: boolean; mine: boolean; canRevoke: boolean };
export type DelegateCandidate = { employeeId: string; employeeNo: string; name: string };

export type ShiftItem = { id: string; code: string; name: string; startTime: string; endTime: string; graceMinutes: number; workDays: number; archived: boolean; assigned: number; overnight?: boolean };
export type ShiftAssignmentRow = { employeeId: string; employeeNo: string; employeeName: string; shiftId: string | null; shiftName: string | null; since: string | null };
export type ImportAttendanceResult = { dryRun: boolean; punches: number; days: number; imported: number; skipped: number; failed: number; rows: { line: number; employeeNo: string | null; date: string | null; status: "ok" | "skipped" | "error"; message: string | null }[] };

export type LoanItem = { id: string; employeeId: string; employeeNo: string; employeeName: string; principal: number; installment: number; remaining: number; status: string; note: string | null; createdAt: string };
export type SeveranceLine = { label: string; kind: "earning" | "deduction"; amount: number; taxable: boolean; basis: string };
export type SeverancePreview = { employeeId: string; employeeName: string; reason: string; lastDay: string; years: number; months: number; wage: number; lines: SeveranceLine[]; netTotal: number };

export type SelfProfile = { employeeId: string; employeeNo: string; fullName: string; workEmail: string | null; jobTitle: string | null; orgUnit: string | null; managerName: string | null; hireDate: string; phone: string | null; address: string | null;
  emergencyContactName: string | null; emergencyContactPhone: string | null; maritalStatus: string | null; bankName: string | null; hasBankAccount: boolean; hasTaxId: boolean; hasNationalId: boolean };
export type ChangeRequestItem = { id: string; employeeId: string; employeeNo: string; employeeName: string; field: string; value: string | null; valueHidden: boolean; reason: string | null; status: "pending" | "approved" | "rejected" | "cancelled"; decisionNote: string | null; createdAt: string; canDecide: boolean; canCancel: boolean };

export type CountBy = { key: string; count: number };
export type HrDashboard = {
  headcount: number; joiners30: number; joiners90: number; leavers12: number; turnoverPct: number; contractsEnding60: number; onLeaveToday: number; pendingLeave: number; pendingCorrections: number; pendingOvertime: number;
  pendingChangeRequests: number; noCompensation: number; noLinkedAccount: number; byDepartment: CountBy[]; byType: CountBy[]; byGender: CountBy[]; byTenure: CountBy[]; trend: { month: string; headcount: number; joiners: number; leavers: number }[];
};
export type AnnouncementItem = { id: string; title: string; body: string; orgUnitId: string | null; orgUnit: string | null; requiresAck: boolean; expiresOn: string | null; publishedAt: string; read: boolean; acked: boolean; archived: boolean };
export type AckReport = { announcementId: string; title: string; audience: number; read: number; acked: number; people: { employeeId: string; employeeNo: string; employeeName: string; read: boolean; ackedAt: string | null }[] };

export type WebhookItem = { id: string; url: string; events: string[]; active: boolean; description: string | null; createdAt: string; lastDeliveredAt: string | null; pending: number; dead: number };
export type WebhookDelivery = { id: string; event: string; status: "pending" | "delivered" | "dead"; attempts: number; lastHttpStatus: number | null; lastError: string | null; createdAt: string; deliveredAt: string | null; nextAttemptAt: string };

export type TerBracket = { upto: number; pct: number };
export type PayrollRulesData = { version: string; kesEmployeeRate: number; kesEmployerRate: number; kesWageCap: number; jhtEmployeeRate: number; jhtEmployerRate: number; jpEmployeeRate: number; jpEmployerRate: number; jpWageCap: number; jkmEmployerRate: number; defaultJkkRate: number; positionCostRate: number; positionCostMonthlyCap: number; noNpwpSurcharge: number; ptkpTable: Record<string, number>; terA: TerBracket[]; terB: TerBracket[]; terC: TerBracket[] };
export type PayrollRuleVersion = { id: string; label: string; effectiveFrom: string; reason: string; changes: string; createdAt: string };
export type PayrollRulesOverview = { label: string; effectiveFrom: string | null; rules: PayrollRulesData; versions: PayrollRuleVersion[] };
