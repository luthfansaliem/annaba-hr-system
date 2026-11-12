"use client";

import { useState } from "react";
import { Download, FilePlus2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get, post, put } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { BranchItem, CustomFieldValue, IssuedLetterItem, LetterTemplateItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** One input for a custom field, by its type. */
export function CustomFieldInput({ f, value, onChange, disabled }: { f: CustomFieldValue; value: string; onChange: (v: string) => void; disabled?: boolean }) {
  const id = `cf-${f.key}`;
  if (f.type === "choice" || f.type === "yesno")
    return <SelectInput id={id} value={value} disabled={disabled} onChange={(e) => onChange(e.target.value)} options={[{ value: "", label: t("Not set") }, ...(f.type === "yesno" ? [{ value: "yes", label: t("Yes") }, { value: "no", label: t("No") }] : f.options.map((o) => ({ value: o, label: o })))]} />;
  return <TextInput id={id} value={value} disabled={disabled} type={f.type === "date" ? "date" : "text"} inputMode={f.type === "number" ? "decimal" : undefined} onChange={(e) => onChange(e.target.value)} />;
}

/** A form over a list of custom fields that saves the changed ones in one call. */
export function CustomFieldsForm({ fields, url, onSaved, readOnlyUnlessEditable }: { fields: CustomFieldValue[]; url: string; onSaved: () => void; readOnlyUnlessEditable?: boolean }) {
  const run = useRun(); const [vals, setVals] = useState<Record<string, string>>(() => Object.fromEntries(fields.map((f) => [f.key, f.value ?? ""]))); const [err, setErr] = useState<Record<string, string>>({});
  const editable = fields.filter((f) => !readOnlyUnlessEditable || f.editable);
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const changed = Object.fromEntries(editable.filter((f) => (f.value ?? "") !== vals[f.key]).map((f) => [f.key, vals[f.key] === "" ? null : vals[f.key]]));
    if (Object.keys(changed).length === 0) return;
    const r = await run(() => put(url, changed), t("Saved."), { silent: false }); if (r.ok) { setErr({}); onSaved(); } else setErr(r.fields ?? {});
  }
  return (
    <form className="stack" noValidate onSubmit={save}>
      <div className="fgrid">
        {fields.map((f) => <Field key={f.key} label={f.label} htmlFor={`cf-${f.key}`} required={f.required} error={err[f.key]}><CustomFieldInput f={f} value={vals[f.key] ?? ""} disabled={readOnlyUnlessEditable && !f.editable} onChange={(v) => setVals({ ...vals, [f.key]: v })} /></Field>)}
      </div>
      {editable.length > 0 && <div><Button variant="primary" type="submit">{t("Save")}</Button></div>}
    </form>
  );
}

