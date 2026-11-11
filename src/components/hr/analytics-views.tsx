"use client";

import Link from "next/link";
import { ArrowRight, Banknote, Building2, CalendarCheck, CalendarClock, CalendarDays, CalendarX, Clock, FileClock, HandCoins, Hourglass, MapPinOff, Percent, Receipt, ReceiptText, Timer, TrendingUp, UserCheck, UserMinus, UserPlus, Users, Wallet } from "lucide-react";
import { BarList, ChartGrid, ChartPanel, ColumnChart, Kpi, KpiRow, ShareBar, compact, monthShort, num, type Share, type Tone } from "@/components/charts/analytics";
import { QueryBoundary } from "@/components/query-boundary";
import { claimLabel } from "@/components/hr/claims";
import { get } from "@/lib/api/client";
import type { AttendanceAnalytics, ClaimsAnalytics, HrDashboard, LeaveAnalytics, PayrollAnalytics } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { idr, monthName } from "@/lib/format";
import { formatLocale, t } from "@/i18n";

/** The analytics of each HR module (RPT-002). One view per module, plus a one-row summary used at the top of the module's main list. */

const tones: Tone[] = ["blue", "teal", "orange", "purple", "green", "red"];
const typeLabel = (k: string) => t(({ permanent: "Permanent", contract: "Contract", probation: "Probation", intern: "Intern" } as Record<string, string>)[k] ?? k);
const genderLabel = (k: string) => t(({ female: "Female", male: "Male", unspecified: "Not specified" } as Record<string, string>)[k] ?? k);
const modeLabel = (k: string) => t(({ office: "Office", remote: "Remote", field: "Field" } as Record<string, string>)[k] ?? k);
const statusLabel = (k: string) => t(({ pending: "Pending", approved: "Approved", rejected: "Rejected", cancelled: "Cancelled", paid: "Paid" } as Record<string, string>)[k] ?? k);
const statusTone: Record<string, Tone> = { pending: "orange", approved: "green", rejected: "red", cancelled: "purple", paid: "blue" };
const partLabel = (k: string) => t(({ basic: "Basic pay", allowance: "Fixed allowance", overtime: "Overtime", extras: "Bonuses and other items", employer_bpjs: "Employer BPJS" } as Record<string, string>)[k] ?? k);
const period = (ym: string | null) => (ym ? monthName(Number(ym.slice(0, 4)), Number(ym.slice(5, 7))) : "—");
const dayLabel = (iso: string) => new Date(iso + "T00:00:00").toLocaleDateString(formatLocale(), { day: "numeric", month: "short" });
const pct = (n: number) => `${num(n)}%`;
/** Money on a tile: short ("Rp 339,2 jt") so it never wraps, with the exact amount as the tooltip. */
const money = (n: number) => <span title={idr(n)}>{new Intl.NumberFormat(formatLocale(), { style: "currency", currency: "IDR", notation: "compact", maximumFractionDigits: 1 }).format(n)}</span>;
/** Hours as "5 h" or, from two days up, as days: decisions are judged in days, not in hundreds of hours. */
const duration = (hours: number) => (hours >= 48 ? t("{n} days", { n: num(Math.round(hours / 2.4) / 10) }) : t("{n} h", { n: num(hours) }));
const more = (href: string) => <Link className="va" href={href}>{t("Open list")}{" "}<ArrowRight size={13} /></Link>;

