"use client";

import { useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get, post, put } from "@/lib/api/client";
import type { ProjectItem, TimesheetWeek } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { isoDay } from "@/components/hr/attendance-plus";
import { t } from "@/i18n";

type Line = { projectId: string; date: string; hours: string; note: string };
const monday = (d: Date) => { const x = new Date(d); x.setDate(x.getDate() - ((x.getDay() + 6) % 7)); return x; };
const STATUS: Record<TimesheetWeek["status"], ["gray" | "orange" | "green" | "red" | "blue", string]> = { empty: ["gray", "Not started"], draft: ["gray", "Draft"], submitted: ["orange", "Handed in"], approved: ["green", "Approved"], rejected: ["red", "Sent back"] };

/** Weekly timesheet: hours per project per day, saved as a draft and handed in to the manager; managers approve their team's weeks here too. */
export default function TimesheetsPage() {
  const { can } = useAuth(); const run = useRun(); const [start, setStart] = useState(() => monday(new Date()));
  const week = useQuery(() => get<TimesheetWeek>("/api/hr/timesheets/week", { weekStart: isoDay(start) }), [start.getTime()]);
  const projects = useQuery(() => get<ProjectItem[]>("/api/hr/projects"), []);
  const pending = useQuery(() => (can("attendance.approve") || can("attendance.manage") ? get<TimesheetWeek[]>("/api/hr/timesheets/pending") : Promise.resolve([] as TimesheetWeek[])), []);
  const [lines, setLines] = useState<Line[]>([]); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [back, setBack] = useState<TimesheetWeek | null>(null); const [note, setNote] = useState("");
  useEffect(() => { if (week.data) setLines(week.data.entries.map((e) => ({ projectId: e.projectId, date: e.date, hours: String(e.hours), note: e.note ?? "" }))); }, [week.data]);
  const days = Array.from({ length: 7 }, (_, i) => { const d = new Date(start); d.setDate(start.getDate() + i); return isoDay(d); });
  const move = (n: number) => { const x = new Date(start); x.setDate(x.getDate() + 7 * n); setStart(x); };
  async function save(submit: boolean) {
    setForm("");
    const r = await run(() => put(`/api/hr/timesheets/week?weekStart=${isoDay(start)}${submit ? "&submit=true" : ""}`, { lines: lines.map((l) => ({ projectId: l.projectId, date: l.date, hours: Number(l.hours), note: l.note || undefined })) }), submit ? t("Timesheet handed in.") : t("Draft saved."), { silent: true });
    if (r.ok) { setErr({}); week.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const decide = async (w: TimesheetWeek, approve: boolean, why?: string) => { if ((await run(() => post("/api/hr/timesheets/decide", { employeeId: w.employeeId, weekStart: w.weekStart, approve, note: why }), approve ? t("Approved.") : t("Sent back."))).ok) { setBack(null); setNote(""); pending.reload(); } };
  const w = week.data; const editable = w?.canEdit ?? false; const active = (projects.data ?? []).filter((p) => !p.archived);
  const unlinked = (week.error as { code?: string } | undefined)?.code === "invariant";
  return (
    <>
      <PageHeader title={t("Timesheets")} description={t("Book your hours per project each day, then hand the week in to your manager.")}
        actions={<div className="row" style={{ gap: 8, alignItems: "center" }}>
          <Button size="sm" aria-label={t("Previous week")} onClick={() => move(-1)}><ChevronLeft size={16} /></Button>
          <b>{t("Week of {date}", { date: fmtDate(isoDay(start)) })}</b>
          <Button size="sm" aria-label={t("Next week")} onClick={() => move(1)}><ChevronRight size={16} /></Button></div>} />
      <div className="stack">
        {(pending.data?.length ?? 0) > 0 && <Panel title={t("Waiting for your approval")}>
          <ul className="mini-list">{pending.data!.map((p) => <li key={`${p.employeeId}-${p.weekStart}`}><div><strong>{p.employeeName}</strong><small>{t("Week of {date}", { date: fmtDate(p.weekStart) })} · {t("{n} h", { n: String(p.total) })} · {p.entries.map((e) => e.project).filter((v, i, a) => a.indexOf(v) === i).join(", ")}</small></div>
            <div className="row" style={{ gap: 6 }}><Button size="sm" variant="primary" onClick={() => decide(p, true)}>{t("Approve")}</Button><Button size="sm" variant="danger-outline" onClick={() => { setNote(""); setBack(p); }}>{t("Send back")}</Button></div></li>)}</ul>
        </Panel>}
        <Panel title={t("My week")} actions={w && <Badge tone={STATUS[w.status][0]}>{t(STATUS[w.status][1])}</Badge>}>
          {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
            : active.length === 0 ? <EmptyState title={t("No projects yet")} description={t("HR adds the projects you can book time on.")} /> : (
            <div className="stack">
              {form && <div className="form-alert err" role="alert">{form}</div>}
              {w?.status === "rejected" && w.entries.find((e) => e.decisionNote) && <div className="form-alert" role="status">{w.entries.find((e) => e.decisionNote)!.decisionNote}</div>}
              {lines.length === 0 && <p className="sub">{editable ? t("Add a line for each project and day you worked on.") : t("Nothing booked this week.")}</p>}
              {lines.map((l, i) => (
                <div key={i} className="row" style={{ gap: 8, alignItems: "flex-end", flexWrap: "wrap" }}>
                  <Field label={t("Day")} htmlFor={`ts-d-${i}`} error={err[`lines.${i}`]}><SelectInput id={`ts-d-${i}`} value={l.date} disabled={!editable} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, date: e.target.value } : x)))} options={days.map((d) => ({ value: d, label: new Date(d).toLocaleDateString(undefined, { weekday: "short", day: "numeric", month: "short" }) }))} /></Field>
                  <Field label={t("Project")} htmlFor={`ts-p-${i}`}><SelectInput id={`ts-p-${i}`} value={l.projectId} disabled={!editable} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, projectId: e.target.value } : x)))} options={(projects.data ?? []).map((p) => ({ value: p.id, label: `${p.code} · ${p.name}` }))} /></Field>
                  <Field label={t("Hours")} htmlFor={`ts-h-${i}`}><TextInput id={`ts-h-${i}`} inputMode="decimal" style={{ width: 80 }} value={l.hours} disabled={!editable} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, hours: e.target.value } : x)))} /></Field>
                  <Field label={t("Note")} htmlFor={`ts-n-${i}`}><TextInput id={`ts-n-${i}`} value={l.note} disabled={!editable} onChange={(e) => setLines(lines.map((x, j) => (j === i ? { ...x, note: e.target.value } : x)))} /></Field>
                  {editable && <Button size="sm" variant="danger-outline" aria-label={t("Remove line")} onClick={() => setLines(lines.filter((_, j) => j !== i))}><Trash2 size={14} /></Button>}
                </div>))}
              {editable && <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
                <Button size="sm" onClick={() => setLines([...lines, { projectId: active[0].id, date: days[Math.min(lines.length, 4)], hours: "8", note: "" }])}><Plus size={14} /> {t("Add line")}</Button>
                <span style={{ flex: 1 }} />
                <b>{t("Total {n} h", { n: String(lines.reduce((s, l) => s + (Number(l.hours) || 0), 0)) })}</b>
                <Button onClick={() => save(false)}>{t("Save draft")}</Button>
                <Button variant="primary" onClick={() => save(true)}>{t("Hand in")}</Button></div>}
            </div>)}
        </Panel>
      </div>
      <Overlay open={!!back} onClose={() => setBack(null)} title={t("Send back")} footer={<><Button onClick={() => setBack(null)}>{t("Cancel")}</Button><Button variant="primary" onClick={() => back && decide(back, false, note)}>{t("Send back")}</Button></>}>
        <Field label={t("What should change?")} htmlFor="ts-back" required><TextInput id="ts-back" data-autofocus value={note} onChange={(e) => setNote(e.target.value)} /></Field>
      </Overlay>
    </>
  );
}
