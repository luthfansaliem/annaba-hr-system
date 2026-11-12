"use client";

import { useState } from "react";
import Link from "next/link";
import { AlertTriangle, CheckCircle2, Crown, Flag, Gauge, Plus, Target, Trash2, TrendingUp, UserPlus, Users } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Tabs } from "@/components/ui/tabs";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { ProgressBar } from "@/components/charts/lists";
import { QueryBoundary } from "@/components/query-boundary";
import { del, get, post, put } from "@/lib/api/client";
import type { Competency, DevelopmentPlan, DirectoryEntry, Goal, GoalCheckIn, IdpAction, MyReviewResult, OrgUnitItem, Paged, ReviewCycle, ReviewForm, ReviewResultRow, ReviewTask, SuccessionPlan, TalentRow, TeamDashboard } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate, fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const num = (n: number | null | undefined, d = 2) => (n === null || n === undefined ? "—" : Number(n).toLocaleString(undefined, { maximumFractionDigits: d }));
const thisYear = new Date().getFullYear();
const fieldsOf = (r: { fields?: Record<string, string> }) => r.fields ?? {};

export const GOAL_KIND: Record<Goal["kind"], string> = { kpi: "KPI", objective: "Objective", key_result: "Key result" };
export const GOAL_STATUS: Record<Goal["status"], [BadgeTone, string]> = { on_track: ["green", "On track"], at_risk: ["orange", "At risk"], off_track: ["red", "Off track"], done: ["blue", "Done"], cancelled: ["gray", "Cancelled"] };
export const CYCLE_STATUS: Record<ReviewCycle["status"], [BadgeTone, string]> = { draft: ["gray", "Draft"], open: ["blue", "Open"], calibration: ["orange", "Calibration"], closed: ["green", "Closed"] };
export const REVIEW_KIND: Record<ReviewTask["kind"], string> = { self: "Self review", manager: "Manager review", peer: "Peer review" };
export const READINESS: Record<string, [BadgeTone, string]> = { ready_now: ["green", "Ready now"], one_to_two_years: ["blue", "1–2 years"], three_plus_years: ["gray", "3+ years"] };
const CATEGORY: Record<string, string> = { core: "Core", leadership: "Leadership", technical: "Technical" };
/** The nine boxes, top row (high potential) first, left to right by performance. */
export const BOXES: { key: string; label: string; tone: BadgeTone; perf: number; pot: number }[] = [
  { key: "enigma", label: "Enigma", tone: "orange", perf: 1, pot: 3 }, { key: "high_potential", label: "High potential", tone: "blue", perf: 2, pot: 3 }, { key: "star", label: "Star", tone: "green", perf: 3, pot: 3 },
  { key: "dilemma", label: "Dilemma", tone: "orange", perf: 1, pot: 2 }, { key: "core_player", label: "Core player", tone: "blue", perf: 2, pot: 2 }, { key: "high_performer", label: "High performer", tone: "green", perf: 3, pot: 2 },
  { key: "underperformer", label: "Underperformer", tone: "red", perf: 1, pot: 1 }, { key: "effective", label: "Effective", tone: "gray", perf: 2, pot: 1 }, { key: "workhorse", label: "Workhorse", tone: "blue", perf: 3, pot: 1 },
];
const boxLabel = (k: string | null) => (k ? t(BOXES.find((b) => b.key === k)?.label ?? k) : "—");
const RATING = ["", "Far below expectations", "Below expectations", "Meets expectations", "Above expectations", "Outstanding"];

// ============================================================ goals
type GoalScope = "mine" | "team" | "company" | "all";
const emptyGoal = { kind: "kpi", title: "", description: "", measure: "", startValue: "0", targetValue: "100", weight: "100", dueDate: "", parentId: "", ownerEmployeeId: "", company: false, orgUnitId: "" };

