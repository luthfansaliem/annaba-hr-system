"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, patch, post, put } from "@/lib/api/client";
import type { ShiftAssignmentRow, ShiftItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const DAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const dayNames = (mask: number) => DAYS.filter((_, i) => mask & (1 << i)).map((d) => t(d)).join(" ");
type Draft = { id?: string; code: string; name: string; startTime: string; endTime: string; graceMinutes: string; workDays: number };
const blank: Draft = { code: "", name: "", startTime: "09:00", endTime: "17:00", graceMinutes: "10", workDays: 31 };

/** ATT-001 — shifts decide who is late and which days are working days; people without one follow the standard week (09:00–17:00, Monday–Friday). */
export default function ShiftsPage() {
  const { can } = useAuth(); const run = useRun(); const manage = can("attendance.manage");
  const shifts = useQuery(() => get<ShiftItem[]>("/api/hr/shifts", { includeArchived: true }), []); const people = useQuery(() => get<ShiftAssignmentRow[]>("/api/hr/shifts/assignments"), []);
  const [d, setD] = useState<Draft | null>(null); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (x: Draft) => { setErr({}); setForm(""); setD(x); };
  const save = async (x: Draft) => {
    const body = { code: x.code, name: x.name, startTime: x.startTime, endTime: x.endTime, graceMinutes: Number(x.graceMinutes), workDays: x.workDays };
    const r = await run(() => (x.id ? patch(`/api/hr/shifts/${x.id}`, body) : post("/api/hr/shifts", body)), t("Shift saved."), { silent: true });
    if (r.ok) { setD(null); shifts.reload(); people.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  };
  const assign = async (p: ShiftAssignmentRow, shiftId: string) => { if ((await run(() => put(`/api/hr/shifts/assignments/${p.employeeId}`, { shiftId: shiftId || null }), t("Shift assigned."))).ok) { people.reload(); shifts.reload(); } };
  const scols: Column<ShiftItem>[] = [
    { key: "n", header: t("Shift"), cell: (s) => <><b>{s.name}</b> <small className="sub">{s.code}{s.archived ? ` · ${t("Archived")}` : ""}</small></> },
    { key: "t", header: t("Hours"), cell: (s) => `${s.startTime}–${s.endTime}${s.overnight ? ` (${t("next day")})` : ""}` }, { key: "g", header: t("Grace"), className: "hide-sm", cell: (s) => `${s.graceMinutes} ${t("min")}` },
    { key: "d", header: t("Working days"), className: "hide-sm", cell: (s) => dayNames(s.workDays) }, { key: "a", header: t("People"), cell: (s) => s.assigned },
    ...(manage ? [{ key: "x", header: "", cell: (s: ShiftItem) => <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
      <Button size="sm" onClick={() => open({ id: s.id, code: s.code, name: s.name, startTime: s.startTime, endTime: s.endTime, graceMinutes: String(s.graceMinutes), workDays: s.workDays })}>{t("Edit")}</Button>
      <Button size="sm" onClick={async () => { if ((await run(() => patch(`/api/hr/shifts/${s.id}`, { archived: !s.archived }), s.archived ? t("Shift restored.") : t("Shift archived."))).ok) shifts.reload(); }}>{s.archived ? t("Restore") : t("Archive")}</Button></div> } as Column<ShiftItem>] : []),
  ];
  const pcols: Column<ShiftAssignmentRow>[] = [
    { key: "e", header: t("Employee"), cell: (p) => <><b>{p.employeeName}</b><small className="sub">{p.employeeNo}</small></> },
    { key: "s", header: t("Shift"), cell: (p) => manage
      ? <SelectInput aria-label={`${t("Shift")} ${p.employeeName}`} value={p.shiftId ?? ""} onChange={(e) => assign(p, e.target.value)} options={[{ value: "", label: t("Standard") }, ...(shifts.data ?? []).filter((s) => !s.archived).map((s) => ({ value: s.id, label: s.name }))]} />
      : (p.shiftName ?? t("Standard")) },
    { key: "f", header: t("Since"), className: "hide-sm", cell: (p) => (p.since ? <Badge tone="gray">{fmtDate(p.since)}</Badge> : "—") },
  ];
  return (
    <Require perm="attendance.read_all">
      <PageHeader title={t("Shifts")} description={t("Working hours, lateness grace and working days.")} actions={manage && <Button variant="primary" onClick={() => open(blank)}><Plus size={16} /> {t("Add shift")}</Button>} />
      <div className="stack">
        <Panel title={t("Shifts")}><DataTable label={t("Shifts")} columns={scols} rows={shifts.data ?? []} rowKey={(s) => s.id} loading={shifts.data === undefined && shifts.loading} error={shifts.error?.message ?? null} onRetry={shifts.reload} /></Panel>
        <Panel title={t("Who works which shift")}><DataTable label={t("Assignments")} columns={pcols} rows={people.data ?? []} rowKey={(p) => p.employeeId} loading={people.data === undefined && people.loading} error={people.error?.message ?? null} onRetry={people.reload} /></Panel>
      </div>
      <Overlay open={!!d} onClose={() => setD(null)} side="right" title={d?.id ? t("Edit shift") : t("Add shift")} footer={<><Button onClick={() => setD(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="shift-form">{t("Save")}</Button></>}>
        {d && <form id="shift-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); save(d); }}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Code")} htmlFor="sh-code" required error={err.code}><TextInput id="sh-code" data-autofocus value={d.code} invalid={!!err.code} onChange={(e) => setD({ ...d, code: e.target.value })} /></Field>
            <Field label={t("Name")} htmlFor="sh-name" required error={err.name}><TextInput id="sh-name" value={d.name} invalid={!!err.name} onChange={(e) => setD({ ...d, name: e.target.value })} /></Field>
            <Field label={t("Starts")} htmlFor="sh-start" error={err.startTime}><TextInput id="sh-start" type="time" value={d.startTime} invalid={!!err.startTime} onChange={(e) => setD({ ...d, startTime: e.target.value })} /></Field>
            <Field label={t("Ends")} htmlFor="sh-end" error={err.endTime}><TextInput id="sh-end" type="time" value={d.endTime} invalid={!!err.endTime} onChange={(e) => setD({ ...d, endTime: e.target.value })} /></Field>
            <Field label={t("Grace before late (minutes)")} htmlFor="sh-grace" error={err.graceMinutes}><TextInput id="sh-grace" inputMode="numeric" value={d.graceMinutes} invalid={!!err.graceMinutes} onChange={(e) => setD({ ...d, graceMinutes: e.target.value })} /></Field>
          </div>
          <fieldset className="fl"><legend>{t("Working days")}</legend>{err.workDays && <p className="field-error" role="alert">{err.workDays}</p>}
            <div className="row" style={{ gap: 12, flexWrap: "wrap" }}>{DAYS.map((n, i) => <label key={n} className="row" style={{ gap: 6 }}><Checkbox checked={!!(d.workDays & (1 << i))} onChange={(e) => setD({ ...d, workDays: e.target.checked ? d.workDays | (1 << i) : d.workDays & ~(1 << i) })} /> {t(n)}</label>)}</div></fieldset>
        </form>}
      </Overlay>
    </Require>
  );
}
