"use client";

import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Card, Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { MyHome } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate, monthName } from "@/lib/format";
import { t } from "@/i18n";

const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

/** ESS-001 — an employee's start page: today's attendance, leave left, what is still open, what to read. */
export default function MyHrHomePage() {
  const q = useQuery(() => get<MyHome>("/api/hr/me/home"), []); const d = q.data;
  const pending = d ? d.pendingLeave + d.pendingTime + d.pendingChanges : 0;
  return (
    <>
      <PageHeader title={d?.fullName ? t("Hello, {name}", { name: d.fullName.split(" ")[0] }) : t("My HR")} description={d?.linked ? `${d.employeeNo}${d.jobTitle ? ` · ${d.jobTitle}` : ""}` : undefined} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      {d && !d.linked && <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} /></Panel>}
      {d && d.linked && <div className="stack">
        <div className="stats">
          <Card className="stat"><div><small>{t("Today")}</small><span className="v" style={{ fontSize: 20 }}>{d.today ? (d.today.clockOut ? `${time(d.today.clockIn)} – ${time(d.today.clockOut)}` : t("In since {time}", { time: time(d.today.clockIn) })) : d.workingDay ? t("Not clocked in") : t("Day off")}</span><span className="lbl"><Link href="/hr/my-attendance">{t("My Attendance")}</Link></span></div></Card>
          {d.balances.slice(0, 2).map((b) => <Card className="stat" key={b.typeId}><div><small>{b.name}</small><span className="v" style={{ fontSize: 26 }}>{b.available}</span><span className="lbl">{t("days left")}{b.pending > 0 ? ` · ${b.pending} ${t("pending")}` : ""}</span></div></Card>)}
          <Card className="stat"><div><small>{t("Waiting for a decision")}</small><span className="v" style={{ fontSize: 26 }}>{pending}</span><span className="lbl"><Link href="/hr/my-leave">{t("My Leave")}</Link></span></div></Card>
        </div>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 300px), 1fr))", gap: 16 }}>
          <Panel title={t("To do")}>
            {d.tasks.length === 0 ? <p className="tab-placeholder">{t("Nothing open.")}</p> : <ul className="mini-list">{d.tasks.map((x) => <li key={x.id}><div><strong>{x.title}</strong>{x.dueDate && <small className="sub">{t("Due")} {fmtDate(x.dueDate)}</small>}</div>{x.overdue && <Badge tone="red">{t("Overdue")}</Badge>}</li>)}</ul>}
          </Panel>
          <Panel title={t("Announcements")} actions={<Link href="/hr/announcements">{t("See all")}</Link>}>
            {d.announcements.length === 0 ? <p className="tab-placeholder">{t("You have read everything.")}</p> : <ul className="mini-list">{d.announcements.map((a) => <li key={a.id}><div><strong><Link href="/hr/announcements">{a.title}</Link></strong></div>{a.requiresAck && <Badge tone="orange">{t("Needs your confirmation")}</Badge>}</li>)}</ul>}
          </Panel>
          <Panel title={t("Coming up")}>
            <ul className="mini-list">
              {d.nextLeave.map((l, i) => <li key={i}><div><strong>{l.leaveType}</strong><small className="sub">{fmtDate(l.startDate)}{l.endDate !== l.startDate ? ` – ${fmtDate(l.endDate)}` : ""}</small></div><Badge tone={l.status === "approved" ? "green" : "orange"}>{l.status === "approved" ? t("Approved") : t("Pending")}</Badge></li>)}
              {d.nextHoliday && <li><div><strong>{d.nextHoliday.slice(11)}</strong><small className="sub">{t("Public holiday")}</small></div><b>{fmtDate(d.nextHoliday.slice(0, 10))}</b></li>}
              {d.latestPayslip && <li><div><strong><Link href="/hr/my-payslips">{t("Latest payslip")}</Link></strong></div><b>{monthName(Number(d.latestPayslip.slice(0, 4)), Number(d.latestPayslip.slice(5, 7)))}</b></li>}
              {d.nextLeave.length === 0 && !d.nextHoliday && !d.latestPayslip && <li><div>{t("Nothing planned.")}</div></li>}
            </ul>
          </Panel>
        </div>
      </div>}
    </>
  );
}
