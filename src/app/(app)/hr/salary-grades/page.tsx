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
import type { SalaryGrade } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Form = { code: string; name: string; minPay: string; midPay: string; maxPay: string; archived: boolean };
const blank: Form = { code: "", name: "", minPay: "", midPay: "", maxPay: "", archived: false };

/** Pay bands: the range a basic salary should fall in for each grade. Compensation shows where each person sits in their band. */
export default function SalaryGradesPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<SalaryGrade[]>("/api/hr/payroll/grades"), []);
  const [edit, setEdit] = useState<SalaryGrade | "new" | null>(null); const [f, setF] = useState<Form>(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  function open(g: SalaryGrade | "new") { setF(g === "new" ? blank : { code: g.code, name: g.name, minPay: String(g.minPay), midPay: String(g.midPay), maxPay: String(g.maxPay), archived: g.archived }); setErr({}); setForm(""); setEdit(g); }
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!edit) return; setForm("");
    const n = (v: string) => (v.trim() === "" ? undefined : Number(v));
    const body = { code: f.code, name: f.name, minPay: n(f.minPay), midPay: n(f.midPay), maxPay: n(f.maxPay), archived: f.archived };
    const r = await run(() => (edit === "new" ? post("/api/hr/payroll/grades", body) : put(`/api/hr/payroll/grades/${edit.id}`, body)), t("Grade saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<SalaryGrade>[] = [
    { key: "c", header: t("Grade"), cell: (g) => <><b>{g.code}</b> {g.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{g.name}</small></> },
    { key: "r", header: t("Range"), cell: (g) => <>{idr(g.minPay)} – {idr(g.maxPay)}<small className="sub">{t("Midpoint {n}", { n: idr(g.midPay) })}</small></> },
    { key: "e", header: t("Employees"), className: "hide-sm", cell: (g) => g.employees },
    { key: "x", header: "", cell: (g) => (can("payroll.configure") ? <Button size="sm" onClick={() => open(g)}>{t("Edit")}</Button> : null) },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Salary Grades")} description={t("Pay bands with a minimum, midpoint and maximum basic salary. Place each employee in a grade under Compensation.")}
        actions={can("payroll.configure") && <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add grade")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No salary grades")} description={t("Grades help keep pay fair and consistent. Add your first band, for example G1 Staff.")} />
          : <DataTable label={t("Salary grades")} columns={cols} rows={rows} rowKey={(g) => g.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add grade") : t("Edit grade")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="gr-form">{t("Save")}</Button></>}>
        <form id="gr-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Code")} htmlFor="gr-code" required error={err.code}><TextInput id="gr-code" data-autofocus value={f.code} invalid={!!err.code} onChange={(e) => setF({ ...f, code: e.target.value })} placeholder="G5" /></Field>
            <Field label={t("Name")} htmlFor="gr-name" required error={err.name}><TextInput id="gr-name" value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("Senior staff")} /></Field>
            <Field label={t("Minimum (IDR / month)")} htmlFor="gr-min" required error={err.minPay}><TextInput id="gr-min" inputMode="decimal" value={f.minPay} invalid={!!err.minPay} onChange={(e) => setF({ ...f, minPay: e.target.value })} /></Field>
            <Field label={t("Maximum (IDR / month)")} htmlFor="gr-max" required error={err.maxPay}><TextInput id="gr-max" inputMode="decimal" value={f.maxPay} invalid={!!err.maxPay} onChange={(e) => setF({ ...f, maxPay: e.target.value })} /></Field>
            <Field label={t("Midpoint (IDR / month)")} htmlFor="gr-mid" error={err.midPay} hint={t("Leave empty to use the middle of the range.")}><TextInput id="gr-mid" inputMode="decimal" value={f.midPay} invalid={!!err.midPay} onChange={(e) => setF({ ...f, midPay: e.target.value })} /></Field>
          </div>
          {edit !== "new" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived (no longer offered)")}</label>}
        </form>
      </Overlay>
    </Require>
  );
}
