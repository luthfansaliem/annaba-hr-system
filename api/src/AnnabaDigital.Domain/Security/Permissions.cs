namespace AnnabaDigital.Domain.Security;

/// <summary>Permission catalog — single source of truth (seeded into the DB, never edited at runtime).</summary>
public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersSuspend = "users.suspend";
    public const string UsersAssignRole = "users.assign_role";
    public const string InvitationsRead = "invitations.read";
    public const string InvitationsManage = "invitations.manage";
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string PermissionsRead = "permissions.read";
    public const string PermissionsAssign = "permissions.assign";
    public const string SessionsReadAll = "sessions.read_all";
    public const string SessionsRevoke = "sessions.revoke";
    public const string AuditRead = "audit.read";
    public const string SecurityRead = "security.read";
    public const string SettingsManage = "settings.manage";
    public const string TenantsManage = "tenants.manage";
    public const string UsersResetCredentials = "users.reset_credentials";
    public const string EmployeesRead = "employees.read";
    public const string EmployeesCreate = "employees.create";
    public const string EmployeesUpdate = "employees.update";
    public const string EmployeesReadSensitive = "employees.read_sensitive";
    public const string EmployeesExport = "employees.export";
    public const string HrConfigManage = "hrconfig.manage";
    public const string LeaveApprove = "leave.approve";
    public const string LeaveManage = "leave.manage";
    public const string LeaveReadAll = "leave.read_all";
    public const string AttendanceReadAll = "attendance.read_all";
    public const string DocumentsRead = "documents.read";
    public const string DocumentsManage = "documents.manage";
    public const string AttendanceApprove = "attendance.approve";
    public const string AttendanceManage = "attendance.manage";
    public const string PayrollRead = "payroll.read";
    public const string PayrollConfigure = "payroll.configure";
    public const string PayrollRun = "payroll.run";
    public const string PayrollApprove = "payroll.approve";
    public const string PayrollLock = "payroll.lock";
    public const string ClaimsApprove = "claims.approve";
    public const string ClaimsManage = "claims.manage";

    public sealed record Def(string Key, string Module, string Description, bool Critical = false);

    public static readonly IReadOnlyList<Def> Catalog =
    [
        new(UsersRead, "Users", "View users and their details"),
        new(UsersCreate, "Users", "Create users"),
        new(UsersUpdate, "Users", "Edit user profiles"),
        new(UsersSuspend, "Users", "Suspend, deactivate and unlock accounts"),
        new(UsersAssignRole, "Users", "Assign and remove roles on users", true),
        new(UsersResetCredentials, "Users", "Reset someone's two-step verification and require a new password", true),
        new(EmployeesRead, "Employees", "View employees and their job history"),
        new(EmployeesCreate, "Employees", "Add employees"),
        new(EmployeesUpdate, "Employees", "Edit employees, change job data and end employment"),
        new(EmployeesReadSensitive, "Employees", "See and edit national ID, tax ID and bank account", true),
        new(EmployeesExport, "Employees", "Export the employee list", true),
        new(HrConfigManage, "Employees", "Manage departments and other HR reference data"),
        new(LeaveApprove, "Leave", "Approve or reject leave requests of your direct reports"),
        new(LeaveReadAll, "Leave", "See every leave request and balance"),
        new(LeaveManage, "Leave", "Manage leave types, holidays, balances, and decide any request", true),
        new(AttendanceReadAll, "Attendance", "See everyone's attendance and the monthly report"),
        new(DocumentsRead, "Employees", "Open employees' documents (contracts, ID cards, certificates)", true),
        new(DocumentsManage, "Employees", "Upload and remove employees' documents", true),
        new(AttendanceApprove, "Attendance", "Decide attendance corrections and overtime of your direct reports"),
        new(AttendanceManage, "Attendance", "Decide any correction or overtime, and close or reopen a month's attendance", true),
        new(PayrollRead, "Payroll", "View pay runs, pay components and compensation", true),
        new(PayrollConfigure, "Payroll", "Set employees' salary, allowances and tax status", true),
        new(PayrollRun, "Payroll", "Create and calculate pay runs, add extra earnings and deductions", true),
        new(PayrollApprove, "Payroll", "Approve a calculated pay run (a different person than the one who calculated it)", true),
        new(PayrollLock, "Payroll", "Lock an approved pay run, export bank files and mark it paid", true),
        new(ClaimsApprove, "Payroll", "Decide expense claims of your direct reports"),
        new(ClaimsManage, "Payroll", "See and decide any expense claim", true),
        new(InvitationsRead, "Invitations", "View invitations"),
        new(InvitationsManage, "Invitations", "Create, resend and revoke invitations"),
        new(RolesRead, "Roles", "View roles"),
        new(RolesCreate, "Roles", "Create and duplicate roles"),
        new(RolesUpdate, "Roles", "Edit and archive roles"),
        new(PermissionsRead, "Permissions", "View permission catalog and access matrix"),
        new(PermissionsAssign, "Permissions", "Change role permission assignments", true),
        new(SessionsReadAll, "Sessions", "View sessions of other users"),
        new(SessionsRevoke, "Sessions", "Revoke sessions of other users"),
        new(AuditRead, "Monitoring", "View audit logs"),
        new(SecurityRead, "Monitoring", "View and acknowledge security events"),
        new(SettingsManage, "Administration", "Manage system settings", true),
        new(TenantsManage, "Administration", "Create and suspend tenants (platform organisation only)", true),
    ];

    public static IEnumerable<string> All => Catalog.Select(c => c.Key);
}

