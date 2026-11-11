"use client";

import Link from "next/link";
import { CalendarDays, Clock, Hourglass } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { Badge } from "@/components/ui/badge";
import type { MyHome } from "@/lib/api/types";
import { fmtDate, monthName } from "@/lib/format";
import { t } from "@/i18n";

const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });

/** ESS-001 — an employee's own day: today's attendance, leave left and what is waiting. Shown on the home page and on My HR. */
export function MyDayStats({ d }: { d: MyHome }) {
  const pending = d.pendingLeave + d.pendingTime + d.pendingChanges;
  return (
    <KpiRow label={t("My day")}>
      <Kpi icon={<Clock size={20} />} tone={d.today ? "green" : "orange"} label={t("Today")} href="/hr/my-attendance"
        value={d.today ? (d.today.clockOut ? `${time(d.today.clockIn)} – ${time(d.today.clockOut)}` : t("In since {time}", { time: time(d.today.clockIn) })) : d.workingDay ? t("Not clocked in") : t("Day off")} hint={t("My Attendance")} />
      {d.balances.slice(0, 2).map((b) => <Kpi key={b.typeId} icon={<CalendarDays size={20} />} tone="purple" label={b.name} value={b.available} hint={`${t("days left")}${b.pending > 0 ? ` · ${b.pending} ${t("pending")}` : ""}`} href="/hr/my-leave" />)}
      <Kpi icon={<Hourglass size={20} />} tone="blue" label={t("Waiting for a decision")} value={pending} hint={t("My Leave")} href="/hr/my-leave" />
    </KpiRow>
  );
}

/** What is still open for me, what to read and what is coming up. */
export function MyDayPanels({ d }: { d: MyHome }) {
  return (
    <div className="panel-grid">
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
  );
}
