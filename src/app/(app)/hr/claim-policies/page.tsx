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
import type { ClaimPolicyItem, SalaryGrade } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { claimLabel } from "@/components/hr/claims";
import { t } from "@/i18n";

type Form = { category: string; name: string; gradeId: string; annualLimit: string; perClaimLimit: string; taxable: boolean; active: boolean };
const blank: Form = { category: "", name: "", gradeId: "", annualLimit: "", perClaimLimit: "", taxable: false, active: true };

/** Limits per claim category (and optionally per pay grade), including the organisation's own benefit categories such as wellness or education. */
export default function ClaimPoliciesPage() {
  const run = useRun(); const q = useQuery(() => get<ClaimPolicyItem[]>("/api/hr/claims/policies"), []);
  const grades = useQuery(() => get<SalaryGrade[]>("/api/hr/payroll/grades").catch(() => [] as SalaryGrade[]), []);
  const [edit, setEdit] = useState<ClaimPolicyItem | "new" | null>(null); const [f, setF] = useState<Form>(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  function open(p: ClaimPolicyItem | "new") {
    setF(p === "new" ? blank : { category: p.category, name: p.name, gradeId: p.gradeId ?? "", annualLimit: p.annualLimit?.toString() ?? "", perClaimLimit: p.perClaimLimit?.toString() ?? "", taxable: p.taxable, active: p.active });
    setErr({}); setForm(""); setEdit(p);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!edit) return; setForm("");
    const n = (v: string) => (v.trim() === "" ? undefined : Number(v));
    const body = { category: f.category, name: f.name, gradeId: f.gradeId || undefined, annualLimit: n(f.annualLimit), perClaimLimit: n(f.perClaimLimit), taxable: f.taxable, active: f.active };
    const r = await run(() => (edit === "new" ? post("/api/hr/claims/policies", body) : put(`/api/hr/claims/policies/${edit.id}`, body)), t("Policy saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<ClaimPolicyItem>[] = [
    { key: "c", header: t("Category"), cell: (p) => <><b>{p.name}</b><small className="sub">{claimLabel(p.category)} · {p.grade ? t("grade {g}", { g: p.grade }) : t("everyone")}</small></> },
    { key: "y", header: t("Per year"), cell: (p) => (p.annualLimit != null ? idr(p.annualLimit) : t("No limit")) },
    { key: "o", header: t("Per claim"), className: "hide-sm", cell: (p) => (p.perClaimLimit != null ? idr(p.perClaimLimit) : t("No limit")) },
    { key: "t", header: t("Paid as"), className: "hide-sm", cell: (p) => (p.taxable ? <Badge tone="orange" dot={false}>{t("Taxable benefit")}</Badge> : <Badge tone="green" dot={false}>{t("Tax-free reimbursement")}</Badge>) },
    { key: "s", header: t("Status"), cell: (p) => (p.active ? <Badge tone="green">{t("Active")}</Badge> : <Badge tone="gray">{t("Off")}</Badge>) },
    { key: "x", header: "", cell: (p) => <Button size="sm" onClick={() => open(p)}>{t("Edit")}</Button> },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="claims.manage">
      <PageHeader title={t("Claim Policies")} description={t("Yearly and per-claim limits for each category, optionally per pay grade. Add your own categories for benefits such as wellness, glasses or education.")}
        actions={<Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add policy")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No claim policies")} description={t("Without a policy a category has no limit. Add one to cap medical claims or to offer a yearly benefit allowance.")} />
          : <DataTable label={t("Claim policies")} columns={cols} rows={rows} rowKey={(p) => p.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add policy") : t("Edit policy")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="cp-form">{t("Save")}</Button></>}>
        <form id="cp-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Category code")} htmlFor="cp-cat" required error={err.category} hint={t("A built-in one (transport, travel, meal, medical, training, other) or your own, e.g. wellness.")}><TextInput id="cp-cat" data-autofocus value={f.category} invalid={!!err.category} onChange={(e) => setF({ ...f, category: e.target.value })} /></Field>
            <Field label={t("Name employees see")} htmlFor="cp-name" required error={err.name}><TextInput id="cp-name" value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("Medical")} /></Field>
            <Field label={t("Yearly limit per person (IDR)")} htmlFor="cp-year" error={err.annualLimit}><TextInput id="cp-year" inputMode="decimal" value={f.annualLimit} invalid={!!err.annualLimit} onChange={(e) => setF({ ...f, annualLimit: e.target.value })} placeholder={t("No limit")} /></Field>
            <Field label={t("Limit per claim (IDR)")} htmlFor="cp-one" error={err.perClaimLimit}><TextInput id="cp-one" inputMode="decimal" value={f.perClaimLimit} invalid={!!err.perClaimLimit} onChange={(e) => setF({ ...f, perClaimLimit: e.target.value })} placeholder={t("No limit")} /></Field>
            <Field label={t("Applies to")} htmlFor="cp-grade" error={err.gradeId}><SelectInput id="cp-grade" value={f.gradeId} onChange={(e) => setF({ ...f, gradeId: e.target.value })} options={[{ value: "", label: t("Everyone") }, ...(grades.data ?? []).filter((g) => !g.archived).map((g) => ({ value: g.id, label: `${t("Grade")} ${g.code} · ${g.name}` }))]} /></Field>
          </div>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.taxable} onChange={(e) => setF({ ...f, taxable: e.target.checked })} /> {t("Taxable benefit (paid as income in the pay run, not as a tax-free reimbursement)")}</label>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.active} onChange={(e) => setF({ ...f, active: e.target.checked })} /> {t("Active")}</label>
        </form>
      </Overlay>
    </Require>
  );
}
