"use client";

import { useState } from "react";
import Link from "next/link";
import { Briefcase, CalendarClock, CheckCircle2, ClipboardCheck, Copy, Download, FileText, Gauge, Plus, Send, Star, Trash2, UserCheck, Users, XCircle } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Tabs } from "@/components/ui/tabs";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { QueryBoundary } from "@/components/query-boundary";
import { TYPE_OPTIONS, typeLabel } from "@/components/hr/employee-status";
import { api, del, get, post, put } from "@/lib/api/client";
import type { ApplicationDetail, ApplicationRow, AssessmentQuestion, AssessmentTemplate, DirectoryEntry, InterviewerTask, JobPosting, ManpowerRow, OrgUnitItem, Paged, Requisition, RecruitmentSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate, fmtDateTime, idr } from "@/lib/format";
import { download } from "@/lib/download";
import { toBase64 } from "@/lib/file";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const fieldsOf = (r: { fields?: Record<string, string> }) => r.fields ?? {};
const thisYear = new Date().getFullYear();
const isoDay = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const plusDays = (n: number) => isoDay(new Date(Date.now() + n * 86400000));
export const REQ_STATUS: Record<Requisition["status"], [BadgeTone, string]> = { pending: ["orange", "Pending"], approved: ["green", "Approved"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"], filled: ["blue", "Filled"] };
export const POSTING_STATUS: Record<JobPosting["status"], [BadgeTone, string]> = { draft: ["gray", "Draft"], open: ["green", "Open"], closed: ["blue", "Closed"] };
const APP_STATUS: Record<ApplicationRow["status"], [BadgeTone, string]> = { active: ["blue", "In process"], rejected: ["red", "Rejected"], withdrawn: ["gray", "Withdrawn"], hired: ["green", "Hired"] };
const OFFER_STATUS: Record<string, [BadgeTone, string]> = { draft: ["gray", "Draft"], sent: ["orange", "Sent"], accepted: ["green", "Accepted"], declined: ["red", "Declined"], withdrawn: ["gray", "Withdrawn"] };
const STAGE_LABEL: Record<string, string> = { applied: "Applied", screening: "Screening", assessment: "Assessment", interview: "Interview", offer: "Offer", hired: "Hired" };
export const stageLabel = (s: string) => t(STAGE_LABEL[s] ?? s.replace(/_/g, " ").replace(/^./, (c) => c.toUpperCase()));
const SOURCE_LABEL: Record<string, string> = { career_page: "Career page", added_by_hr: "Added by HR", referral: "Referral", job_portal: "Job portal", linkedin: "LinkedIn", campus: "Campus" };
const sourceLabel = (s: string) => t(SOURCE_LABEL[s] ?? s);
const REC: Record<string, string> = { hire: "Hire", maybe: "Maybe", no_hire: "Do not hire" };
const salary = (min: number | null, max: number | null) => (min == null && max == null ? "—" : min != null && max != null ? `${idr(min)} – ${idr(max)}` : idr(min ?? max));
const useUnits = () => useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]), []);
const unitOptions = (u: OrgUnitItem[] | undefined, none: string) => [{ value: "", label: none }, ...(u ?? []).filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))];

// ============================================================ overview: postings
export function RecruitmentSummaryRow() {
  const q = useQuery(() => get<RecruitmentSummary>("/api/hr/recruitment/summary"), []);
  return <QueryBoundary q={q} bare quiet>{(s) => (
    <KpiRow label={t("Recruitment figures")}>
      <Kpi icon={<Briefcase size={18} />} label={t("Open postings")} value={s.openPostings} hint={t("{n} approved hiring requests", { n: s.openRequisitions })} />
      <Kpi icon={<Users size={18} />} tone="blue" label={t("Applicants in process")} value={s.activeApplicants} />
      <Kpi icon={<UserCheck size={18} />} tone="green" label={t("Hired this year")} value={s.hiredThisYear} hint={s.avgDaysToHire != null ? t("{n} days on average", { n: s.avgDaysToHire }) : undefined} />
      <Kpi icon={<Gauge size={18} />} tone="orange" label={t("Offers accepted")} value={s.offerAcceptance != null ? `${s.offerAcceptance}%` : "—"} hint={s.pendingRequisitions ? t("{n} hiring requests waiting", { n: s.pendingRequisitions }) : undefined} />
    </KpiRow>)}</QueryBoundary>;
}

const blankPosting = { requisitionId: "", title: "", orgUnitId: "", location: "", type: "permanent", description: "", requirements: "", salaryMin: "", salaryMax: "", showSalary: false, stages: "screening, assessment, interview, offer", closesOn: "" };