/** KPI and OKR: my goals, my team's, the company's; set, align to a parent goal and update progress. */
export function GoalsBoard() {
  const { can } = useAuth(); const run = useRun();
  const [scope, setScope] = useState<GoalScope>("mine"); const [year, setYear] = useState(thisYear);
  const q = useQuery(() => get<Goal[]>("/api/hr/goals", { scope, year }), [scope, year]);
  const company = useQuery(() => get<Goal[]>("/api/hr/goals", { scope: "company", year }), [year]);
  const team = useQuery(() => (can("leave.approve") ? get<TeamDashboard>("/api/hr/team", undefined, { noRoute: true }).catch(() => null) : Promise.resolve(null)), []);
  const units = useQuery(() => (can("hrconfig.manage") ? get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]) : Promise.resolve([] as OrgUnitItem[])), []);
  const [edit, setEdit] = useState<Goal | "new" | null>(null); const [f, setF] = useState(emptyGoal); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [check, setCheck] = useState<Goal | null>(null);
  const open = (g: Goal | "new") => {
    setErr({}); setForm(""); setEdit(g);
    setF(g === "new" ? { ...emptyGoal, company: scope === "company" } : { kind: g.kind, title: g.title, description: g.description ?? "", measure: g.measure ?? "", startValue: String(g.startValue), targetValue: String(g.targetValue), weight: String(g.weight), dueDate: g.dueDate ?? "", parentId: g.parentId ?? "", ownerEmployeeId: g.ownerEmployeeId ?? "", company: g.ownerEmployeeId === null, orgUnitId: g.orgUnitId ?? "" });
  };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { kind: f.kind, title: f.title, description: f.description, measure: f.measure, startValue: Number(f.startValue || 0), targetValue: Number(f.targetValue || 0), weight: Number(f.weight || 0), year, dueDate: f.dueDate || null,
      parentId: f.parentId || null, ownerEmployeeId: f.company ? null : f.ownerEmployeeId || null, company: f.company, orgUnitId: f.company ? f.orgUnitId || null : null };
    const r = await run(() => (edit === "new" ? post("/api/hr/goals", body) : put(`/api/hr/goals/${(edit as Goal).id}`, body)), t("Goal saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); company.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const rows = q.data ?? [];
  const avg = rows.length ? rows.reduce((s, g) => s + g.progress, 0) / rows.length : 0;
  const cols: Column<Goal>[] = [
    { key: "g", header: t("Goal"), cell: (g) => <><b>{g.title}</b><small className="sub">{t(GOAL_KIND[g.kind])}{g.parent ? ` · ${t("supports {goal}", { goal: g.parent })}` : ""}{g.children ? ` · ${t("{n} aligned", { n: g.children })}` : ""}</small></> },
    ...(scope !== "mine" ? [{ key: "o", header: t("Owner"), className: "hide-sm", cell: (g: Goal) => g.owner ?? g.unit ?? t("Company") }] : []),
    { key: "p", header: t("Progress"), cell: (g) => <div style={{ minWidth: 140 }}><ProgressBar value={g.progress} color={g.status === "off_track" ? "#ef4444" : g.status === "at_risk" ? "#f59e0b" : "#22c55e"} /><small className="sub">{num(g.currentValue)} / {num(g.targetValue)} {g.measure ?? ""} · {num(g.progress, 0)}%</small></div> },
    { key: "w", header: t("Weight"), className: "hide-sm", cell: (g) => `${num(g.weight, 0)}%` },
    { key: "s", header: t("Status"), cell: (g) => <Badge tone={GOAL_STATUS[g.status][0]}>{t(GOAL_STATUS[g.status][1])}</Badge> },
    { key: "d", header: t("Due"), className: "hide-sm", cell: (g) => fmtDate(g.dueDate) },
    { key: "x", header: "", cell: (g) => <div className="row" style={{ gap: 6 }}>{g.canCheckIn && <Button size="sm" onClick={() => setCheck(g)}>{t("Update progress")}</Button>}{g.canEdit && <Button size="sm" variant="soft" onClick={() => open(g)}>{t("Edit")}</Button>}</div> },
  ];
  const scopes = [{ key: "mine" as const, label: t("My goals") }, ...(team.data ? [{ key: "team" as const, label: t("My team") }] : []), { key: "company" as const, label: t("Company goals") }, ...(can("hrconfig.manage") ? [{ key: "all" as const, label: t("All goals") }] : [])];
  const parents = [...(company.data ?? []), ...(scope === "mine" ? rows : [])].filter((g) => edit === "new" || g.id !== (edit as Goal | null)?.id);
  return (
    <>
      <div className="row" style={{ justifyContent: "space-between", flexWrap: "wrap", gap: 8, marginBottom: 12 }}>
        <Tabs variant="segmented" items={scopes} value={scope} onChange={setScope} label={t("Whose goals")} />
        <div className="row" style={{ gap: 8 }}>
          <SelectInput aria-label={t("Year")} value={String(year)} onChange={(e) => setYear(Number(e.target.value))} options={[thisYear + 1, thisYear, thisYear - 1, thisYear - 2].map(String)} />
          {(scope !== "company" || can("hrconfig.manage")) && <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add goal")}</Button>}
        </div>
      </div>
      <KpiRow label={t("Goal figures")}>
        <Kpi icon={<Target size={18} />} label={t("Goals")} value={rows.length} />
        <Kpi icon={<Gauge size={18} />} tone="green" label={t("Average progress")} value={`${num(avg, 0)}%`} />
        <Kpi icon={<AlertTriangle size={18} />} tone="orange" label={t("At risk or off track")} value={rows.filter((g) => g.status === "at_risk" || g.status === "off_track").length} />
        <Kpi icon={<CheckCircle2 size={18} />} tone="blue" label={t("Done")} value={rows.filter((g) => g.status === "done").length} />
      </KpiRow>
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No goals yet")} description={t("Set measurable goals (KPI) or objectives with key results (OKR), and align them to the company's goals.")} />
          : <DataTable label={t("Goals")} columns={cols} rows={rows} rowKey={(g) => g.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add goal") : t("Edit goal")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="goal-form">{t("Save")}</Button></>}>
        <form id="goal-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {edit === "new" && can("hrconfig.manage") && <label className="check"><Checkbox checked={f.company} onChange={(e) => setF({ ...f, company: e.target.checked })} /> {t("Company or department goal")}</label>}
          {edit === "new" && f.company && <Field label={t("Department")} htmlFor="g-unit" hint={t("Leave empty for a company-wide goal.")}><SelectInput id="g-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={[{ value: "", label: t("Whole company") }, ...(units.data ?? []).filter((u) => !u.archived).map((u) => ({ value: u.id, label: u.name }))]} /></Field>}
          {edit === "new" && !f.company && team.data && team.data.members.length > 0 && <Field label={t("For")} htmlFor="g-owner" error={err.ownerEmployeeId}><SelectInput id="g-owner" value={f.ownerEmployeeId} onChange={(e) => setF({ ...f, ownerEmployeeId: e.target.value })} options={[{ value: "", label: t("Myself") }, ...team.data.members.map((m) => ({ value: m.employeeId, label: m.fullName }))]} /></Field>}
          <Field label={t("Type")} htmlFor="g-kind" error={err.kind}><SelectInput id="g-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })} options={(["kpi", "objective", "key_result"] as const).map((k) => ({ value: k, label: t(GOAL_KIND[k]) }))} /></Field>
          <Field label={t("Goal")} htmlFor="g-title" required error={err.title}><TextInput id="g-title" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder={t("e.g. Close 10 new clients")} /></Field>
          <Field label={t("Description")} htmlFor="g-desc"><textarea id="g-desc" rows={2} maxLength={1000} className="textarea" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></Field>
          {f.kind !== "objective" && <div className="fgrid">
            <Field label={t("Start value")} htmlFor="g-start"><TextInput id="g-start" inputMode="decimal" value={f.startValue} onChange={(e) => setF({ ...f, startValue: e.target.value })} /></Field>
            <Field label={t("Target")} htmlFor="g-target" error={err.targetValue}><TextInput id="g-target" inputMode="decimal" value={f.targetValue} onChange={(e) => setF({ ...f, targetValue: e.target.value })} /></Field>
            <Field label={t("Unit of measure")} htmlFor="g-unitm"><TextInput id="g-unitm" value={f.measure} maxLength={20} onChange={(e) => setF({ ...f, measure: e.target.value })} placeholder={t("e.g. clients, %, IDR m")} /></Field>
            <Field label={t("Weight (%)")} htmlFor="g-weight" error={err.weight} hint={t("How much it counts in the review.")}><TextInput id="g-weight" inputMode="decimal" value={f.weight} onChange={(e) => setF({ ...f, weight: e.target.value })} /></Field>
          </div>}
          <Field label={t("Due date")} htmlFor="g-due"><TextInput id="g-due" type="date" value={f.dueDate} onChange={(e) => setF({ ...f, dueDate: e.target.value })} /></Field>
          <Field label={t("Supports")} htmlFor="g-parent" error={err.parentId} hint={t("Align this goal to a company, department or own objective.")}><SelectInput id="g-parent" value={f.parentId} onChange={(e) => setF({ ...f, parentId: e.target.value })} options={[{ value: "", label: t("Nothing") }, ...parents.map((g) => ({ value: g.id, label: `${g.title}${g.owner ? ` (${g.owner})` : ""}` }))]} /></Field>
        </form>
      </Overlay>
      {check && <CheckInOverlay goal={check} onClose={() => setCheck(null)} onSaved={() => { setCheck(null); q.reload(); }} />}
    </>
  );
}

function CheckInOverlay({ goal, onClose, onSaved }: { goal: Goal; onClose: () => void; onSaved: () => void }) {
  const run = useRun(); const history = useQuery(() => get<GoalCheckIn[]>(`/api/hr/goals/${goal.id}/check-ins`), [goal.id]);
  const [value, setValue] = useState(String(goal.currentValue)); const [status, setStatus] = useState<string>(goal.status); const [note, setNote] = useState(""); const [form, setForm] = useState("");
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/goals/${goal.id}/check-in`, { value: goal.kind === "objective" ? undefined : Number(value), status, note: note || undefined }), t("Progress updated."), { silent: true });
    if (r.ok) onSaved(); else setForm(r.error);
  }
  return (
    <Overlay open onClose={onClose} side="right" title={t("Update progress")} footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="ci-form">{t("Save")}</Button></>}>
      <form id="ci-form" className="stack" noValidate onSubmit={save}>
        {form && <div className="form-alert err" role="alert">{form}</div>}
        <p><b>{goal.title}</b><br /><small className="sub">{t("From {start} to {target}", { start: num(goal.startValue), target: `${num(goal.targetValue)} ${goal.measure ?? ""}` })}</small></p>
        {goal.kind !== "objective" && <Field label={t("Current value")} htmlFor="ci-val"><TextInput id="ci-val" inputMode="decimal" value={value} onChange={(e) => setValue(e.target.value)} /></Field>}
        <Field label={t("Status")} htmlFor="ci-st"><SelectInput id="ci-st" value={status} onChange={(e) => setStatus(e.target.value)} options={(Object.keys(GOAL_STATUS) as Goal["status"][]).map((k) => ({ value: k, label: t(GOAL_STATUS[k][1]) }))} /></Field>
        <Field label={t("What happened")} htmlFor="ci-note"><textarea id="ci-note" rows={3} maxLength={500} className="textarea" value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        <h4>{t("Earlier updates")}</h4>
        {(history.data ?? []).length === 0 ? <small className="sub">{t("No updates yet.")}</small>
          : <ul className="mini-list">{history.data!.map((c) => <li key={c.id}><b>{num(c.value)}</b> · {fmtDateTime(c.at)} · {c.by}{c.note && <><br /><small className="sub">{c.note}</small></>}</li>)}</ul>}
      </form>
    </Overlay>
  );
}

// ============================================================ my reviews
/** Reviews I must write, and my own released results. */
export function MyReviews() {
  const tasks = useQuery(() => get<ReviewTask[]>("/api/hr/reviews/mine"), []); const results = useQuery(() => get<MyReviewResult[]>("/api/hr/reviews/results"), []);
  const cols: Column<ReviewTask>[] = [
    { key: "c", header: t("Review cycle"), cell: (r) => <><b>{r.cycle}</b><small className="sub">{t(REVIEW_KIND[r.kind])}</small></> },
    { key: "e", header: t("Person"), cell: (r) => (r.kind === "self" ? t("Myself") : r.employeeName) },
    { key: "d", header: t("Due"), className: "hide-sm", cell: (r) => fmtDate(r.dueDate) },
    { key: "s", header: t("Status"), cell: (r) => (r.submitted ? <Badge tone="green">{t("Submitted")}</Badge> : r.cycleStatus === "open" ? <Badge tone="orange">{t("To do")}</Badge> : <Badge tone="gray">{t("Closed")}</Badge>) },
    { key: "x", header: "", cell: (r) => <Link className="btn sm" href={`/hr/reviews/${r.id}`}>{r.submitted || r.cycleStatus !== "open" ? t("View") : t("Write review")}</Link> },
  ];
  return (
    <>
      <Panel title={t("Reviews to write")}>
        {!tasks.loading && (tasks.data ?? []).length === 0 ? <EmptyState title={t("No reviews to write")} description={t("When HR opens a review cycle, your self review and any reviews of colleagues appear here.")} />
          : <DataTable label={t("Reviews to write")} columns={cols} rows={tasks.data ?? []} rowKey={(r) => r.id} loading={tasks.data === undefined && tasks.loading} error={tasks.error?.message ?? null} onRetry={tasks.reload} />}
      </Panel>
      <h3 style={{ margin: "20px 0 10px" }}>{t("My results")}</h3>
      {(results.data ?? []).length === 0 ? <Panel><EmptyState title={t("No results yet")} description={t("Your result appears here once HR closes the review cycle.")} /></Panel>
        : results.data!.map((r) => (
          <Panel key={r.cycleId} title={r.cycle} description={r.rating ? `${t("Final rating")}: ${r.rating} — ${t(RATING[r.rating])}` : undefined}>
            <KpiRow label={t("Scores")}>
              <Kpi icon={<Target size={18} />} label={t("Goals")} value={num(r.goalScore)} hint={t("of 5")} />
              <Kpi icon={<TrendingUp size={18} />} tone="green" label={t("Competencies")} value={num(r.competencyScore)} hint={t("of 5")} />
              <Kpi icon={<Gauge size={18} />} tone="blue" label={t("Overall score")} value={num(r.finalScore)} hint={t("of 5")} />
            </KpiRow>
            <FeedbackList title={t("Strengths")} items={r.strengths} /><FeedbackList title={t("To improve")} items={r.improvements} /><FeedbackList title={t("Comments")} items={r.comments} />
            <small className="sub">{t("Feedback from colleagues is shown without their names.")}</small>
          </Panel>))}
    </>
  );
}
const FeedbackList = ({ title, items }: { title: string; items: string[] }) => (items.length === 0 ? null : <div style={{ marginTop: 12 }}><h4>{title}</h4><ul className="mini-list">{items.map((s, i) => <li key={i}>{s}</li>)}</ul></div>);

function Stars({ value, onChange, label, disabled }: { value: number | undefined; onChange: (v: number) => void; label: string; disabled?: boolean }) {
  return (
    <div className="seg rate" role="radiogroup" aria-label={label}>
      {[1, 2, 3, 4, 5].map((n) => <button key={n} type="button" role="radio" aria-checked={value === n} className={value === n ? "on" : ""} disabled={disabled} title={t(RATING[n])} onClick={() => onChange(n)}>{n}</button>)}
    </div>
  );
}

/** Writing one review: rate each goal and competency 1–5 and say what goes well and what could be better. */
export function ReviewFormView({ id }: { id: string }) {
  const run = useRun(); const q = useQuery(() => get<ReviewForm>(`/api/hr/reviews/${id}`), [id]);
  const [a, setA] = useState<{ goals: Record<string, number>; competencies: Record<string, number>; strengths: string; improvements: string; comment: string } | null>(null); const [form, setForm] = useState("");
  return (
    <QueryBoundary q={q}>{(d) => {
      const s = a ?? { goals: d.answers.goals ?? {}, competencies: d.answers.competencies ?? {}, strengths: d.answers.strengths ?? "", improvements: d.answers.improvements ?? "", comment: d.answers.comment ?? "" };
      const locked = d.task.submitted || d.task.cycleStatus !== "open";
      const send = async (submit: boolean) => { setForm(""); const r = await run(() => put<ReviewForm>(`/api/hr/reviews/${id}`, { ...s, submit }), submit ? t("Review submitted.") : t("Draft saved."), { silent: true }); if (r.ok) { setA(null); q.reload(); } else setForm(r.error); };
      return (
        <>
          <Panel title={`${t(REVIEW_KIND[d.task.kind])} · ${d.task.kind === "self" ? t("Myself") : d.task.employeeName}`} description={`${d.task.cycle}${d.task.dueDate ? ` · ${t("due {date}", { date: fmtDate(d.task.dueDate) })}` : ""}`}>
            {form && <div className="form-alert err" role="alert">{form}</div>}
            {locked && <div className="form-alert" role="status">{d.task.submitted ? t("This review is submitted and can no longer be changed.") : t("This review cycle is not open.")}</div>}
            {d.task.kind === "peer" && <small className="sub">{t("Your name is not shown to the person you review.")}</small>}
          </Panel>
          {d.goals.length > 0 && <Panel title={t("Goals")} description={t("1 = far below expectations, 5 = outstanding")}>
            <div className="stack">{d.goals.map((g) => (
              <div key={g.id} className="row" style={{ justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
                <div style={{ minWidth: 220, flex: 1 }}><b>{g.title}</b><small className="sub">{num(g.currentValue)} / {num(g.targetValue)} {g.measure ?? ""} · {num(g.progress, 0)}% · {t("weight {w}%", { w: num(g.weight, 0) })}</small></div>
                <Stars label={g.title} value={s.goals[g.id]} disabled={locked} onChange={(v) => setA({ ...s, goals: { ...s.goals, [g.id]: v } })} />
              </div>))}</div>
          </Panel>}
          {d.competencies.length > 0 && <Panel title={t("Competencies")} description={t("1 = far below expectations, 5 = outstanding")}>
            <div className="stack">{d.competencies.map((c) => (
              <div key={c.id} className="row" style={{ justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
                <div style={{ minWidth: 220, flex: 1 }}><b>{c.name}</b><small className="sub">{t(CATEGORY[c.category] ?? c.category)}{c.description ? ` · ${c.description}` : ""}</small></div>
                <Stars label={c.name} value={s.competencies[c.id]} disabled={locked} onChange={(v) => setA({ ...s, competencies: { ...s.competencies, [c.id]: v } })} />
              </div>))}</div>
          </Panel>}
          <Panel title={t("Feedback")}>
            <div className="stack">
              <Field label={t("Strengths")} htmlFor="rv-str"><textarea id="rv-str" rows={3} maxLength={2000} className="textarea" disabled={locked} value={s.strengths} onChange={(e) => setA({ ...s, strengths: e.target.value })} /></Field>
              <Field label={t("To improve")} htmlFor="rv-imp"><textarea id="rv-imp" rows={3} maxLength={2000} className="textarea" disabled={locked} value={s.improvements} onChange={(e) => setA({ ...s, improvements: e.target.value })} /></Field>
              <Field label={t("Other comments")} htmlFor="rv-com"><textarea id="rv-com" rows={2} maxLength={2000} className="textarea" disabled={locked} value={s.comment} onChange={(e) => setA({ ...s, comment: e.target.value })} /></Field>
              {!locked && <div className="row" style={{ gap: 8, justifyContent: "flex-end" }}><Button onClick={() => send(false)}>{t("Save draft")}</Button><Button variant="primary" onClick={() => send(true)}>{t("Submit review")}</Button></div>}
            </div>
          </Panel>
        </>
      );
    }}</QueryBoundary>
  );
}

// ============================================================ HR: competencies
export function CompetencyLibrary() {
  const run = useRun(); const q = useQuery(() => get<Competency[]>("/api/hr/competencies"), []);
  const [edit, setEdit] = useState<Competency | "new" | null>(null); const [f, setF] = useState({ code: "", name: "", category: "core", description: "", archived: false }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (c: Competency | "new") => { setErr({}); setForm(""); setEdit(c); setF(c === "new" ? { code: "", name: "", category: "core", description: "", archived: false } : { code: c.code, name: c.name, category: c.category, description: c.description ?? "", archived: c.archived }); };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => (edit === "new" ? post("/api/hr/competencies", f) : put(`/api/hr/competencies/${(edit as Competency).id}`, f)), t("Competency saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const cols: Column<Competency>[] = [
    { key: "c", header: t("Code"), cell: (c) => <code>{c.code}</code> }, { key: "n", header: t("Competency"), cell: (c) => <><b>{c.name}</b>{c.description && <small className="sub">{c.description}</small>}</> },
    { key: "k", header: t("Category"), cell: (c) => t(CATEGORY[c.category] ?? c.category) }, { key: "s", header: t("Status"), cell: (c) => (c.archived ? <Badge tone="gray">{t("Archived")}</Badge> : <Badge tone="green">{t("Active")}</Badge>) },
    { key: "x", header: "", cell: (c) => <Button size="sm" variant="soft" onClick={() => open(c)}>{t("Edit")}</Button> },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add competency")}</Button></div>
      <Panel>
        {!q.loading && (q.data ?? []).length === 0 ? <EmptyState title={t("No competencies yet")} description={t("List the skills and behaviours you assess, e.g. Communication, Ownership, Customer focus.")} />
          : <DataTable label={t("Competencies")} columns={cols} rows={q.data ?? []} rowKey={(c) => c.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add competency") : t("Edit competency")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cp-form">{t("Save")}</Button></>}>
        <form id="cp-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Code")} htmlFor="cp-code" required error={err.code}><TextInput id="cp-code" value={f.code} maxLength={20} onChange={(e) => setF({ ...f, code: e.target.value })} placeholder="COMM" /></Field>
          <Field label={t("Name")} htmlFor="cp-name" required error={err.name}><TextInput id="cp-name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
          <Field label={t("Category")} htmlFor="cp-cat" error={err.category}><SelectInput id="cp-cat" value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })} options={Object.keys(CATEGORY).map((k) => ({ value: k, label: t(CATEGORY[k]) }))} /></Field>
          <Field label={t("What good looks like")} htmlFor="cp-desc"><textarea id="cp-desc" rows={4} maxLength={1000} className="textarea" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></Field>
          {edit !== "new" && <label className="check"><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived (not offered for new cycles)")}</label>}
        </form>
      </Overlay>
    </>
  );
}

// ============================================================ HR: review cycles
const emptyCycle = () => ({ name: "", periodStart: `${thisYear}-01-01`, periodEnd: `${thisYear}-12-31`, dueDate: "", orgUnitId: "", selfReview: true, managerReview: true, peerReview: false, goalsWeight: "60", competencyIds: [] as string[] });

export function ReviewCycles() {
  const run = useRun(); const q = useQuery(() => get<ReviewCycle[]>("/api/hr/review-cycles"), []);
  const comps = useQuery(() => get<Competency[]>("/api/hr/competencies"), []); const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]), []);
  const [edit, setEdit] = useState<ReviewCycle | "new" | null>(null); const [f, setF] = useState(emptyCycle()); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (c: ReviewCycle | "new") => { setErr({}); setForm(""); setEdit(c); setF(c === "new" ? emptyCycle() : { name: c.name, periodStart: c.periodStart, periodEnd: c.periodEnd, dueDate: c.dueDate ?? "", orgUnitId: c.orgUnitId ?? "", selfReview: c.selfReview, managerReview: c.managerReview, peerReview: c.peerReview, goalsWeight: String(c.goalsWeight), competencyIds: c.competencyIds }); };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { ...f, dueDate: f.dueDate || null, orgUnitId: f.orgUnitId || null, goalsWeight: Number(f.goalsWeight || 0) };
    const r = await run(() => (edit === "new" ? post("/api/hr/review-cycles", body) : put(`/api/hr/review-cycles/${(edit as ReviewCycle).id}`, body)), t("Review cycle saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const act = async (c: ReviewCycle, action: "launch" | "calibrate" | "close", msg: string) => { if ((await run(() => post(`/api/hr/review-cycles/${c.id}/${action}`), msg)).ok) q.reload(); };
  const rows = q.data ?? []; const active = rows.filter((c) => c.status === "open" || c.status === "calibration");
  const cols: Column<ReviewCycle>[] = [
    { key: "n", header: t("Review cycle"), cell: (c) => <><Link href={`/hr/review-cycles/${c.id}`}><b>{c.name}</b></Link><small className="sub">{fmtDate(c.periodStart)} – {fmtDate(c.periodEnd)}</small></> },
    { key: "k", header: t("Reviews"), className: "hide-sm", cell: (c) => [c.selfReview && t("Self review"), c.managerReview && t("Manager review"), c.peerReview && t("Peer review")].filter(Boolean).join(", ") },
    { key: "w", header: t("Weighting"), className: "hide-sm", cell: (c) => t("Goals {g}% · competencies {c}%", { g: num(c.goalsWeight, 0), c: num(c.competencyWeight, 0) }) },
    { key: "p", header: t("Submitted"), cell: (c) => (c.status === "draft" ? "—" : <div style={{ minWidth: 120 }}><ProgressBar value={c.reviews ? (c.submitted / c.reviews) * 100 : 0} color="#3b82f6" /><small className="sub">{c.submitted} / {c.reviews} · {t("{n} people", { n: c.people })}</small></div>) },
    { key: "s", header: t("Status"), cell: (c) => <Badge tone={CYCLE_STATUS[c.status][0]}>{t(CYCLE_STATUS[c.status][1])}</Badge> },
    { key: "x", header: "", cell: (c) => <div className="row" style={{ gap: 6 }}>
      {c.status === "draft" && <><Button size="sm" variant="soft" onClick={() => open(c)}>{t("Edit")}</Button><Button size="sm" variant="primary" onClick={() => act(c, "launch", t("Review cycle launched. Reviewers were notified."))}>{t("Launch")}</Button></>}
      {c.status === "open" && <Button size="sm" onClick={() => act(c, "calibrate", t("Moved to calibration."))}>{t("Start calibration")}</Button>}
      {c.status === "calibration" && <Button size="sm" variant="primary" onClick={() => act(c, "close", t("Cycle closed. Results released to employees."))}>{t("Close and release")}</Button>}
      {c.status !== "draft" && <Link className="btn sm" href={`/hr/review-cycles/${c.id}`}>{t("Results")}</Link>}</div> },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("New review cycle")}</Button></div>
      <KpiRow label={t("Review figures")}>
        <Kpi icon={<Flag size={18} />} label={t("Active cycles")} value={active.length} />
        <Kpi icon={<Users size={18} />} tone="blue" label={t("People being reviewed")} value={active.reduce((s, c) => s + c.people, 0)} />
        <Kpi icon={<CheckCircle2 size={18} />} tone="green" label={t("Reviews submitted")} value={`${active.reduce((s, c) => s + c.submitted, 0)} / ${active.reduce((s, c) => s + c.reviews, 0)}`} />
        <Kpi icon={<Crown size={18} />} tone="orange" label={t("Closed cycles")} value={rows.filter((c) => c.status === "closed").length} />
      </KpiRow>
      <Panel>
        {!q.loading && rows.length === 0 ? <EmptyState title={t("No review cycles yet")} description={t("A cycle sets who reviews whom (self, manager, peers), what is rated and how goals and competencies weigh in the score.")} />
          : <DataTable label={t("Review cycles")} columns={cols} rows={rows} rowKey={(c) => c.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("New review cycle") : t("Edit review cycle")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rc-form">{t("Save")}</Button></>}>
        <form id="rc-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Name")} htmlFor="rc-name" required error={err.name}><TextInput id="rc-name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("e.g. Annual review {year}", { year: thisYear })} /></Field>
          <div className="fgrid">
            <Field label={t("Period from")} htmlFor="rc-from"><TextInput id="rc-from" type="date" value={f.periodStart} onChange={(e) => setF({ ...f, periodStart: e.target.value })} /></Field>
            <Field label={t("Period to")} htmlFor="rc-to" error={err.periodEnd}><TextInput id="rc-to" type="date" value={f.periodEnd} onChange={(e) => setF({ ...f, periodEnd: e.target.value })} /></Field>
          </div>
          <Field label={t("Reviews due")} htmlFor="rc-due"><TextInput id="rc-due" type="date" value={f.dueDate} onChange={(e) => setF({ ...f, dueDate: e.target.value })} /></Field>
          <Field label={t("Who is reviewed")} htmlFor="rc-unit" error={err.orgUnitId}><SelectInput id="rc-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={[{ value: "", label: t("Everyone") }, ...(units.data ?? []).filter((u) => !u.archived).map((u) => ({ value: u.id, label: u.name }))]} /></Field>
          <fieldset className="stack"><legend>{t("Kinds of review")}</legend>
            <label className="check"><Checkbox checked={f.selfReview} onChange={(e) => setF({ ...f, selfReview: e.target.checked })} /> {t("Self review")}</label>
            <label className="check"><Checkbox checked={f.managerReview} onChange={(e) => setF({ ...f, managerReview: e.target.checked })} /> {t("Manager review")}</label>
            <label className="check"><Checkbox checked={f.peerReview} onChange={(e) => setF({ ...f, peerReview: e.target.checked })} /> {t("Peer review (360°): managers pick colleagues after launch")}</label>
            {err.managerReview && <small className="err">{err.managerReview}</small>}
          </fieldset>
          <Field label={t("Weight of goals (%)")} htmlFor="rc-gw" error={err.goalsWeight} hint={t("Competencies count for the rest ({n}%).", { n: 100 - Number(f.goalsWeight || 0) })}><TextInput id="rc-gw" inputMode="decimal" value={f.goalsWeight} onChange={(e) => setF({ ...f, goalsWeight: e.target.value })} /></Field>
          <fieldset className="stack"><legend>{t("Competencies rated")}</legend>
            {(comps.data ?? []).filter((c) => !c.archived).map((c) => <label key={c.id} className="check"><Checkbox checked={f.competencyIds.includes(c.id)} onChange={(e) => setF({ ...f, competencyIds: e.target.checked ? [...f.competencyIds, c.id] : f.competencyIds.filter((x) => x !== c.id) })} /> {c.name}</label>)}
            {(comps.data ?? []).length === 0 && <small className="sub">{t("Add competencies first, under Competencies.")}</small>}
            {err.competencyIds && <small className="err">{err.competencyIds}</small>}
          </fieldset>
        </form>
      </Overlay>
    </>
  );
}

/** A 3×3 grid of performance (across) against potential (up), with the people in each box. */
export function NineBox({ people }: { people: { employeeId: string; employeeName: string; box: string | null }[] }) {
  return (
    <div className="ninebox" role="table" aria-label={t("9-box grid")}>
      <span className="nb-y">{t("Potential")} →</span>
      <div className="nb-grid">
        {BOXES.map((b) => { const inBox = people.filter((p) => p.box === b.key); return (
          <div key={b.key} className={`nb-cell tone-${b.tone}`} role="cell">
            <div className="nb-head"><b>{t(b.label)}</b><span>{inBox.length}</span></div>
            <ul>{inBox.slice(0, 6).map((p) => <li key={p.employeeId}>{p.employeeName}</li>)}{inBox.length > 6 && <li className="sub">{t("+{n} more", { n: inBox.length - 6 })}</li>}</ul>
          </div>); })}
      </div>
      <span className="nb-x">{t("Performance")} →</span>
    </div>
  );
}

/** Results of one cycle: scores, calibration (rating and potential), the 9-box, and peers for 360° reviews. */
export function CycleResults({ id }: { id: string }) {
  const run = useRun(); const cycles = useQuery(() => get<ReviewCycle[]>("/api/hr/review-cycles"), []); const q = useQuery(() => get<ReviewResultRow[]>(`/api/hr/review-cycles/${id}/results`), [id]);
  const people = useQuery(() => get<Paged<DirectoryEntry>>("/api/hr/directory", { pageSize: 200 }), []);
  const cycle = cycles.data?.find((c) => c.id === id);
  const [cal, setCal] = useState<ReviewResultRow | null>(null); const [f, setF] = useState({ rating: "", potential: "", note: "" }); const [form, setForm] = useState("");
  const [peer, setPeer] = useState(false); const [pf, setPf] = useState({ employeeId: "", reviewerEmployeeId: "" });
  async function saveCal(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/review-cycles/${id}/results`, { employeeId: cal!.employeeId, rating: f.rating ? Number(f.rating) : null, potential: f.potential ? Number(f.potential) : null, note: f.note }), t("Calibration saved."), { silent: true });
    if (r.ok) { setCal(null); q.reload(); } else setForm(r.error);
  }
  async function addPeer(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/review-cycles/${id}/peers`, pf), t("Peer reviewer added and notified."), { silent: true });
    if (r.ok) { setPeer(false); q.reload(); cycles.reload(); } else setForm(r.error);
  }
  const rows = q.data ?? []; const scored = rows.filter((r) => r.finalScore !== null);
  const editable = cycle?.status === "open" || cycle?.status === "calibration";
  const cols: Column<ReviewResultRow>[] = [
    { key: "e", header: t("Employee"), cell: (r) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo}{r.department ? ` · ${r.department}` : ""}</small></> },
    { key: "r", header: t("Reviews"), className: "hide-sm", cell: (r) => `${r.reviewsDone} / ${r.reviewsTotal}` },
    { key: "g", header: t("Goals"), className: "hide-sm", cell: (r) => num(r.goalScore) }, { key: "c", header: t("Competencies"), className: "hide-sm", cell: (r) => num(r.competencyScore) },
    { key: "f", header: t("Score"), cell: (r) => <b>{num(r.finalScore)}</b> },
    { key: "k", header: t("Rating"), cell: (r) => (r.calibratedRating ? `${r.calibratedRating} · ${t(RATING[r.calibratedRating])}` : "—") },
    { key: "p", header: t("Potential"), className: "hide-sm", cell: (r) => (r.potential ? [t("Low"), t("Medium"), t("High")][r.potential - 1] : "—") },
    { key: "b", header: t("9-box"), cell: (r) => boxLabel(r.box) },
    { key: "x", header: "", cell: (r) => (editable ? <Button size="sm" onClick={() => { setForm(""); setCal(r); setF({ rating: r.calibratedRating ? String(r.calibratedRating) : r.finalScore ? String(Math.round(r.finalScore)) : "", potential: r.potential ? String(r.potential) : "", note: r.note ?? "" }); }}>{t("Calibrate")}</Button> : null) },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "space-between", gap: 8, marginBottom: 12, flexWrap: "wrap" }}>
        <div>{cycle && <><b>{cycle.name}</b> <Badge tone={CYCLE_STATUS[cycle.status][0]}>{t(CYCLE_STATUS[cycle.status][1])}</Badge></>}</div>
        {cycle?.peerReview && cycle.status === "open" && <Button onClick={() => { setForm(""); setPf({ employeeId: rows[0]?.employeeId ?? "", reviewerEmployeeId: "" }); setPeer(true); }}><UserPlus size={16} /> {t("Add peer reviewer")}</Button>}
      </div>
      <KpiRow label={t("Cycle figures")}>
        <Kpi icon={<Users size={18} />} label={t("People")} value={rows.length} />
        <Kpi icon={<CheckCircle2 size={18} />} tone="green" label={t("Reviews submitted")} value={`${rows.reduce((s, r) => s + r.reviewsDone, 0)} / ${rows.reduce((s, r) => s + r.reviewsTotal, 0)}`} />
        <Kpi icon={<Gauge size={18} />} tone="blue" label={t("Average score")} value={scored.length ? num(scored.reduce((s, r) => s + (r.finalScore ?? 0), 0) / scored.length) : "—"} hint={t("of 5")} />
        <Kpi icon={<Crown size={18} />} tone="orange" label={t("Stars and high performers")} value={rows.filter((r) => r.box === "star" || r.box === "high_performer").length} />
      </KpiRow>
      <Panel title={t("9-box")} description={t("Placed from the calibrated rating (or the score) and the potential set in calibration.")}><NineBox people={rows} /></Panel>
      <Panel title={t("Results")}>
        <DataTable label={t("Results")} columns={cols} rows={rows} rowKey={(r) => r.employeeId} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      </Panel>
      <Overlay open={cal !== null} onClose={() => setCal(null)} side="right" title={t("Calibrate {name}", { name: cal?.employeeName ?? "" })} footer={<><Button onClick={() => setCal(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cal-form">{t("Save")}</Button></>}>
        <form id="cal-form" className="stack" noValidate onSubmit={saveCal}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {cal && <p><small className="sub">{t("Score from reviews: {score} (goals {g}, competencies {c})", { score: num(cal.finalScore), g: num(cal.goalScore), c: num(cal.competencyScore) })}</small></p>}
          <Field label={t("Final rating")} htmlFor="cal-r"><SelectInput id="cal-r" value={f.rating} onChange={(e) => setF({ ...f, rating: e.target.value })} options={[{ value: "", label: "—" }, ...[1, 2, 3, 4, 5].map((n) => ({ value: String(n), label: `${n} · ${t(RATING[n])}` }))]} /></Field>
          <Field label={t("Potential")} htmlFor="cal-p"><SelectInput id="cal-p" value={f.potential} onChange={(e) => setF({ ...f, potential: e.target.value })} options={[{ value: "", label: "—" }, { value: "1", label: t("Low") }, { value: "2", label: t("Medium") }, { value: "3", label: t("High") }]} /></Field>
          <Field label={t("Calibration note")} htmlFor="cal-n" hint={t("Seen by HR only.")}><textarea id="cal-n" rows={3} maxLength={500} className="textarea" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} /></Field>
        </form>
      </Overlay>
      <Overlay open={peer} onClose={() => setPeer(false)} side="right" title={t("Add peer reviewer")} footer={<><Button onClick={() => setPeer(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="peer-form">{t("Add")}</Button></>}>
        <form id="peer-form" className="stack" noValidate onSubmit={addPeer}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Person reviewed")} htmlFor="pr-who"><SelectInput id="pr-who" value={pf.employeeId} onChange={(e) => setPf({ ...pf, employeeId: e.target.value })} options={rows.map((r) => ({ value: r.employeeId, label: r.employeeName }))} /></Field>
          <Field label={t("Colleague who reviews")} htmlFor="pr-by" hint={t("Their name is not shown to the person.")}><SelectInput id="pr-by" value={pf.reviewerEmployeeId} onChange={(e) => setPf({ ...pf, reviewerEmployeeId: e.target.value })} options={[{ value: "", label: t("Choose…") }, ...(people.data?.data ?? []).filter((p) => p.employeeId !== pf.employeeId).map((p) => ({ value: p.employeeId, label: `${p.fullName} (${p.employeeNo})` }))]} /></Field>
        </form>
      </Overlay>
    </>
  );
}

// ============================================================ talent
export function TalentPool() {
  const q = useQuery(() => get<TalentRow[]>("/api/hr/talent/pool"), []); const rows = q.data ?? [];
  const cols: Column<TalentRow>[] = [
    { key: "e", header: t("Employee"), cell: (r) => <><b>{r.employeeName}</b><small className="sub">{r.jobTitle ?? ""}{r.department ? ` · ${r.department}` : ""}</small></> },
    { key: "r", header: t("Rating"), cell: (r) => `${r.rating} · ${t(RATING[r.rating])}` }, { key: "p", header: t("Potential"), cell: (r) => [t("Low"), t("Medium"), t("High")][r.potential - 1] },
    { key: "b", header: t("9-box"), cell: (r) => { const b = BOXES.find((x) => x.key === r.box); return <Badge tone={b?.tone ?? "gray"}>{boxLabel(r.box)}</Badge>; } },
  ];
  return (
    <>
      <KpiRow label={t("Talent figures")}>
        <Kpi icon={<Users size={18} />} label={t("People placed")} value={rows.length} hint={rows[0]?.cycle} />
        <Kpi icon={<Crown size={18} />} tone="green" label={t("Stars")} value={rows.filter((r) => r.box === "star").length} />
        <Kpi icon={<TrendingUp size={18} />} tone="blue" label={t("High potential")} value={rows.filter((r) => r.potential === 3).length} />
        <Kpi icon={<AlertTriangle size={18} />} tone="red" label={t("Underperformers")} value={rows.filter((r) => r.box === "underperformer").length} />
      </KpiRow>
      {!q.loading && rows.length === 0 ? <Panel><EmptyState title={t("No talent data yet")} description={t("People appear here after a review cycle is calibrated and closed.")} /></Panel> : <>
        <Panel title={t("9-box")}><NineBox people={rows} /></Panel>
        <Panel title={t("Talent directory")}><DataTable label={t("Talent directory")} columns={cols} rows={rows} rowKey={(r) => r.employeeId} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} /></Panel>
      </>}
    </>
  );
}

export function SuccessionBoard() {
  const run = useRun(); const q = useQuery(() => get<SuccessionPlan[]>("/api/hr/talent/succession"), []);
  const people = useQuery(() => get<Paged<DirectoryEntry>>("/api/hr/directory", { pageSize: 200 }), []); const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]), []);
  const [edit, setEdit] = useState<SuccessionPlan | "new" | null>(null); const [f, setF] = useState({ position: "", orgUnitId: "", incumbentEmployeeId: "", critical: false, note: "" }); const [form, setForm] = useState(""); const [err, setErr] = useState<Record<string, string>>({});
  const [cand, setCand] = useState<SuccessionPlan | null>(null); const [cf, setCf] = useState({ employeeId: "", readiness: "one_to_two_years", note: "" });
  const opts = (people.data?.data ?? []).map((p) => ({ value: p.employeeId, label: `${p.fullName} (${p.employeeNo})` }));
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { ...f, orgUnitId: f.orgUnitId || null, incumbentEmployeeId: f.incumbentEmployeeId || null };
    const r = await run(() => (edit === "new" ? post("/api/hr/talent/succession", body) : put(`/api/hr/talent/succession/${(edit as SuccessionPlan).id}`, body)), t("Position saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  async function addCand(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/talent/succession/${cand!.id}/candidates`, { ...cf, employeeId: cf.employeeId || opts[0]?.value }), t("Successor saved."), { silent: true });
    if (r.ok) { setCand(null); q.reload(); } else setForm(r.error);
  }
  const plans = q.data ?? [];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ position: "", orgUnitId: "", incumbentEmployeeId: "", critical: false, note: "" }); setEdit("new"); }}><Plus size={16} /> {t("Add key position")}</Button></div>
      <KpiRow label={t("Succession figures")}>
        <Kpi icon={<Flag size={18} />} label={t("Key positions")} value={plans.length} />
        <Kpi icon={<AlertTriangle size={18} />} tone="red" label={t("Critical without a ready successor")} value={plans.filter((p) => p.critical && !p.candidates.some((c) => c.readiness === "ready_now")).length} />
        <Kpi icon={<UserPlus size={18} />} tone="green" label={t("Ready-now successors")} value={plans.reduce((s, p) => s + p.candidates.filter((c) => c.readiness === "ready_now").length, 0)} />
      </KpiRow>
      {!q.loading && plans.length === 0 ? <Panel><EmptyState title={t("No key positions yet")} description={t("List the positions the business depends on and who could take them over.")} /></Panel>
        : plans.map((p) => (
          <Panel key={p.id} title={p.position} description={[p.unit, p.incumbent ? t("Held by {name}", { name: p.incumbent }) : t("Vacant")].filter(Boolean).join(" · ")}
            actions={<div className="row" style={{ gap: 6 }}>{p.critical && <Badge tone="red">{t("Critical")}</Badge>}<Button size="sm" variant="soft" onClick={() => { setErr({}); setForm(""); setF({ position: p.position, orgUnitId: p.orgUnitId ?? "", incumbentEmployeeId: p.incumbentEmployeeId ?? "", critical: p.critical, note: p.note ?? "" }); setEdit(p); }}>{t("Edit")}</Button>
              <Button size="sm" onClick={() => { setForm(""); setCf({ employeeId: "", readiness: "one_to_two_years", note: "" }); setCand(p); }}><UserPlus size={14} /> {t("Add successor")}</Button></div>}>
            {p.candidates.length === 0 ? <small className="sub">{t("No successors named yet.")}</small> : (
              <ul className="mini-list">{p.candidates.map((c) => (
                <li key={c.id} className="row" style={{ justifyContent: "space-between", gap: 8 }}>
                  <span><b>{c.employeeName}</b>{c.jobTitle ? ` · ${c.jobTitle}` : ""} <Badge tone={READINESS[c.readiness][0]}>{t(READINESS[c.readiness][1])}</Badge>{c.box && <> <Badge tone="gray" dot={false}>{boxLabel(c.box)}</Badge></>}{c.note && <small className="sub">{c.note}</small>}</span>
                  <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={async () => { if ((await run(() => del(`/api/hr/talent/succession/${p.id}/candidates/${c.id}`), t("Removed."))).ok) q.reload(); }}><Trash2 size={14} /></Button>
                </li>))}</ul>)}
          </Panel>))}
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add key position") : t("Edit key position")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="sp-form">{t("Save")}</Button></>}>
        <form id="sp-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Position")} htmlFor="sp-pos" required error={err.position}><TextInput id="sp-pos" value={f.position} onChange={(e) => setF({ ...f, position: e.target.value })} placeholder={t("e.g. Finance Manager")} /></Field>
          <Field label={t("Department")} htmlFor="sp-unit"><SelectInput id="sp-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={[{ value: "", label: "—" }, ...(units.data ?? []).filter((u) => !u.archived).map((u) => ({ value: u.id, label: u.name }))]} /></Field>
          <Field label={t("Held by")} htmlFor="sp-inc" error={err.incumbentEmployeeId}><SelectInput id="sp-inc" value={f.incumbentEmployeeId} onChange={(e) => setF({ ...f, incumbentEmployeeId: e.target.value })} options={[{ value: "", label: t("Vacant") }, ...opts]} /></Field>
          <label className="check"><Checkbox checked={f.critical} onChange={(e) => setF({ ...f, critical: e.target.checked })} /> {t("Critical position")}</label>
          <Field label={t("Note")} htmlFor="sp-note"><TextInput id="sp-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
      <Overlay open={cand !== null} onClose={() => setCand(null)} side="right" title={t("Add successor")} footer={<><Button onClick={() => setCand(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="sc-form">{t("Save")}</Button></>}>
        <form id="sc-form" className="stack" noValidate onSubmit={addCand}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Employee")} htmlFor="sc-who"><SelectInput id="sc-who" value={cf.employeeId || opts[0]?.value || ""} onChange={(e) => setCf({ ...cf, employeeId: e.target.value })} options={opts} /></Field>
          <Field label={t("Readiness")} htmlFor="sc-ready"><SelectInput id="sc-ready" value={cf.readiness} onChange={(e) => setCf({ ...cf, readiness: e.target.value })} options={Object.keys(READINESS).map((k) => ({ value: k, label: t(READINESS[k][1]) }))} /></Field>
          <Field label={t("Note")} htmlFor="sc-note"><TextInput id="sc-note" value={cf.note} onChange={(e) => setCf({ ...cf, note: e.target.value })} placeholder={t("e.g. needs budgeting experience")} /></Field>
        </form>
      </Overlay>
    </>
  );
}

