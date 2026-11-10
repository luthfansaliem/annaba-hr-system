"use client";

import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Card, Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { TeamDashboard } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { t } from "@/i18n";

const Stat = ({ label, value }: { label: string; value: number }) => <Card className="stat"><div><small>{label}</small><span className="v" style={{ fontSize: 26 }}>{value}</span></div></Card>;
const tone: Record<string, ["green" | "blue" | "orange" | "gray", string]> = { present: ["green", "Present"], leave: ["blue", "On leave"], absent: ["orange", "Not clocked in"], off: ["gray", "Day off"] };

/** MSS-002 — a manager's view of the people who report to them: who is in today, who is away soon, whose contract is ending. */
export default function TeamPage() {
  const q = useQuery(() => get<TeamDashboard>("/api/hr/team"), []); const d = q.data;
  return (
    <>
      <PageHeader title={t("My Team")} description={t("The people who report to you (or to someone who delegated approvals to you), today.")} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      {d && d.size === 0 && <Panel><EmptyState title={t("Nobody reports to you")} description={t("When HR sets you as someone's manager, they appear here.")} /></Panel>}
      {d && d.size > 0 && <div className="stack">
        <div className="stats"><Stat label={t("Team members")} value={d.size} /><Stat label={t("Present")} value={d.present} /><Stat label={t("On leave")} value={d.onLeave} /><Stat label={t("Not clocked in")} value={d.notClockedIn} /></div>
        {d.pendingApprovals > 0 && <div className="form-alert" role="note"><Link href="/hr/approvals">{t("{n} request(s) are waiting for your decision.", { n: d.pendingApprovals })}</Link></div>}
        <Panel title={t("Today")}>
          <div style={{ overflowX: "auto" }}><table className="tbl" aria-label={t("Team today")}><thead><tr><th>{t("Employee")}</th><th>{t("Status")}</th><th className="hide-sm">{t("Clock in")}</th></tr></thead>
            <tbody>{d.members.map((m) => <tr key={m.employeeId}><td><b>{m.fullName}</b><small className="sub">{m.employeeNo}{m.jobTitle ? ` · ${m.jobTitle}` : ""}</small></td>
              <td><Badge tone={tone[m.today]?.[0] ?? "gray"}>{t(tone[m.today]?.[1] ?? m.today)}</Badge>{m.late && <small className="sub">{t("Late")}</small>}</td>
              <td className="hide-sm">{m.clockIn ? new Date(m.clockIn).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }) : "—"}</td></tr>)}</tbody></table></div>
        </Panel>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 300px), 1fr))", gap: 16 }}>
          <Panel title={t("Leave in the next 30 days")}>
            {d.upcomingLeave.length === 0 ? <p className="tab-placeholder">{t("Nobody is away.")}</p> : <ul className="mini-list">{d.upcomingLeave.map((l, i) => <li key={i}><div><strong>{l.fullName}</strong><small className="sub">{l.leaveType} · {fmtDate(l.startDate)}{l.endDate !== l.startDate ? ` – ${fmtDate(l.endDate)}` : ""}</small></div><Badge tone={l.status === "approved" ? "green" : "orange"}>{l.status === "approved" ? t("Approved") : t("Pending")}</Badge></li>)}</ul>}
          </Panel>
          <Panel title={t("Contracts ending within 60 days")}>
            {d.contractsEnding.length === 0 ? <p className="tab-placeholder">{t("None.")}</p> : <ul className="mini-list">{d.contractsEnding.map((m) => <li key={m.employeeId}><div><strong>{m.fullName}</strong><small className="sub">{m.jobTitle ?? ""}</small></div><b>{fmtDate(m.contractEnd)}</b></li>)}</ul>}
          </Panel>
        </div>
      </div>}
    </>
  );
}
