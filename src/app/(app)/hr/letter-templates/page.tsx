"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { CustomFieldDef, LetterTemplateItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const PLACEHOLDERS = ["name", "employee_no", "job_title", "department", "branch", "manager", "hire_date", "company", "company_address", "today", "number"];
type Form = { name: string; prefix: string; title: string; body: string; visibleToEmployee: boolean; archived: boolean };
const blank: Form = { name: "", prefix: "", title: "", body: "", visibleToEmployee: true, archived: false };

/** Letter templates (assignment letter, warning, certificate…) with placeholders; HR issues them from an employee's More tab, numbered per prefix and year. */
export default function LetterTemplatesPage() {
  const run = useRun(); const q = useQuery(() => get<LetterTemplateItem[]>("/api/hr/letter-templates"), []); const fields = useQuery(() => get<CustomFieldDef[]>("/api/hr/custom-fields"), []);
  const [edit, setEdit] = useState<LetterTemplateItem | "new" | null>(null); const [f, setF] = useState<Form>(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  function open(x: LetterTemplateItem | "new") { setF(x === "new" ? blank : { name: x.name, prefix: x.prefix, title: x.title, body: x.body, visibleToEmployee: x.visibleToEmployee, archived: x.archived }); setErr({}); setForm(""); setEdit(x); }
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!edit) return; setForm("");
    const r = await run(() => (edit === "new" ? post("/api/hr/letter-templates", f) : put(`/api/hr/letter-templates/${edit.id}`, f)), t("Template saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<LetterTemplateItem>[] = [
    { key: "n", header: t("Template"), cell: (x) => <><b>{x.name}</b> {x.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{x.title}</small></> },
    { key: "p", header: t("Number"), className: "hide-sm", cell: (x) => `${x.prefix}/YYYY/0001` },
    { key: "i", header: t("Issued"), className: "hide-sm", cell: (x) => x.issued },
    { key: "v", header: t("Employee sees it"), className: "hide-md", cell: (x) => (x.visibleToEmployee ? t("Yes") : t("No")) },
    { key: "x", header: "", cell: (x) => <Button size="sm" onClick={() => open(x)}>{t("Edit")}</Button> },
  ];
  const rows = q.data ?? []; const tags = [...PLACEHOLDERS, ...(fields.data ?? []).filter((d) => !d.archived).map((d) => `cf.${d.key}`)];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Letter Templates")} description={t("Texts for the letters HR issues often. Placeholders are filled in from the employee record; each letter gets its own number and verification code.")}
        actions={<Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add template")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No letter templates")} description={t("Add the letters you write again and again, such as an assignment letter or a warning letter.")} />
          : <DataTable label={t("Letter templates")} columns={cols} rows={rows} rowKey={(x) => x.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add template") : t("Edit template")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="lt-form">{t("Save")}</Button></>}>
        <form id="lt-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Name")} htmlFor="lt-name" required error={err.name}><TextInput id="lt-name" data-autofocus value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("Assignment letter")} /></Field>
            <Field label={t("Number prefix")} htmlFor="lt-prefix" required error={err.prefix} hint={t("ST gives ST/2027/0001, ST/2027/0002…")}><TextInput id="lt-prefix" value={f.prefix} invalid={!!err.prefix} onChange={(e) => setF({ ...f, prefix: e.target.value })} placeholder="ST" /></Field>
          </div>
          <Field label={t("Letter title")} htmlFor="lt-title" required error={err.title}><TextInput id="lt-title" value={f.title} invalid={!!err.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder={t("Assignment letter for {{name}}")} /></Field>
          <Field label={t("Text")} htmlFor="lt-body" required error={err.body}><textarea id="lt-body" className="input" rows={14} value={f.body} onChange={(e) => setF({ ...f, body: e.target.value })} /></Field>
          <p className="sub">{t("Placeholders:")} {tags.map((x) => <code key={x} style={{ marginRight: 6 }}>{`{{${x}}}`}</code>)}</p>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.visibleToEmployee} onChange={(e) => setF({ ...f, visibleToEmployee: e.target.checked })} /> {t("The employee sees issued letters under My Letters")}</label>
          {edit !== "new" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived (no longer issued)")}</label>}
        </form>
      </Overlay>
    </Require>
  );
}
