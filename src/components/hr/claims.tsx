"use client";

import { useRef, useState } from "react";
import { Paperclip, Plus } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { ClaimBalance, ClaimItem, Paged } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate, idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const CLAIM_CATEGORIES = ["transport", "travel", "meal", "medical", "training", "other"];
export const claimLabel = (c: string) => t(({ transport: "Transport", travel: "Business travel", meal: "Meals", medical: "Medical", training: "Training", other: "Other" } as Record<string, string>)[c] ?? c);
const TONE: Record<string, ["orange" | "green" | "red" | "gray" | "blue", string]> = { pending: ["orange", "Pending"], approved: ["blue", "Approved · paid with the next payslip"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"], paid: ["green", "Paid"] };
const MAX = 4 * 1024 * 1024;

function toBase64(buf: ArrayBuffer): string {
  let s = ""; const bytes = new Uint8Array(buf); const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) s += String.fromCharCode(...bytes.subarray(i, i + chunk));
  return btoa(s);
}

/** ESS-004 — expense claims. scope "mine" lets the person submit and cancel; "all" is the payroll view with decisions. Buttons appear only where the server says the caller may act. */
export function ClaimsPanel({ scope }: { scope: "mine" | "all" }) {
  const run = useRun(); const [status, setStatus] = useState("all");
  const q = useQuery(() => get<Paged<ClaimItem>>("/api/hr/claims", { scope, status, pageSize: 100 }), [scope, status]);
  const [open, setOpen] = useState(false); const today = new Date().toISOString().slice(0, 10);
  const [f, setF] = useState({ category: "transport", expenseDate: today, amount: "", description: "" }); const file = useRef<HTMLInputElement>(null);
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [busy, setBusy] = useState(false);
  const [reject, setReject] = useState<ClaimItem | null>(null); const [note, setNote] = useState(""); const [noteErr, setNoteErr] = useState("");
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  // the person's own categories include the organisation's benefit categories (from the claim policies)
  const balances = useQuery(() => (scope === "mine" ? get<ClaimBalance[]>("/api/hr/claims/balances") : Promise.resolve([] as ClaimBalance[])), [scope]);
  const categories = (balances.data?.length ? balances.data.map((b) => ({ value: b.category, label: b.name === b.category ? claimLabel(b.category) : b.name })) : CLAIM_CATEGORIES.map((c) => ({ value: c, label: claimLabel(c) })));
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm(""); const picked = file.current?.files?.[0];
    if (picked && picked.size > MAX) { setErr({ receipt: t("The file is larger than 4 MB.") }); return; }
    setBusy(true);
    const r = await run(async () => post("/api/hr/claims", { category: f.category, expenseDate: f.expenseDate, amount: Number(f.amount), description: f.description, receiptName: picked?.name, receiptBase64: picked ? toBase64(await picked.arrayBuffer()) : undefined }), t("Claim submitted."), { silent: true });
    setBusy(false);
    if (r.ok) { setOpen(false); q.reload(); balances.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const decide = async (c: ClaimItem, kind: "approve" | "reject", why?: string) => {
    const r = await run(() => post(`/api/hr/claims/${c.id}/${kind}`, { note: why }), kind === "approve" ? t("Claim approved.") : t("Claim rejected."), { silent: kind === "reject" });
    if (r.ok) { setReject(null); setNote(""); q.reload(); } else if (kind === "reject") setNoteErr(r.fields?.note ?? r.error);
  };
  const cols: Column<ClaimItem>[] = [
    ...(scope === "all" ? [{ key: "e", header: t("Employee"), cell: (c: ClaimItem) => <><b>{c.employeeName}</b><small className="sub">{c.employeeNo}</small></> }] : []),
    { key: "c", header: t("Expense"), cell: (c) => <><b>{claimLabel(c.category)}</b><small className="sub">{fmtDate(c.expenseDate)} · {c.description}</small></> },
    { key: "a", header: t("Amount"), cell: (c) => idr(c.amount) },
    { key: "s", header: t("Status"), cell: (c) => <><Badge tone={TONE[c.status]?.[0] ?? "gray"}>{t(TONE[c.status]?.[1] ?? c.status)}</Badge>{c.status === "pending" && c.waitingFor && c.progress && c.progress.split("/")[1] !== "1" && <small className="sub">{t("Step {p} · waiting for {who}", { p: c.progress, who: t(({ manager: "the manager", manager2: "the manager's manager", hr: "HR" } as Record<string, string>)[c.waitingFor] ?? c.waitingFor) })}</small>}{c.decisionNote && <small className="sub">{c.decisionNote}</small>}</> },
    { key: "x", header: "", cell: (c) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end", flexWrap: "wrap" }}>
        {c.hasReceipt && <Button size="sm" aria-label={t("Open the receipt")} onClick={() => run(() => download(`/api/hr/claims/${c.id}/receipt`))}><Paperclip size={14} /></Button>}
        {c.canDecide && <Button size="sm" variant="primary" onClick={() => decide(c, "approve")}>{t("Approve")}</Button>}
        {c.canDecide && <Button size="sm" variant="danger-outline" onClick={() => { setNote(""); setNoteErr(""); setReject(c); }}>{t("Reject")}</Button>}
        {c.canCancel && <Button size="sm" onClick={async () => { if ((await run(() => post(`/api/hr/claims/${c.id}/cancel`), t("Claim cancelled."))).ok) q.reload(); }}>{t("Cancel")}</Button>}
      </div>) },
  ];
  return (
    <>
      <Panel actions={<div className="row" style={{ gap: 8, alignItems: "center", flexWrap: "wrap" }}>
        <SelectInput aria-label={t("Status")} value={status} onChange={(e) => setStatus(e.target.value)} options={[{ value: "all", label: t("All") }, ...Object.keys(TONE).map((k) => ({ value: k, label: t(TONE[k][1].split(" · ")[0]) }))]} />
        {scope === "mine" && !unlinked && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ category: "transport", expenseDate: today, amount: "", description: "" }); setOpen(true); }}><Plus size={16} /> {t("New claim")}</Button>}
      </div>}>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !q.loading && !q.error && (q.data?.data.length ?? 0) === 0 ? <EmptyState title={t("No claims")} description={scope === "mine" ? t("Money you spent for work and want back goes here.") : t("Claims appear here once employees submit them.")} />
          : <DataTable label={t("Expense claims")} columns={cols} rows={q.data?.data ?? []} rowKey={(c) => c.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New claim")} description={t("Approved claims are paid with your next payslip; benefits marked taxable count as income.")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="claim-form" disabled={busy}>{busy ? t("Uploading…") : t("Submit")}</Button></>}>
        <form id="claim-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Category")} htmlFor="cl-cat" error={err.category}><SelectInput id="cl-cat" value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })} options={categories} /></Field>
            <Field label={t("Date of the expense")} htmlFor="cl-date" required error={err.expenseDate}><TextInput id="cl-date" type="date" value={f.expenseDate} invalid={!!err.expenseDate} onChange={(e) => setF({ ...f, expenseDate: e.target.value })} /></Field>
          </div>
          <Field label={t("Amount (IDR)")} htmlFor="cl-amt" required error={err.amount}><TextInput id="cl-amt" inputMode="decimal" data-autofocus value={f.amount} invalid={!!err.amount} onChange={(e) => setF({ ...f, amount: e.target.value })} /></Field>
          <Field label={t("What was it for?")} htmlFor="cl-desc" required error={err.description}><TextInput id="cl-desc" value={f.description} invalid={!!err.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></Field>
          <Field label={t("Receipt (PDF, PNG or JPEG, up to 4 MB)")} htmlFor="cl-file" error={err.receipt}><input id="cl-file" ref={file} type="file" accept="application/pdf,image/png,image/jpeg" /></Field>
        </form>
      </Overlay>
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject claim")} footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="claim-reject">{t("Reject")}</Button></>}>
        <form id="claim-reject" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); if (reject) void decide(reject, "reject", note); }}>
          <Field label={t("Reason (the employee will see it)")} htmlFor="cr-note" required error={noteErr}><TextInput id="cr-note" data-autofocus value={note} invalid={!!noteErr} onChange={(e) => setNote(e.target.value)} /></Field>
        </form>
      </Overlay>
    </>
  );
}
