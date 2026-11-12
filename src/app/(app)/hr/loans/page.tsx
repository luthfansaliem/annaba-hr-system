"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { del, get, post } from "@/lib/api/client";
import type { CompensationRow, LoanItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** PAY-006 — advances repaid through payroll. Every calculation takes the instalment as its own deduction line; locking the pay run lowers the balance. */
export default function LoansPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<LoanItem[]>("/api/hr/payroll/loans"), []); const emps = useQuery(() => get<CompensationRow[]>("/api/hr/payroll/compensation"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ employeeId: "", principal: "", installment: "", note: "", rate: "", tenor: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const rows = q.data ?? [];
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/payroll/loans", { employeeId: f.employeeId || emps.data?.[0]?.employeeId, principal: Number(f.principal), installment: f.tenor ? undefined : Number(f.installment), note: f.note || undefined, interestRatePct: f.rate ? Number(f.rate) : undefined, tenorMonths: f.tenor ? Number(f.tenor) : undefined }), t("Loan added."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<LoanItem>[] = [
    { key: "e", header: t("Employee"), cell: (l) => <><b>{l.employeeName}</b><small className="sub">{l.employeeNo}{l.note ? ` · ${l.note}` : ""}</small></> },
    { key: "p", header: t("Loan"), className: "hide-sm", cell: (l) => <>{idr(l.principal)}{!!l.interestRatePct && <small className="sub">{t("{rate}% a year · {months} months · {total} in total", { rate: String(l.interestRatePct), months: String(l.tenorMonths ?? "—"), total: idr(l.totalPayable) })}</small>}</> }, { key: "i", header: t("Instalment"), className: "hide-sm", cell: (l) => idr(l.installment) }, { key: "r", header: t("Remaining"), cell: (l) => idr(l.remaining) },
    { key: "s", header: t("Status"), cell: (l) => <Badge tone={l.status === "active" ? "blue" : l.status === "paidoff" ? "green" : "gray"}>{l.status === "active" ? t("Active") : l.status === "paidoff" ? t("Paid off") : t("Cancelled")}</Badge> },
    { key: "x", header: "", cell: (l) => can("payroll.configure") && l.status === "active" ? <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/hr/payroll/loans/${l.id}`), t("Loan cancelled."))).ok) q.reload(); }}>{t("Cancel loan")}</Button> : null },
  ];
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Loans")} description={t("Advances repaid through payroll, one instalment a month.")} actions={can("payroll.configure") && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ employeeId: "", principal: "", installment: "", note: "", rate: "", tenor: "" }); setOpen(true); }}><Plus size={16} /> {t("New loan")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No loans")} description={t("Loans you add appear here.")} />
          : <DataTable label={t("Loans")} columns={cols} rows={rows} rowKey={(l) => l.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New loan")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="loan-form">{t("Save")}</Button></>}>
        <form id="loan-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Employee")} htmlFor="ln-who" required error={err.employeeId}><SelectInput id="ln-who" value={f.employeeId || emps.data?.[0]?.employeeId || ""} onChange={(e) => setF({ ...f, employeeId: e.target.value })} options={(emps.data ?? []).map((c) => ({ value: c.employeeId, label: `${c.employeeName} (${c.employeeNo})` }))} /></Field>
          <div className="fgrid">
            <Field label={t("Loan amount (IDR)")} htmlFor="ln-amt" required error={err.principal}><TextInput id="ln-amt" inputMode="decimal" value={f.principal} invalid={!!err.principal} onChange={(e) => setF({ ...f, principal: e.target.value })} /></Field>
            <Field label={t("Term (months)")} htmlFor="ln-tenor" error={err.tenorMonths} hint={t("With a term the instalment is worked out for you.")}><TextInput id="ln-tenor" inputMode="numeric" value={f.tenor} invalid={!!err.tenorMonths} onChange={(e) => setF({ ...f, tenor: e.target.value })} placeholder={t("Optional")} /></Field>
            <Field label={t("Interest per year (%)")} htmlFor="ln-rate" error={err.interestRatePct} hint={t("Flat rate on the loan amount; leave empty for an interest-free loan.")}><TextInput id="ln-rate" inputMode="decimal" value={f.rate} invalid={!!err.interestRatePct} onChange={(e) => setF({ ...f, rate: e.target.value })} placeholder="0" /></Field>
            {!f.tenor && <Field label={t("Monthly instalment (IDR)")} htmlFor="ln-inst" required error={err.installment}><TextInput id="ln-inst" inputMode="decimal" value={f.installment} invalid={!!err.installment} onChange={(e) => setF({ ...f, installment: e.target.value })} /></Field>}
          </div>
          <Field label={t("Note")} htmlFor="ln-note" error={err.note}><TextInput id="ln-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