// ------------------------------------------------------------------ people
export function PeopleAnalyticsView({ d }: { d: HrDashboard }) {
  return (
    <div className="analytics">
      <KpiRow>
        <Kpi icon={<Users size={20} />} label={t("Headcount")} value={num(d.headcount)} hint={t("active employees")} href="/hr/employees" />
        <Kpi icon={<UserPlus size={20} />} tone="green" label={t("Joined, last 30 days")} value={num(d.joiners30)} hint={t("{n} in 90 days", { n: d.joiners90 })} />
        <Kpi icon={<UserMinus size={20} />} tone="red" label={t("Left, last 12 months")} value={num(d.leavers12)} hint={`${pct(d.turnoverPct)} ${t("turnover")}`} />
        <Kpi icon={<FileClock size={20} />} tone="orange" label={t("Contracts ending within 60 days")} value={num(d.contractsEnding60)} hint={t("contract, probation and intern")} href="/hr/employees" />
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<TrendingUp size={18} />} title={t("Headcount, last 12 months")} subtitle={t("Employees on the last day of each month")}>
          <ColumnChart points={d.trend.map((p) => ({ label: monthShort(p.month), value: p.headcount, title: period(p.month) }))} label={t("Headcount per month")} valueName={t("Headcount")} empty={t("No employees yet. Add them under Workforce → Employees.")} />
        </ChartPanel>
        <ChartPanel icon={<UserPlus size={18} />} title={t("Joiners per month")} subtitle={t("By hire date")}>
          <ColumnChart height={150} tone="green" points={d.trend.map((p) => ({ label: monthShort(p.month), value: p.joiners, title: period(p.month) }))} label={t("Joiners per month")} valueName={t("Joiners")} />
        </ChartPanel>
        <ChartPanel icon={<UserMinus size={18} />} title={t("Leavers per month")} subtitle={t("By last working day")}>
          <ColumnChart height={150} tone="red" points={d.trend.map((p) => ({ label: monthShort(p.month), value: p.leavers, title: period(p.month) }))} label={t("Leavers per month")} valueName={t("Leavers")} />
        </ChartPanel>
        <ChartPanel icon={<Building2 size={18} />} title={t("By department")} subtitle={t("Active employees")}>
          <BarList rows={d.byDepartment.map((r) => ({ label: r.key === "—" ? t("No department") : r.key, value: r.count }))} label={t("Employees by department")} />
        </ChartPanel>
        <ChartPanel icon={<Hourglass size={18} />} title={t("By length of service")}>
          <BarList tone="teal" rows={["< 1 year", "1–3 years", "3–5 years", "5–10 years", "10+ years"].map((k) => ({ label: t(k), value: d.byTenure.find((r) => r.key === k)?.count ?? 0 }))} label={t("Employees by length of service")} />
        </ChartPanel>
        <ChartPanel icon={<ReceiptText size={18} />} title={t("By employment type")}>
          <ShareBar parts={d.byType.map((r, i): Share => ({ label: typeLabel(r.key), value: r.count, tone: tones[i % tones.length] }))} label={t("Employees by employment type")} />
        </ChartPanel>
        <ChartPanel icon={<Users size={18} />} title={t("By gender")}>
          <ShareBar parts={d.byGender.map((r, i): Share => ({ label: genderLabel(r.key), value: r.count, tone: tones[(i + 3) % tones.length] }))} label={t("Employees by gender")} />
        </ChartPanel>
      </ChartGrid>
    </div>
  );
}

