"use client";

import { useState } from "react";
import Link from "next/link";
import { Award, BookOpen, CheckCircle2, Clock, Download, ExternalLink, FileText, GraduationCap, Link2, Plus, Trash2, TriangleAlert, Users, Video } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { ProgressBar } from "@/components/charts/lists";
import { QueryBoundary } from "@/components/query-boundary";
import { del, get, post, put } from "@/lib/api/client";
import type { AssessmentQuestion, CourseEditorData, CourseItem, CoursePlayerData, DirectoryEntry, EnrollmentItem, LearningSummary, LessonItem, MyCourseItem, OrgUnitItem, Paged, TeamLearningRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { download } from "@/lib/download";
import { toBase64 } from "@/lib/file";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const fieldsOf = (r: { fields?: Record<string, string> }) => r.fields ?? {};
export const CATEGORY: Record<string, string> = { general: "General", compliance: "Compliance", safety: "Safety (K3)", technical: "Technical", leadership: "Leadership", onboarding: "Onboarding", product: "Product" };
const COURSE_STATUS: Record<CourseItem["status"], [BadgeTone, string]> = { draft: ["gray", "Draft"], published: ["green", "Published"], archived: ["gray", "Archived"] };
const ENR_STATUS: Record<string, [BadgeTone, string]> = { available: ["blue", "Open to join"], not_started: ["gray", "Not started"], in_progress: ["orange", "In progress"], completed: ["green", "Completed"] };
const KIND_ICON = { text: <BookOpen size={16} />, video: <Video size={16} />, link: <Link2 size={16} />, file: <FileText size={16} /> };
const KIND_LABEL: Record<string, string> = { text: "Reading", video: "Video", link: "Web page", file: "PDF" };
const isoDay = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

// ============================================================ people: my learning
export function MyLearning() {
  const run = useRun(); const q = useQuery(() => get<{ mine: MyCourseItem[]; catalog: MyCourseItem[] }>("/api/hr/learning/mine"), []);
  return (
    <QueryBoundary q={q}>{(d) => (
      <>
        <KpiRow label={t("My learning")}>
          <Kpi icon={<BookOpen size={18} />} label={t("My courses")} value={d.mine.length} />
          <Kpi icon={<Clock size={18} />} tone="orange" label={t("To finish")} value={d.mine.filter((c) => c.status !== "completed").length} hint={d.mine.some((c) => c.overdue) ? t("{n} overdue", { n: d.mine.filter((c) => c.overdue).length }) : undefined} />
          <Kpi icon={<Award size={18} />} tone="green" label={t("Certificates")} value={d.mine.filter((c) => c.certificateNo).length} />
        </KpiRow>
        {d.mine.length === 0 ? <Panel><EmptyState title={t("No courses yet")} description={t("Courses HR assigns to you appear here. You can also join an open course below.")} /></Panel>
          : <div className="course-grid">{d.mine.map((c) => <CourseCard key={c.courseId} c={c} />)}</div>}
        {d.catalog.length > 0 && <>
          <h3 style={{ margin: "22px 0 10px" }}>{t("Open courses you can join")}</h3>
          <div className="course-grid">{d.catalog.map((c) => <CourseCard key={c.courseId} c={c} action={<Button size="sm" variant="primary" onClick={async () => { if ((await run(() => post(`/api/hr/learning/take/${c.courseId}/enroll`), t("You joined the course."))).ok) q.reload(); }}>{t("Join")}</Button>} />)}</div>
        </>}
      </>)}</QueryBoundary>
  );
}

function CourseCard({ c, action }: { c: MyCourseItem; action?: React.ReactNode }) {
  return (
    <article className="card course-card">
      <div className="row" style={{ justifyContent: "space-between", gap: 8 }}><small className="sub">{t(CATEGORY[c.category] ?? c.category)}{c.mandatory ? ` · ${t("Mandatory")}` : ""}</small><Badge tone={c.overdue ? "red" : ENR_STATUS[c.status][0]}>{c.overdue ? t("Overdue") : t(ENR_STATUS[c.status][1])}</Badge></div>
      <h3>{c.status === "available" ? c.title : <Link href={`/hr/learning/${c.courseId}`}>{c.title}</Link>}</h3>
      {c.description && <p className="sub clamp">{c.description}</p>}
      <small className="sub">{t("{n} lessons", { n: c.lessons })} · {t("{n} min", { n: c.minutes })}{c.dueDate ? ` · ${t("due {date}", { date: fmtDate(c.dueDate) })}` : ""}</small>
      {c.status !== "available" && <ProgressBar value={c.progress} color={c.status === "completed" ? "#22c55e" : "#3b82f6"} />}
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        {c.certificateNo && c.enrollmentId && <Button size="sm" onClick={() => download(`/api/hr/learning/certificates/${c.enrollmentId}`)}><Award size={14} /> {t("Certificate")}</Button>}
        {action ?? <Link className="btn sm primary" href={`/hr/learning/${c.courseId}`}>{c.status === "completed" ? t("Review") : c.status === "not_started" ? t("Start") : t("Continue")}</Link>}
      </div>
    </article>
  );
}

/** Taking one course: lessons in order, then the quiz. */
export function CoursePlayer({ id }: { id: string }) {
  const run = useRun(); const q = useQuery(() => get<CoursePlayerData>(`/api/hr/learning/take/${id}`), [id]);
  const [open, setOpen] = useState<string | null>(null); const [answers, setAnswers] = useState<Record<string, number>>({}); const [result, setResult] = useState<{ score: number; passed: boolean } | null>(null);
  const done = async (l: LessonItem) => { const r = await run(() => post<CoursePlayerData>(`/api/hr/learning/take/${id}/lessons/${l.id}/done`)); if (r.ok) q.setData(r.data); };
  const submit = async () => { const r = await run(() => post<{ score: number; passed: boolean }>(`/api/hr/learning/take/${id}/quiz`, { answers })); if (r.ok) { setResult(r.data); q.reload(); setAnswers({}); } };
  return (
    <QueryBoundary q={q}>{(d) => (
      <>
        <Panel title={d.course.title} description={d.course.description ?? undefined} actions={<Badge tone={ENR_STATUS[d.course.status][0]}>{t(ENR_STATUS[d.course.status][1])}</Badge>}>
          <ProgressBar value={d.course.progress} color="#22c55e" />
          <small className="sub">{t("{p}% done", { p: d.course.progress })}{d.course.dueDate ? ` · ${t("due {date}", { date: fmtDate(d.course.dueDate) })}` : ""}{d.quiz.length > 0 ? ` · ${t("quiz pass mark {n}%", { n: d.passScore })}` : ""}</small>
          {d.course.status === "available" && <div style={{ marginTop: 10 }}><Button variant="primary" onClick={async () => { const r = await run(() => post<CoursePlayerData>(`/api/hr/learning/take/${id}/enroll`), t("You joined the course.")); if (r.ok) q.setData(r.data); }}>{t("Join this course")}</Button></div>}
          {d.course.certificateNo && d.course.enrollmentId && <div className="row" style={{ gap: 8, marginTop: 10 }}><CheckCircle2 size={18} color="#22c55e" /> <b>{t("Completed")}</b><Button size="sm" onClick={() => download(`/api/hr/learning/certificates/${d.course.enrollmentId}`)}><Award size={14} /> {t("Download certificate")}</Button>{d.course.expiresOn && <small className="sub">{t("valid until {date}", { date: fmtDate(d.course.expiresOn) })}</small>}</div>}
        </Panel>
        <Panel title={t("Lessons")}>
          <ol className="lesson-list">{d.lessons.map((l) => (
            <li key={l.id} className={l.done ? "done" : ""}>
              <button type="button" className="lesson-head" aria-expanded={open === l.id} onClick={() => setOpen(open === l.id ? null : l.id)}>
                {l.done ? <CheckCircle2 size={18} color="#22c55e" /> : KIND_ICON[l.kind]}<span><b>{l.title}</b><small className="sub">{t(KIND_LABEL[l.kind])}{l.minutes ? ` · ${t("{n} min", { n: l.minutes })}` : ""}</small></span>
              </button>
              {open === l.id && <div className="lesson-body">
                {l.body && <div className="job-body">{l.body}</div>}
                {l.url && <p><a href={l.url} target="_blank" rel="noopener noreferrer" className="btn sm"><ExternalLink size={14} /> {l.kind === "video" ? t("Watch the video") : t("Open the page")}</a></p>}
                {l.kind === "file" && d.course.status !== "available" && <p><Button size="sm" onClick={() => download(`/api/hr/learning/take/${id}/lessons/${l.id}/file`)}><Download size={14} /> {l.fileName ?? t("Download")}</Button></p>}
                {!l.done && d.course.status !== "available" && <Button size="sm" variant="primary" onClick={() => done(l)}>{t("Mark as done")}</Button>}
              </div>}
            </li>))}</ol>
        </Panel>
        {d.quiz.length > 0 && d.course.status !== "completed" && d.course.status !== "available" && <Panel title={t("Quiz")} description={d.quizOpen ? t("Answer every question. You need {n}% to pass; you have 5 attempts.", { n: d.passScore }) : t("The quiz opens when every lesson is done.")}>
          {result && <div className={`form-alert${result.passed ? "" : " err"}`} role="status">{result.passed ? t("Passed with {n}%. Well done!", { n: result.score }) : t("{n}% — not enough to pass yet. Review the lessons and try again.", { n: result.score })}</div>}
          {d.quizOpen && <div className="stack">
            {d.quiz.map((qq, i) => (
              <fieldset key={qq.id} className="test-q"><legend><b>{i + 1}. {qq.text}</b></legend>
                {qq.options.map((o, k) => <label key={k}><input type="radio" name={qq.id} checked={answers[qq.id] === k} onChange={() => setAnswers({ ...answers, [qq.id]: k })} /> <span>{o}</span></label>)}</fieldset>))}
            <div><Button variant="primary" disabled={Object.keys(answers).length < d.quiz.length} onClick={submit}>{t("Send answers")}</Button></div>
          </div>}
        </Panel>}
      </>)}</QueryBoundary>
  );
}

// ============================================================ HR: catalogue
export function CourseCatalogue() {
  const run = useRun(); const q = useQuery(() => get<CourseItem[]>("/api/hr/learning/courses"), []); const s = useQuery(() => get<LearningSummary>("/api/hr/learning/summary"), []);
  const [add, setAdd] = useState(false); const [f, setF] = useState({ title: "", category: "general", description: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<CourseEditorData>("/api/hr/learning/courses", f), t("Course created. Add its lessons next."), { silent: true });
    if (r.ok) window.location.assign(`/hr/courses/${r.data.course.id}`); else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const cols: Column<CourseItem>[] = [
    { key: "t", header: t("Course"), cell: (c) => <><Link href={`/hr/courses/${c.id}`}><b>{c.title}</b></Link><small className="sub">{t(CATEGORY[c.category] ?? c.category)}{c.mandatory ? ` · ${t("Mandatory")}` : ""} · {t("{n} lessons", { n: c.lessons })} · {t("{n} min", { n: c.minutes })}{c.quizQuestions ? ` · ${t("quiz")}` : ""}</small></> },
    { key: "e", header: t("Enrolled"), cell: (c) => c.enrolled }, { key: "p", header: t("Completed"), cell: (c) => <div style={{ minWidth: 110 }}><ProgressBar value={c.enrolled ? (c.completed / c.enrolled) * 100 : 0} color="#22c55e" /><small className="sub">{c.completed} / {c.enrolled}</small></div> },
    { key: "o", header: t("Overdue"), cell: (c) => (c.overdue ? <b className="neg">{c.overdue}</b> : "0") },
    { key: "s", header: t("Status"), cell: (c) => <Badge tone={COURSE_STATUS[c.status][0]}>{t(COURSE_STATUS[c.status][1])}</Badge> },
    { key: "x", header: "", cell: (c) => <Link className="btn sm" href={`/hr/courses/${c.id}`}>{t("Manage")}</Link> },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => { setErr({}); setForm(""); setAdd(true); }}><Plus size={16} /> {t("New course")}</Button></div>
      {s.data && <KpiRow label={t("Learning figures")}>
        <Kpi icon={<BookOpen size={18} />} label={t("Published courses")} value={s.data.published} />
        <Kpi icon={<GraduationCap size={18} />} tone="green" label={t("Completion rate")} value={s.data.completionRate != null ? `${s.data.completionRate}%` : "—"} hint={`${s.data.completed} / ${s.data.enrolled}`} />
        <Kpi icon={<TriangleAlert size={18} />} tone="red" label={t("Overdue")} value={s.data.overdue} />
        <Kpi icon={<Award size={18} />} tone="blue" label={t("Certificates this year")} value={s.data.certificatesThisYear} hint={s.data.expiringSoon ? t("{n} expire within 60 days", { n: s.data.expiringSoon }) : undefined} />
      </KpiRow>}
      <Panel>
        {!q.loading && (q.data ?? []).length === 0 ? <EmptyState title={t("No courses yet")} description={t("Build a course from short lessons (reading, video, web page or PDF) and an optional quiz, then assign it with a due date.")} />
          : <DataTable label={t("Courses")} columns={cols} rows={q.data ?? []} rowKey={(c) => c.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={add} onClose={() => setAdd(false)} side="right" title={t("New course")} footer={<><Button onClick={() => setAdd(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="nc-form">{t("Create")}</Button></>}>
        <form id="nc-form" className="stack" noValidate onSubmit={create}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Title")} htmlFor="nc-t" required error={err.title}><TextInput id="nc-t" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></Field>
          <Field label={t("Category")} htmlFor="nc-c" error={err.category}><SelectInput id="nc-c" value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })} options={Object.keys(CATEGORY).map((k) => ({ value: k, label: t(CATEGORY[k]) }))} /></Field>
          <Field label={t("Description")} htmlFor="nc-d"><textarea id="nc-d" rows={3} maxLength={2000} className="textarea" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></Field>
        </form>
      </Overlay>
    </>
  );
}

const blankLesson = { title: "", kind: "text", body: "", url: "", minutes: "5", fileName: "", fileBase64: "" };
export function CourseManager({ id }: { id: string }) {
  const run = useRun(); const q = useQuery(() => get<CourseEditorData>(`/api/hr/learning/courses/${id}`), [id]); const enr = useQuery(() => get<EnrollmentItem[]>(`/api/hr/learning/courses/${id}/enrollments`), [id]);
  const people = useQuery(() => get<Paged<DirectoryEntry>>("/api/hr/directory", { pageSize: 200 }), []); const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]), []);
  const [settings, setSettings] = useState<null | { title: string; category: string; description: string; mandatory: boolean; selfEnroll: boolean; passScore: string; certificate: boolean; validMonths: string }>(null);
  const [lesson, setLesson] = useState<LessonItem | "new" | null>(null); const [lf, setLf] = useState(blankLesson);
  const [quiz, setQuiz] = useState<AssessmentQuestion[] | null>(null);
  const [assign, setAssign] = useState(false); const [af, setAf] = useState({ who: "people", employeeIds: [] as string[], orgUnitId: "", dueDate: isoDay(new Date(Date.now() + 14 * 86400000)) });
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const saveWith = async (fn: () => Promise<CourseEditorData>, msg: string, close: () => void) => { setForm(""); const r = await run(fn, msg, { silent: true }); if (r.ok) { q.setData(r.data); close(); } else { setErr(fieldsOf(r)); setForm(r.error); } };
  return (
    <QueryBoundary q={q}>{(d) => { const c = d.course; const rows = enr.data ?? [];
      return (
        <>
          <Panel title={c.title} description={`${t(CATEGORY[c.category] ?? c.category)}${c.mandatory ? ` · ${t("Mandatory")}` : ""} · ${t("pass mark {n}%", { n: c.passScore })}${c.validMonths ? ` · ${t("certificate valid {n} months", { n: c.validMonths })}` : ""}`}
            actions={<div className="row" style={{ gap: 6, flexWrap: "wrap" }}><Badge tone={COURSE_STATUS[c.status][0]}>{t(COURSE_STATUS[c.status][1])}</Badge>
              <Button size="sm" variant="soft" onClick={() => { setErr({}); setForm(""); setSettings({ title: c.title, category: c.category, description: c.description ?? "", mandatory: c.mandatory, selfEnroll: c.selfEnroll, passScore: String(c.passScore), certificate: c.certificate, validMonths: c.validMonths?.toString() ?? "" }); }}>{t("Settings")}</Button>
              {c.status !== "published" && <Button size="sm" variant="primary" onClick={() => saveWith(() => post(`/api/hr/learning/courses/${id}/publish`), t("Course published."), () => undefined)}>{t("Publish")}</Button>}
              {c.status === "published" && <><Button size="sm" variant="primary" onClick={() => { setErr({}); setForm(""); setAssign(true); }}><Users size={14} /> {t("Assign")}</Button><Button size="sm" onClick={() => saveWith(() => post(`/api/hr/learning/courses/${id}/archive`), t("Course archived."), () => undefined)}>{t("Archive")}</Button></>}</div>}>
            {form && !settings && !lesson && !quiz && !assign && <div className="form-alert err" role="alert">{form}</div>}
            {c.description && <p>{c.description}</p>}
          </Panel>
          <Panel title={t("Lessons")} actions={<Button size="sm" onClick={() => { setErr({}); setForm(""); setLf(blankLesson); setLesson("new"); }}><Plus size={14} /> {t("Add lesson")}</Button>}>
            {d.lessons.length === 0 ? <small className="sub">{t("No lessons yet. A course needs at least one before it can be published.")}</small> : (
              <ul className="mini-list">{d.lessons.map((l) => (
                <li key={l.id}>{KIND_ICON[l.kind]}<div><strong>{l.order}. {l.title}</strong><small>{t(KIND_LABEL[l.kind])}{l.minutes ? ` · ${t("{n} min", { n: l.minutes })}` : ""}{l.url ? ` · ${l.url}` : ""}{l.fileName ? ` · ${l.fileName}` : ""}</small></div>
                  <Button size="sm" variant="soft" onClick={() => { setErr({}); setForm(""); setLf({ title: l.title, kind: l.kind, body: l.body ?? "", url: l.url ?? "", minutes: String(l.minutes), fileName: "", fileBase64: "" }); setLesson(l); }}>{t("Edit")}</Button>
                  <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={() => saveWith(() => del(`/api/hr/learning/courses/${id}/lessons/${l.id}`), t("Lesson removed."), () => undefined)}><Trash2 size={14} /></Button></li>))}</ul>)}
          </Panel>
          <Panel title={t("Quiz")} description={d.quiz.length ? t("{n} questions · pass mark {p}%", { n: d.quiz.length, p: c.passScore }) : t("No quiz: the course is complete when every lesson is done.")} actions={<Button size="sm" onClick={() => { setErr({}); setForm(""); setQuiz(d.quiz.length ? d.quiz : [{ id: "q1", text: "", options: [{ text: "", correct: true }, { text: "", correct: false }] }]); }}>{d.quiz.length ? t("Edit quiz") : t("Add quiz")}</Button>} />
          <Panel title={t("Participants")}>
            <KpiRow label={t("Participant figures")}>
              <Kpi icon={<Users size={18} />} label={t("Enrolled")} value={rows.length} />
              <Kpi icon={<CheckCircle2 size={18} />} tone="green" label={t("Completed")} value={rows.filter((r) => r.status === "completed").length} />
              <Kpi icon={<TriangleAlert size={18} />} tone="red" label={t("Overdue")} value={rows.filter((r) => r.overdue).length} />
            </KpiRow>
            <DataTable label={t("Participants")} rows={rows} rowKey={(r) => r.id} loading={enr.data === undefined && enr.loading} error={enr.error?.message ?? null} onRetry={enr.reload} columns={[
              { key: "e", header: t("Employee"), cell: (r) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo}{r.department ? ` · ${r.department}` : ""}</small></> },
              { key: "p", header: t("Progress"), cell: (r) => <div style={{ minWidth: 110 }}><ProgressBar value={r.progress} color={r.status === "completed" ? "#22c55e" : "#3b82f6"} /><small className="sub">{r.progress}%</small></div> },
              { key: "q", header: t("Quiz"), className: "hide-sm", cell: (r) => (r.quizScore != null ? `${r.quizScore}% · ${t("{n} tries", { n: r.quizAttempts })}` : "—") },
              { key: "d", header: t("Due"), cell: (r) => (r.dueDate ? <span className={r.overdue ? "neg" : undefined}>{fmtDate(r.dueDate)}</span> : "—") },
              { key: "s", header: t("Status"), cell: (r) => <Badge tone={ENR_STATUS[r.status][0]}>{t(ENR_STATUS[r.status][1])}</Badge> },
              { key: "x", header: "", cell: (r) => <div className="row" style={{ gap: 6 }}>{r.certificateNo && <Button size="sm" variant="soft" aria-label={t("Certificate")} onClick={() => download(`/api/hr/learning/certificates/${r.id}`)}><Award size={14} /></Button>}
                {(r.quizAttempts > 0 || r.status === "completed") && <Button size="sm" variant="soft" onClick={async () => { if ((await run(() => post(`/api/hr/learning/enrollments/${r.id}/reset`), t("Quiz attempts reset."))).ok) enr.reload(); }}>{t("Reset quiz")}</Button>}</div> },
            ] as Column<EnrollmentItem>[]} />
          </Panel>

          <Overlay open={settings !== null} onClose={() => setSettings(null)} side="right" title={t("Course settings")} footer={<><Button onClick={() => setSettings(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cs-form">{t("Save")}</Button></>}>
            {settings && <form id="cs-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); saveWith(() => put(`/api/hr/learning/courses/${id}`, { ...settings, passScore: Number(settings.passScore || 0), validMonths: settings.validMonths ? Number(settings.validMonths) : null }), t("Course saved."), () => setSettings(null)); }}>
              {form && <div className="form-alert err" role="alert">{form}</div>}
              <Field label={t("Title")} htmlFor="cs-t" required error={err.title}><TextInput id="cs-t" value={settings.title} onChange={(e) => setSettings({ ...settings, title: e.target.value })} /></Field>
              <Field label={t("Category")} htmlFor="cs-c"><SelectInput id="cs-c" value={settings.category} onChange={(e) => setSettings({ ...settings, category: e.target.value })} options={Object.keys(CATEGORY).map((k) => ({ value: k, label: t(CATEGORY[k]) }))} /></Field>
              <Field label={t("Description")} htmlFor="cs-d"><textarea id="cs-d" rows={3} maxLength={2000} className="textarea" value={settings.description} onChange={(e) => setSettings({ ...settings, description: e.target.value })} /></Field>
              <label className="check"><Checkbox checked={settings.mandatory} onChange={(e) => setSettings({ ...settings, mandatory: e.target.checked })} /> {t("Mandatory course")}</label>
              <label className="check"><Checkbox checked={settings.selfEnroll} onChange={(e) => setSettings({ ...settings, selfEnroll: e.target.checked })} /> {t("Anyone may join it on their own")}</label>
              <label className="check"><Checkbox checked={settings.certificate} onChange={(e) => setSettings({ ...settings, certificate: e.target.checked })} /> {t("Give a certificate on completion")}</label>
              <div className="fgrid">
                <Field label={t("Pass mark (%)")} htmlFor="cs-p" error={err.passScore}><TextInput id="cs-p" inputMode="numeric" value={settings.passScore} onChange={(e) => setSettings({ ...settings, passScore: e.target.value })} /></Field>
                <Field label={t("Certificate valid (months)")} htmlFor="cs-v" error={err.validMonths} hint={t("Empty = no expiry.")}><TextInput id="cs-v" inputMode="numeric" value={settings.validMonths} onChange={(e) => setSettings({ ...settings, validMonths: e.target.value })} /></Field>
              </div>
            </form>}
          </Overlay>
          <Overlay open={lesson !== null} onClose={() => setLesson(null)} side="right" title={lesson === "new" ? t("Add lesson") : t("Edit lesson")} footer={<><Button onClick={() => setLesson(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="ls-form">{t("Save")}</Button></>}>
            <form id="ls-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); const body = { ...lf, minutes: Number(lf.minutes || 0), fileBase64: lf.fileBase64 || undefined }; saveWith(() => (lesson === "new" ? post(`/api/hr/learning/courses/${id}/lessons`, body) : put(`/api/hr/learning/courses/${id}/lessons/${(lesson as LessonItem).id}`, body)), t("Lesson saved."), () => setLesson(null)); }}>
              {form && <div className="form-alert err" role="alert">{form}</div>}
              <Field label={t("Title")} htmlFor="ls-t" required error={err.title}><TextInput id="ls-t" value={lf.title} onChange={(e) => setLf({ ...lf, title: e.target.value })} /></Field>
              <div className="fgrid">
                <Field label={t("Type")} htmlFor="ls-k" error={err.kind}><SelectInput id="ls-k" value={lf.kind} onChange={(e) => setLf({ ...lf, kind: e.target.value })} options={Object.keys(KIND_LABEL).map((k) => ({ value: k, label: t(KIND_LABEL[k]) }))} /></Field>
                <Field label={t("Minutes")} htmlFor="ls-m" error={err.minutes}><TextInput id="ls-m" inputMode="numeric" value={lf.minutes} onChange={(e) => setLf({ ...lf, minutes: e.target.value })} /></Field>
              </div>
              {(lf.kind === "video" || lf.kind === "link") && <Field label={t("Address (https://)")} htmlFor="ls-u" required error={err.url}><TextInput id="ls-u" value={lf.url} onChange={(e) => setLf({ ...lf, url: e.target.value })} placeholder="https://" /></Field>}
              {lf.kind === "file" && <Field label={t("PDF (up to 10 MB)")} htmlFor="ls-f" error={err.file}><input id="ls-f" type="file" accept="application/pdf" onChange={async (e) => { const file = e.target.files?.[0]; if (file) setLf({ ...lf, fileName: file.name, fileBase64: await toBase64(file) }); }} /></Field>}
              <Field label={lf.kind === "text" ? t("Content") : t("Notes for the learner")} htmlFor="ls-b" error={err.body}><textarea id="ls-b" rows={lf.kind === "text" ? 12 : 4} maxLength={20000} className="textarea" value={lf.body} onChange={(e) => setLf({ ...lf, body: e.target.value })} /></Field>
            </form>
          </Overlay>
          <Overlay open={quiz !== null} onClose={() => setQuiz(null)} side="right" title={t("Quiz")} footer={<><Button onClick={() => setQuiz(null)}>{t("Cancel")}</Button><Button variant="primary" onClick={() => saveWith(() => put(`/api/hr/learning/courses/${id}`, { quiz }), t("Quiz saved."), () => setQuiz(null))}>{t("Save")}</Button></>}>
            {quiz && <div className="stack">
              {form && <div className="form-alert err" role="alert">{form}</div>}
              {quiz.map((qq, i) => {
                const setQ = (nq: AssessmentQuestion) => setQuiz(quiz.map((x, j) => (j === i ? nq : x)));
                return (
                  <fieldset key={i} className="stack q-edit"><legend>{t("Question {n}", { n: i + 1 })}</legend>
                    <TextInput aria-label={t("Question {n}", { n: i + 1 })} value={qq.text} onChange={(e) => setQ({ ...qq, text: e.target.value })} />
                    {qq.options.map((o, k) => (
                      <div key={k} className="row" style={{ gap: 6 }}>
                        <input type="radio" name={`qc-${i}`} aria-label={t("Correct answer")} checked={o.correct === true} onChange={() => setQ({ ...qq, options: qq.options.map((x, m) => ({ ...x, correct: m === k })) })} />
                        <TextInput aria-label={t("Option {n}", { n: k + 1 })} value={o.text} onChange={(e) => setQ({ ...qq, options: qq.options.map((x, m) => (m === k ? { ...x, text: e.target.value } : x)) })} />
                        {qq.options.length > 2 && <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={() => setQ({ ...qq, options: qq.options.filter((_, m) => m !== k) })}><Trash2 size={14} /></Button>}
                      </div>))}
                    <div className="row" style={{ gap: 6 }}>{qq.options.length < 6 && <Button size="sm" onClick={() => setQ({ ...qq, options: [...qq.options, { text: "", correct: false }] })}><Plus size={14} /> {t("Option")}</Button>}
                      <Button size="sm" variant="danger-outline" onClick={() => setQuiz(quiz.filter((_, j) => j !== i))}>{t("Remove question")}</Button></div>
                  </fieldset>);
              })}
              <Button size="sm" onClick={() => setQuiz([...quiz, { id: `q${quiz.length + 1}`, text: "", options: [{ text: "", correct: true }, { text: "", correct: false }] }])}><Plus size={14} /> {t("Add question")}</Button>
            </div>}
          </Overlay>
          <Overlay open={assign} onClose={() => setAssign(false)} side="right" title={t("Assign course")} footer={<><Button onClick={() => setAssign(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="asg-form">{t("Assign")}</Button></>}>
            <form id="asg-form" className="stack" noValidate onSubmit={async (e) => {
              e.preventDefault(); setForm("");
              const body = { dueDate: af.dueDate || null, everyone: af.who === "everyone", orgUnitId: af.who === "unit" ? af.orgUnitId || null : null, employeeIds: af.who === "people" ? af.employeeIds : null };
              const r = await run(() => post<{ added: number }>(`/api/hr/learning/courses/${id}/assign`, body), undefined, { silent: true });
              if (r.ok) { setAssign(false); await run(async () => undefined, t("{n} people added. They were notified.", { n: r.data.added })); enr.reload(); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); } }}>
              {form && <div className="form-alert err" role="alert">{form}</div>}
              <Field label={t("Who")} htmlFor="asg-who"><SelectInput id="asg-who" value={af.who} onChange={(e) => setAf({ ...af, who: e.target.value })} options={[{ value: "people", label: t("Chosen people") }, { value: "unit", label: t("A department") }, { value: "everyone", label: t("Everyone") }]} /></Field>
              {af.who === "people" && <Field label={t("People")} htmlFor="asg-p" error={err.employeeIds}><select id="asg-p" multiple className="input" style={{ minHeight: 160 }} value={af.employeeIds} onChange={(e) => setAf({ ...af, employeeIds: Array.from(e.target.selectedOptions).map((o) => o.value) })}>
                {(people.data?.data ?? []).map((p) => <option key={p.employeeId} value={p.employeeId}>{p.fullName}{p.department ? ` · ${p.department}` : ""}</option>)}</select></Field>}
              {af.who === "unit" && <Field label={t("Department")} htmlFor="asg-u"><SelectInput id="asg-u" value={af.orgUnitId} onChange={(e) => setAf({ ...af, orgUnitId: e.target.value })} options={[{ value: "", label: t("Choose…") }, ...(units.data ?? []).filter((u) => !u.archived).map((u) => ({ value: u.id, label: u.name }))]} /></Field>}
              <Field label={t("Due date")} htmlFor="asg-d" error={err.dueDate}><TextInput id="asg-d" type="date" value={af.dueDate} onChange={(e) => setAf({ ...af, dueDate: e.target.value })} /></Field>
            </form>
          </Overlay>
        </>);
    }}</QueryBoundary>
  );
}

/** A manager's direct reports and their courses. */
export function TeamLearning() {
  const q = useQuery(() => get<TeamLearningRow[]>("/api/hr/learning/team"), []); const rows = q.data ?? [];
  const cols: Column<TeamLearningRow>[] = [
    { key: "e", header: t("Employee"), cell: (r) => <b>{r.employeeName}</b> }, { key: "c", header: t("Course"), cell: (r) => r.course },
    { key: "p", header: t("Progress"), cell: (r) => <div style={{ minWidth: 110 }}><ProgressBar value={r.progress} color={r.status === "completed" ? "#22c55e" : "#3b82f6"} /><small className="sub">{r.progress}%</small></div> },
    { key: "d", header: t("Due"), cell: (r) => (r.dueDate ? <span className={r.overdue ? "neg" : undefined}>{fmtDate(r.dueDate)}</span> : "—") },
    { key: "s", header: t("Status"), cell: (r) => <Badge tone={r.overdue ? "red" : ENR_STATUS[r.status]?.[0] ?? "gray"}>{r.overdue ? t("Overdue") : t(ENR_STATUS[r.status]?.[1] ?? r.status)}</Badge> },
  ];
  return (
    <>
      <KpiRow label={t("Team learning")}>
        <Kpi icon={<BookOpen size={18} />} label={t("Courses in progress")} value={rows.filter((r) => r.status !== "completed").length} />
        <Kpi icon={<CheckCircle2 size={18} />} tone="green" label={t("Completed")} value={rows.filter((r) => r.status === "completed").length} />
        <Kpi icon={<TriangleAlert size={18} />} tone="red" label={t("Overdue")} value={rows.filter((r) => r.overdue).length} />
      </KpiRow>
      <Panel>
        {!q.loading && rows.length === 0 ? <EmptyState title={t("No courses in your team")} description={t("Courses assigned to the people who report to you appear here.")} />
          : <DataTable label={t("Team learning")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
    </>
  );
}