// ============================================================ development plans
/** Individual development plans: my own, or (for a manager or HR) one person's. */
export function DevelopmentPlans({ employeeId }: { employeeId?: string }) {
  const run = useRun(); const q = useQuery(() => get<DevelopmentPlan[]>("/api/hr/talent/plans", employeeId ? { employeeId } : undefined), [employeeId]);
  const comps = useQuery(() => get<Competency[]>("/api/hr/competencies"), []);
  const [edit, setEdit] = useState<DevelopmentPlan | "new" | null>(null); const [f, setF] = useState({ title: "", aim: "", competencyId: "", status: "active", actions: [] as IdpAction[] }); const [form, setForm] = useState(""); const [err, setErr] = useState<Record<string, string>>({});
  const open = (p: DevelopmentPlan | "new") => { setErr({}); setForm(""); setEdit(p); setF(p === "new" ? { title: "", aim: "", competencyId: "", status: "active", actions: [{ id: "", text: "", due: null, done: false }] } : { title: p.title, aim: p.aim ?? "", competencyId: p.competencyId ?? "", status: p.status, actions: p.actions }); };
  const body = (x: typeof f) => ({ employeeId, title: x.title, aim: x.aim, competencyId: x.competencyId || null, status: x.status, actions: x.actions.filter((a) => a.text.trim()) });
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => (edit === "new" ? post("/api/hr/talent/plans", body(f)) : put(`/api/hr/talent/plans/${(edit as DevelopmentPlan).id}`, body(f))), t("Plan saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const toggle = async (p: DevelopmentPlan, a: IdpAction) => {
    const actions = p.actions.map((x) => (x.id === a.id ? { ...x, done: !x.done } : x));
    if ((await run(() => put(`/api/hr/talent/plans/${p.id}`, { actions }), a.done ? t("Marked as not done.") : t("Well done!"))).ok) q.reload();
  };
  const plans = q.data ?? [];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("New development plan")}</Button></div>
      {!q.loading && plans.length === 0 ? <Panel><EmptyState title={t("No development plans yet")} description={t("Write down what you want to grow in, and the steps you agree with your manager: a course, a project, mentoring.")} /></Panel>
        : plans.map((p) => (
          <Panel key={p.id} title={p.title} description={[p.competency, p.aim].filter(Boolean).join(" · ")}
            actions={<div className="row" style={{ gap: 6 }}><Badge tone={p.status === "done" ? "green" : p.status === "active" ? "blue" : "gray"}>{t(p.status === "done" ? "Done" : p.status === "active" ? "Active" : "Draft")}</Badge>{p.canEdit && <Button size="sm" variant="soft" onClick={() => open(p)}>{t("Edit")}</Button>}</div>}>
            <ProgressBar value={p.actions.length ? (p.done / p.actions.length) * 100 : 0} color="#22c55e" />
            <small className="sub">{t("{done} of {n} steps done", { done: p.done, n: p.actions.length })}</small>
            <ul className="mini-list">{p.actions.map((a) => <li key={a.id}><label className="check"><Checkbox checked={a.done} disabled={!p.canEdit} onChange={() => toggle(p, a)} /> <span style={a.done ? { textDecoration: "line-through" } : undefined}>{a.text}</span>{a.due && <small className="sub"> · {fmtDate(a.due)}</small>}</label></li>)}</ul>
          </Panel>))}
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("New development plan") : t("Edit development plan")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="idp-form">{t("Save")}</Button></>}>
        <form id="idp-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Plan")} htmlFor="idp-title" required error={err.title}><TextInput id="idp-title" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder={t("e.g. Become a team lead")} /></Field>
          <Field label={t("Aim")} htmlFor="idp-aim"><textarea id="idp-aim" rows={2} maxLength={1000} className="textarea" value={f.aim} onChange={(e) => setF({ ...f, aim: e.target.value })} /></Field>
          <Field label={t("Competency to grow")} htmlFor="idp-comp"><SelectInput id="idp-comp" value={f.competencyId} onChange={(e) => setF({ ...f, competencyId: e.target.value })} options={[{ value: "", label: "—" }, ...(comps.data ?? []).filter((c) => !c.archived).map((c) => ({ value: c.id, label: c.name }))]} /></Field>
          <Field label={t("Status")} htmlFor="idp-st"><SelectInput id="idp-st" value={f.status} onChange={(e) => setF({ ...f, status: e.target.value })} options={[{ value: "draft", label: t("Draft") }, { value: "active", label: t("Active") }, { value: "done", label: t("Done") }]} /></Field>
          <fieldset className="stack"><legend>{t("Steps")}</legend>
            {f.actions.map((a, i) => (
              <div key={i} className="row" style={{ gap: 6 }}>
                <TextInput aria-label={t("Step {n}", { n: i + 1 })} value={a.text} maxLength={300} onChange={(e) => setF({ ...f, actions: f.actions.map((x, j) => (j === i ? { ...x, text: e.target.value } : x)) })} placeholder={t("e.g. Finish the leadership course")} />
                <TextInput aria-label={t("Due date")} type="date" value={a.due ?? ""} onChange={(e) => setF({ ...f, actions: f.actions.map((x, j) => (j === i ? { ...x, due: e.target.value || null } : x)) })} style={{ maxWidth: 160 }} />
                <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={() => setF({ ...f, actions: f.actions.filter((_, j) => j !== i) })}><Trash2 size={14} /></Button>
              </div>))}
            {err.actions && <small className="err">{err.actions}</small>}
            <Button size="sm" onClick={() => setF({ ...f, actions: [...f.actions, { id: "", text: "", due: null, done: false }] })}><Plus size={14} /> {t("Add step")}</Button>
          </fieldset>
        </form>
      </Overlay>
    </>
  );
}