// ------------------------------------------------------------------ attendance
export function AttendanceAnalyticsView({ d }: { d: AttendanceAnalytics }) {
  return (
    <div className="analytics">
      <KpiRow>
        <Kpi icon={<UserCheck size={20} />} tone="green" label={t("Present today")} value={num(d.presentToday)} hint={t("of {n} scheduled", { n: d.scheduledToday })} />
        <Kpi icon={<Timer size={20} />} tone="orange" label={t("Late today")} value={num(d.lateToday)} hint={t("first clock-in after the shift's grace time")} />
        <Kpi icon={<CalendarX size={20} />} tone="purple" label={t("On leave today")} value={num(d.onLeaveToday)} hint={t("approved leave")} />
        <Kpi icon={<Clock size={20} />} tone="red" label={t("Not in yet")} value={num(d.notInYetToday)} hint={t("scheduled, no clock-in, not on leave")} />
        <Kpi icon={<Percent size={20} />} label={t("On-time rate")} value={pct(d.onTimePct)} hint={t("last 30 days")} />
        <Kpi icon={<Hourglass size={20} />} tone="teal" label={t("Average hours per session")} value={num(d.avgHours)} hint={t("last 30 days")} />
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<CalendarCheck size={18} />} title={t("Attendance, last 30 days")} subtitle={t("People who clocked in each day, and how many of them were late")} trailing={more("/hr/attendance-report")}>
          <ColumnChart points={d.days.map((p) => ({ label: dayLabel(p.date), value: p.present, part: p.late, title: dayLabel(p.date) }))} label={t("Attendance per day")} valueName={t("Present")} partName={t("Late")} tone="blue" partTone="orange" empty={t("Nobody has clocked in during the last 30 days. Employees clock in under My HR → My Attendance.")} />
        </ChartPanel>
        <ChartPanel icon={<Building2 size={18} />} title={t("Where people worked")} subtitle={t("Working days in the last 30 days")}>
          <ShareBar parts={["office", "remote", "field"].map((k, i): Share => ({ label: modeLabel(k), value: d.byMode.find((m) => m.key === k)?.count ?? 0, tone: tones[i] }))} label={t("Working days by place of work")} />
        </ChartPanel>
        <ChartPanel icon={<Timer size={18} />} title={t("Late arrivals by department")} subtitle={t("Last 30 days")}>
          <BarList tone="orange" rows={d.lateByDepartment.map((r) => ({ label: r.key === "—" ? t("No department") : r.key, value: r.count }))} label={t("Late arrivals by department")} />
        </ChartPanel>
        <ChartPanel icon={<Clock size={18} />} title={t("Approved overtime")} subtitle={t("Hours per month, last 6 months")}>
          <ColumnChart height={160} tone="purple" points={d.overtimeHours.map((p) => ({ label: monthShort(p.label), value: p.value, title: period(p.label) }))} label={t("Approved overtime hours per month")} valueName={t("Hours")} />
        </ChartPanel>
        <ChartPanel icon={<MapPinOff size={18} />} title={t("Needs a look")} trailing={more("/hr/time-approvals")}>
          <ul className="fact-list">
            <li><span>{t("Attendance corrections waiting")}</span><b>{d.pendingCorrections}</b></li>
            <li><span>{t("Overtime requests waiting")}</span><b>{d.pendingOvertime}</b></li>
            <li><span>{t("Sessions still open (no clock-out)")}</span><b>{d.openSessions}</b></li>
            <li><span>{t("Office clock-ins outside every work location")}</span><b>{d.outsideZone}</b></li>
          </ul>
        </ChartPanel>
      </ChartGrid>
    </div>
  );
}

// ------------------------------------------------------------------ leave
export function LeaveAnalyticsView({ d }: { d: LeaveAnalytics }) {
  const daysFmt = (n: number) => num(n);
  return (
    <div className="analytics">
      <KpiRow>
        <Kpi icon={<CalendarX size={20} />} tone="purple" label={t("On leave today")} value={num(d.onLeaveToday)} href="/hr/leave-calendar" hint={t("approved leave")} />
        <Kpi icon={<Hourglass size={20} />} tone="orange" label={t("Waiting for a decision")} value={num(d.pending)} href="/hr/leave-all" hint={t("leave requests")} />
        <Kpi icon={<CalendarDays size={20} />} label={t("Leave days this year")} value={daysFmt(d.daysThisYear)} hint={t("approved working days")} />
        <Kpi icon={<CalendarClock size={20} />} tone="teal" label={t("Away in the next 30 days")} value={num(d.upcomingNext30)} hint={t("people with approved leave")} />
        <Kpi icon={<Percent size={20} />} tone="green" label={t("Approval rate")} value={pct(d.approvalRatePct)} hint={t("decided in the last 12 months")} />
        <Kpi icon={<Timer size={20} />} tone="blue" label={t("Average time to decide")} value={duration(d.avgDecisionHours)} hint={t("from request to decision")} />
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<CalendarDays size={18} />} title={t("Leave days per month")} subtitle={t("Approved days, counted in the month the leave starts")} trailing={more("/hr/leave-all")}>
          <ColumnChart tone="purple" points={d.daysByMonth.map((p) => ({ label: monthShort(p.label), value: p.value, title: period(p.label) }))} label={t("Approved leave days per month")} valueName={t("Days")} empty={t("No approved leave in the last 12 months.")} />
        </ChartPanel>
        <ChartPanel icon={<CalendarCheck size={18} />} title={t("By leave type")} subtitle={t("Approved days this year")}>
          <BarList tone="purple" rows={d.byType.map((r) => ({ label: r.key, value: r.amount, note: t("{n} requests", { n: r.count }) }))} label={t("Leave days by type")} />
        </ChartPanel>
        <ChartPanel icon={<Building2 size={18} />} title={t("By department")} subtitle={t("Approved days this year")}>
          <BarList tone="teal" rows={d.byDepartment.map((r) => ({ label: r.key === "—" ? t("No department") : r.key, value: r.amount }))} label={t("Leave days by department")} />
        </ChartPanel>
        <ChartPanel icon={<ReceiptText size={18} />} title={t("Requests this year")} subtitle={t("By outcome")}>
          <ShareBar parts={d.byStatus.map((r): Share => ({ label: statusLabel(r.key), value: r.count, tone: statusTone[r.key] ?? "blue" }))} label={t("Leave requests by outcome")} />
        </ChartPanel>
      </ChartGrid>
    </div>
  );
}

