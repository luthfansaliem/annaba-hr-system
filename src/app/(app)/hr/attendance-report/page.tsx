"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { SelectInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { get, post } from "@/lib/api/client";
import { useAuth } from "@/lib/auth";
import { useRun } from "@/lib/use-run";
import type { AttendancePeriod, AttendanceReportRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const monthKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;

export default function AttendanceReportPage() {
  const { can } = useAuth(); const run = useRun(); const [month, setMonth] = useState(monthKey(new Date()));
  const q = useQuery(() => get<AttendanceReportRow[]>("/api/hr/attendance/report", { month }), [month]);
  const periods = useQuery(() => get<AttendancePeriod[]>("/api/hr/attendance/periods"), []);
  const closed = (periods.data ?? []).some((p) => `${p.year}-${String(p.month).padStart(2, "0")}` === month);
  const [reopen, setReopen] = useState(false); const [reason, setReason] = useState(""); const [rerr, setRerr] = useState("");
  const close = async () => { if ((await run(() => post(`/api/hr/attendance/periods/${month}/close`), t("Month closed."))).ok) periods.reload(); };
  const months = Array.from({ length: 12 }, (_, i) => { const d = new Date(); d.setDate(1); d.setMonth(d.getMonth() - i); return monthKey(d); });
  const cols: Column<AttendanceReportRow>[] = [
    { key: "e", header: t("Employee"), cell: (r) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo}</small></> },
    { key: "p", header: t("Days present"), cell: (r) => r.daysPresent }, { key: "l", header: t("Late days"), cell: (r) => r.lateDays > 0 ? <Badge tone="orange">{r.lateDays}</Badge> : 0 },
    { key: "h", header: t("Hours worked"), cell: (r) => r.hours.toFixed(1) },
    { key: "o", header: t("Open sessions"), className: "hide-sm", cell: (r) => r.openSessions > 0 ? <Badge tone="blue">{r.openSessions}</Badge> : "—" },
  ];
  return (
    <Require perm="attendance.read_all">
      <PageHeader title={t("Attendance Report")} description={t("Monthly summary per employee. Opening this report is recorded in the audit log.")} />
      <Panel actions={<div className="row" style={{ gap: 8, alignItems: "center" }}>
        {closed && <Badge tone="green">{t("Closed for payroll")}</Badge>}
        {can("attendance.manage") && (closed ? <Button size="sm" onClick={() => { setRerr(""); setReason(""); setReopen(true); }}>{t("Reopen month")}</Button> : <Button size="sm" variant="primary" onClick={close}>{t("Close month")}</Button>)}
        <SelectInput aria-label={t("Month")} value={month} onChange={(e) => setMonth(e.target.value)} options={months} /></div>}>
        {!q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No employees")} description={t("There is nothing to report for this month.")} />
          : <DataTable label={t("Attendance report")} columns={cols} rows={q.data ?? []} rowKey={(r) => r.employeeId} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={reopen} onClose={() => setReopen(false)} title={t("Reopen month")} description={t("Requests and decisions for this month become possible again. The reason is recorded.")}
        footer={<><Button onClick={() => setReopen(false)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="reopen-form">{t("Reopen month")}</Button></>}>
        <form id="reopen-form" className="stack" noValidate onSubmit={async (e) => { e.preventDefault(); const r = await run(() => post(`/api/hr/attendance/periods/${month}/reopen`, { reason }), t("Month reopened."), { silent: true }); if (r.ok) { setReopen(false); periods.reload(); } else setRerr(r.fields?.reason ?? r.error); }}>
          <Field label={t("Reason")} htmlFor="ro-reason" required error={rerr}><TextInput id="ro-reason" data-autofocus value={reason} invalid={!!rerr} onChange={(e) => setReason(e.target.value)} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
