import {
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
    { label: "Employment Letters", href: "/hr/my-letters", icon: FileText },
    { label: "My HR Profile", href: "/hr/my-profile", icon: IdCard },
    { label: "Announcements", href: "/hr/announcements", icon: Megaphone },
  ] },
  { title: "Approvals", icon: Inbox, children: [
    { label: "Approvals Inbox", href: "/hr/approvals", icon: Inbox, perm: "leave.approve" },
    { label: "My Team", href: "/hr/team", icon: UsersRound, perm: "leave.approve" },
    { label: "Delegate Approvals", href: "/hr/delegations", icon: UserCog, perm: "leave.approve" },
  ] },
  { title: "People", icon: Users, children: [
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
    { label: "Recurring Pay Items", href: "/hr/recurring-pay", icon: Repeat, perm: "payroll.read" },
    { label: "Loans", href: "/hr/loans", icon: HandCoins, perm: "payroll.read" },
    { label: "Payroll Rules", href: "/hr/payroll-rules", icon: Scale, perm: "payroll.read" },
  ] },
  { title: "Claims", icon: Receipt, children: [
    { label: "Claims Analytics", href: "/hr/analytics/claims", icon: BarChart3, perm: "claims.manage" },
    { label: "Expense Claims", href: "/hr/claims", icon: Receipt, perm: "claims.manage" },
  ] },
  { title: "HR Setup", icon: Briefcase, children: [
    { label: "Companies", href: "/hr/companies", icon: Landmark, perm: "hrconfig.manage" },
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