// ------------------------------------------------------------------ payroll
export function PayrollAnalyticsView({ d }: { d: PayrollAnalytics }) {
  const none = d.latestPeriod === null;
  return (
    <div className="analytics">
      <KpiRow wide>
        <Kpi icon={<Banknote size={20} />} label={t("Gross pay")} value={none ? "—" : money(d.latestGross)} hint={none ? t("no locked pay run yet") : `${period(d.latestPeriod)}${d.changePct !== null ? ` · ${d.changePct > 0 ? "+" : ""}${pct(d.changePct)} ${t("vs the month before")}` : ""}`} href="/hr/payroll" />
        <Kpi icon={<Wallet size={20} />} tone="green" label={t("Take-home pay")} value={none ? "—" : money(d.latestNet)} hint={none ? undefined : t("{n} employees paid", { n: d.latestEmployees })} />
        <Kpi icon={<ReceiptText size={20} />} tone="orange" label={t("PPh 21 withheld")} value={none ? "—" : money(d.latestTax)} hint={none ? undefined : period(d.latestPeriod)} />
        <Kpi icon={<Building2 size={20} />} tone="purple" label={t("Total cost to the company")} value={none ? "—" : money(d.latestEmployerCost)} hint={t("gross pay plus employer BPJS")} />
        <Kpi icon={<Hourglass size={20} />} tone="teal" label={t("Pay runs in progress")} value={num(d.runsInProgress)} hint={t("draft, calculated or approved")} href="/hr/payroll" />
        <Kpi icon={<HandCoins size={20} />} tone="red" label={t("Loans outstanding")} value={money(d.loansOutstanding)} hint={t("{n} active loans", { n: d.activeLoans })} href="/hr/loans" />
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<TrendingUp size={18} />} title={t("Pay per month")} subtitle={t("Locked pay runs only; take-home pay is the part of gross pay that reached employees")} trailing={more("/hr/payroll")}>
          <ColumnChart points={d.months.map((p) => ({ label: monthShort(p.period), value: p.gross, part: p.net, title: period(p.period) }))} format={compact} label={t("Gross and take-home pay per month")} valueName={t("Gross pay")} partName={t("Take-home pay")} tone="blue" partTone="green" empty={t("No locked pay run yet. The figures appear once a pay run is locked.")} />
        </ChartPanel>
        <ChartPanel icon={<Building2 size={18} />} title={t("Cost by department")} subtitle={none ? undefined : period(d.latestPeriod)}>
          <BarList tone="purple" format={compact} rows={d.costByDepartment.map((r) => ({ label: r.key === "—" ? t("No department") : r.key, value: r.amount, note: t("{n} people", { n: r.count }) }))} label={t("Cost to the company by department")} />
        </ChartPanel>
        <ChartPanel icon={<ReceiptText size={18} />} title={t("What the cost is made of")} subtitle={none ? undefined : period(d.latestPeriod)}>
          <ShareBar format={compact} parts={d.composition.map((r, i): Share => ({ label: partLabel(r.key), value: r.amount, tone: tones[i % tones.length] }))} label={t("Composition of the pay cost")} />
        </ChartPanel>
        <ChartPanel wide icon={<Users size={18} />} title={t("Employees paid per month")} subtitle={t("Locked pay runs")}>
          <ColumnChart height={150} tone="teal" points={d.months.map((p) => ({ label: monthShort(p.period), value: p.employees, title: period(p.period) }))} label={t("Employees paid per month")} valueName={t("Employees")} />
        </ChartPanel>
      </ChartGrid>
    </div>
  );
}

