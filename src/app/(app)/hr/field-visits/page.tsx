"use client";

import { useState } from "react";
import { ExternalLink } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { FieldVisitItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { isoDay } from "@/components/hr/attendance-plus";
import { t } from "@/i18n";

/** Field visits of the team (managers) or everyone (HR) on one day, with a link to each position on a map. Every look is recorded in the audit log. */
export default function FieldVisitsPage() {
  const [date, setDate] = useState(isoDay(new Date()));
  const q = useQuery(() => get<FieldVisitItem[]>("/api/hr/attendance/visits", { date, scope: "all" }), [date]);
  const cols: Column<FieldVisitItem>[] = [
    { key: "t", header: t("Time"), cell: (v) => new Date(v.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false }) },
    { key: "e", header: t("Employee"), cell: (v) => <b>{v.employeeName}</b> },
    { key: "p", header: t("Visit"), cell: (v) => <><Badge tone={v.kind === "arrive" ? "green" : "gray"} dot={false}>{v.kind === "arrive" ? t("Arrived") : t("Left")}</Badge> {v.place}{v.note && <small className="sub">{v.note}</small>}</> },
    { key: "m", header: t("Position"), cell: (v) => <a className="link-btn" href={`https://www.google.com/maps?q=${v.latitude},${v.longitude}`} target="_blank" rel="noreferrer noopener">{v.latitude.toFixed(5)}, {v.longitude.toFixed(5)} <ExternalLink size={12} /></a> },
    { key: "a", header: t("Accuracy"), className: "hide-sm", cell: (v) => (v.accuracy != null ? `± ${Math.round(v.accuracy)} m` : "—") },
  ];
  const rows = q.data ?? [];
  return (
    <>
      <PageHeader title={t("Field Visits")} description={t("Where field staff checked in during the day. Positions are recorded only when someone checks in, never in the background.")}
        actions={<TextInput aria-label={t("Day")} type="date" value={date} onChange={(e) => setDate(e.target.value)} />} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No visits on this day")} description={t("Visits appear when field staff check in from My Attendance.")} />
          : <DataTable label={t("Field visits")} columns={cols} rows={rows} rowKey={(v) => v.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
    </>
  );
}
