"use client";

import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel, Card } from "@/components/ui/card";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { CountBy, HrDashboard } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const Stat = ({ label, value, hint }: { label: string; value: string | number; hint?: string }) => <Card className="stat"><div><small>{label}</small><span className="v" style={{ fontSize: 26 }}>{value}</span>{hint && <span className="lbl">{hint}</span>}</div></Card>;

function Bars({ title, rows, label }: { title: string; rows: CountBy[]; label?: (k: string) => string }) {
  const max = Math.max(1, ...rows.map((r) => r.count));
  return (
    <Panel title={title}>
      {rows.length === 0 ? <p className="tab-placeholder">{t("No data.")}</p> : (
        <ul className="mini-list" aria-label={title}>{rows.map((r) => (
          <li key={r.key} style={{ display: "grid", gridTemplateColumns: "140px 1fr 40px", gap: 10, alignItems: "center" }}>
            <span>{label ? label(r.key) : r.key}</span>
            <span aria-hidden style={{ background: "var(--border, #2b3a63)", borderRadius: 4, height: 10 }}><span style={{ display: "block", width: `${(r.count / max) * 100}%`, height: 10, borderRadius: 4, background: "var(--blue, #0b62e6)" }} /></span>
            <b style={{ textAlign: "right" }}>{r.count}</b>
          </li>))}</ul>)}
    </Panel>
  );
}

/** RPT-001 — headcount and movement, and the queues that need someone's attention. Counts only; no salaries or personal details. */
export default function HrDashboardPage() {
  const { can } = useAuth(); const q = useQuery(() => get<HrDashboard>("/api/hr/dashboard"), []);
  const typeLabel = (k: string) => ({ permanent: t("Permanent"), contract: t("Contract"), probation: t("Probation"), intern: t("Intern") }[k] ?? k);
  const genderLabel = (k: string) => ({ female: t("Female"), male: t("Male"), unspecified: t("Not specified") }[k] ?? k);
  const tenureLabel = (k: string) => ({ "< 1 year": t("< 1 year"), "1–3 years": t("1–3 years"), "3–5 years": t("3–5 years"), "5–10 years": t("5–10 years"), "10+ years": t("10+ years") }[k] ?? k);
  return (
    <Require perm="employees.read">
      <PageHeader title={t("HR Dashboard")} description={t("Headcount, movement and what needs attention.")} />
      <QueryBoundary q={q} rows={6}>{(d) => {
        const queue: [string, number, string, string | null][] = [
          [t("Leave requests waiting"), d.pendingLeave, "/hr/leave-all", "leave.read_all"], [t("Attendance corrections waiting"), d.pendingCorrections, "/hr/time-approvals", "attendance.approve"],
          [t("Overtime requests waiting"), d.pendingOvertime, "/hr/time-approvals", "attendance.approve"], [t("Change requests waiting"), d.pendingChangeRequests, "/hr/change-requests", "employees.update"],
          [t("Employees without compensation"), d.noCompensation, "/hr/compensation", "payroll.read"], [t("Employees without a sign-in account"), d.noLinkedAccount, "/hr/employees", null],
          [t("Contracts ending within 60 days"), d.contractsEnding60, "/hr/employees", null],
        ];
        const maxH = Math.max(1, ...d.trend.map((p) => p.headcount));
        return (
          <div className="stack">
            <div className="stats">
              <Stat label={t("Headcount")} value={d.headcount} /><Stat label={t("Joined, last 30 days")} value={d.joiners30} hint={t("{n} in 90 days", { n: d.joiners90 })} /><Stat label={t("Left, last 12 months")} value={d.leavers12} hint={`${d.turnoverPct}% ${t("turnover")}`} /><Stat label={t("On leave today")} value={d.onLeaveToday} />
            </div>
            <Panel title={t("Needs attention")}>
              <ul className="mini-list">{queue.map(([label, n, href, perm]) => (
                <li key={label}><div><strong>{perm && !can(perm) ? label : <Link href={href}>{label}</Link>}</strong></div><b style={{ color: n > 0 ? "var(--orange, #e69b0b)" : undefined }}>{n}</b></li>))}</ul>
            </Panel>
            <Panel title={t("Headcount, last 12 months")} description={t("Joiners and leavers per month shown in the table below the bars.")}>
              <div role="img" aria-label={t("Headcount per month")} style={{ display: "flex", gap: 6, alignItems: "flex-end", height: 120 }}>{d.trend.map((p) => <div key={p.month} title={`${p.month}: ${p.headcount}`} style={{ flex: 1, background: "var(--blue, #0b62e6)", borderRadius: "4px 4px 0 0", height: `${(p.headcount / maxH) * 100}%`, minHeight: 2 }} />)}</div>
              <table className="tbl" style={{ marginTop: 12 }} aria-label={t("Monthly movement")}><thead><tr><th>{t("Month")}</th>{d.trend.map((p) => <th key={p.month} style={{ textAlign: "center", padding: "4px 2px" }}>{p.month.slice(5)}</th>)}</tr></thead>
                <tbody><tr><td>{t("Headcount")}</td>{d.trend.map((p) => <td key={p.month} style={{ textAlign: "center" }}>{p.headcount}</td>)}</tr><tr><td>{t("Joiners")}</td>{d.trend.map((p) => <td key={p.month} style={{ textAlign: "center" }}>{p.joiners}</td>)}</tr><tr><td>{t("Leavers")}</td>{d.trend.map((p) => <td key={p.month} style={{ textAlign: "center" }}>{p.leavers}</td>)}</tr></tbody></table>
            </Panel>
            <div className="grid-2" style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(300px, 1fr))", gap: 16 }}>
              <Bars title={t("By department")} rows={d.byDepartment} /><Bars title={t("By employment type")} rows={d.byType} label={typeLabel} /><Bars title={t("By gender")} rows={d.byGender} label={genderLabel} /><Bars title={t("By length of service")} rows={d.byTenure} label={tenureLabel} />
            </div>
          </div>);
      }}</QueryBoundary>
    </Require>
  );
}
