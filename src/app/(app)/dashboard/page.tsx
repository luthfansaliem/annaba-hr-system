"use client";

import Link from "next/link";
import { ArrowRight, Banknote, Building2, CalendarCheck, CalendarDays, CalendarX, Check, ClipboardCheck, FileClock, Hourglass, Inbox, Rocket, TrendingUp, UserCheck, UserPlus, Users } from "lucide-react";
import { GreetingHero, type HeroHighlight } from "@/components/greeting-hero";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { BarList, ChartGrid, ChartPanel, ColumnChart, Kpi, KpiRow, monthShort, num } from "@/components/charts/analytics";
import { MyDayPanels, MyDayStats } from "@/components/hr/my-day";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { AttendanceAnalytics, HrDashboard, LeaveAnalytics, MyHome } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { formatLocale, t } from "@/i18n";

const dayLabel = (iso: string) => new Date(iso + "T00:00:00").toLocaleDateString(formatLocale(), { day: "numeric", month: "short" });
const optional = { noRoute: true } as const;

/**
 * Home. Everyone sees their own day (attendance, leave left, what is open). People who work in HR also see the organisation today:
 * who is in, who is away, what waits for a decision, and how headcount, attendance and leave are moving.
 */
export default function HomePage() {
  const { user, can } = useAuth(); const hr = can("employees.read");
  // the one request every signed-in person may make: when the session has ended, it is the one that sends them to sign in again
  const mine = useQuery(() => get<MyHome>("/api/hr/me/home"), []);
  const org = useQuery(() => (hr ? get<HrDashboard>("/api/hr/dashboard", undefined, optional) : Promise.resolve(null)), [hr]);
  const att = useQuery(() => (can("attendance.read_all") ? get<AttendanceAnalytics>("/api/hr/analytics/attendance", undefined, optional) : Promise.resolve(null)), [can]);
  const leave = useQuery(() => (can("leave.read_all") ? get<LeaveAnalytics>("/api/hr/analytics/leave", undefined, optional) : Promise.resolve(null)), [can]);
  const me = mine.data; const d = org.data; const a = att.data; const l = leave.data;
  const first = (me?.fullName ?? user?.name ?? "").split(" ")[0];

  const waiting = d ? d.pendingLeave + d.pendingCorrections + d.pendingOvertime + d.pendingChangeRequests : 0;
  const chips: HeroHighlight[] = [];
  if (d) {
    if (waiting) chips.push({ label: t("{n} waiting for a decision", { n: waiting }), href: can("leave.approve") ? "/hr/approvals" : "/hr/leave-all", tone: "warn" });
    if (d.contractsEnding60) chips.push({ label: d.contractsEnding60 === 1 ? t("1 contract ending within 60 days") : t("{n} contracts ending within 60 days", { n: d.contractsEnding60 }), href: "/hr/employees", tone: "info" });
    if (!waiting && !d.contractsEnding60) chips.push({ label: t("All clear — nothing needs attention"), tone: "ok" });
  } else if (me?.linked) {
    const open = me.pendingLeave + me.pendingTime + me.pendingChanges;
    chips.push(open ? { label: t("{n} of your requests are waiting for a decision", { n: open }), href: "/hr/my-leave", tone: "info" } : { label: t("Nothing is waiting for you"), tone: "ok" });
  }
  const queue: [string, number, string, string | null][] = d ? [
    [t("Leave requests waiting"), d.pendingLeave, "/hr/leave-all", "leave.read_all"], [t("Attendance corrections waiting"), d.pendingCorrections, "/hr/time-approvals", "attendance.approve"],
    [t("Overtime requests waiting"), d.pendingOvertime, "/hr/time-approvals", "attendance.approve"], [t("Change requests waiting"), d.pendingChangeRequests, "/hr/change-requests", "employees.update"],
    [t("Employees without compensation"), d.noCompensation, "/hr/compensation", "payroll.read"], [t("Employees without a sign-in account"), d.noLinkedAccount, "/hr/employees", null],
    [t("Contracts ending within 60 days"), d.contractsEnding60, "/hr/employees", null],
  ] : [];

  // while the organisation is still being set up, the home page says what comes next (it disappears once every step is done)
  const steps: { title: string; why: string; done: boolean; href: string; perm: string }[] = d ? [
    { title: t("Add your departments"), why: t("People, leave and cost are reported per department."), done: d.setup.departments > 0, href: "/hr/departments", perm: "hrconfig.manage" },
    { title: t("Add your employees"), why: t("One by one, or all at once from a CSV file."), done: d.headcount > 0, href: "/hr/employees", perm: "employees.create" },
    { title: t("Give each employee a sign-in account"), why: t("Linked employees clock in, request leave and read their payslips themselves."), done: d.headcount > 0 && d.noLinkedAccount === 0, href: "/hr/employees", perm: "employees.update" },
    { title: t("Enter this year's public holidays"), why: t("Holidays are not counted as leave days or as absence."), done: d.setup.holidaysThisYear > 0, href: "/hr/leave-setup", perm: "leave.manage" },
    { title: t("Set every employee's pay"), why: t("A pay run only includes people whose compensation is set."), done: d.headcount > 0 && d.noCompensation === 0, href: "/hr/compensation", perm: "payroll.read" },
    { title: t("Run and lock the first pay run"), why: t("Payslips appear for employees when the run is locked."), done: d.setup.lockedPayRuns > 0, href: "/hr/payroll", perm: "payroll.read" },
  ] : [];
  const stepsDone = steps.filter((s) => s.done).length;

  return (
    <>
      <GreetingHero name={first} subtitle={hr ? t("Here is your organisation today: who is in, who is away and what is waiting.") : t("Here is your day: attendance, leave and what is waiting for you.")} highlights={chips} />

      {me?.linked && (
        <section className="home-block" aria-label={t("My day")}>
          {hr && <h2 className="block-title">{t("My day")}</h2>}
          <MyDayStats d={me} />
          {!hr && <MyDayPanels d={me} />}
        </section>
      )}
      {me && !me.linked && !hr && <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} /></Panel>}

      {d && stepsDone < steps.length && (
        <section className="home-block" aria-label={t("Get started")}>
          <ChartPanel icon={<Rocket size={18} />} title={t("Get started")} subtitle={t("{done} of {total} steps done. This list disappears when the last one is.", { done: stepsDone, total: steps.length })}>
            <ol className="start-list">
              {steps.map((s) => (
                <li key={s.title} className={s.done ? "done" : undefined}>
                  <span className="tick" aria-hidden><Check size={14} /></span>
                  <div><strong>{s.title}{s.done && <span className="sr-only"> — {t("done")}</span>}</strong><small>{s.why}</small></div>
                  {!s.done && can(s.perm) && <Button size="sm" href={s.href}>{t("Open page")}</Button>}
                </li>))}
            </ol>
          </ChartPanel>
        </section>
      )}

      {d && (
        <section className="home-block analytics" aria-label={t("The organisation today")}>
          <h2 className="block-title">{t("The organisation today")}</h2>
          <KpiRow>
            <Kpi icon={<Users size={20} />} label={t("Headcount")} value={num(d.headcount)} hint={t("active employees")} href="/hr/employees" />
            {a && <Kpi icon={<UserCheck size={20} />} tone="green" label={t("Present today")} value={num(a.presentToday)} hint={t("of {n} scheduled", { n: a.scheduledToday })} href="/hr/analytics/attendance" />}
            <Kpi icon={<CalendarX size={20} />} tone="purple" label={t("On leave today")} value={num(d.onLeaveToday)} hint={t("approved leave")} href={can("leave.approve") ? "/hr/leave-calendar" : undefined} />
            <Kpi icon={<Hourglass size={20} />} tone="orange" label={t("Waiting for a decision")} value={num(waiting)} hint={t("leave, attendance and profile changes")} href={can("leave.approve") ? "/hr/approvals" : undefined} />
            <Kpi icon={<UserPlus size={20} />} tone="teal" label={t("Joined, last 30 days")} value={num(d.joiners30)} hint={t("{n} in 90 days", { n: d.joiners90 })} />
            <Kpi icon={<FileClock size={20} />} tone="red" label={t("Contracts ending within 60 days")} value={num(d.contractsEnding60)} href="/hr/employees" />
          </KpiRow>
          <ChartGrid>
            {a && (
              <ChartPanel wide icon={<CalendarCheck size={18} />} title={t("Attendance, last 30 days")} subtitle={t("People who clocked in each day, and how many of them were late")} trailing={<Link className="va" href="/hr/analytics/attendance">{t("Attendance analytics")}{" "}<ArrowRight size={13} /></Link>}>
                <ColumnChart points={a.days.map((p) => ({ label: dayLabel(p.date), value: p.present, part: p.late, title: dayLabel(p.date) }))} label={t("Attendance per day")} valueName={t("Present")} partName={t("Late")} empty={t("Nobody has clocked in during the last 30 days. Employees clock in under My HR → My Attendance.")} />
              </ChartPanel>
            )}
            <ChartPanel icon={<TrendingUp size={18} />} title={t("Headcount, last 12 months")} trailing={<Link className="va" href="/hr/analytics/people">{t("People analytics")}{" "}<ArrowRight size={13} /></Link>}>
              <ColumnChart height={170} points={d.trend.map((p) => ({ label: monthShort(p.month), value: p.headcount }))} label={t("Headcount per month")} valueName={t("Headcount")} empty={t("No employees yet. Add them under Workforce → Employees.")} />
            </ChartPanel>
            <ChartPanel icon={<ClipboardCheck size={18} />} title={t("Needs attention")}>
              <ul className="fact-list">{queue.map(([label, n, href, perm]) => <li key={label} className={n > 0 ? "hot" : undefined}><span>{perm && !can(perm) ? label : <Link href={href}>{label}</Link>}</span><b>{n}</b></li>)}</ul>
            </ChartPanel>
            {l && (
              <ChartPanel icon={<CalendarDays size={18} />} title={t("Leave days per month")} trailing={<Link className="va" href="/hr/analytics/leave">{t("Leave analytics")}{" "}<ArrowRight size={13} /></Link>}>
                <ColumnChart height={170} tone="purple" points={l.daysByMonth.map((p) => ({ label: monthShort(p.label), value: p.value }))} label={t("Approved leave days per month")} valueName={t("Days")} empty={t("No approved leave in the last 12 months.")} />
              </ChartPanel>
            )}
            <ChartPanel icon={<Building2 size={18} />} title={t("By department")} subtitle={t("Active employees")}>
              <BarList rows={d.byDepartment.slice(0, 7).map((r) => ({ label: r.key === "—" ? t("No department") : r.key, value: r.count }))} label={t("Employees by department")} empty={t("No employees yet. Add them under Workforce → Employees.")} />
            </ChartPanel>
            <ChartPanel icon={<ArrowRight size={18} />} title={t("Shortcuts")}>
              <div className="shortcut-grid">
                {can("leave.approve") && <Button href="/hr/approvals"><Inbox size={15} /> {t("Approvals Inbox")}</Button>}
                {can("employees.create") && <Button href="/hr/employees"><UserPlus size={15} /> {t("Employees")}</Button>}
                {can("leave.approve") && <Button href="/hr/leave-calendar"><CalendarDays size={15} /> {t("Leave Calendar")}</Button>}
                {can("attendance.read_all") && <Button href="/hr/attendance-report"><CalendarCheck size={15} /> {t("Attendance Report")}</Button>}
                {can("payroll.read") && <Button href="/hr/payroll"><Banknote size={15} /> {t("Pay Runs")}</Button>}
                {can("payroll.read") && <Button href="/hr/analytics/payroll"><TrendingUp size={15} /> {t("Payroll analytics")}</Button>}
              </div>
            </ChartPanel>
          </ChartGrid>
        </section>
      )}
      {hr && me?.linked && <section className="home-block" aria-label={t("For me")}><MyDayPanels d={me} /></section>}
    </>
  );
}
