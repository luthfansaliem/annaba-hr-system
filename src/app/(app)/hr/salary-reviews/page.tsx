"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { REVIEW_STATUS } from "@/components/hr/review-status";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { SalaryReviewSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";


/** Salary review rounds: prepared by payroll, approved by a second person, then applied as new salary periods on one date. */
export default function SalaryReviewsPage() {
  const { can } = useAuth(); const run = useRun(); const router = useRouter(); const q = useQuery(() => get<SalaryReviewSummary[]>("/api/hr/payroll/reviews"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ name: "", effectiveFrom: "", note: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<SalaryReviewSummary>("/api/hr/payroll/reviews", { name: f.name, effectiveFrom: f.effectiveFrom || undefined, note: f.note || undefined }), t("Review created."), { silent: true });
    if (r.ok) router.push(`/hr/salary-reviews/${r.data.id}`); else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<SalaryReviewSummary>[] = [
    { key: "n", header: t("Review"), cell: (r) => <><Link href={`/hr/salary-reviews/${r.id}`}><b>{r.name}</b></Link><small className="sub">{t("New salaries from {date}", { date: fmtDate(r.effectiveFrom) })}</small></> },
    { key: "e", header: t("Employees"), className: "hide-sm", cell: (r) => r.employees },
    { key: "s", header: t("Status"), cell: (r) => <><Badge tone={REVIEW_STATUS[r.status][0]}>{t(REVIEW_STATUS[r.status][1])}</Badge>{r.note && <small className="sub">{r.note}</small>}</> },
    { key: "x", header: "", cell: (r) => <Button size="sm" href={`/hr/salary-reviews/${r.id}`}>{t("Open")}</Button> },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Salary Reviews")} description={t("Annual increases and promotion rounds in one batch: prepare the proposals, have them approved by someone else, then apply them on one date.")}
        actions={can("payroll.configure") && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ name: "", effectiveFrom: "", note: "" }); setOpen(true); }}><Plus size={16} /> {t("New review")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No salary reviews")} description={t("Start a round, add people or a whole department with a percentage, and adjust each proposal.")} />
          : <DataTable label={t("Salary reviews")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New review")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rv-form">{t("Create")}</Button></>}>
        <form id="rv-form" className="stack" noValidate onSubmit={create}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Name")} htmlFor="rv-name" required error={err.name}><TextInput id="rv-name" data-autofocus value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("Annual review 2027")} /></Field>
          <Field label={t("New salaries start on")} htmlFor="rv-from" required error={err.effectiveFrom}><TextInput id="rv-from" type="date" value={f.effectiveFrom} invalid={!!err.effectiveFrom} onChange={(e) => setF({ ...f, effectiveFrom: e.target.value })} /></Field>
          <Field label={t("Note")} htmlFor="rv-note"><TextInput id="rv-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
