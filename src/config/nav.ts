import {
  Target, Star, Sprout, Grid2x2Check, Crown, Medal, UserSearch, FilePlus2, ChartGantt, ClipboardCheck as TestIcon, MessagesSquare,
  Layers, TrendingUp, Coins, Plane, FileCheck2, BookUser, CalendarHeart, MessageCircleQuestion, ClipboardPen, ListPlus, FileSignature, Store, CalendarRange as CalendarGrid, PhoneCall, FolderKanban, Tablet, MapPinned, Timer as TimerIcon,
  LayoutDashboard, Users, UserRound, UsersRound, MailPlus, ShieldCheck, KeyRound, Grid3x3, MonitorSmartphone, ScrollText, ShieldAlert,
  Settings, SlidersHorizontal, Fingerprint, BarChart3, Mail, KeySquare, ClipboardCheck, Building2, UserX, Briefcase, Network, CalendarDays, CalendarCheck, CalendarCog, Clock, ClipboardList, Banknote, Wallet, ReceiptText, CalendarRange, TimerReset, Workflow, UserCog, CalendarClock, HandCoins, IdCard, FilePen, LayoutPanelLeft, Megaphone, Webhook, Scale, Landmark, Repeat, Inbox, MapPin, Receipt, FileText, ListChecks, Cable, GitBranch, type LucideIcon,
} from "lucide-react";

export type NavLeaf = { label: string; href: string; icon: LucideIcon; perm?: string };
export type NavGroup = { title: string; icon: LucideIcon; children: (NavLeaf | NavGroup)[] };
export type NavNode = NavLeaf | NavGroup;
export const isGroup = (n: NavNode): n is NavGroup => "children" in n;

/**
 * The menu follows the work, not the technology: home, my own HR, approvals, then one group per HR module (each starts with its analytics),
 * HR setup, and last the administration of accounts and the system. Add User is intentionally NOT here: it is the primary action on All Users (NAV-U1).
 */
