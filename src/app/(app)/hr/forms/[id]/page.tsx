"use client";

import { use, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { QueryBoundary } from "@/components/query-boundary";
import { BarList } from "@/components/charts/analytics";
import { get, post, put } from "@/lib/api/client";
import type { FormQuestion, FormResultsData, HrFormItem, OrgUnitItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const QTYPES = [["rating", "Rating 1–5"], ["choice", "One choice"], ["multi", "Several choices"], ["yesno", "Yes or no"], ["text", "Free text"]] as const;

function Builder({ form, onSaved }: { form: HrFormItem; onSaved: () => void }) {
  const run = useRun(); const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units"), []);
  const [f, setF] = useState({ title: form.title, description: form.description ?? "", orgUnitId: form.orgUnitId ?? "", anonymous: form.anonymous, closesOn: form.closesOn ?? "" });
  const [qs, setQs] = useState<(FormQuestion & { optionsText: string })[]>(() => form.questions.map((q) => ({ ...q, optionsText: (q.options ?? []).join("\n") })));
  const [err, setErr] = useState<Record<string, string>>({}); const [msg, setMsg] = useState("");
  const draft = form.status === "draft";
  useEffect(() => { setMsg(""); }, [qs, f]);
  const setQ = (i: number, patch: Partial<FormQuestion & { optionsText: string }>) => setQs(qs.map((q, j) => (j === i ? { ...q, ...patch } : q)));
  async function save() {
    const body = { title: f.title, description: f.description, orgUnitId: f.orgUnitId || undefined, anonymous: f.anonymous, closesOn: f.closesOn || undefined,
      questions: draft ? qs.map((q) => ({ id: q.id, type: q.type, label: q.label, required: q.required, options: q.type === "choice" || q.type === "multi" ? q.optionsText.split("\n").map((x) => x.trim()).filter(Boolean) : undefined })) : undefined };
    const r = await run(() => put(`/api/hr/forms/${form.id}`, body), t("Form saved."), { silent: true });
    if (r.ok) { setErr({}); onSaved(); } else { setErr(r.fields ?? {}); setMsg(r.error); }
  }
  return (
    <Panel title={t("Form")}>
      <div className="stack">
        {msg && <div className="form-alert err" role="alert">{msg}</div>}
        <div className="fgrid">
          <Field label={t("Title")} htmlFor="fm-title" required error={err.title}><TextInput id="fm-title" value={f.title} invalid={!!err.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></Field>
          <Field label={t("Who is asked")} htmlFor="fm-aud" error={err.orgUnitId}><SelectInput id="fm-aud" value={f.orgUnitId} disabled={!draft} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={[{ value: "", label: t("Everyone") }, ...(units.data ?? []).map((u) => ({ value: u.id, label: u.name }))]} /></Field>
          <Field label={t("Closes on")} htmlFor="fm-close" error={err.closesOn}><TextInput id="fm-close" type="date" value={f.closesOn} onChange={(e) => setF({ ...f, closesOn: e.target.value })} /></Field>
        </div>
        <Field label={t("Introduction")} htmlFor="fm-desc"><TextInput id="fm-desc" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} placeholder={t("Optional")} /></Field>
        <label className="row" style={{ gap: 8 }}><Checkbox checked={f.anonymous} disabled={!draft} onChange={(e) => setF({ ...f, anonymous: e.target.checked })} /> {t("Anonymous: results never show who answered")}</label>
        <h3 className="block-title">{t("Questions")}</h3>
        {err.questions && <p className="field-error" role="alert">{err.questions}</p>}
        {qs.map((q, i) => (
          <div key={i} className="stack" style={{ border: "1px solid var(--line)", borderRadius: 10, padding: 12 }}>
            <div className="fgrid">
              <Field label={t("Question {n}", { n: String(i + 1) })} htmlFor={`q-${i}`} error={err[`questions.${i}`]}><TextInput id={`q-${i}`} value={q.label} disabled={!draft} onChange={(e) => setQ(i, { label: e.target.value })} /></Field>
              <Field label={t("Answer type")} htmlFor={`qt-${i}`}><SelectInput id={`qt-${i}`} value={q.type} disabled={!draft} onChange={(e) => setQ(i, { type: e.target.value as FormQuestion["type"] })} options={QTYPES.map(([k, v]) => ({ value: k, label: t(v) }))} /></Field>
            </div>
            {(q.type === "choice" || q.type === "multi") && <Field label={t("Answers (one per line)")} htmlFor={`qo-${i}`}><textarea id={`qo-${i}`} className="input" rows={3} value={q.optionsText} disabled={!draft} onChange={(e) => setQ(i, { optionsText: e.target.value })} /></Field>}
            <div className="row" style={{ justifyContent: "space-between" }}>
              <label className="row" style={{ gap: 8 }}><Checkbox checked={q.required} disabled={!draft} onChange={(e) => setQ(i, { required: e.target.checked })} /> {t("Required")}</label>
              {draft && qs.length > 1 && <Button size="sm" variant="danger-outline" aria-label={t("Remove question {n}", { n: String(i + 1) })} onClick={() => setQs(qs.filter((_, j) => j !== i))}><Trash2 size={14} /></Button>}
            </div>
          </div>))}
        {draft && <div><Button size="sm" onClick={() => setQs([...qs, { id: `q${Date.now().toString(36)}`, type: "text", label: "", required: false, optionsText: "" }])}><Plus size={14} /> {t("Add question")}</Button></div>}
        <div><Button variant="primary" onClick={save}>{t("Save")}</Button></div>
      </div>
    </Panel>
  );
}

function Results({ id }: { id: string }) {
  const q = useQuery(() => get<FormResultsData>(`/api/hr/forms/${id}/results`), [id]);
  return (
    <QueryBoundary q={q} rows={4}>{(r) => (
      <div className="stack">
        {r.questions.map((x) => (
          <Panel key={x.id} title={x.label} description={t("{n} answered", { n: String(x.answered) })}>
            {x.type === "text" ? (x.texts.length === 0 ? <p className="sub">{t("No answers yet.")}</p> : <ul className="mini-list">{x.texts.map((s, i) => <li key={i}><div><span>{s}</span></div></li>)}</ul>)
              : <>{x.average != null && <p><b>{t("Average {n} of 5", { n: String(x.average) })}</b></p>}<BarList label={x.label} rows={x.counts.map((c) => ({ label: c.option === "yes" ? t("Yes") : c.option === "no" ? t("No") : c.option, value: c.count }))} /></>}
          </Panel>))}
      </div>)}
    </QueryBoundary>
  );
}

function Detail({ id }: { id: string }) {
  const run = useRun(); const q = useQuery(() => get<HrFormItem[]>("/api/hr/forms"), [id]);
  return (
    <QueryBoundary q={q} rows={4}>{(all) => {
      const form = all.find((f) => f.id === id); if (!form) return <p className="sub">{t("This form no longer exists.")}</p>;
      return (
        <>
          <PageHeader title={form.title} description={t("{a} of {b} answered", { a: String(form.responses), b: String(form.audienceSize) }) + (form.anonymous ? ` · ${t("anonymous")}` : "")}
            actions={<div className="row" style={{ gap: 8 }}>
              <Badge tone={form.status === "open" ? "green" : form.status === "draft" ? "gray" : "blue"}>{form.status === "open" ? t("Open") : form.status === "draft" ? t("Draft") : t("Closed")}</Badge>
              {form.status === "draft" && <Button variant="primary" onClick={async () => { if ((await run(() => post(`/api/hr/forms/${id}/open`), t("Form opened. Employees were notified."))).ok) q.reload(); }}>{t("Open and notify")}</Button>}
              {form.status === "open" && <Button onClick={async () => { if ((await run(() => post(`/api/hr/forms/${id}/close`), t("Form closed."))).ok) q.reload(); }}>{t("Close form")}</Button>}
            </div>} />
          <div className="stack">
            <Builder key={`${form.id}-${form.status}`} form={form} onSaved={q.reload} />
            {form.status !== "draft" && <Results id={id} />}
          </div>
        </>
      );
    }}</QueryBoundary>
  );
}

export default function FormPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="hrconfig.manage"><p style={{ marginBottom: 12 }}><Link href="/hr/forms" className="link-btn"><ArrowLeft size={14} /> {t("All forms")}</Link></p><Detail id={id} /></Require>;
}
