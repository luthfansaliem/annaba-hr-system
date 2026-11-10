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
import { del, get, post } from "@/lib/api/client";
import type { CompensationRow, RecurringPayItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const blank = { employeeId: "", kind: "earning", label: "", amount: "", taxable: true, from: "", through: "" };
const ym = (d: string | null) => (d ? d.slice(0, 7) : "");

/** PAY-001 — pay components that repeat every month for one person (allowances, fees). They are added to each pay run as system lines while in force. */
export default function RecurringPayPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<RecurringPayItem[]>("/api/hr/payroll/recurring"), []); const emps = useQuery(() => get<CompensationRow[]>("/api/hr/payroll/compensation"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const rows = q.data ?? [];
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { employeeId: f.employeeId || emps.data?.[0]?.employeeId, kind: f.kind, label: f.label, amount: Number(f.amount), taxable: f.kind === "earning" ? f.taxable : false, from: f.from ? `${f.from}-01` : undefined, through: f.through ? `${f.through}-01` : undefined };
    const r = await run(() => post("/api/hr/payroll/recurring", body), t("Recurring item added."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<RecurringPayItem>[] = [
    { key: "e", header: t("Employee"), cell: (l) => <><b>{l.employeeName}</b><small className="sub">{l.employeeNo}</small></> },
    { key: "l", header: t("Item"), cell: (l) => <>{l.label}<small className="sub">{l.kind === "earning" ? (l.taxable ? t("Earning · taxable") : t("Earning · not taxable")) : t("Deduction")}</small></> },
    { key: "a", header: t("Amount / month"), cell: (l) => idr(l.amount) },
    { key: "p", header: t("Months"), className: "hide-sm", cell: (l) => `${ym(l.from)} → ${l.through ? ym(l.through) : "…"}` },
    { key: "s", header: t("Status"), className: "hide-sm", cell: (l) => <Badge tone={l.ended ? "gray" : "blue"}>{l.ended ? t("Ended") : t("Active")}</Badge> },
    { key: "x", header: "", cell: (l) => can("payroll.configure") && !l.ended ? <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/hr/payroll/recurring/${l.id}`), t("Recurring item ended."))).ok) q.reload(); }}>{t("End")}</Button> : null },
  ];
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Recurring Pay Items")} description={t("Allowances and deductions that repeat every month. They join each pay run automatically while in force.")} actions={can("payroll.configure") && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF(blank); setOpen(true); }}><Plus size={16} /> {t("New item")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No recurring items")} description={t("A transport allowance or a cooperative fee, for example.")} />
          : <DataTable label={t("Recurring Pay Items")} columns={cols} rows={rows} rowKey={(l) => l.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New item")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rc-form">{t("Save")}</Button></>}>
        <form id="rc-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Employee")} htmlFor="rc-who" required error={err.employeeId}><SelectInput id="rc-who" value={f.employeeId || emps.data?.[0]?.employeeId || ""} onChange={(e) => setF({ ...f, employeeId: e.target.value })} options={(emps.data ?? []).map((c) => ({ value: c.employeeId, label: `${c.employeeName} (${c.employeeNo})` }))} /></Field>
          <div className="fgrid">
            <Field label={t("Type")} htmlFor="rc-kind" error={err.kind}><SelectInput id="rc-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })} options={[{ value: "earning", label: t("Earning") }, { value: "deduction", label: t("Deduction") }]} /></Field>
            <Field label={t("Amount per month (IDR)")} htmlFor="rc-amt" required error={err.amount}><TextInput id="rc-amt" inputMode="decimal" value={f.amount} invalid={!!err.amount} onChange={(e) => setF({ ...f, amount: e.target.value })} /></Field>
          </div>
          <Field label={t("Label")} htmlFor="rc-label" required error={err.label}><TextInput id="rc-label" data-autofocus value={f.label} invalid={!!err.label} onChange={(e) => setF({ ...f, label: e.target.value })} placeholder={t("Transport allowance")} /></Field>
          {f.kind === "earning" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.taxable} onChange={(e) => setF({ ...f, taxable: e.target.checked })} /> {t("Counts as taxable income")}</label>}
          <div className="fgrid">
            <Field label={t("First month")} htmlFor="rc-from" error={err.from}><TextInput id="rc-from" type="month" value={f.from} invalid={!!err.from} onChange={(e) => setF({ ...f, from: e.target.value })} /></Field>
            <Field label={t("Last month (optional)")} htmlFor="rc-thru" error={err.through}><TextInput id="rc-thru" type="month" value={f.through} invalid={!!err.through} onChange={(e) => setF({ ...f, through: e.target.value })} /></Field>
          </div>
        </form>
      </Overlay>
    </Require>
  );
}