/// <summary>Seed roles (matches docs/09-rbac-matrix.md).</summary>
public static class SystemRoles
{
    public const string SuperAdmin = "super_admin";
    public const string UserAdmin = "user_admin";
    public const string SecurityAuditor = "security_auditor";
    public const string User = "user";
    public const string HrAdmin = "hr_admin";
    public const string HrHead = "hr_head";
    public const string Manager = "manager";
    public const string PayrollOfficer = "payroll_officer";
    public const string PayrollApprover = "payroll_approver";

    public sealed record Def(string Key, string Name, string Description, bool Protected, string[] Permissions);

    public static readonly IReadOnlyList<Def> Seed =
    [
        new(SuperAdmin, "Super Admin", "Unrestricted access. Permission set is immutable.", true, [.. Permissions.All]),
        new(UserAdmin, "User Admin", "Manages users and invitations.", false,
        [
            Permissions.UsersRead, Permissions.UsersCreate, Permissions.UsersUpdate, Permissions.UsersSuspend, Permissions.UsersAssignRole,
            Permissions.InvitationsRead, Permissions.InvitationsManage, Permissions.RolesRead, Permissions.SessionsReadAll, Permissions.SessionsRevoke,
        ]),
        new(SecurityAuditor, "Security Auditor", "Read-only access to monitoring and sessions.", false,
        [
            Permissions.UsersRead, Permissions.RolesRead, Permissions.PermissionsRead, Permissions.SessionsReadAll, Permissions.AuditRead, Permissions.SecurityRead,
        ]),
        new(HrAdmin, "HR Admin", "Manages employee records and HR reference data.", false,
        [
            Permissions.EmployeesRead, Permissions.EmployeesCreate, Permissions.EmployeesUpdate, Permissions.EmployeesReadSensitive,
            Permissions.EmployeesExport, Permissions.HrConfigManage, Permissions.ClaimsApprove, Permissions.ClaimsManage, Permissions.LeaveManage, Permissions.LeaveReadAll, Permissions.LeaveApprove, Permissions.AttendanceReadAll, Permissions.AttendanceApprove, Permissions.AttendanceManage, Permissions.DocumentsRead, Permissions.DocumentsManage, Permissions.UsersRead, Permissions.InvitationsRead, Permissions.InvitationsManage,
        ]),
        new(HrHead, "HR Head", "Read-only view of employees and reports.", false, [Permissions.EmployeesRead, Permissions.EmployeesExport, Permissions.LeaveReadAll, Permissions.AttendanceReadAll]),
        new(PayrollOfficer, "Payroll Officer", "Prepares pay runs. Cannot approve or lock them.", false,
            [Permissions.EmployeesRead, Permissions.PayrollRead, Permissions.PayrollConfigure, Permissions.PayrollRun, Permissions.ClaimsApprove, Permissions.ClaimsManage]),
        new(PayrollApprover, "Payroll Approver", "Approves and locks pay runs prepared by someone else.", false, [Permissions.EmployeesRead, Permissions.PayrollRead, Permissions.PayrollApprove, Permissions.PayrollLock]),
        new(Manager, "Manager", "Approves leave, attendance corrections and overtime of direct reports.", false, [Permissions.LeaveApprove, Permissions.AttendanceApprove, Permissions.ClaimsApprove]),
        new(User, "User", "Self-service only. No administrative permissions.", false, []),
    ];
}
