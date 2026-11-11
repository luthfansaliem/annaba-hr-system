"use client";

import { useState } from "react";
import { Download, Plus } from "lucide-react";
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
import { download } from "@/lib/download";
import type { LegalEntityItem, PayoutDetail, PayoutSummary } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate, idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TONE: Record<string, ["gray" | "blue" | "green" | "orange", string]> = { draft: ["gray", "Draft"], calculated: ["orange", "Calculated"], approved: ["blue", "Approved"], paid: ["green", "Paid"] };
const lastMonday = () => { const d = new Date(); d.setDate(d.getDate() - ((d.getDay() + 6) % 7) - 7); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };

/** PAY-013 — weekly pay for daily-wage workers. Each payout is an interim payment; the monthly pay run computes tax and BPJS for the whole month and deducts what was paid here. */
export default function WeeklyPayoutsPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<PayoutSummary[]>("/api/hr/payroll/payouts"), []); const companies = useQuery(() => get<LegalEntityItem[]>("/api/hr/legal-entities"), []);
  const multi = (companies.data?.length ?? 0) > 1;
  const [open, setOpen] = useState(false); const [f, setF] = useState({ weekStart: lastMonday(), holdbackPct: "10", legalEntityId: "", note: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [detail, setDetail] = useState<PayoutDetail | null>(null);
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<PayoutSummary>("/api/hr/payroll/payouts", { weekStart: f.weekStart, holdbackPct: Number(f.holdbackPct), legalEntityId: f.legalEntityId || undefined, note: f.note || undefined }), t("Weekly payout created."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); void show(r.data.id); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const show = async (id: string) => { const r = await run(() => get<PayoutDetail>(`/api/hr/payroll/payouts/${id}`)); if (r.ok) setDetail(r.data); };
  const act = async (path: string, msg: string) => { if (!detail) return; const r = await run(() => post(`/api/hr/payroll/payouts/${detail.payout.id}/${path}`), msg); if (r.ok) { q.reload(); void show(detail.payout.id); } };
  const remove = async () => { if (!detail) return; if ((await run(() => del(`/api/hr/payroll/payouts/${detail.payout.id}`), t("Weekly payout deleted."))).ok) { setDetail(null); q.reload(); } };
  const week = (p: PayoutSummary) => `${fmtDate(p.weekStart)} – ${fmtDate(p.weekEnd)}`;
  const cols: Column<PayoutSummary>[] = [
    { key: "w", header: t("Week"), cell: (p) => <button type="button" className="linklike" onClick={() => show(p.id)}><b>{week(p)}</b></button> },
    ...(multi ? [{ key: "c", header: t("Company"), cell: (p: PayoutSummary) => p.legalEntityName ?? "—" }] : []),
    { key: "s", header: t("Status"), cell: (p) => <Badge tone={TONE[p.status]?.[0] ?? "gray"}>{t(TONE[p.status]?.[1] ?? p.status)}</Badge> },
    { key: "n", header: t("Workers"), className: "hide-sm", cell: (p) => p.employeeCount },
    { key: "g", header: t("Wages"), className: "hide-md", cell: (p) => idr(p.totalGross) }, { key: "p", header: t("Paid out"), cell: (p) => idr(p.totalNet) },
  ];
  const p = detail?.payout;
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Weekly Payouts")} description={t("Weekly pay for daily-wage workers. Tax and BPJS are settled in the monthly pay run, which deducts what was paid here.")} actions={can("payroll.run") && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ weekStart: lastMonday(), holdbackPct: "10", legalEntityId: "", note: "" }); setOpen(true); }}><Plus size={16} /> {t("New weekly payout")}</Button>} />
      <Panel>
        {!q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No weekly payouts")} description={t("Set a worker's pay basis to daily in Compensation, then create a payout after the week has ended.")} />
          : <DataTable label={t("Weekly Payouts")} columns={cols} rows={q.data ?? []} rowKey={(x) => x.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New weekly payout")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="wp-form">{t("Create")}</Button></>}>
        <form id="wp-form" className="stack" noValidate onSubmit={create}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {multi && <Field label={t("Company")} htmlFor="wp-co" error={err.legalEntityId}><SelectInput id="wp-co" value={f.legalEntityId} onChange={(e) => setF({ ...f, legalEntityId: e.target.value })} options={[{ value: "", label: t("Default company") }, ...companies.data!.map((c) => ({ value: c.id, label: c.name }))]} /></Field>}
          <Field label={t("Week starting on (a Monday)")} htmlFor="wp-week" required error={err.weekStart}><TextInput id="wp-week" type="date" value={f.weekStart} invalid={!!err.weekStart} onChange={(e) => setF({ ...f, weekStart: e.target.value })} /></Field>
          <Field label={t("Holdback for tax and BPJS (%)")} htmlFor="wp-hold" error={err.holdbackPct}><TextInput id="wp-hold" inputMode="decimal" value={f.holdbackPct} invalid={!!err.holdbackPct} onChange={(e) => setF({ ...f, holdbackPct: e.target.value })} /><small className="sub">{t("Kept from each week's wage and paid out, after tax and BPJS, in the monthly pay run.")}</small></Field>
          <Field label={t("Note")} htmlFor="wp-note"><TextInput id="wp-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
      <Overlay open={!!detail} onClose={() => setDetail(null)} side="right" title={p ? `${t("Weekly payout")} ${week(p)}` : ""} description={p ? `${t(TONE[p.status]?.[1] ?? p.status)} · ${t("holdback")} ${p.holdbackPct}%` : undefined} footer={<Button onClick={() => setDetail(null)}>{t("Close")}</Button>}>
        {p && detail && <div className="stack">
          <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            {p.canCalculate && <Button variant="primary" onClick={() => act("calculate", t("Calculated."))}>{p.status === "draft" ? t("Calculate") : t("Recalculate")}</Button>}
            {p.canApprove && <Button variant="primary" onClick={() => act("approve", t("Approved."))}>{t("Approve")}</Button>}
            {p.canMarkPaid && <Button variant="primary" onClick={() => act("paid", t("Marked as paid."))}>{t("Mark as paid")}</Button>}
            {(p.status === "approved" || p.status === "paid") && can("payroll.lock") && <Button onClick={() => run(() => download(`/api/hr/payroll/payouts/${p.id}/bank-file`), t("File downloaded."))}><Download size={15} /> {t("Bank file")}</Button>}
            {(p.status === "draft" || p.status === "calculated") && can("payroll.run") && <Button variant="danger-outline" onClick={remove}>{t("Delete")}</Button>}
          </div>
          {p.status === "calculated" && !p.canApprove && <div className="form-alert" role="note">{t("Another person with the approve permission must check this payout.")}</div>}
          {detail.items.length === 0 ? <p className="tab-placeholder">{p.status === "draft" ? t("Not calculated yet.") : t("No daily-wage worker has days in this week.")}</p> : (
            <div style={{ overflowX: "auto" }}><table className="tbl" aria-label={t("Workers in this payout")}><thead><tr><th>{t("Worker")}</th><th>{t("Days")}</th><th>{t("Wages")}</th><th>{t("Held back")}</th><th>{t("Paid out")}</th></tr></thead>
              <tbody>{detail.items.map((i) => <tr key={i.id}><td><b>{i.employeeName}</b><small className="sub">{i.employeeNo} · {idr(i.dailyRate)} / {t("day")}{i.settled ? ` · ${t("settled in the monthly run")}` : ""}</small></td><td>{i.days}</td><td>{idr(i.gross)}</td><td>{idr(i.holdback)}</td><td><b>{idr(i.net)}</b></td></tr>)}</tbody></table></div>)}
        </div>}
      </Overlay>
    </Require>
  );
}
