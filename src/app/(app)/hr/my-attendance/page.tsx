"use client";

import { useEffect, useState } from "react";
import { LogIn, LogOut } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel, Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { SelectInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import type { AttendanceLog, MyAttendance } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const time = (iso: string | null) => (iso ? new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false }) : "—");
const monthKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;

export default function MyAttendancePage() {
  const run = useRun(); const [month, setMonth] = useState(monthKey(new Date()));
  const q = useQuery(() => get<MyAttendance>("/api/hr/attendance/me", { month }), [month]);
  const [now, setNow] = useState(() => new Date());
  useEffect(() => { const i = setInterval(() => setNow(new Date()), 30000); return () => clearInterval(i); }, []);
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  const open = q.data?.open;
  const clock = async (kind: "clock-in" | "clock-out") => { if ((await run(() => post(`/api/hr/attendance/${kind}`), kind === "clock-in" ? t("Clocked in.") : t("Clocked out."))).ok) q.reload(); };
  const months = Array.from({ length: 6 }, (_, i) => { const d = new Date(); d.setMonth(d.getMonth() - i); return monthKey(d); });
  const cols: Column<AttendanceLog>[] = [
    { key: "d", header: t("Date"), cell: (l) => fmtDate(l.date) }, { key: "in", header: t("Clock in"), cell: (l) => <>{time(l.clockIn)} {l.late && <Badge tone="orange">{t("Late")}</Badge>}</> },
    { key: "out", header: t("Clock out"), cell: (l) => l.clockOut ? time(l.clockOut) : <Badge tone="blue">{t("Open")}</Badge> }, { key: "h", header: t("Hours"), cell: (l) => l.hours ?? "—" },
  ];
  return (
    <>
      <PageHeader title={t("My Attendance")} description={t("Clock in and out. The time is taken from the server, not from your device.")} />
      {unlinked ? <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account so you can clock in.")} /></Panel> : <>
        <Panel>
          <div className="row" style={{ gap: 16, alignItems: "center", justifyContent: "space-between", flexWrap: "wrap" }}>
            <div><b style={{ fontSize: 28 }}>{time(now.toISOString())}</b><small className="sub">{open ? t("Clocked in since {time}", { time: time(open.clockIn) }) : t("Not clocked in")}</small></div>
            {open ? <Button variant="danger" onClick={() => clock("clock-out")}><LogOut size={16} /> {t("Clock out")}</Button> : <Button variant="primary" onClick={() => clock("clock-in")}><LogIn size={16} /> {t("Clock in")}</Button>}
          </div>
        </Panel>
        <div className="stats" style={{ margin: "16px 0" }}>
          <Card className="stat"><div><small>{t("Days present")}</small><span className="v">{q.data?.daysPresent ?? 0}</span></div></Card>
          <Card className="stat"><div><small>{t("Late days")}</small><span className="v">{q.data?.lateDays ?? 0}</span></div></Card>
          <Card className="stat"><div><small>{t("Hours worked")}</small><span className="v">{(q.data?.hours ?? 0).toFixed(1)}</span></div></Card>
        </div>
        <Panel title={t("Records")} actions={<SelectInput aria-label={t("Month")} value={month} onChange={(e) => setMonth(e.target.value)} options={months} />}>
          {!q.loading && (q.data?.logs.length ?? 0) === 0 ? <EmptyState title={t("No records this month")} description={t("Your clock-ins will appear here.")} />
            : <DataTable label={t("Attendance records")} columns={cols} rows={q.data?.logs ?? []} rowKey={(l) => l.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
        </Panel></>}
    </>
  );
}