export function PostingsBoard() {
  const run = useRun(); const q = useQuery(() => get<JobPosting[]>("/api/hr/recruitment/postings"), []); const units = useUnits();
  const reqs = useQuery(() => get<Requisition[]>("/api/hr/recruitment/requisitions", { scope: "all" }), []);
  const slug = useQuery(() => get<{ slug: string }>("/api/hr/recruitment/career-link").catch(() => null), []);
  const [edit, setEdit] = useState<JobPosting | "new" | null>(null); const [f, setF] = useState(blankPosting); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (p: JobPosting | "new") => {
    setErr({}); setForm(""); setEdit(p);
    setF(p === "new" ? blankPosting : { requisitionId: p.requisitionId ?? "", title: p.title, orgUnitId: p.orgUnitId ?? "", location: p.location ?? "", type: p.type, description: p.description, requirements: p.requirements ?? "", salaryMin: p.salaryMin?.toString() ?? "", salaryMax: p.salaryMax?.toString() ?? "", showSalary: p.showSalary, stages: p.stages.join(", "), closesOn: p.closesOn ?? "" });
  };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { ...f, requisitionId: f.requisitionId || null, orgUnitId: f.orgUnitId || null, salaryMin: f.salaryMin ? Number(f.salaryMin) : null, salaryMax: f.salaryMax ? Number(f.salaryMax) : null, closesOn: f.closesOn || null, stages: f.stages.split(",").map((s) => s.trim()).filter(Boolean) };
    const r = await run(() => (edit === "new" ? post("/api/hr/recruitment/postings", body) : put(`/api/hr/recruitment/postings/${(edit as JobPosting).id}`, body)), t("Job posting saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const publicUrl = (p: JobPosting) => (slug.data ? `${window.location.origin}/careers/${slug.data.slug}/${p.publicId}` : "");
  const copy = async (p: JobPosting) => { const u = publicUrl(p); if (!u) return; await navigator.clipboard?.writeText(u).catch(() => undefined); await run(async () => undefined, t("Link copied: {url}", { url: u })); };
  const setStatus = async (p: JobPosting, openIt: boolean) => { if ((await run(() => post(`/api/hr/recruitment/postings/${p.id}/${openIt ? "open" : "close"}`), openIt ? t("The posting is live on the career page.") : t("Posting closed."))).ok) q.reload(); };
  const cols: Column<JobPosting>[] = [
    { key: "t", header: t("Job"), cell: (p) => <><Link href={`/hr/recruitment/${p.id}`}><b>{p.title}</b></Link><small className="sub">{[p.unit, p.location, typeLabel(p.type), p.requisitionNumber].filter(Boolean).join(" · ")}</small></> },
    { key: "a", header: t("Applicants"), cell: (p) => <>{p.applicants}<small className="sub">{t("{n} in process", { n: p.active })}</small></> },
    { key: "p", header: t("Pipeline"), className: "hide-sm", cell: (p) => <div className="chips">{Object.entries(p.byStage).filter(([, n]) => n > 0).map(([s, n]) => <code key={s}>{stageLabel(s)} {n}</code>)}</div> },
    { key: "s", header: t("Status"), cell: (p) => <Badge tone={POSTING_STATUS[p.status][0]}>{t(POSTING_STATUS[p.status][1])}</Badge> },
    { key: "c", header: t("Closes"), className: "hide-sm", cell: (p) => fmtDate(p.closesOn) },
    { key: "x", header: "", cell: (p) => <div className="row" style={{ gap: 6 }}>
      <Link className="btn sm" href={`/hr/recruitment/${p.id}`}>{t("Pipeline")}</Link>
      {p.status === "open" && slug.data && <Button size="sm" variant="soft" aria-label={t("Copy public link")} onClick={() => copy(p)}><Copy size={14} /></Button>}
      {p.status !== "open" ? <Button size="sm" variant="primary" onClick={() => setStatus(p, true)}>{t("Publish")}</Button> : <Button size="sm" onClick={() => setStatus(p, false)}>{t("Close")}</Button>}
      <Button size="sm" variant="soft" onClick={() => open(p)}>{t("Edit")}</Button></div> },
  ];
  const approved = (reqs.data ?? []).filter((r) => r.status === "approved");
  return (
    <>
      <div className="row" style={{ justifyContent: "space-between", flexWrap: "wrap", gap: 8, marginBottom: 12 }}>
        <span>{slug.data && <small className="sub">{t("Career page")}: <a href={`/careers/${slug.data.slug}`} target="_blank" rel="noreferrer">/careers/{slug.data.slug}</a></small>}</span>
        <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("New job posting")}</Button>
      </div>
      <RecruitmentSummaryRow />
      <Panel>
        {!q.loading && (q.data ?? []).length === 0 ? <EmptyState title={t("No job postings yet")} description={t("Open a posting from an approved hiring request, publish it on the career page and follow applicants through your stages.")} />
          : <DataTable label={t("Job postings")} columns={cols} rows={q.data ?? []} rowKey={(p) => p.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("New job posting") : t("Edit job posting")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="jp-form">{t("Save")}</Button></>}>
        <form id="jp-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {edit === "new" && <Field label={t("Hiring request")} htmlFor="jp-req" error={err.requisitionId} hint={t("The posting takes its title, department and type from the request.")}>
            <SelectInput id="jp-req" value={f.requisitionId} onChange={(e) => { const r = approved.find((x) => x.id === e.target.value); setF({ ...f, requisitionId: e.target.value, title: r?.jobTitle ?? f.title, orgUnitId: r?.orgUnitId ?? f.orgUnitId, type: r?.type ?? f.type, salaryMin: r?.salaryMin?.toString() ?? f.salaryMin, salaryMax: r?.salaryMax?.toString() ?? f.salaryMax }); }}
              options={[{ value: "", label: t("None (standalone posting)") }, ...approved.map((r) => ({ value: r.id, label: `${r.number} · ${r.jobTitle} (${r.openings - r.hired})` }))]} /></Field>}
          <Field label={t("Job title")} htmlFor="jp-title" required error={err.title}><TextInput id="jp-title" value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></Field>
          <div className="fgrid">
            <Field label={t("Department")} htmlFor="jp-unit" error={err.orgUnitId}><SelectInput id="jp-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={unitOptions(units.data, "—")} /></Field>
            <Field label={t("Location")} htmlFor="jp-loc"><TextInput id="jp-loc" value={f.location} onChange={(e) => setF({ ...f, location: e.target.value })} placeholder={t("e.g. Jakarta (hybrid)")} /></Field>
            <Field label={t("Employment type")} htmlFor="jp-type" error={err.type}><SelectInput id="jp-type" value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })} options={TYPE_OPTIONS} /></Field>
            <Field label={t("Applications close")} htmlFor="jp-close" error={err.closesOn}><TextInput id="jp-close" type="date" value={f.closesOn} onChange={(e) => setF({ ...f, closesOn: e.target.value })} /></Field>
            <Field label={t("Salary from (IDR)")} htmlFor="jp-min"><TextInput id="jp-min" inputMode="numeric" value={f.salaryMin} onChange={(e) => setF({ ...f, salaryMin: e.target.value })} /></Field>
            <Field label={t("Salary to (IDR)")} htmlFor="jp-max" error={err.salaryMax}><TextInput id="jp-max" inputMode="numeric" value={f.salaryMax} onChange={(e) => setF({ ...f, salaryMax: e.target.value })} /></Field>
          </div>
          <label className="check"><Checkbox checked={f.showSalary} onChange={(e) => setF({ ...f, showSalary: e.target.checked })} /> {t("Show the salary range on the career page")}</label>
          <Field label={t("About the job")} htmlFor="jp-desc" required error={err.description}><textarea id="jp-desc" rows={6} maxLength={8000} className="textarea" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></Field>
          <Field label={t("Requirements")} htmlFor="jp-reqs"><textarea id="jp-reqs" rows={4} maxLength={4000} className="textarea" value={f.requirements} onChange={(e) => setF({ ...f, requirements: e.target.value })} /></Field>
          <Field label={t("Stages, in order")} htmlFor="jp-stages" error={err.stages} hint={t("Between Applied and Hired, separated by commas, e.g. screening, assessment, interview, offer.")}><TextInput id="jp-stages" value={f.stages} onChange={(e) => setF({ ...f, stages: e.target.value })} /></Field>
        </form>
      </Overlay>
    </>
  );
}

