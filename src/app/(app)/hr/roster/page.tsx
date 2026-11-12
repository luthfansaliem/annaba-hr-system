"use client";

import { useState } from "react";
import { ChevronLeft, ChevronRight, Repeat } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { OrgUnitItem, RosterRow, ShiftItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { isoDay } from "@/components/hr/attendance-plus";
import { t } from "@/i18n";

const monday = (d: Date) => { const x = new Date(d); x.setDate(x.getDate() - ((x.getDay() + 6) % 7)); return x; };

/** Shift planner: two weeks per screen, one cell per person per day. A planned day overrides the person's standing shift; "standing" removes the plan. */
export default function RosterPage() {
  const { can } = useAuth(); const run = useRun(); const edit = can("attendance.manage");
  const [start, setStart] = useState(() => monday(new Date())); const [unit, setUnit] = useState("");
  const end = new Date(start); end.setDate(start.getDate() + 13);
  const q = useQuery(() => get<RosterRow[]>("/api/hr/attendance/roster", { from: isoDay(start), to: isoDay(end), orgUnitId: unit || undefined }), [start.getTime(), unit]);
  const shifts = useQuery(() => get<ShiftItem[]>("/api/hr/shifts"), []); const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]), []);
  const [pat, setPat] = useState(false); const [pf, setPf] = useState({ from: isoDay(start), to: isoDay(end), pattern: [""] as string[] }); const [err, setErr] = useState("");
  const active = (shifts.data ?? []).filter((s) => !s.archived);
  const move = (weeks: number) => { const x = new Date(start); x.setDate(x.getDate() + 7 * weeks); setStart(x); };
  async function setCell(employeeId: string, date: string, value: string) {
    const item = value === "standing" ? { employeeId, date, clear: true } : { employeeId, date, shiftId: value === "off" ? null : value };
    if ((await run(() => put("/api/hr/attendance/roster", { items: [item] }), undefined, { silent: false })).ok) q.reload();
  }
  async function applyPattern(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    const r = await run(() => post("/api/hr/attendance/roster/pattern", { employeeIds: (q.data ?? []).map((x) => x.employeeId), from: pf.from, to: pf.to, pattern: pf.pattern.map((p) => (p === "" ? null : p)) }), t("Pattern applied."), { silent: true });
    if (r.ok) { setPat(false); q.reload(); } else setErr(r.error);
  }
  const days = q.data?.[0]?.days.map((d) => d.date) ?? [];
  return (
    <Require perm="attendance.read_all">
      <PageHeader title={t("Shift Planner")} description={t("Plan who works which shift on which day. A planned day overrides the standing shift; leave, lateness and overtime follow the plan.")}
        actions={<div className="row" style={{ gap: 8, alignItems: "center", flexWrap: "wrap" }}>
          <Button size="sm" aria-label={t("Previous weeks")} onClick={() => move(-2)}><ChevronLeft size={16} /></Button>
          <b>{start.toLocaleDateString(undefined, { day: "numeric", month: "short" })} – {end.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })}</b>
          <Button size="sm" aria-label={t("Next weeks")} onClick={() => move(2)}><ChevronRight size={16} /></Button>
          {(units.data?.length ?? 0) > 0 && <SelectInput aria-label={t("Department")} value={unit} onChange={(e) => setUnit(e.target.value)} options={[{ value: "", label: t("All departments") }, ...units.data!.map((u) => ({ value: u.id, label: u.name }))]} />}
          {edit && <Button onClick={() => { setErr(""); setPf({ from: isoDay(start), to: isoDay(end), pattern: active.length ? [active[0].id, active[0].id, ""] : [""] }); setPat(true); }}><Repeat size={16} /> {t("Fill with a pattern")}</Button>}
        </div>} />
      <Panel>
        {!q.loading && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No one to plan")} description={t("Active employees appear here.")} /> : (
          <div style={{ overflowX: "auto" }}>
            <table className="roster-table" aria-label={t("Shift plan")}>
              <thead><tr><th>{t("Employee")}</th>{days.map((d) => <th key={d}>{new Date(d).toLocaleDateString(undefined, { weekday: "short", day: "numeric" })}</th>)}</tr></thead>
              <tbody>{(q.data ?? []).map((r) => (
                <tr key={r.employeeId}><td><b>{r.employeeName}</b><br /><small className="sub">{r.employeeNo}</small></td>
                  {r.days.map((c) => (
                    <td key={c.date} className={`${c.works ? "" : "off"}${c.planned ? " planned" : ""}`}>
                      {edit ? <select aria-label={t("{name} on {date}", { name: r.employeeName, date: c.date })} value={c.planned ? (c.works ? c.shiftId ?? "" : "off") : "standing"} onChange={(e) => setCell(r.employeeId, c.date, e.target.value)}>
                        <option value="standing">{c.planned ? t("Standing shift") : (c.works ? c.shiftCode ?? t("Work") : t("Day off"))}</option>
                        {active.map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}
                        <option value="off">{t("Day off")}</option>
                      </select> : (c.works ? c.shiftCode ?? t("Work") : t("Day off"))}
                    </td>))}
                </tr>))}</tbody>
            </table>
          </div>)}
      </Panel>
      <Overlay open={pat} onClose={() => setPat(false)} side="right" title={t("Fill with a pattern")} description={t("Repeats the pattern for everyone shown, starting on the first day. Existing plans in the period are replaced.")}
        footer={<><Button onClick={() => setPat(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="pat-form">{t("Apply")}</Button></>}>
        <form id="pat-form" className="stack" noValidate onSubmit={applyPattern}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <div className="fgrid">
            <Field label={t("From")} htmlFor="pt-from"><TextInput id="pt-from" type="date" value={pf.from} onChange={(e) => setPf({ ...pf, from: e.target.value })} /></Field>
            <Field label={t("Until")} htmlFor="pt-to"><TextInput id="pt-to" type="date" value={pf.to} onChange={(e) => setPf({ ...pf, to: e.target.value })} /></Field>
          </div>
          {pf.pattern.map((p, i) => (
            <Field key={i} label={t("Day {n} of the pattern", { n: String(i + 1) })} htmlFor={`pt-${i}`}>
              <div className="row" style={{ gap: 8 }}><SelectInput id={`pt-${i}`} value={p} onChange={(e) => setPf({ ...pf, pattern: pf.pattern.map((x, j) => (j === i ? e.target.value : x)) })} options={[...active.map((s) => ({ value: s.id, label: `${s.code} · ${s.name}` })), { value: "", label: t("Day off") }]} />
                {pf.pattern.length > 1 && <Button size="sm" variant="danger-outline" onClick={() => setPf({ ...pf, pattern: pf.pattern.filter((_, j) => j !== i) })}>{t("Remove")}</Button>}</div>
            </Field>))}
          {pf.pattern.length < 28 && <div><Button size="sm" onClick={() => setPf({ ...pf, pattern: [...pf.pattern, ""] })}>{t("Add a day")}</Button></div>}
        </form>
      </Overlay>
    </Require>
  );
}