export const nav: NavNode[] = [
  { label: "Home", href: "/dashboard", icon: LayoutDashboard },
  { title: "My HR", icon: UserRound, children: [
    { label: "My Attendance", href: "/hr/my-attendance", icon: Clock },
    { label: "My Leave", href: "/hr/my-leave", icon: CalendarDays },
    { label: "My Payslips", href: "/hr/my-payslips", icon: ReceiptText },
    { label: "My Claims", href: "/hr/my-claims", icon: Receipt },
    { label: "Timesheets", href: "/hr/timesheets", icon: TimerIcon },
    { label: "Goals & OKR", href: "/hr/goals", icon: Target },
    { label: "Performance Reviews", href: "/hr/reviews", icon: Star },
    { label: "My Development", href: "/hr/my-development", icon: Sprout },
    { label: "My Interviews", href: "/hr/interviews", icon: MessagesSquare },
    { label: "My Forms", href: "/hr/my-forms", icon: ClipboardPen },
    { label: "Ask HR", href: "/hr/helpdesk", icon: MessageCircleQuestion },
    { label: "Company Calendar", href: "/hr/calendar", icon: CalendarHeart },
    { label: "Employee Directory", href: "/hr/directory", icon: BookUser },
    { label: "Employment Letters", href: "/hr/my-letters", icon: FileText },
    { label: "My HR Profile", href: "/hr/my-profile", icon: IdCard },
    { label: "Announcements", href: "/hr/announcements", icon: Megaphone },
  ] },
  { title: "Approvals", icon: Inbox, children: [
    { label: "Approvals Inbox", href: "/hr/approvals", icon: Inbox, perm: "leave.approve" },
    { label: "My Team", href: "/hr/team", icon: UsersRound, perm: "leave.approve" },
    { label: "Delegate Approvals", href: "/hr/delegations", icon: UserCog, perm: "leave.approve" },
    { label: "Hiring Requests", href: "/hr/requisitions", icon: FilePlus2, perm: "leave.approve" },
  ] },
  { title: "Workforce", icon: Users, children: [
    { label: "People Analytics", href: "/hr/analytics/people", icon: BarChart3, perm: "employees.read" },
    { label: "Employees", href: "/hr/employees", icon: UsersRound, perm: "employees.read" },
    { label: "Departments", href: "/hr/departments", icon: Network, perm: "employees.read" },
    { label: "Org Chart", href: "/hr/org-chart", icon: Workflow, perm: "employees.read" },
    { label: "Change Requests", href: "/hr/change-requests", icon: FilePen, perm: "employees.update" },
  ] },
  { title: "Attendance", icon: Clock, children: [
    { label: "Attendance Analytics", href: "/hr/analytics/attendance", icon: BarChart3, perm: "attendance.read_all" },
    { label: "Attendance Report", href: "/hr/attendance-report", icon: ClipboardList, perm: "attendance.read_all" },
    { label: "Time Approvals", href: "/hr/time-approvals", icon: TimerReset, perm: "attendance.approve" },
    { label: "Shifts", href: "/hr/shifts", icon: CalendarClock, perm: "attendance.read_all" },
    { label: "Work Locations", href: "/hr/work-locations", icon: MapPin, perm: "attendance.read_all" },
    { label: "Shift Planner", href: "/hr/roster", icon: CalendarGrid, perm: "attendance.read_all" },
    { label: "On-call Duty", href: "/hr/on-call", icon: PhoneCall, perm: "attendance.read_all" },
    { label: "Projects", href: "/hr/projects", icon: FolderKanban, perm: "attendance.read_all" },
    { label: "Field Visits", href: "/hr/field-visits", icon: MapPinned, perm: "attendance.approve" },
    { label: "Attendance Tablets", href: "/hr/kiosks", icon: Tablet, perm: "attendance.manage" },
  ] },
  { title: "Leave", icon: CalendarDays, children: [
    { label: "Leave Analytics", href: "/hr/analytics/leave", icon: BarChart3, perm: "leave.read_all" },
    { label: "Leave Approvals", href: "/hr/leave-approvals", icon: CalendarCheck, perm: "leave.approve" },
    { label: "All Leave", href: "/hr/leave-all", icon: ClipboardList, perm: "leave.read_all" },
    { label: "Leave Calendar", href: "/hr/leave-calendar", icon: CalendarRange, perm: "leave.approve" },
    { label: "Leave Setup", href: "/hr/leave-setup", icon: CalendarCog, perm: "leave.manage" },
  ] },
  { title: "Payroll", icon: Banknote, children: [
    { label: "Payroll Analytics", href: "/hr/analytics/payroll", icon: BarChart3, perm: "payroll.read" },
    { label: "Pay Runs", href: "/hr/payroll", icon: Banknote, perm: "payroll.read" },
    { label: "Weekly Payouts", href: "/hr/weekly-payouts", icon: CalendarRange, perm: "payroll.read" },
    { label: "Compensation", href: "/hr/compensation", icon: Wallet, perm: "payroll.read" },
    { label: "Salary Grades", href: "/hr/salary-grades", icon: Layers, perm: "payroll.read" },
    { label: "Salary Reviews", href: "/hr/salary-reviews", icon: TrendingUp, perm: "payroll.read" },
    { label: "Recurring Pay Items", href: "/hr/recurring-pay", icon: Repeat, perm: "payroll.read" },
    { label: "Loans", href: "/hr/loans", icon: HandCoins, perm: "payroll.read" },
    { label: "Early Wage Withdrawals", href: "/hr/wage-advances", icon: Coins, perm: "payroll.read" },
    { label: "Payroll Rules", href: "/hr/payroll-rules", icon: Scale, perm: "payroll.read" },
  ] },
  { title: "Claims", icon: Receipt, children: [
    { label: "Claims Analytics", href: "/hr/analytics/claims", icon: BarChart3, perm: "claims.manage" },
    { label: "Expense Claims", href: "/hr/claims", icon: Receipt, perm: "claims.manage" },
    { label: "Cash Advances", href: "/hr/cash-advances", icon: Plane, perm: "claims.approve" },
    { label: "Claim Policies", href: "/hr/claim-policies", icon: FileCheck2, perm: "claims.manage" },
  ] },
  { title: "Recruitment", icon: UserSearch, children: [
    { label: "Job Postings", href: "/hr/recruitment", icon: UserSearch, perm: "hrconfig.manage" },
    { label: "Manpower Planning", href: "/hr/manpower", icon: ChartGantt, perm: "hrconfig.manage" },
    { label: "Assessments", href: "/hr/assessments", icon: TestIcon, perm: "hrconfig.manage" },
  ] },
  { title: "Performance & Talent", icon: Medal, children: [
    { label: "Review Cycles", href: "/hr/review-cycles", icon: Star, perm: "hrconfig.manage" },
    { label: "Competencies", href: "/hr/competencies", icon: Grid2x2Check, perm: "hrconfig.manage" },
    { label: "Talent Pool", href: "/hr/talent", icon: Crown, perm: "hrconfig.manage" },
    { label: "Succession Planning", href: "/hr/succession", icon: Sprout, perm: "hrconfig.manage" },
  ] },
  { title: "HR Setup", icon: Briefcase, children: [
    { label: "Companies", href: "/hr/companies", icon: Landmark, perm: "hrconfig.manage" },
    { label: "Branches & Units", href: "/hr/branches", icon: Store, perm: "hrconfig.manage" },
    { label: "Custom Fields", href: "/hr/custom-fields", icon: ListPlus, perm: "hrconfig.manage" },
    { label: "Letter Templates", href: "/hr/letter-templates", icon: FileSignature, perm: "hrconfig.manage" },
    { label: "Forms & Surveys", href: "/hr/forms", icon: ClipboardPen, perm: "hrconfig.manage" },
    { label: "Approval Flows", href: "/hr/approval-flows", icon: GitBranch, perm: "hrconfig.manage" },
    { label: "Webhooks", href: "/hr/webhooks", icon: Webhook, perm: "hrconfig.manage" },
    { label: "Go-Live Check", href: "/hr/readiness", icon: ListChecks, perm: "settings.manage" },
  ] },
  { title: "Administration", icon: SlidersHorizontal, children: [
    { label: "Access Overview", href: "/admin/overview", icon: LayoutPanelLeft, perm: "users.read" },
    { title: "Users", icon: Users, children: [
      { label: "Overview", href: "/users", icon: BarChart3, perm: "users.read" },
      { label: "All Users", href: "/users/all", icon: UsersRound, perm: "users.read" },
      { label: "Invitations", href: "/users/invitations", icon: MailPlus, perm: "invitations.read" },
    ] },
    { title: "Roles & Permissions", icon: ShieldCheck, children: [
      { label: "Roles", href: "/roles", icon: ShieldCheck, perm: "roles.read" },
      { label: "Permissions", href: "/permissions", icon: KeyRound, perm: "permissions.read" },
      { label: "Access Matrix", href: "/access-matrix", icon: Grid3x3, perm: "permissions.read" },
    ] },
    { label: "Access Reviews", href: "/access-reviews", icon: ClipboardCheck, perm: "users.assign_role" },
    { label: "Account Closures", href: "/account-closures", icon: UserX, perm: "users.suspend" },
    { label: "Provisioning (SCIM)", href: "/provisioning", icon: Cable, perm: "settings.manage" },
    { label: "Audit Logs", href: "/audit", icon: ScrollText, perm: "audit.read" },
    { label: "Security Events", href: "/security-events", icon: ShieldAlert, perm: "security.read" },
    { label: "System Settings", href: "/admin/settings", icon: Settings, perm: "settings.system" },
    { label: "Security Policy", href: "/admin/security-policy", icon: Fingerprint, perm: "settings.tenant" },
    { label: "Email Delivery", href: "/admin/email", icon: Mail, perm: "settings.manage" },
    { label: "Tenants", href: "/admin/tenants", icon: Building2, perm: "tenants.manage" },
  ] },
  { title: "My Account", icon: MonitorSmartphone, children: [
    { label: "My Profile", href: "/profile", icon: UserRound },
    { label: "Sessions & Devices", href: "/sessions", icon: MonitorSmartphone },
    { label: "Access Requests", href: "/access-requests", icon: KeySquare },
    { label: "Settings", href: "/settings", icon: Settings },
  ] },
];

/** Filter by permission; drop groups that end up empty. */
export function visibleNav(nodes: NavNode[], can: (p: string) => boolean): NavNode[] {
  return nodes.flatMap((n): NavNode[] => {
    if (!isGroup(n)) return !n.perm || can(n.perm) ? [n] : [];
    const kids = visibleNav(n.children, can);
    return kids.length ? [{ ...n, children: kids }] : [];
  });
}
export function leaves(nodes: NavNode[]): NavLeaf[] { return nodes.flatMap((n) => (isGroup(n) ? leaves(n.children) : [n])); }
export function activeTrail(nodes: NavNode[], path: string, trail: string[] = []): string[] | null {
  for (const n of nodes) {
    if (isGroup(n)) { const r = activeTrail(n.children, path, [...trail, n.title]); if (r) return r; }
    else if (path === n.href || (n.href !== "/dashboard" && path.startsWith(n.href + "/") && !leavesMatchBetter(nodes, path, n))) return trail;
  }
  return null;
}
function leavesMatchBetter(siblings: NavNode[], path: string, current: NavLeaf) {
  return leaves(siblings).some((l) => l.href !== current.href && l.href.length > current.href.length && (path === l.href || path.startsWith(l.href + "/")));
}