/** The employee page's "More" tab: branch, the organisation's own fields, and letters issued from templates. */
export function EmployeeExtrasTab({ employeeId, canEdit }: { employeeId: string; canEdit: boolean }) {
  const run = useRun();
  const fields = useQuery(() => get<CustomFieldValue[]>(`/api/hr/employees/${employeeId}/custom-fields`), [employeeId]);
  const branches = useQuery(() => get<BranchItem[]>("/api/hr/branches"), []); const mine = useQuery(() => get<{ branchId: string | null }>(`/api/hr/employees/${employeeId}/branch`), [employeeId]);
  const letters = useQuery(() => get<IssuedLetterItem[]>("/api/hr/issued-letters", { employeeId }), [employeeId]);
  const templates = useQuery(() => (canEdit ? get<LetterTemplateItem[]>("/api/hr/letter-templates") : Promise.resolve([] as LetterTemplateItem[])), [canEdit]);
  const [issue, setIssue] = useState(false); const [tid, setTid] = useState(""); const [preview, setPreview] = useState<IssuedLetterItem | null>(null);
  const active = (templates.data ?? []).filter((x) => !x.archived);
  async function show(id: string) { setTid(id); setPreview(null); const r = await run(() => post<IssuedLetterItem>("/api/hr/issued-letters/preview", { templateId: id, employeeId }), undefined, { silent: true }); if (r.ok) setPreview(r.data); }
  async function doIssue() { const r = await run(() => post<IssuedLetterItem>("/api/hr/issued-letters", { templateId: tid, employeeId }), t("Letter issued.")); if (r.ok) { setIssue(false); letters.reload(); } }
  return (
    <div className="stack">
      <section>
        <h3 className="block-title">{t("Branch or business unit")}</h3>
        <div style={{ maxWidth: 360 }}>
          <SelectInput aria-label={t("Branch or business unit")} value={mine.data?.branchId ?? ""} disabled={!canEdit} onChange={async (e) => { if ((await run(() => put(`/api/hr/employees/${employeeId}/branch`, { branchId: e.target.value || null }), t("Branch saved."))).ok) mine.reload(); }}
            options={[{ value: "", label: t("None") }, ...(branches.data ?? []).filter((b) => !b.archived || b.id === mine.data?.branchId).map((b) => ({ value: b.id, label: `${b.name} (${b.company})` }))]} />
        </div>
      </section>
      <section>
        <h3 className="block-title">{t("Other details")}</h3>
        {fields.data && (fields.data.length === 0 ? <p className="sub">{t("Your organisation has not added its own fields yet (HR Setup → Custom Fields).")}</p>
          : canEdit ? <CustomFieldsForm key={JSON.stringify(fields.data)} fields={fields.data} url={`/api/hr/employees/${employeeId}/custom-fields`} onSaved={fields.reload} />
          : <dl className="kvs">{fields.data.map((f) => <div key={f.key}><dt>{f.label}</dt><dd>{f.value ?? "—"}</dd></div>)}</dl>)}
      </section>
      <section>
        <div className="row" style={{ justifyContent: "space-between", alignItems: "center" }}>
          <h3 className="block-title">{t("Letters")}</h3>
          {canEdit && active.length > 0 && <Button size="sm" onClick={() => { setIssue(true); void show(active[0].id); }}><FilePlus2 size={14} /> {t("Issue a letter")}</Button>}
        </div>
        {(letters.data?.length ?? 0) === 0 ? <p className="sub">{t("No letters issued yet.")}</p> : (
          <ul className="mini-list">{letters.data!.map((l) => <li key={l.id}><div><strong>{l.title}</strong><small>{l.number} · {fmtDate(l.issuedAt)}{l.visibleToEmployee ? "" : ` · ${t("HR only")}`}</small></div>
            <Button size="sm" aria-label={t("Download {name}", { name: l.number })} onClick={() => run(() => download(`/api/hr/issued-letters/${l.id}/pdf`))}><Download size={14} /></Button></li>)}</ul>)}
      </section>
      <Overlay open={issue} onClose={() => setIssue(false)} side="right" title={t("Issue a letter")} footer={<><Button onClick={() => setIssue(false)}>{t("Cancel")}</Button><Button variant="primary" disabled={!preview} onClick={doIssue}>{t("Issue")}</Button></>}>
        <div className="stack">
          <Field label={t("Template")} htmlFor="il-t"><SelectInput id="il-t" value={tid} onChange={(e) => show(e.target.value)} options={active.map((x) => ({ value: x.id, label: x.name }))} /></Field>
          {preview && <div className="letter-preview"><b>{preview.title}</b><small className="sub">{t("Number given when issued, e.g. {n}", { n: preview.number })}</small><pre>{preview.body}</pre></div>}
        </div>
      </Overlay>
    </div>
  );
}

/** Letters issued to the signed-in employee. */
export function MyIssuedLetters() {
  const run = useRun(); const q = useQuery(() => get<IssuedLetterItem[]>("/api/hr/me/issued-letters"), []);
  if ((q.data?.length ?? 0) === 0) return null;
  return (
    <Panel title={t("Letters from HR")} description={t("Letters HR issued to you, with a verification code on each.")}>
      {q.data!.length === 0 ? <EmptyState title={t("No letters")} description="" /> :
        <ul className="mini-list">{q.data!.map((l) => <li key={l.id}><div><strong>{l.title}</strong><small>{l.number} · {fmtDate(l.issuedAt)}</small></div><Button size="sm" onClick={() => run(() => download(`/api/hr/issued-letters/${l.id}/pdf`))}><Download size={14} /> PDF</Button></li>)}</ul>}
    </Panel>
  );
}

/** The organisation's own fields that HR chose to show the employee; some may be edited. */
export function MyCustomFields() {
  const q = useQuery(() => get<CustomFieldValue[]>("/api/hr/me/custom-fields"), []);
  if (!q.data || q.data.length === 0) return null;
  return (
    <Panel title={t("Other details")} description={t("Extra details your organisation keeps. Fields you cannot change are filled in by HR.")}>
      <CustomFieldsForm key={JSON.stringify(q.data)} fields={q.data} url="/api/hr/me/custom-fields" onSaved={q.reload} readOnlyUnlessEditable />
    </Panel>
  );
}
