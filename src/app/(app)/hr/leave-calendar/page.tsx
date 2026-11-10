"use client";

import { useMemo, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { SelectInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { LeaveRequestItem, Paged } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { monthName } from "@/lib/format";
import { t } from "@/i18n";

const key = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
const iso = (y: number, m: number, d: number) => `${y}-${String(m).padStart(2, "0")}-${String(d).padStart(2, "0")}`;

/** LVE-007 — who is away on which day. Managers see their reports; people who may read all leave see everyone. Pending requests are shown lighter than approved ones. */
export default function LeaveCalendarPage() {
  const { can } = useAuth(); const [month, setMonth] = useState(key(new Date()));
  const [y, m] = month.split("-").map(Number); const days = new Date(y, m, 0).getDate(); const scope = can("leave.read_all") ? "all" : "team";
  const q = useQuery(() => get<Paged<LeaveRequestItem>>("/api/hr/leave/requests", { scope, status: "all", from: iso(y, m, 1), to: iso(y, m, days), pageSize: 500 }), [month, scope]);
  const people = useMemo(() => {
    const live = (q.data?.data ?? []).filter((r) => r.status === "approved" || r.status === "pending"); const by = new Map<string, { name: string; no: string; rows: LeaveRequestItem[] }>();
    for (const r of live) { const e = by.get(r.employeeId) ?? { name: r.employeeName, no: r.employeeNo, rows: [] }; e.rows.push(r); by.set(r.employeeId, e); }
    return [...by.values()].sort((a, b) => a.name.localeCompare(b.name));
  }, [q.data]);
  const months = Array.from({ length: 7 }, (_, i) => { const d = new Date(); d.setDate(1); d.setMonth(d.getMonth() - 1 + i); return key(d); });
  const cell = (rows: LeaveRequestItem[], day: number) => { const d = iso(y, m, day); return rows.find((r) => r.startDate <= d && r.endDate >= d); };
  return (
    <Require perm="leave.approve">
      <PageHeader title={t("Leave Calendar")} description={scope === "all" ? t("Who is away, across the organisation.") : t("Who is away in your team.")} />
      <Panel title={monthName(y, m)} actions={<SelectInput aria-label={t("Month")} value={month} onChange={(e) => setMonth(e.target.value)} options={months} />}>
        {!q.loading && !q.error && people.length === 0 ? <EmptyState title={t("No one is away")} description={t("No approved or pending leave in this month.")} /> : (
          <div style={{ overflowX: "auto" }}>
            <table className="tbl" aria-label={t("Leave calendar")}>
              <thead><tr><th style={{ minWidth: 160 }}>{t("Employee")}</th>{Array.from({ length: days }, (_, i) => { const dow = new Date(y, m - 1, i + 1).getDay(); return <th key={i} style={{ textAlign: "center", padding: "6px 3px", fontWeight: dow === 0 || dow === 6 ? 400 : 700, textDecoration: dow === 0 || dow === 6 ? "underline dotted" : undefined }} title={dow === 0 || dow === 6 ? t("Weekend") : undefined}>{i + 1}</th>; })}</tr></thead>
              <tbody>{people.map((p) => (
                <tr key={p.no}><td><b>{p.name}</b><small className="sub">{p.no}</small></td>
                  {Array.from({ length: days }, (_, i) => { const r = cell(p.rows, i + 1); return <td key={i} title={r ? `${r.leaveType} (${r.status})` : undefined} style={{ textAlign: "center", padding: 2 }}>{r && <span role="img" aria-label={`${r.leaveType} ${r.status}`} style={{ display: "block", height: 18, borderRadius: 4, background: r.status === "approved" ? "var(--blue, #0b62e6)" : "var(--orange, #e69b0b)", opacity: r.status === "approved" ? 1 : 0.55 }} />}</td>; })}</tr>))}</tbody>
            </table>
            <p className="sub" style={{ marginTop: 8 }}>{t("Solid = approved, light = waiting for approval.")}</p>
          </div>)}
      </Panel>
    </Require>
  );
}