// ============================================================ one posting: the pipeline
export function Pipeline({ id }: { id: string }) {
  const run = useRun(); const postings = useQuery(() => get<JobPosting[]>("/api/hr/recruitment/postings"), []); const q = useQuery(() => get<ApplicationRow[]>(`/api/hr/recruitment/postings/${id}/applications`), [id]);
  const posting = postings.data?.find((p) => p.id === id);
  const [view, setView] = useState<"board" | "list">("board"); const [openId, setOpenId] = useState<string | null>(null);
  const [add, setAdd] = useState(false); const [f, setF] = useState({ fullName: "", email: "", phone: "", source: "referral", cvName: "", cvBase64: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function addCandidate(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/recruitment/postings/${id}/applications`, { ...f, cvBase64: f.cvBase64 || undefined }), t("Candidate added."), { silent: true });
    if (r.ok) { setAdd(false); q.reload(); postings.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const rows = q.data ?? []; const stages = ["applied", ...(posting?.stages ?? []), "hired"];
  const active = rows.filter((a) => a.status === "active" || a.status === "hired");
  const cols: Column<ApplicationRow>[] = [
    { key: "n", header: t("Candidate"), cell: (a) => <button type="button" className="link-btn" onClick={() => setOpenId(a.id)}><b>{a.name}</b></button> },
    { key: "s", header: t("Stage"), cell: (a) => stageLabel(a.stage) }, { key: "st", header: t("Status"), cell: (a) => <Badge tone={APP_STATUS[a.status][0]}>{t(APP_STATUS[a.status][1])}</Badge> },
    { key: "r", header: t("Rating"), className: "hide-sm", cell: (a) => (a.rating ? "★".repeat(a.rating) : "—") }, { key: "t", header: t("Test"), className: "hide-sm", cell: (a) => a.testSummary ?? "—" },
    { key: "src", header: t("Source"), className: "hide-sm", cell: (a) => sourceLabel(a.source) }, { key: "d", header: t("Applied"), className: "hide-sm", cell: (a) => fmtDate(a.appliedAt) },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "space-between", flexWrap: "wrap", gap: 8, marginBottom: 12 }}>
        <div>{posting && <><b>{posting.title}</b> <Badge tone={POSTING_STATUS[posting.status][0]}>{t(POSTING_STATUS[posting.status][1])}</Badge> <small className="sub">{[posting.unit, posting.location].filter(Boolean).join(" · ")}</small></>}</div>
        <div className="row" style={{ gap: 8 }}>
          <Tabs variant="pill" items={[{ key: "board" as const, label: t("Board") }, { key: "list" as const, label: t("List") }]} value={view} onChange={setView} label={t("View")} />
          {posting?.status !== "closed" && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ fullName: "", email: "", phone: "", source: "referral", cvName: "", cvBase64: "" }); setAdd(true); }}><Plus size={16} /> {t("Add candidate")}</Button>}
        </div>
      </div>
      <KpiRow label={t("Pipeline figures")}>
        <Kpi icon={<Users size={18} />} label={t("Applicants")} value={rows.length} />
        <Kpi icon={<ClipboardCheck size={18} />} tone="blue" label={t("In process")} value={rows.filter((a) => a.status === "active").length} />
        <Kpi icon={<XCircle size={18} />} tone="red" label={t("Rejected")} value={rows.filter((a) => a.status === "rejected").length} />
        <Kpi icon={<UserCheck size={18} />} tone="green" label={t("Hired")} value={rows.filter((a) => a.status === "hired").length} />
      </KpiRow>
      {view === "board" ? (
        <div className="kanban" role="list" aria-label={t("Pipeline")}>
          {stages.map((s) => { const inStage = active.filter((a) => a.stage === s); return (
            <section key={s} className="kanban-col" role="listitem" aria-label={stageLabel(s)}>
              <header><b>{stageLabel(s)}</b><span>{inStage.length}</span></header>
              {inStage.map((a) => (
                <button key={a.id} type="button" className="kanban-card" onClick={() => setOpenId(a.id)}>
                  <b>{a.name}</b><small className="sub">{[a.rating ? "★".repeat(a.rating) : null, a.testSummary, sourceLabel(a.source)].filter(Boolean).join(" · ")}</small>
                </button>))}
            </section>); })}
        </div>)
        : <Panel><DataTable label={t("Applicants")} columns={cols} rows={rows} rowKey={(a) => a.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} /></Panel>}
      {!q.loading && rows.length === 0 && <Panel><EmptyState title={t("No applicants yet")} description={t("Publish the posting and share the career page link, or add candidates you found yourself.")} /></Panel>}
      {openId && <ApplicationDrawer id={openId} onClose={() => setOpenId(null)} onChanged={() => { q.reload(); postings.reload(); }} />}
      <Overlay open={add} onClose={() => setAdd(false)} side="right" title={t("Add candidate")} footer={<><Button onClick={() => setAdd(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="ac-form">{t("Add")}</Button></>}>
        <form id="ac-form" className="stack" noValidate onSubmit={addCandidate}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Full name")} htmlFor="ac-name" required error={err.fullName}><TextInput id="ac-name" value={f.fullName} onChange={(e) => setF({ ...f, fullName: e.target.value })} /></Field>
          <Field label={t("Email")} htmlFor="ac-mail" required error={err.email}><TextInput id="ac-mail" type="email" value={f.email} onChange={(e) => setF({ ...f, email: e.target.value })} /></Field>
          <Field label={t("Phone")} htmlFor="ac-phone"><TextInput id="ac-phone" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} /></Field>
          <Field label={t("Source")} htmlFor="ac-src"><SelectInput id="ac-src" value={f.source} onChange={(e) => setF({ ...f, source: e.target.value })} options={["referral", "job_portal", "linkedin", "campus", "added_by_hr"].map((s) => ({ value: s, label: sourceLabel(s) }))} /></Field>
          <Field label={t("CV (PDF)")} htmlFor="ac-cv" error={err.cv}><input id="ac-cv" type="file" accept="application/pdf" onChange={async (e) => { const file = e.target.files?.[0]; if (!file) return; setF({ ...f, cvName: file.name, cvBase64: await toBase64(file) }); }} /></Field>
          <small className="sub">{t("Only add people who agreed that you keep their data for this recruitment.")}</small>
        </form>
      </Overlay>
    </>
  );
}


type Panel2 = null | "interview" | "test" | "offer" | "reject" | "hire";
function ApplicationDrawer({ id, onClose, onChanged }: { id: string; onClose: () => void; onChanged: () => void }) {
  const run = useRun(); const { can } = useAuth(); const q = useQuery(() => get<ApplicationDetail>(`/api/hr/recruitment/applications/${id}`), [id]);
  const [panel, setPanel] = useState<Panel2>(null); const [note, setNote] = useState(""); const [form, setForm] = useState(""); const [err, setErr] = useState<Record<string, string>>({});
  const people = useQuery(() => get<Paged<DirectoryEntry>>("/api/hr/directory", { pageSize: 200 }), []); const tests = useQuery(() => get<AssessmentTemplate[]>("/api/hr/recruitment/assessments"), []); const units = useUnits();
  const [iv, setIv] = useState({ date: plusDays(2), time: "10:00", duration: "60", location: "", interviewers: [] as string[] });
  const [ts, setTs] = useState({ templateId: "", days: "7" }); const [link, setLink] = useState("");
  const [of, setOf] = useState({ monthlySalary: "", startDate: plusDays(30), expiresOn: plusDays(7), type: "permanent", contractEnd: "", terms: "" });
  const [rj, setRj] = useState({ reason: "", notify: true }); const [hf, setHf] = useState({ employeeNo: "", orgUnitId: "", managerId: "", workEmail: "" });
  const patch = async (body: object, msg: string) => { setForm(""); const r = await run(() => api<ApplicationDetail>(`/api/hr/recruitment/applications/${id}`, { method: "PATCH", body }), msg, { silent: true }); if (r.ok) { q.reload(); onChanged(); setPanel(null); setNote(""); } else { setErr(fieldsOf(r)); setForm(r.error); } return r.ok; };
  const act = async (fn: () => Promise<unknown>, msg: string, after?: (v: unknown) => void) => { setForm(""); setErr({}); const r = await run(fn, msg, { silent: true }); if (r.ok) { after?.(r.data); q.reload(); onChanged(); } else { setErr(fieldsOf(r)); setForm(r.error); } return r.ok; };
  return (
    <Overlay open onClose={onClose} side="right" title={q.data?.application.name ?? t("Applicant")}>
      <QueryBoundary q={q}>{(d) => { const a = d.application; const live = a.status === "active"; const accepted = d.offers.some((o) => o.status === "accepted"); const openOffer = d.offers.find((o) => o.status === "draft" || o.status === "sent");
        return (
          <div className="stack">
            {form && <div className="form-alert err" role="alert">{form}</div>}
            <div><Badge tone={APP_STATUS[a.status][0]}>{t(APP_STATUS[a.status][1])}</Badge> <small className="sub">{a.email}{a.phone ? ` · ${a.phone}` : ""} · {sourceLabel(a.source)} · {t("applied {date}", { date: fmtDate(a.appliedAt) })}</small></div>
            <div className="row" style={{ gap: 6, flexWrap: "wrap" }}>
              {a.hasCv && <Button size="sm" onClick={() => download(`/api/hr/recruitment/applications/${id}/cv`)}><Download size={14} /> {t("CV")}</Button>}
              {live && <Button size="sm" onClick={() => setPanel("interview")}><CalendarClock size={14} /> {t("Schedule interview")}</Button>}
              {live && <Button size="sm" onClick={() => { setLink(""); setPanel("test"); }}><ClipboardCheck size={14} /> {t("Send test")}</Button>}
              {live && !openOffer && !accepted && <Button size="sm" onClick={() => setPanel("offer")}><FileText size={14} /> {t("Make offer")}</Button>}
              {live && accepted && can("employees.create") && <Button size="sm" variant="primary" onClick={() => setPanel("hire")}><UserCheck size={14} /> {t("Hire")}</Button>}
              {live && <Button size="sm" variant="danger-outline" onClick={() => setPanel("reject")}>{t("Reject")}</Button>}
            </div>
            {live && <div className="fgrid">
              <Field label={t("Stage")} htmlFor="ap-stage"><SelectInput id="ap-stage" value={a.stage} onChange={(e) => patch({ stage: e.target.value }, t("Moved to {stage}.", { stage: stageLabel(e.target.value) }))} options={d.stages.filter((s) => s !== "hired").map((s) => ({ value: s, label: stageLabel(s) }))} /></Field>
              <Field label={t("Your rating")} htmlFor="ap-rate"><SelectInput id="ap-rate" value={a.rating?.toString() ?? ""} onChange={(e) => e.target.value && patch({ rating: Number(e.target.value) }, t("Rating saved."))} options={[{ value: "", label: "—" }, ...[1, 2, 3, 4, 5].map((n) => ({ value: String(n), label: "★".repeat(n) }))]} /></Field>
            </div>}
            {d.hiredEmployeeId && <Link className="btn sm" href={`/hr/employees/${d.hiredEmployeeId}`}>{t("Open employee record")}</Link>}
            {d.rejectReason && <small className="sub">{t("Reason")}: {d.rejectReason}</small>}
            {d.coverLetter && <details><summary>{t("Cover letter")}</summary><p style={{ whiteSpace: "pre-wrap" }}>{d.coverLetter}</p></details>}

            {panel === "interview" && <Panel title={t("Schedule interview")}>
              <div className="stack">
                <div className="fgrid">
                  <Field label={t("Date")} htmlFor="iv-d" error={err.at}><TextInput id="iv-d" type="date" value={iv.date} onChange={(e) => setIv({ ...iv, date: e.target.value })} /></Field>
                  <Field label={t("Time (WIB)")} htmlFor="iv-t"><TextInput id="iv-t" type="time" value={iv.time} onChange={(e) => setIv({ ...iv, time: e.target.value })} /></Field>
                  <Field label={t("Minutes")} htmlFor="iv-m" error={err.durationMinutes}><TextInput id="iv-m" inputMode="numeric" value={iv.duration} onChange={(e) => setIv({ ...iv, duration: e.target.value })} /></Field>
                  <Field label={t("Place or video link")} htmlFor="iv-l"><TextInput id="iv-l" value={iv.location} onChange={(e) => setIv({ ...iv, location: e.target.value })} /></Field>
                </div>
                <Field label={t("Interviewers")} htmlFor="iv-who" error={err.interviewerIds}><select id="iv-who" multiple className="input" style={{ minHeight: 120 }} value={iv.interviewers} onChange={(e) => setIv({ ...iv, interviewers: Array.from(e.target.selectedOptions).map((o) => o.value) })}>
                  {(people.data?.data ?? []).map((p) => <option key={p.employeeId} value={p.employeeId}>{p.fullName}{p.jobTitle ? ` · ${p.jobTitle}` : ""}</option>)}</select></Field>
                <div className="row" style={{ gap: 8, justifyContent: "flex-end" }}><Button onClick={() => setPanel(null)}>{t("Cancel")}</Button>
                  <Button variant="primary" onClick={() => act(() => post(`/api/hr/recruitment/applications/${id}/interviews`, { at: new Date(`${iv.date}T${iv.time}:00+07:00`).toISOString(), durationMinutes: Number(iv.duration), location: iv.location, interviewerIds: iv.interviewers }), t("Interview scheduled. The candidate and interviewers were told."), () => setPanel(null))}>{t("Schedule")}</Button></div>
              </div></Panel>}
            {panel === "test" && <Panel title={t("Send test")}>
              <div className="stack">
                {link ? <><p>{t("Sent by email. You can also share this personal link:")}</p><code style={{ wordBreak: "break-all" }}>{link}</code></> : <>
                  <Field label={t("Test")} htmlFor="ts-t" error={err.templateId}><SelectInput id="ts-t" value={ts.templateId || tests.data?.find((x) => !x.archived)?.id || ""} onChange={(e) => setTs({ ...ts, templateId: e.target.value })} options={(tests.data ?? []).filter((x) => !x.archived).map((x) => ({ value: x.id, label: x.name }))} /></Field>
                  {(tests.data ?? []).length === 0 && <small className="sub">{t("Create a test first under Assessments.")}</small>}
                  <Field label={t("Days to complete")} htmlFor="ts-d" error={err.daysValid}><TextInput id="ts-d" inputMode="numeric" value={ts.days} onChange={(e) => setTs({ ...ts, days: e.target.value })} /></Field>
                  <div className="row" style={{ gap: 8, justifyContent: "flex-end" }}><Button onClick={() => setPanel(null)}>{t("Cancel")}</Button>
                    <Button variant="primary" onClick={() => act(() => post<{ link: string }>(`/api/hr/recruitment/applications/${id}/tests`, { templateId: ts.templateId || tests.data?.find((x) => !x.archived)?.id, daysValid: Number(ts.days) }), t("Test sent."), (v) => setLink((v as { link: string } | undefined)?.link ?? ""))}><Send size={14} /> {t("Send")}</Button></div></>}
              </div></Panel>}
            {panel === "offer" && <Panel title={t("Make offer")}>
              <div className="stack">
                <div className="fgrid">
                  <Field label={t("Monthly salary (IDR)")} htmlFor="of-s" required error={err.monthlySalary}><TextInput id="of-s" inputMode="numeric" value={of.monthlySalary} onChange={(e) => setOf({ ...of, monthlySalary: e.target.value })} /></Field>
                  <Field label={t("Employment type")} htmlFor="of-ty" error={err.type}><SelectInput id="of-ty" value={of.type} onChange={(e) => setOf({ ...of, type: e.target.value })} options={TYPE_OPTIONS} /></Field>
                  <Field label={t("Start date")} htmlFor="of-st" error={err.startDate}><TextInput id="of-st" type="date" value={of.startDate} onChange={(e) => setOf({ ...of, startDate: e.target.value })} /></Field>
                  <Field label={t("Answer by")} htmlFor="of-ex" error={err.expiresOn}><TextInput id="of-ex" type="date" value={of.expiresOn} onChange={(e) => setOf({ ...of, expiresOn: e.target.value })} /></Field>
                  {of.type !== "permanent" && <Field label={t("Contract end")} htmlFor="of-ce" error={err.contractEnd}><TextInput id="of-ce" type="date" value={of.contractEnd} onChange={(e) => setOf({ ...of, contractEnd: e.target.value })} /></Field>}
                </div>
                <Field label={t("Other terms")} htmlFor="of-terms"><textarea id="of-terms" rows={4} maxLength={4000} className="textarea" value={of.terms} onChange={(e) => setOf({ ...of, terms: e.target.value })} placeholder={t("e.g. three months' probation, benefits, working hours")} /></Field>
                <div className="row" style={{ gap: 8, justifyContent: "flex-end" }}><Button onClick={() => setPanel(null)}>{t("Cancel")}</Button>
                  <Button variant="primary" onClick={() => act(() => post(`/api/hr/recruitment/applications/${id}/offers`, { ...of, monthlySalary: Number(of.monthlySalary || 0), contractEnd: of.contractEnd || null }), t("Offer drafted. Check it, then send it."), () => setPanel(null))}>{t("Save draft")}</Button></div>
              </div></Panel>}
            {panel === "reject" && <Panel title={t("Reject applicant")}>
              <div className="stack">
                <Field label={t("Reason (internal)")} htmlFor="rj-r"><TextInput id="rj-r" value={rj.reason} onChange={(e) => setRj({ ...rj, reason: e.target.value })} /></Field>
                <label className="check"><Checkbox checked={rj.notify} onChange={(e) => setRj({ ...rj, notify: e.target.checked })} /> {t("Send the candidate a polite email (without the reason)")}</label>
                <div className="row" style={{ gap: 8, justifyContent: "flex-end" }}><Button onClick={() => setPanel(null)}>{t("Cancel")}</Button><Button variant="danger" onClick={() => patch({ reject: true, rejectReason: rj.reason, notifyCandidate: rj.notify }, t("Applicant rejected."))}>{t("Reject")}</Button></div>
              </div></Panel>}
            {panel === "hire" && <Panel title={t("Hire")} description={t("Creates the employee record with the onboarding checklist. Set the salary in Compensation afterwards.")}>
              <div className="stack">
                <div className="fgrid">
                  <Field label={t("Employee number")} htmlFor="hi-no" hint={t("Leave empty to number automatically.")}><TextInput id="hi-no" value={hf.employeeNo} onChange={(e) => setHf({ ...hf, employeeNo: e.target.value })} /></Field>
                  <Field label={t("Work email")} htmlFor="hi-mail"><TextInput id="hi-mail" type="email" value={hf.workEmail} onChange={(e) => setHf({ ...hf, workEmail: e.target.value })} /></Field>
                  <Field label={t("Department")} htmlFor="hi-unit"><SelectInput id="hi-unit" value={hf.orgUnitId} onChange={(e) => setHf({ ...hf, orgUnitId: e.target.value })} options={unitOptions(units.data, t("As in the posting"))} /></Field>
                  <Field label={t("Manager")} htmlFor="hi-mgr"><SelectInput id="hi-mgr" value={hf.managerId} onChange={(e) => setHf({ ...hf, managerId: e.target.value })} options={[{ value: "", label: "—" }, ...(people.data?.data ?? []).map((p) => ({ value: p.employeeId, label: p.fullName }))]} /></Field>
                </div>
                <div className="row" style={{ gap: 8, justifyContent: "flex-end" }}><Button onClick={() => setPanel(null)}>{t("Cancel")}</Button>
                  <Button variant="primary" onClick={() => act(() => post(`/api/hr/recruitment/applications/${id}/hire`, { employeeNo: hf.employeeNo || null, workEmail: hf.workEmail || null, orgUnitId: hf.orgUnitId || null, managerId: hf.managerId || null }), t("Hired. The employee record and onboarding checklist are ready."), () => setPanel(null))}>{t("Create employee")}</Button></div>
              </div></Panel>}

            {d.offers.length > 0 && <Panel title={t("Offers")}>
              <ul className="mini-list">{d.offers.map((o) => (
                <li key={o.id}><div><strong>{o.jobTitle} · {idr(o.monthlySalary)}</strong><small>{t("Starts {date}", { date: fmtDate(o.startDate) })} · {t("answer by {date}", { date: fmtDate(o.expiresOn) })}{o.responseNote ? ` · ${o.responseNote}` : ""}</small></div>
                  <Badge tone={OFFER_STATUS[o.status][0]}>{t(OFFER_STATUS[o.status][1])}</Badge>
                  <Button size="sm" variant="soft" aria-label={t("Offer letter (PDF)")} onClick={() => download(`/api/hr/recruitment/offers/${o.id}/pdf`)}><Download size={14} /></Button>
                  {(o.status === "draft" || o.status === "sent") && <><Button size="sm" variant="primary" onClick={() => act(() => post(`/api/hr/recruitment/offers/${o.id}/send`), o.status === "draft" ? t("Offer sent to the candidate.") : t("Offer sent again."))}><Send size={14} /> {o.status === "draft" ? t("Send") : t("Resend")}</Button>
                    <Button size="sm" variant="danger-outline" onClick={() => act(() => post(`/api/hr/recruitment/offers/${o.id}/withdraw`), t("Offer withdrawn."))}>{t("Withdraw")}</Button></>}
                </li>))}</ul></Panel>}
            {d.interviews.length > 0 && <Panel title={t("Interviews")}>
              <ul className="mini-list">{d.interviews.map((i) => (
                <li key={i.id}><div><strong>{fmtDateTime(i.at)} · {i.durationMinutes} {t("min")}</strong><small>{i.interviewers.join(", ")}{i.location ? ` · ${i.location}` : ""}{i.rating ? ` · ${"★".repeat(i.rating)} ${t(REC[i.recommendation ?? ""] ?? "")}` : ""}{i.feedback ? ` · ${i.feedback}` : ""}</small></div>
                  <Badge tone={i.status === "done" ? "green" : i.status === "cancelled" ? "gray" : "blue"}>{t(i.status === "done" ? "Done" : i.status === "cancelled" ? "Cancelled" : "Scheduled")}</Badge>
                  {i.status === "scheduled" && <Button size="sm" variant="danger-outline" onClick={() => act(() => post(`/api/hr/recruitment/interviews/${i.id}/cancel`), t("Interview cancelled."))}>{t("Cancel")}</Button>}
                </li>))}</ul></Panel>}
            {d.tests.length > 0 && <Panel title={t("Tests")}>
              <ul className="mini-list">{d.tests.map((x) => (
                <li key={x.id}><div><strong>{x.template}</strong><small>{x.status === "done" ? (x.score != null ? `${x.score}%${x.passScore != null ? ` · ${x.score >= x.passScore ? t("passed") : t("below the pass mark")}` : ""}` : x.result) : t("waiting · link valid until {date}", { date: fmtDate(x.expiresAt) })}</small></div>
                  <Badge tone={x.status === "done" ? "green" : "orange"}>{t(x.status === "done" ? "Done" : "Sent")}</Badge></li>))}</ul></Panel>}
            <Panel title={t("Activity")}>
              <div className="row" style={{ gap: 6, marginBottom: 10 }}><TextInput aria-label={t("Add a note")} value={note} onChange={(e) => setNote(e.target.value)} placeholder={t("Add a note for the team")} /><Button size="sm" disabled={!note.trim()} onClick={() => patch({ note }, t("Note added."))}>{t("Add")}</Button></div>
              <ul className="mini-list">{d.activity.map((x) => <li key={x.id}><div><strong>{x.text}</strong><small>{fmtDateTime(x.at)}{x.by ? ` · ${x.by}` : ""}</small></div></li>)}</ul>
            </Panel>
          </div>); }}</QueryBoundary>
    </Overlay>
  );
}

// ============================================================ hiring requests
export function Requisitions({ all }: { all: boolean }) {
  const run = useRun(); const q = useQuery(() => get<Requisition[]>("/api/hr/recruitment/requisitions", all ? { scope: "all" } : undefined), [all]); const units = useUnits();
  const [ask, setAsk] = useState(false); const [f, setF] = useState({ jobTitle: "", orgUnitId: "", openings: "1", type: "permanent", salaryMin: "", salaryMax: "", replacement: false, justification: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [decide, setDecide] = useState<Requisition | null>(null); const [note, setNote] = useState("");
  async function send(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/recruitment/requisitions", { ...f, orgUnitId: f.orgUnitId || null, openings: Number(f.openings || 0), salaryMin: f.salaryMin ? Number(f.salaryMin) : null, salaryMax: f.salaryMax ? Number(f.salaryMax) : null }), t("Hiring request sent to HR."), { silent: true });
    if (r.ok) { setAsk(false); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const doDecide = async (approve: boolean) => { const r = await run(() => post(`/api/hr/recruitment/requisitions/${decide!.id}/decide`, { approve, note }), approve ? t("Hiring request approved.") : t("Hiring request rejected."), { silent: true }); if (r.ok) { setDecide(null); q.reload(); } else setForm(r.error); };
  const rows = q.data ?? [];
  const cols: Column<Requisition>[] = [
    { key: "n", header: t("Request"), cell: (r) => <><b>{r.jobTitle}</b><small className="sub">{r.number}{r.unit ? ` · ${r.unit}` : ""} · {typeLabel(r.type)}{r.replacement ? ` · ${t("replacement")}` : ""}</small></> },
    { key: "o", header: t("Openings"), cell: (r) => `${r.hired} / ${r.openings}` }, { key: "s", header: t("Salary"), className: "hide-sm", cell: (r) => salary(r.salaryMin, r.salaryMax) },
    ...(all ? [{ key: "by", header: t("Requested by"), className: "hide-sm", cell: (r: Requisition) => <>{r.requestedBy}<small className="sub">{fmtDate(r.createdAt)}</small></> }] : []),
    { key: "st", header: t("Status"), cell: (r) => <><Badge tone={REQ_STATUS[r.status][0]}>{t(REQ_STATUS[r.status][1])}</Badge>{r.overPlan && r.status === "pending" && <small className="sub over-plan">{t("Over the manpower plan")}</small>}{r.decisionNote && <small className="sub">{r.decisionNote}</small>}</> },
    { key: "x", header: "", cell: (r) => <div className="row" style={{ gap: 6 }}>
      {all && r.status === "pending" && !r.mine && <Button size="sm" variant="primary" onClick={() => { setNote(""); setForm(""); setDecide(r); }}>{t("Decide")}</Button>}
      {(r.mine || all) && (r.status === "pending" || r.status === "approved") && r.hired === 0 && <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => post(`/api/hr/recruitment/requisitions/${r.id}/cancel`), t("Hiring request cancelled."))).ok) q.reload(); }}>{t("Cancel")}</Button>}</div> },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => { setErr({}); setForm(""); setAsk(true); }}><Plus size={16} /> {t("Request to hire")}</Button></div>
      <Panel>
        {!q.loading && rows.length === 0 ? <EmptyState title={t("No hiring requests")} description={t("Ask HR for a new or replacement position. HR checks it against the manpower plan and decides.")} />
          : <DataTable label={t("Hiring requests")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={ask} onClose={() => setAsk(false)} side="right" title={t("Request to hire")} footer={<><Button onClick={() => setAsk(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rq-form">{t("Send request")}</Button></>}>
        <form id="rq-form" className="stack" noValidate onSubmit={send}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Job title")} htmlFor="rq-title" required error={err.jobTitle}><TextInput id="rq-title" value={f.jobTitle} onChange={(e) => setF({ ...f, jobTitle: e.target.value })} /></Field>
          <div className="fgrid">
            <Field label={t("Department")} htmlFor="rq-unit" error={err.orgUnitId}><SelectInput id="rq-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={unitOptions(units.data, "—")} /></Field>
            <Field label={t("Openings")} htmlFor="rq-n" error={err.openings}><TextInput id="rq-n" inputMode="numeric" value={f.openings} onChange={(e) => setF({ ...f, openings: e.target.value })} /></Field>
            <Field label={t("Employment type")} htmlFor="rq-type"><SelectInput id="rq-type" value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })} options={TYPE_OPTIONS} /></Field>
            <span />
            <Field label={t("Salary from (IDR)")} htmlFor="rq-min"><TextInput id="rq-min" inputMode="numeric" value={f.salaryMin} onChange={(e) => setF({ ...f, salaryMin: e.target.value })} /></Field>
            <Field label={t("Salary to (IDR)")} htmlFor="rq-max" error={err.salaryMax}><TextInput id="rq-max" inputMode="numeric" value={f.salaryMax} onChange={(e) => setF({ ...f, salaryMax: e.target.value })} /></Field>
          </div>
          <label className="check"><Checkbox checked={f.replacement} onChange={(e) => setF({ ...f, replacement: e.target.checked })} /> {t("Replaces someone who left")}</label>
          <Field label={t("Why is the position needed?")} htmlFor="rq-why" required error={err.justification}><textarea id="rq-why" rows={4} maxLength={1000} className="textarea" value={f.justification} onChange={(e) => setF({ ...f, justification: e.target.value })} /></Field>
        </form>
      </Overlay>
      <Overlay open={decide !== null} onClose={() => setDecide(null)} side="right" title={t("Decide {number}", { number: decide?.number ?? "" })} footer={<><Button variant="danger-outline" onClick={() => doDecide(false)}>{t("Reject")}</Button><Button variant="primary" onClick={() => doDecide(true)}>{t("Approve")}</Button></>}>
        <div className="stack">
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {decide && <><p><b>{decide.jobTitle}</b> · {decide.openings} · {typeLabel(decide.type)}<br /><small className="sub">{decide.requestedBy} · {salary(decide.salaryMin, decide.salaryMax)}</small></p><p style={{ whiteSpace: "pre-wrap" }}>{decide.justification}</p>
            {decide.overPlan && <div className="form-alert" role="status">{t("This request goes beyond the manpower plan for the year.")}</div>}</>}
          <Field label={t("Note (required when rejecting)")} htmlFor="dc-note"><textarea id="dc-note" rows={3} maxLength={300} className="textarea" value={note} onChange={(e) => setNote(e.target.value)} /></Field>
        </div>
      </Overlay>
    </>
  );
}

// ============================================================ manpower planning
export function ManpowerPlans() {
  const run = useRun(); const [year, setYear] = useState(thisYear); const q = useQuery(() => get<ManpowerRow[]>("/api/hr/recruitment/manpower", { year }), [year]); const units = useUnits();
  const [edit, setEdit] = useState<ManpowerRow | "new" | null>(null); const [f, setF] = useState({ orgUnitId: "", jobTitle: "", headcount: "1", monthlyBudget: "", note: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { year, orgUnitId: f.orgUnitId || null, jobTitle: f.jobTitle, headcount: Number(f.headcount || 0), monthlyBudget: Number(f.monthlyBudget || 0), note: f.note };
    const r = await run(() => (edit === "new" ? post("/api/hr/recruitment/manpower", body) : put(`/api/hr/recruitment/manpower/${(edit as ManpowerRow).id}`, body)), t("Plan saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const rows = q.data ?? [];
  const cols: Column<ManpowerRow>[] = [
    { key: "j", header: t("Position"), cell: (r) => <><b>{r.jobTitle}</b><small className="sub">{r.unit ?? t("Whole company")}{r.note ? ` · ${r.note}` : ""}</small></> },
    { key: "h", header: t("Planned"), cell: (r) => r.headcount }, { key: "f", header: t("Filled"), cell: (r) => r.filled }, { key: "o", header: t("Being hired"), cell: (r) => r.openRequests },
    { key: "g", header: t("Gap"), cell: (r) => <b className={r.gap < 0 ? "neg" : undefined}>{r.gap}</b> }, { key: "b", header: t("Monthly budget"), className: "hide-sm", cell: (r) => idr(r.monthlyBudget) },
    { key: "x", header: "", cell: (r) => <div className="row" style={{ gap: 6 }}><Button size="sm" variant="soft" onClick={() => { setErr({}); setForm(""); setF({ orgUnitId: r.orgUnitId ?? "", jobTitle: r.jobTitle, headcount: String(r.headcount), monthlyBudget: String(r.monthlyBudget), note: r.note ?? "" }); setEdit(r); }}>{t("Edit")}</Button>
      <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={async () => { if ((await run(() => del(`/api/hr/recruitment/manpower/${r.id}`), t("Removed."))).ok) q.reload(); }}><Trash2 size={14} /></Button></div> },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "space-between", marginBottom: 12, gap: 8 }}>
        <SelectInput aria-label={t("Year")} value={String(year)} onChange={(e) => setYear(Number(e.target.value))} options={[thisYear - 1, thisYear, thisYear + 1, thisYear + 2].map(String)} />
        <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ orgUnitId: "", jobTitle: "", headcount: "1", monthlyBudget: "", note: "" }); setEdit("new"); }}><Plus size={16} /> {t("Add position")}</Button>
      </div>
      <KpiRow label={t("Plan figures")}>
        <Kpi icon={<Users size={18} />} label={t("Planned headcount")} value={rows.reduce((s, r) => s + r.headcount, 0)} />
        <Kpi icon={<CheckCircle2 size={18} />} tone="green" label={t("Filled")} value={rows.reduce((s, r) => s + r.filled, 0)} />
        <Kpi icon={<Briefcase size={18} />} tone="blue" label={t("Being hired")} value={rows.reduce((s, r) => s + r.openRequests, 0)} />
        <Kpi icon={<Star size={18} />} tone="orange" label={t("Still open")} value={rows.reduce((s, r) => s + Math.max(0, r.gap), 0)} hint={idr(rows.reduce((s, r) => s + r.monthlyBudget, 0)) + " " + t("per month")} />
      </KpiRow>
      <Panel>
        {!q.loading && rows.length === 0 ? <EmptyState title={t("No plan for this year")} description={t("List the positions each department needs this year, with the headcount and budget. Hiring requests are checked against it.")} />
          : <DataTable label={t("Manpower plan")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add position") : t("Edit position")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="mp-form">{t("Save")}</Button></>}>
        <form id="mp-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Department")} htmlFor="mp-unit" error={err.orgUnitId}><SelectInput id="mp-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={unitOptions(units.data, t("Whole company"))} /></Field>
          <Field label={t("Job title")} htmlFor="mp-job" required error={err.jobTitle} hint={t("Use the same title as on employee records, so filled positions are counted.")}><TextInput id="mp-job" value={f.jobTitle} onChange={(e) => setF({ ...f, jobTitle: e.target.value })} /></Field>
          <div className="fgrid">
            <Field label={t("Headcount")} htmlFor="mp-hc" error={err.headcount}><TextInput id="mp-hc" inputMode="numeric" value={f.headcount} onChange={(e) => setF({ ...f, headcount: e.target.value })} /></Field>
            <Field label={t("Monthly budget (IDR)")} htmlFor="mp-b" error={err.monthlyBudget}><TextInput id="mp-b" inputMode="numeric" value={f.monthlyBudget} onChange={(e) => setF({ ...f, monthlyBudget: e.target.value })} /></Field>
          </div>
          <Field label={t("Note")} htmlFor="mp-note"><TextInput id="mp-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </>
  );
}

// ============================================================ assessments
const blankQ = (kind: string, n: number): AssessmentQuestion => (kind === "test" ? { id: `q${n}`, text: "", options: [{ text: "", correct: true }, { text: "", correct: false }] } : { id: `q${n}`, text: t("Which describes you better?"), options: [{ text: "", pole: "E" }, { text: "", pole: "I" }] });
const PAIRS = [["E", "I"], ["S", "N"], ["T", "F"], ["J", "P"]];

export function Assessments() {
  const run = useRun(); const q = useQuery(() => get<AssessmentTemplate[]>("/api/hr/recruitment/assessments"), []);
  const [edit, setEdit] = useState<AssessmentTemplate | "new" | null>(null); const [f, setF] = useState({ name: "", kind: "test", passScore: "60", timeLimitMinutes: "30", archived: false, questions: [] as AssessmentQuestion[] }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (x: AssessmentTemplate | "new") => { setErr({}); setForm(""); setEdit(x); setF(x === "new" ? { name: "", kind: "test", passScore: "60", timeLimitMinutes: "30", archived: false, questions: [blankQ("test", 1)] } : { name: x.name, kind: x.kind, passScore: x.passScore?.toString() ?? "", timeLimitMinutes: String(x.timeLimitMinutes), archived: x.archived, questions: x.questions }); };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { name: f.name, kind: f.kind, passScore: f.passScore ? Number(f.passScore) : null, timeLimitMinutes: Number(f.timeLimitMinutes || 0), archived: f.archived, questions: f.questions };
    const r = await run(() => (edit === "new" ? post("/api/hr/recruitment/assessments", body) : put(`/api/hr/recruitment/assessments/${(edit as AssessmentTemplate).id}`, body)), t("Test saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const setQ = (i: number, qq: AssessmentQuestion) => setF({ ...f, questions: f.questions.map((x, j) => (j === i ? qq : x)) });
  const cols: Column<AssessmentTemplate>[] = [
    { key: "n", header: t("Test"), cell: (x) => <><b>{x.name}</b><small className="sub">{t("{n} questions", { n: x.questions.length })}{x.timeLimitMinutes ? ` · ${x.timeLimitMinutes} ${t("min")}` : ""}</small></> },
    { key: "k", header: t("Type"), cell: (x) => t(x.kind === "test" ? "Knowledge test" : "Personality profile") }, { key: "p", header: t("Pass mark"), cell: (x) => (x.passScore != null ? `${x.passScore}%` : "—") },
    { key: "s", header: t("Status"), cell: (x) => (x.archived ? <Badge tone="gray">{t("Archived")}</Badge> : <Badge tone="green">{t("Active")}</Badge>) },
    { key: "x", header: "", cell: (x) => <Button size="sm" variant="soft" onClick={() => open(x)}>{t("Edit")}</Button> },
  ];
  return (
    <>
      <div className="row" style={{ justifyContent: "flex-end", gap: 8, marginBottom: 12 }}>
        <Button onClick={async () => { if ((await run(() => post("/api/hr/recruitment/assessments/sample-profile"), t("Personality profile added."))).ok) q.reload(); }}>{t("Add ready-made personality profile")}</Button>
        <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("New test")}</Button>
      </div>
      <Panel>
        {!q.loading && (q.data ?? []).length === 0 ? <EmptyState title={t("No tests yet")} description={t("Write a knowledge test with right answers and a pass mark, or add the ready-made personality profile (four preferences).")} />
          : <DataTable label={t("Tests")} columns={cols} rows={q.data ?? []} rowKey={(x) => x.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("New test") : t("Edit test")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="as-form">{t("Save")}</Button></>}>
        <form id="as-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Name")} htmlFor="as-name" required error={err.name}><TextInput id="as-name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
          <div className="fgrid">
            {edit === "new" && <Field label={t("Type")} htmlFor="as-kind"><SelectInput id="as-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value, questions: [blankQ(e.target.value, 1)] })} options={[{ value: "test", label: t("Knowledge test") }, { value: "personality", label: t("Personality profile") }]} /></Field>}
            {f.kind === "test" && <Field label={t("Pass mark (%)")} htmlFor="as-pass" error={err.passScore}><TextInput id="as-pass" inputMode="numeric" value={f.passScore} onChange={(e) => setF({ ...f, passScore: e.target.value })} /></Field>}
            <Field label={t("Time limit (minutes, 0 = none)")} htmlFor="as-time" error={err.timeLimitMinutes}><TextInput id="as-time" inputMode="numeric" value={f.timeLimitMinutes} onChange={(e) => setF({ ...f, timeLimitMinutes: e.target.value })} /></Field>
          </div>
          {edit !== "new" && <label className="check"><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived (cannot be sent)")}</label>}
          {err.questions && <div className="form-alert err" role="alert">{err.questions}</div>}
          {f.questions.map((qq, i) => (
            <fieldset key={i} className="stack q-edit"><legend>{t("Question {n}", { n: i + 1 })}</legend>
              <TextInput aria-label={t("Question {n}", { n: i + 1 })} value={qq.text} onChange={(e) => setQ(i, { ...qq, text: e.target.value })} />
              {f.kind === "test" ? qq.options.map((o, k) => (
                <div key={k} className="row" style={{ gap: 6 }}>
                  <input type="radio" name={`correct-${i}`} aria-label={t("Correct answer")} checked={o.correct === true} onChange={() => setQ(i, { ...qq, options: qq.options.map((x, m) => ({ ...x, correct: m === k })) })} />
                  <TextInput aria-label={t("Option {n}", { n: k + 1 })} value={o.text} onChange={(e) => setQ(i, { ...qq, options: qq.options.map((x, m) => (m === k ? { ...x, text: e.target.value } : x)) })} />
                  {qq.options.length > 2 && <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={() => setQ(i, { ...qq, options: qq.options.filter((_, m) => m !== k) })}><Trash2 size={14} /></Button>}
                </div>))
                : <><SelectInput aria-label={t("Dimension")} value={qq.options[0]?.pole ?? "E"} onChange={(e) => { const pair = PAIRS.find((p) => p[0] === e.target.value)!; setQ(i, { ...qq, options: [{ ...qq.options[0], pole: pair[0] }, { ...qq.options[1], pole: pair[1] }] }); }} options={PAIRS.map((p) => ({ value: p[0], label: `${p[0]} / ${p[1]}` }))} />
                  {qq.options.map((o, k) => <TextInput key={k} aria-label={`${o.pole}`} value={o.text} placeholder={t("Statement for {pole}", { pole: o.pole ?? "" })} onChange={(e) => setQ(i, { ...qq, options: qq.options.map((x, m) => (m === k ? { ...x, text: e.target.value } : x)) })} />)}</>}
              <div className="row" style={{ gap: 6 }}>
                {f.kind === "test" && qq.options.length < 6 && <Button size="sm" onClick={() => setQ(i, { ...qq, options: [...qq.options, { text: "", correct: false }] })}><Plus size={14} /> {t("Option")}</Button>}
                {f.questions.length > 1 && <Button size="sm" variant="danger-outline" onClick={() => setF({ ...f, questions: f.questions.filter((_, j) => j !== i) })}>{t("Remove question")}</Button>}
              </div>
            </fieldset>))}
          <Button size="sm" onClick={() => setF({ ...f, questions: [...f.questions, blankQ(f.kind, f.questions.length + 1)] })}><Plus size={14} /> {t("Add question")}</Button>
        </form>
      </Overlay>
    </>
  );
}

// ============================================================ interviewer: my interviews
export function MyInterviews() {
  const run = useRun(); const q = useQuery(() => get<InterviewerTask[]>("/api/hr/recruitment/interviews/mine"), []);
  const [fb, setFb] = useState<InterviewerTask | null>(null); const [f, setF] = useState({ rating: "4", recommendation: "hire", feedback: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/recruitment/interviews/${fb!.id}/feedback`, { ...f, rating: Number(f.rating) }), t("Feedback sent to HR."), { silent: true });
    if (r.ok) { setFb(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const cols: Column<InterviewerTask>[] = [
    { key: "w", header: t("When"), cell: (i) => <><b>{fmtDateTime(i.at)}</b><small className="sub">{i.durationMinutes} {t("min")}{i.location ? ` · ${i.location}` : ""}</small></> },
    { key: "c", header: t("Candidate"), cell: (i) => <><b>{i.candidate}</b><small className="sub">{i.posting}</small></> },
    { key: "s", header: t("Status"), cell: (i) => <Badge tone={i.status === "done" ? "green" : i.status === "cancelled" ? "gray" : "blue"}>{t(i.status === "done" ? "Done" : i.status === "cancelled" ? "Cancelled" : "Scheduled")}</Badge> },
    { key: "f", header: t("Your feedback"), className: "hide-sm", cell: (i) => (i.rating ? `${"★".repeat(i.rating)} · ${t(REC[i.recommendation ?? ""] ?? "")}` : "—") },
    { key: "x", header: "", cell: (i) => <div className="row" style={{ gap: 6 }}>{i.hasCv && i.status !== "cancelled" && <Button size="sm" variant="soft" onClick={() => download(`/api/hr/recruitment/applications/${i.applicationId}/cv`)}><Download size={14} /> {t("CV")}</Button>}
      {i.status !== "cancelled" && <Button size="sm" variant={i.status === "done" ? "soft" : "primary"} onClick={() => { setErr({}); setForm(""); setF({ rating: String(i.rating ?? 4), recommendation: i.recommendation ?? "hire", feedback: i.feedback ?? "" }); setFb(i); }}>{i.status === "done" ? t("Edit feedback") : t("Give feedback")}</Button>}</div> },
  ];
  return (
    <>
      <Panel>
        {!q.loading && (q.data ?? []).length === 0 ? <EmptyState title={t("No interviews")} description={t("When HR asks you to interview a candidate, it appears here with the CV and a feedback form.")} />
          : <DataTable label={t("My interviews")} columns={cols} rows={q.data ?? []} rowKey={(i) => i.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={fb !== null} onClose={() => setFb(null)} side="right" title={t("Interview feedback")} footer={<><Button onClick={() => setFb(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="fb-form">{t("Send")}</Button></>}>
        <form id="fb-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {fb && <p><b>{fb.candidate}</b> · {fb.posting}</p>}
          <Field label={t("Rating")} htmlFor="fb-r" error={err.rating}><SelectInput id="fb-r" value={f.rating} onChange={(e) => setF({ ...f, rating: e.target.value })} options={[1, 2, 3, 4, 5].map((n) => ({ value: String(n), label: "★".repeat(n) }))} /></Field>
          <Field label={t("Recommendation")} htmlFor="fb-rec" error={err.recommendation}><SelectInput id="fb-rec" value={f.recommendation} onChange={(e) => setF({ ...f, recommendation: e.target.value })} options={Object.keys(REC).map((k) => ({ value: k, label: t(REC[k]) }))} /></Field>
          <Field label={t("Notes")} htmlFor="fb-n"><textarea id="fb-n" rows={5} maxLength={2000} className="textarea" value={f.feedback} onChange={(e) => setF({ ...f, feedback: e.target.value })} /></Field>
        </form>
      </Overlay>
    </>
  );
}
