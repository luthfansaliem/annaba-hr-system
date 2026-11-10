"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { SelectInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { AttendanceReportRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const monthKey = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;

export default function AttendanceReportPage() {
  const [month, setMonth] = useState(monthKey(new Date()));
  const q = useQuery(() => get<AttendanceReportRow[]>("/api/hr/attendance/report", { month }), [month]);
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
      <Panel actions={<SelectInput aria-label={t("Month")} value={month} onChange={(e) => setMonth(e.target.value)} options={months} />}>
        {!q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No employees")} description={t("There is nothing to report for this month.")} />
          : <DataTable label={t("Attendance report")} columns={cols} rows={q.data ?? []} rowKey={(r) => r.employeeId} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
    </Require>
  );
}
