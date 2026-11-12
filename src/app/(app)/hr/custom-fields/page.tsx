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
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { CustomFieldDef } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TYPES = [["text", "Text"], ["number", "Number"], ["date", "Date"], ["choice", "Choice from a list"], ["yesno", "Yes or no"]] as const;
type Form = { key: string; label: string; type: string; options: string; required: boolean; visibleToEmployee: boolean; editableByEmployee: boolean; sortOrder: string; archived: boolean };
const blank: Form = { key: "", label: "", type: "text", options: "", required: false, visibleToEmployee: false, editableByEmployee: false, sortOrder: "0", archived: false };

/** The organisation's own fields on the employee record, shown under the employee's "More" tab and, when chosen, in the employee's own profile. */
export default function CustomFieldsPage() {
  const run = useRun(); const q = useQuery(() => get<CustomFieldDef[]>("/api/hr/custom-fields"), []);
  const [edit, setEdit] = useState<CustomFieldDef | "new" | null>(null); const [f, setF] = useState<Form>(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  function open(d: CustomFieldDef | "new") {
    setF(d === "new" ? blank : { key: d.key, label: d.label, type: d.type, options: d.options.join("\n"), required: d.required, visibleToEmployee: d.visibleToEmployee, editableByEmployee: d.editableByEmployee, sortOrder: String(d.sortOrder), archived: d.archived });
    setErr({}); setForm(""); setEdit(d);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!edit) return; setForm("");
    const body = { key: f.key, label: f.label, type: f.type, options: f.type === "choice" ? f.options.split("\n").map((x) => x.trim()).filter(Boolean) : undefined, required: f.required, visibleToEmployee: f.visibleToEmployee, editableByEmployee: f.editableByEmployee, sortOrder: Number(f.sortOrder || 0), archived: f.archived };
    const r = await run(() => (edit === "new" ? post("/api/hr/custom-fields", body) : put(`/api/hr/custom-fields/${edit.id}`, body)), t("Field saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const label = (type: string) => t(TYPES.find(([k]) => k === type)?.[1] ?? type);
  const cols: Column<CustomFieldDef>[] = [
    { key: "l", header: t("Field"), cell: (d) => <><b>{d.label}</b> {d.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{d.key} · {label(d.type)}{d.type === "choice" ? `: ${d.options.join(", ")}` : ""}</small></> },
    { key: "r", header: t("Required"), className: "hide-sm", cell: (d) => (d.required ? t("Yes") : t("No")) },
    { key: "v", header: t("Employee"), className: "hide-sm", cell: (d) => (d.editableByEmployee ? t("Sees and edits") : d.visibleToEmployee ? t("Sees") : t("Hidden")) },
    { key: "x", header: "", cell: (d) => <Button size="sm" onClick={() => open(d)}>{t("Edit")}</Button> },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Custom Fields")} description={t("Extra details your organisation keeps on each employee, such as shirt size or a licence number. HR fills them in under the employee's More tab.")}
        actions={<Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add field")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No custom fields")} description={t("Add the details your organisation needs that the standard employee record does not have.")} />
          : <DataTable label={t("Custom fields")} columns={cols} rows={rows} rowKey={(d) => d.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add field") : t("Edit field")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cf-form">{t("Save")}</Button></>}>
        <form id="cf-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Label")} htmlFor="cf-label" required error={err.label}><TextInput id="cf-label" data-autofocus value={f.label} invalid={!!err.label} onChange={(e) => setF({ ...f, label: e.target.value })} placeholder={t("Shirt size")} /></Field>
            <Field label={t("Key")} htmlFor="cf-key" required={edit === "new"} error={err.key} hint={t("Used in letter templates as {{cf.key}}. Cannot change later.")}><TextInput id="cf-key" value={f.key} disabled={edit !== "new"} invalid={!!err.key} onChange={(e) => setF({ ...f, key: e.target.value })} placeholder="shirt_size" /></Field>
            <Field label={t("Type")} htmlFor="cf-type" error={err.type}><SelectInput id="cf-type" value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })} options={TYPES.map(([k, v]) => ({ value: k, label: t(v) }))} /></Field>
            <Field label={t("Order")} htmlFor="cf-order"><TextInput id="cf-order" inputMode="numeric" value={f.sortOrder} onChange={(e) => setF({ ...f, sortOrder: e.target.value })} /></Field>
          </div>
          {f.type === "choice" && <Field label={t("Answers (one per line)")} htmlFor="cf-opts" error={err.options}><textarea id="cf-opts" className="input" rows={4} value={f.options} onChange={(e) => setF({ ...f, options: e.target.value })} /></Field>}
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.required} onChange={(e) => setF({ ...f, required: e.target.checked })} /> {t("Required")}</label>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.visibleToEmployee} onChange={(e) => setF({ ...f, visibleToEmployee: e.target.checked, editableByEmployee: e.target.checked && f.editableByEmployee })} /> {t("Shown to the employee in their profile")}</label>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.editableByEmployee} disabled={!f.visibleToEmployee} onChange={(e) => setF({ ...f, editableByEmployee: e.target.checked })} /> {t("The employee may change it")}</label>
          {edit !== "new" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived (hidden, values kept)")}</label>}
        </form>
      </Overlay>
    </Require>
  );
}
