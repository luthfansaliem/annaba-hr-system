"use client";

import { useState } from "react";
import { ClipboardList } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import type { HrFormItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Forms and surveys the employee is asked to fill in. */
export default function MyFormsPage() {
  const run = useRun(); const q = useQuery(() => get<HrFormItem[]>("/api/hr/me/forms"), []);
  const [open, setOpen] = useState<HrFormItem | null>(null); const [ans, setAns] = useState<Record<string, unknown>>({}); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!open) return; setForm("");
    const r = await run(() => post(`/api/hr/me/forms/${open.id}`, { answers: ans }), t("Thank you, your answers were sent."), { silent: true });
    if (r.ok) { setOpen(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant"; const rows = q.data ?? [];
  return (
    <>
      <PageHeader title={t("My Forms")} description={t("Surveys and forms HR asked you to fill in.")} />
      <Panel>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !q.loading && rows.length === 0 ? <EmptyState title={t("Nothing to fill in")} description={t("When HR sends a survey or form, it appears here.")} />
          : <ul className="mini-list">{rows.map((f) => (
            <li key={f.id}><div><strong>{f.title}</strong><small>{f.description ?? ""}{f.closesOn ? ` · ${t("closes {date}", { date: fmtDate(f.closesOn) })}` : ""}{f.anonymous ? ` · ${t("anonymous")}` : ""}</small></div>
              {f.answered ? <Badge tone="green">{t("Answered")}</Badge> : f.status === "open" ? <Button size="sm" variant="primary" onClick={() => { setAns({}); setErr({}); setForm(""); setOpen(f); }}><ClipboardList size={14} /> {t("Fill in")}</Button> : <Badge tone="gray">{t("Closed")}</Badge>}</li>))}</ul>}
      </Panel>
      <Overlay open={!!open} onClose={() => setOpen(null)} side="right" title={open?.title ?? ""} description={open?.anonymous ? t("Your answers are anonymous: HR sees totals only.") : open?.description ?? undefined}
        footer={<><Button onClick={() => setOpen(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="fill-form">{t("Send")}</Button></>}>
        {open && <form id="fill-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {open.questions.map((qq) => (
            <Field key={qq.id} label={qq.label} htmlFor={`a-${qq.id}`} required={qq.required} error={err[qq.id]}>
              {qq.type === "text" ? <TextInput id={`a-${qq.id}`} value={(ans[qq.id] as string) ?? ""} onChange={(e) => setAns({ ...ans, [qq.id]: e.target.value })} />
                : qq.type === "rating" ? <SelectInput id={`a-${qq.id}`} value={String(ans[qq.id] ?? "")} onChange={(e) => setAns({ ...ans, [qq.id]: e.target.value ? Number(e.target.value) : undefined })} options={[{ value: "", label: t("Choose") }, ...[1, 2, 3, 4, 5].map((n) => ({ value: String(n), label: `${n}${n === 1 ? ` · ${t("poor")}` : n === 5 ? ` · ${t("excellent")}` : ""}` }))]} />
                : qq.type === "yesno" ? <SelectInput id={`a-${qq.id}`} value={(ans[qq.id] as string) ?? ""} onChange={(e) => setAns({ ...ans, [qq.id]: e.target.value || undefined })} options={[{ value: "", label: t("Choose") }, { value: "yes", label: t("Yes") }, { value: "no", label: t("No") }]} />
                : qq.type === "choice" ? <SelectInput id={`a-${qq.id}`} value={(ans[qq.id] as string) ?? ""} onChange={(e) => setAns({ ...ans, [qq.id]: e.target.value || undefined })} options={[{ value: "", label: t("Choose") }, ...(qq.options ?? []).map((o) => ({ value: o, label: o }))]} />
                : <div className="stack" id={`a-${qq.id}`}>{(qq.options ?? []).map((o) => { const cur = (ans[qq.id] as string[]) ?? []; return <label key={o} className="row" style={{ gap: 8 }}><Checkbox checked={cur.includes(o)} onChange={(e) => setAns({ ...ans, [qq.id]: e.target.checked ? [...cur, o] : cur.filter((x) => x !== o) })} /> {o}</label>; })}</div>}
            </Field>))}
        </form>}
      </Overlay>
    </>
  );
}
