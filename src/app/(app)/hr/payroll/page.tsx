"use client";

import { useState } from "react";
import Link from "next/link";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { download } from "@/lib/download";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { PayStatusBadge } from "@/components/hr/pay-status";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { LegalEntityItem, PayRunSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { idr, monthName } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export default function PayRunsPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<PayRunSummary[]>("/api/hr/payroll/runs"), []); const companies = useQuery(() => get<LegalEntityItem[]>("/api/hr/legal-entities"), []); const multi = (companies.data?.length ?? 0) > 1;
  const now = new Date(); const [open, setOpen] = useState(false); const [f, setF] = useState({ year: String(now.getFullYear()), month: String(now.getMonth() + 1), note: "", deductAbsence: false, legalEntityId: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const rows = q.data ?? [];
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<PayRunSummary>("/api/hr/payroll/runs", { year: Number(f.year), month: Number(f.month), note: f.note || undefined, deductAbsence: f.deductAbsence, legalEntityId: f.legalEntityId || undefined }), t("Pay run created."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<PayRunSummary>[] = [
    { key: "p", header: t("Period"), cell: (r) => <Link href={`/hr/payroll/${r.id}`}><b>{monthName(r.year, r.month)}</b></Link> },
    ...(multi ? [{ key: "co", header: t("Company"), cell: (r: PayRunSummary) => r.legalEntityName ?? "—" }] : []),
    { key: "s", header: t("Status"), cell: (r) => <PayStatusBadge status={r.status} /> }, { key: "n", header: t("Employees"), className: "hide-sm", cell: (r) => r.employeeCount },
    { key: "g", header: t("Gross"), className: "hide-md", cell: (r) => idr(r.totalGross) }, { key: "t", header: t("PPh 21"), className: "hide-md", cell: (r) => idr(r.totalTax) }, { key: "net", header: t("Net pay"), cell: (r) => idr(r.totalNet) },
  ];
  const years = [now.getFullYear() + 1, now.getFullYear(), now.getFullYear() - 1, now.getFullYear() - 2].map(String);
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Pay Runs")} description={t("One run per month: prepare, check, approve, lock, pay.")} actions={<div className="row" style={{ gap: 8 }}><Button onClick={() => run(() => download(`/api/hr/payroll/reports/annual-tax?year=${now.getFullYear()}`), t("File downloaded."))}>{t("Annual tax summary")}</Button>{can("payroll.run") && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setOpen(true); }}><Plus size={16} /> {t("New pay run")}</Button>}</div>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No pay runs yet")} description={t("Set each employee's compensation, then create the first pay run.")} />
          : <DataTable label={t("Pay runs")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New pay run")} description={t("Covers every employee of the chosen company who was employed during the month.")}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="run-form">{t("Create")}</Button></>}>
        <form id="run-form" className="stack" noValidate onSubmit={create}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {multi && <Field label={t("Company")} htmlFor="pr-co" error={err.legalEntityId}><SelectInput id="pr-co" value={f.legalEntityId} onChange={(e) => setF({ ...f, legalEntityId: e.target.value })} options={[{ value: "", label: t("Default company") }, ...companies.data!.map((c) => ({ value: c.id, label: c.name }))]} /></Field>}
          <div className="fgrid">
            <Field label={t("Month")} htmlFor="pr-month" error={err.month}><SelectInput id="pr-month" value={f.month} onChange={(e) => setF({ ...f, month: e.target.value })} options={Array.from({ length: 12 }, (_, i) => ({ value: String(i + 1), label: monthName(2000, i + 1).split(" ")[0] }))} /></Field>
            <Field label={t("Year")} htmlFor="pr-year" error={err.year}><SelectInput id="pr-year" value={f.year} onChange={(e) => setF({ ...f, year: e.target.value })} options={years} /></Field>
          </div>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.deductAbsence} onChange={(e) => setF({ ...f, deductAbsence: e.target.checked })} /> {t("Deduct unpaid absences (needs the month's attendance to be closed)")}</label>
          <Field label={t("Note")} htmlFor="pr-note"><TextInput id="pr-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