/** On an employee's page (HR or the manager): their goals for this year and their development plans. */
export function EmployeeGrowth({ employeeId }: { employeeId: string }) {
  const q = useQuery(() => get<Goal[]>("/api/hr/goals", { scope: "employee", employeeId, year: thisYear }), [employeeId]);
  const cols: Column<Goal>[] = [
    { key: "g", header: t("Goal"), cell: (g) => <><b>{g.title}</b><small className="sub">{t(GOAL_KIND[g.kind])}{g.parent ? ` · ${t("supports {goal}", { goal: g.parent })}` : ""}</small></> },
    { key: "p", header: t("Progress"), cell: (g) => <div style={{ minWidth: 140 }}><ProgressBar value={g.progress} color="#22c55e" /><small className="sub">{num(g.progress, 0)}%</small></div> },
    { key: "s", header: t("Status"), cell: (g) => <Badge tone={GOAL_STATUS[g.status][0]}>{t(GOAL_STATUS[g.status][1])}</Badge> },
  ];
  return (
    <div className="stack">
      <Panel title={t("Goals this year")}>
        {!q.loading && (q.data ?? []).length === 0 ? <EmptyState title={t("No goals yet")} description={t("Goals are set under Goals & OKR.")} />
          : <DataTable label={t("Goals")} columns={cols} rows={q.data ?? []} rowKey={(g) => g.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <DevelopmentPlans employeeId={employeeId} />
    </div>
  );
}