// ------------------------------------------------------------------ claims
export function ClaimsAnalyticsView({ d }: { d: ClaimsAnalytics }) {
  return (
    <div className="analytics">
      <KpiRow wide>
        <Kpi icon={<Hourglass size={20} />} tone="orange" label={t("Waiting for a decision")} value={num(d.pending)} hint={idr(d.pendingAmount)} href="/hr/claims" />
        <Kpi icon={<Receipt size={20} />} label={t("Approved, not yet paid")} value={money(d.approvedUnpaidAmount)} hint={t("joins the next pay run")} />
        <Kpi icon={<Wallet size={20} />} tone="green" label={t("Paid this year")} value={money(d.paidThisYear)} hint={t("through locked pay runs")} />
        <Kpi icon={<Percent size={20} />} tone="teal" label={t("Approval rate")} value={pct(d.approvalRatePct)} hint={t("of decided claims")} />
        <Kpi icon={<Timer size={20} />} tone="purple" label={t("Average time to decide")} value={duration(d.avgDecisionHours)} hint={t("from claim to decision")} />
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<TrendingUp size={18} />} title={t("Claims per month")} subtitle={t("Approved and paid claims, by expense date")} trailing={more("/hr/claims")}>
          <ColumnChart tone="teal" format={compact} points={d.amountByMonth.map((p) => ({ label: monthShort(p.label), value: p.value, title: period(p.label) }))} label={t("Approved claim amount per month")} valueName={t("Amount")} empty={t("No approved claims in the last 12 months.")} />
        </ChartPanel>
        <ChartPanel icon={<Receipt size={18} />} title={t("By category")} subtitle={t("Approved and paid this year")}>
          <BarList tone="teal" format={compact} rows={d.byCategory.map((r) => ({ label: claimLabel(r.key), value: r.amount, note: t("{n} claims", { n: r.count }) }))} label={t("Claim amount by category")} />
        </ChartPanel>
        <ChartPanel icon={<Building2 size={18} />} title={t("By department")} subtitle={t("Approved and paid this year")}>
          <BarList tone="blue" format={compact} rows={d.byDepartment.map((r) => ({ label: r.key === "—" ? t("No department") : r.key, value: r.amount }))} label={t("Claim amount by department")} />
        </ChartPanel>
        <ChartPanel icon={<ReceiptText size={18} />} title={t("Claims this year")} subtitle={t("Amount by status")}>
          <ShareBar format={compact} parts={d.byStatus.map((r): Share => ({ label: statusLabel(r.key), value: r.amount, tone: statusTone[r.key] ?? "blue" }))} label={t("Claim amount by status")} />
        </ChartPanel>
      </ChartGrid>
    </div>
  );
}

