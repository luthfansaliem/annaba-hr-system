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
import { RequestTimeDrawer, TimeRequestTable } from "@/components/hr/time-requests";
import { Tabs } from "@/components/ui/tabs";
import type { AttendanceLog, MyAttendance, Paged, TimeRequest } from "@/lib/api/types";
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
  const [reqKind, setReqKind] = useState<"corrections" | "overtime">("corrections"); const [drawer, setDrawer] = useState<"correction" | "overtime" | null>(null);
  const reqs = useQuery(() => get<Paged<TimeRequest>>(`/api/hr/attendance/${reqKind}`, { scope: "mine", pageSize: 50 }), [reqKind]);
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  const open = q.data?.open;
  const [mode, setMode] = useState("office");
  /** Asks the browser where we are, only when the company has work locations and the person says "office". No answer within 6 s (or a refusal) still clocks in; HR then sees it as unconfirmed. */
  const position = () => new Promise<{ latitude: number; longitude: number } | null>((resolve) => {
    if (!q.data?.geofence || mode !== "office" || !("geolocation" in navigator)) { resolve(null); return; }
    navigator.geolocation.getCurrentPosition((p) => resolve({ latitude: p.coords.latitude, longitude: p.coords.longitude }), () => resolve(null), { timeout: 6000, maximumAge: 60000 });
  });
  const clock = async (kind: "clock-in" | "clock-out") => {
    const body = kind === "clock-in" ? { mode, ...((await position()) ?? {}) } : undefined;
    const r = await run(() => post<AttendanceLog>(`/api/hr/attendance/${kind}`, body), kind === "clock-in" ? t("Clocked in.") : t("Clocked out."));
    if (r.ok) q.reload();
  };
  const months = Array.from({ length: 6 }, (_, i) => { const d = new Date(); d.setMonth(d.getMonth() - i); return monthKey(d); });
  const cols: Column<AttendanceLog>[] = [
    { key: "d", header: t("Date"), cell: (l) => fmtDate(l.date) }, { key: "in", header: t("Clock in"), cell: (l) => <>{time(l.clockIn)} {l.late && <Badge tone="orange">{t("Late")}</Badge>} {l.workMode === "remote" && <Badge tone="blue" dot={false}>{t("From home")}</Badge>} {l.workMode === "field" && <Badge tone="blue" dot={false}>{t("Field")}</Badge>} {l.outsideZone && <Badge tone="gray" dot={false}>{t("Location not confirmed")}</Badge>}</> },
    { key: "out", header: t("Clock out"), cell: (l) => l.clockOut ? time(l.clockOut) : <Badge tone="blue">{t("Open")}</Badge> }, { key: "h", header: t("Hours"), cell: (l) => l.hours ?? "—" },
  ];
  return (
    <>
      <PageHeader title={t("My Attendance")} description={t("Clock in and out. The time is taken from the server, not from your device.")} />
      {unlinked ? <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account so you can clock in.")} /></Panel> : <>
        <Panel>
          <div className="row" style={{ gap: 16, alignItems: "center", justifyContent: "space-between", flexWrap: "wrap" }}>
            <div><b style={{ fontSize: 28 }}>{time(now.toISOString())}</b><small className="sub">{open ? t("Clocked in since {time}", { time: time(open.clockIn) }) : t("Not clocked in")}</small></div>
            {open ? <Button variant="danger" onClick={() => clock("clock-out")}><LogOut size={16} /> {t("Clock out")}</Button> : <div className="row" style={{ gap: 8, alignItems: "center", flexWrap: "wrap" }}><SelectInput aria-label={t("Where are you working today?")} value={mode} onChange={(e) => setMode(e.target.value)} options={[{ value: "office", label: t("At the office") }, { value: "remote", label: t("From home") }, { value: "field", label: t("Client / business trip") }]} /><Button variant="primary" onClick={() => clock("clock-in")}><LogIn size={16} /> {t("Clock in")}</Button></div>}
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
        </Panel>
        <div style={{ height: 16 }} />
        <Panel title={t("My requests")} actions={<div className="row" style={{ gap: 8 }}><Button size="sm" onClick={() => setDrawer("correction")}>{t("Request correction")}</Button><Button size="sm" onClick={() => setDrawer("overtime")}>{t("Request overtime")}</Button></div>}>
          <Tabs items={[{ key: "corrections", label: t("Corrections") }, { key: "overtime", label: t("Overtime") }]} value={reqKind} onChange={setReqKind} label={t("Request type")} />
          <div style={{ paddingTop: 12 }}>
            {!reqs.loading && (reqs.data?.data.length ?? 0) === 0 ? <EmptyState title={t("No requests yet")} description={t("Corrections and overtime you ask for will appear here.")} />
              : <TimeRequestTable rows={reqs.data?.data ?? []} showEmployee={false} loading={reqs.data === undefined && reqs.loading} error={reqs.error?.message ?? null} onRetry={reqs.reload} onChanged={() => { reqs.reload(); q.reload(); }} />}
          </div>
        </Panel>
        <RequestTimeDrawer kind={drawer ?? "correction"} open={drawer !== null} onClose={() => setDrawer(null)} onDone={() => { reqs.reload(); q.reload(); }} /></>}
    </>
  );
}