// ------------------------------------------------------------------ one-row summaries for the top of each module's list
const SummaryFrame = ({ href, children }: { href: string; children: React.ReactNode }) => (
  <section className="module-summary" aria-label={t("Summary")}>
    <div className="kpis compact">{children}</div>
    <Link className="ms-more" href={href}>{t("See analytics")}{" "}<ArrowRight size={14} /></Link>
  </section>
);
/** Summaries stay out of the way: while loading, and when the figures cannot be read, the list below simply starts at the top. */
export function PeopleSummary() {
  const q = useQuery(() => get<HrDashboard>("/api/hr/dashboard", undefined, { noRoute: true }), []);
  return <QueryBoundary q={q} bare quiet>{(d) => (
    <SummaryFrame href="/hr/analytics/people">
      <Kpi icon={<Users size={18} />} label={t("Headcount")} value={num(d.headcount)} />
      <Kpi icon={<UserPlus size={18} />} tone="green" label={t("Joined, last 30 days")} value={num(d.joiners30)} />
      <Kpi icon={<UserMinus size={18} />} tone="red" label={t("Left, last 12 months")} value={num(d.leavers12)} hint={`${pct(d.turnoverPct)} ${t("turnover")}`} />
      <Kpi icon={<FileClock size={18} />} tone="orange" label={t("Contracts ending within 60 days")} value={num(d.contractsEnding60)} />
    </SummaryFrame>)}</QueryBoundary>;
}
export function AttendanceSummary() {
  const q = useQuery(() => get<AttendanceAnalytics>("/api/hr/analytics/attendance", undefined, { noRoute: true }), []);
  return <QueryBoundary q={q} bare quiet>{(d) => (
    <SummaryFrame href="/hr/analytics/attendance">
      <Kpi icon={<UserCheck size={18} />} tone="green" label={t("Present today")} value={num(d.presentToday)} hint={t("of {n} scheduled", { n: d.scheduledToday })} />
      <Kpi icon={<Timer size={18} />} tone="orange" label={t("Late today")} value={num(d.lateToday)} />
      <Kpi icon={<Clock size={18} />} tone="red" label={t("Not in yet")} value={num(d.notInYetToday)} />
      <Kpi icon={<Percent size={18} />} label={t("On-time rate")} value={pct(d.onTimePct)} hint={t("last 30 days")} />
    </SummaryFrame>)}</QueryBoundary>;
}
export function LeaveSummary() {
  const q = useQuery(() => get<LeaveAnalytics>("/api/hr/analytics/leave", undefined, { noRoute: true }), []);
  return <QueryBoundary q={q} bare quiet>{(d) => (
    <SummaryFrame href="/hr/analytics/leave">
      <Kpi icon={<CalendarX size={18} />} tone="purple" label={t("On leave today")} value={num(d.onLeaveToday)} />
      <Kpi icon={<Hourglass size={18} />} tone="orange" label={t("Waiting for a decision")} value={num(d.pending)} />
      <Kpi icon={<CalendarDays size={18} />} label={t("Leave days this year")} value={num(d.daysThisYear)} />
      <Kpi icon={<CalendarClock size={18} />} tone="teal" label={t("Away in the next 30 days")} value={num(d.upcomingNext30)} />
    </SummaryFrame>)}</QueryBoundary>;
}
export function PayrollSummary() {
  const q = useQuery(() => get<PayrollAnalytics>("/api/hr/analytics/payroll", undefined, { noRoute: true }), []);
  return <QueryBoundary q={q} bare quiet>{(d) => (
    <SummaryFrame href="/hr/analytics/payroll">
      <Kpi icon={<Banknote size={18} />} label={t("Gross pay")} value={d.latestPeriod ? money(d.latestGross) : "—"} hint={d.latestPeriod ? period(d.latestPeriod) : t("no locked pay run yet")} />
      <Kpi icon={<Wallet size={18} />} tone="green" label={t("Take-home pay")} value={d.latestPeriod ? money(d.latestNet) : "—"} />
      <Kpi icon={<Building2 size={18} />} tone="purple" label={t("Total cost to the company")} value={d.latestPeriod ? money(d.latestEmployerCost) : "—"} />
      <Kpi icon={<Hourglass size={18} />} tone="teal" label={t("Pay runs in progress")} value={num(d.runsInProgress)} />
    </SummaryFrame>)}</QueryBoundary>;
}
export function ClaimsSummary() {
  const q = useQuery(() => get<ClaimsAnalytics>("/api/hr/analytics/claims", undefined, { noRoute: true }), []);
  return <QueryBoundary q={q} bare quiet>{(d) => (
    <SummaryFrame href="/hr/analytics/claims">
      <Kpi icon={<Hourglass size={18} />} tone="orange" label={t("Waiting for a decision")} value={num(d.pending)} hint={idr(d.pendingAmount)} />
      <Kpi icon={<Receipt size={18} />} label={t("Approved, not yet paid")} value={money(d.approvedUnpaidAmount)} />
      <Kpi icon={<Wallet size={18} />} tone="green" label={t("Paid this year")} value={money(d.paidThisYear)} />
      <Kpi icon={<Percent size={18} />} tone="teal" label={t("Approval rate")} value={pct(d.approvalRatePct)} />
    </SummaryFrame>)}</QueryBoundary>;
}
