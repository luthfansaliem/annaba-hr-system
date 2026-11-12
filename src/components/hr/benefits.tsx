"use client";

import { useRef, useState } from "react";
import { Coins, Paperclip, Plus } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge, type BadgeTone } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { CashAdvanceItem, ClaimBalance, WageAdvanceAvailability, WageAdvanceItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate, idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { claimLabel } from "@/components/hr/claims";
import { t } from "@/i18n";

const MAX = 4 * 1024 * 1024;
function toBase64(buf: ArrayBuffer): string {
  let s = ""; const bytes = new Uint8Array(buf); const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) s += String.fromCharCode(...bytes.subarray(i, i + chunk));
  return btoa(s);
}

/** What the person may still claim this year per category (only categories with a limit, or a taxable benefit). */
export function ClaimBalances() {
  const q = useQuery(() => get<ClaimBalance[]>("/api/hr/claims/balances"), []);
  const rows = (q.data ?? []).filter((b) => b.annualLimit != null || b.perClaimLimit != null || b.taxable);
  if (rows.length === 0) return null;
  return (
    <Panel title={t("Your claim limits this year")} description={t("Waiting and approved claims count against the limit.")}>
      <div className="kpis">
        {rows.map((b) => (
          <div key={b.category} className="kpi">
            <span className="kpi-label">{b.name === b.category ? claimLabel(b.category) : b.name}{b.taxable && <> · {t("taxable")}</>}</span>
            <b className="kpi-value">{b.remaining != null ? idr(b.remaining) : t("No yearly limit")}</b>
            <small className="sub">{b.annualLimit != null ? t("of {limit} · used {used}", { limit: idr(b.annualLimit), used: idr(b.used) }) : t("used {used}", { used: idr(b.used) })}{b.perClaimLimit != null && <> · {t("max {n} per claim", { n: idr(b.perClaimLimit) })}</>}</small>
          </div>))}
      </div>
    </Panel>
  );
}

const ADV: Record<CashAdvanceItem["status"], [BadgeTone, string]> = {
  pending: ["orange", "Waiting for approval"], approved: ["blue", "Approved · to be paid out"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"], disbursed: ["blue", "Paid out · report what you spent"],
  settlement_pending: ["orange", "Settlement waiting for review"], settlement_approved: ["blue", "Settled in the next payslip"], settled: ["green", "Settled"],
};

/** Cash advances: "mine" asks, cancels and settles; "team" and "all" decide, pay out (all) and accept settlements. */
export function CashAdvancesPanel({ scope }: { scope: "mine" | "team" | "all" }) {
  const run = useRun(); const q = useQuery(() => get<CashAdvanceItem[]>("/api/hr/advances", { scope }), [scope]);
  const [ask, setAsk] = useState(false); const [f, setF] = useState({ amount: "", purpose: "", neededBy: "" });
  const [settle, setSettle] = useState<CashAdvanceItem | null>(null); const [sf, setSf] = useState({ spent: "", note: "" }); const file = useRef<HTMLInputElement>(null);
  const [decide, setDecide] = useState<{ a: CashAdvanceItem; kind: "decide" | "settlement" } | null>(null); const [note, setNote] = useState("");
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  const done = () => { setAsk(false); setSettle(null); setDecide(null); setNote(""); q.reload(); };
  async function request(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/advances", { amount: Number(f.amount), purpose: f.purpose, neededBy: f.neededBy || undefined }), t("Cash advance requested."), { silent: true });
    if (r.ok) done(); else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  async function report(e: React.FormEvent) {
    e.preventDefault(); if (!settle) return; setForm(""); const picked = file.current?.files?.[0];
    if (picked && picked.size > MAX) { setErr({ receipt: t("The file is larger than 4 MB.") }); return; }
    const r = await run(async () => post(`/api/hr/advances/${settle.id}/settle`, { spentAmount: Number(sf.spent), note: sf.note || undefined, receiptBase64: picked ? toBase64(await picked.arrayBuffer()) : undefined }), t("Settlement sent."), { silent: true });
    if (r.ok) done(); else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  async function answer(approve: boolean) {
    if (!decide) return; setForm("");
    const r = await run(() => post(`/api/hr/advances/${decide.a.id}/${decide.kind}`, { approve, note: note || undefined }), approve ? t("Approved.") : t("Sent back."), { silent: true });
    if (r.ok) done(); else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<CashAdvanceItem>[] = [
    ...(scope !== "mine" ? [{ key: "e", header: t("Employee"), cell: (a: CashAdvanceItem) => <><b>{a.employeeName}</b><small className="sub">{a.employeeNo}</small></> }] : []),
    { key: "p", header: t("Purpose"), cell: (a) => <><b>{a.purpose}</b><small className="sub">{t("Asked {date}", { date: fmtDate(a.createdAt) })}{a.neededBy ? ` · ${t("needed by {date}", { date: fmtDate(a.neededBy) })}` : ""}</small></> },
    { key: "a", header: t("Advance"), cell: (a) => <>{idr(a.amount)}{a.spentAmount != null && <small className="sub">{t("spent {n}", { n: idr(a.spentAmount) })}{a.difference ? ` · ${a.difference > 0 ? t("{n} to pay back to you", { n: idr(a.difference) }) : t("{n} to return", { n: idr(-a.difference) })}` : ""}</small>}</> },
    { key: "s", header: t("Status"), cell: (a) => <><Badge tone={ADV[a.status][0]}>{t(ADV[a.status][1])}</Badge>{a.decisionNote && <small className="sub">{a.decisionNote}</small>}</> },
    { key: "x", header: "", cell: (a) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end", flexWrap: "wrap" }}>
        {a.hasReceipt && <Button size="sm" aria-label={t("Open the receipt")} onClick={() => run(() => download(`/api/hr/advances/${a.id}/receipt`))}><Paperclip size={14} /></Button>}
        {a.canDecide && <Button size="sm" variant="primary" onClick={() => { setErr({}); setForm(""); setNote(""); setDecide({ a, kind: "decide" }); }}>{t("Decide")}</Button>}
        {a.canDisburse && <Button size="sm" variant="primary" onClick={async () => { if ((await run(() => post(`/api/hr/advances/${a.id}/disburse`, {}), t("Marked as paid out."))).ok) q.reload(); }}>{t("Mark as paid out")}</Button>}
        {a.canApproveSettlement && <Button size="sm" variant="primary" onClick={() => { setErr({}); setForm(""); setNote(""); setDecide({ a, kind: "settlement" }); }}>{t("Review settlement")}</Button>}
        {a.canSettle && <Button size="sm" variant="primary" onClick={() => { setErr({}); setForm(""); setSf({ spent: String(a.amount), note: "" }); setSettle(a); }}>{t("Report spending")}</Button>}
        {a.canCancel && <Button size="sm" onClick={async () => { if ((await run(() => post(`/api/hr/advances/${a.id}/cancel`), t("Request cancelled."))).ok) q.reload(); }}>{t("Cancel")}</Button>}
      </div>) },
  ];
  const rows = q.data ?? [];
  return (
    <>
      <Panel title={scope === "mine" ? t("Cash advances") : undefined} description={scope === "mine" ? t("Money handed to you before a work trip. Afterwards you report what you spent; the difference is settled in your next payslip.") : undefined}
        actions={scope === "mine" && !unlinked ? <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ amount: "", purpose: "", neededBy: "" }); setAsk(true); }}><Plus size={16} /> {t("Ask for an advance")}</Button> : undefined}>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No cash advances")} description={scope === "mine" ? t("Need money before a work trip? Ask for an advance here.") : t("Advances appear here when employees ask for them.")} />
          : <DataTable label={t("Cash advances")} columns={cols} rows={rows} rowKey={(a) => a.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={ask} onClose={() => setAsk(false)} side="right" title={t("Ask for an advance")} footer={<><Button onClick={() => setAsk(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="adv-form">{t("Send")}</Button></>}>
        <form id="adv-form" className="stack" noValidate onSubmit={request}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Amount (IDR)")} htmlFor="adv-amt" required error={err.amount}><TextInput id="adv-amt" inputMode="decimal" data-autofocus value={f.amount} invalid={!!err.amount} onChange={(e) => setF({ ...f, amount: e.target.value })} /></Field>
          <Field label={t("What is it for?")} htmlFor="adv-purpose" required error={err.purpose}><TextInput id="adv-purpose" value={f.purpose} invalid={!!err.purpose} onChange={(e) => setF({ ...f, purpose: e.target.value })} placeholder={t("Client visit to Surabaya, 3 days")} /></Field>
          <Field label={t("Needed by")} htmlFor="adv-by" error={err.neededBy}><TextInput id="adv-by" type="date" value={f.neededBy} onChange={(e) => setF({ ...f, neededBy: e.target.value })} /></Field>
        </form>
      </Overlay>
      <Overlay open={!!settle} onClose={() => setSettle(null)} side="right" title={t("Report spending")} description={settle ? t("Advance of {n}: {purpose}", { n: idr(settle.amount), purpose: settle.purpose }) : undefined} footer={<><Button onClick={() => setSettle(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="settle-form">{t("Send")}</Button></>}>
        <form id="settle-form" className="stack" noValidate onSubmit={report}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Amount actually spent (IDR)")} htmlFor="st-spent" required error={err.spentAmount} hint={t("Spent more: the difference is paid with your next payslip. Spent less: the rest is deducted from it.")}><TextInput id="st-spent" inputMode="decimal" data-autofocus value={sf.spent} invalid={!!err.spentAmount} onChange={(e) => setSf({ ...sf, spent: e.target.value })} /></Field>
          <Field label={t("Note")} htmlFor="st-note" error={err.note}><TextInput id="st-note" value={sf.note} onChange={(e) => setSf({ ...sf, note: e.target.value })} placeholder={t("Optional")} /></Field>
          <Field label={t("Receipts (one PDF, PNG or JPEG, up to 4 MB)")} htmlFor="st-file" error={err.receipt}><input id="st-file" ref={file} type="file" accept="application/pdf,image/png,image/jpeg" /></Field>
        </form>
      </Overlay>
      <Overlay open={!!decide} onClose={() => setDecide(null)} title={decide?.kind === "settlement" ? t("Review settlement") : t("Decide cash advance")}
        description={decide ? (decide.kind === "settlement" ? t("{name} spent {spent} of an advance of {n}.", { name: decide.a.employeeName, spent: idr(decide.a.spentAmount), n: idr(decide.a.amount) }) : `${decide.a.employeeName} · ${idr(decide.a.amount)} · ${decide.a.purpose}`) : undefined}
        footer={<><Button onClick={() => setDecide(null)}>{t("Cancel")}</Button><Button variant="danger-outline" onClick={() => answer(false)}>{decide?.kind === "settlement" ? t("Send back") : t("Reject")}</Button><Button variant="primary" onClick={() => answer(true)}>{t("Approve")}</Button></>}>
        <div className="stack">
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Note (required to reject or send back; the employee will see it)")} htmlFor="adv-note" error={err.note}><TextInput id="adv-note" data-autofocus value={note} invalid={!!err.note} onChange={(e) => setNote(e.target.value)} /></Field>
        </div>
      </Overlay>
    </>
  );
}

const WA: Record<WageAdvanceItem["status"], [BadgeTone, string]> = {
  pending: ["orange", "Waiting for approval"], approved: ["blue", "Approved · to be transferred"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"], disbursed: ["blue", "Transferred · deducted from this month's pay"], recovered: ["green", "Deducted"],
};
const monthOf = (y: number, m: number) => new Date(y, m - 1, 1).toLocaleDateString(undefined, { month: "long", year: "numeric" });

/** Early wage withdrawal: "mine" shows what is available now and asks; "all" decides and marks transfers (payroll). */
export function WageAdvancePanel({ scope }: { scope: "mine" | "all" }) {
  const run = useRun(); const q = useQuery(() => get<WageAdvanceItem[]>("/api/hr/payroll/wage-advances", { scope }), [scope]);
  const av = useQuery(() => (scope === "mine" ? get<WageAdvanceAvailability>("/api/hr/payroll/wage-advances/available") : Promise.resolve(null)), [scope]);
  const [ask, setAsk] = useState(false); const [f, setF] = useState({ amount: "", reason: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [reject, setReject] = useState<WageAdvanceItem | null>(null); const [note, setNote] = useState("");
  const reload = () => { q.reload(); av.reload(); };
  async function request(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/payroll/wage-advances", { amount: Number(f.amount), reason: f.reason || undefined }), t("Request sent."), { silent: true });
    if (r.ok) { setAsk(false); reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const act = async (w: WageAdvanceItem, path: string, body: object, msg: string) => { const r = await run(() => post(`/api/hr/payroll/wage-advances/${w.id}/${path}`, body), msg, { silent: path === "decide" && !("approve" in body && (body as { approve: boolean }).approve) }); if (r.ok) { setReject(null); setNote(""); reload(); } else setErr(r.fields ?? {}); };
  const a = av.data; const rows = q.data ?? [];
  const cols: Column<WageAdvanceItem>[] = [
    ...(scope === "all" ? [{ key: "e", header: t("Employee"), cell: (w: WageAdvanceItem) => <><b>{w.employeeName}</b><small className="sub">{w.employeeNo}</small></> }] : []),
    { key: "m", header: t("Month"), cell: (w) => <>{monthOf(w.year, w.month)}<small className="sub">{t("Asked {date}", { date: fmtDate(w.createdAt) })}{w.reason ? ` · ${w.reason}` : ""}</small></> },
    { key: "a", header: t("Amount"), cell: (w) => idr(w.amount) },
    { key: "s", header: t("Status"), cell: (w) => <><Badge tone={WA[w.status][0]}>{t(WA[w.status][1])}</Badge>{w.decisionNote && <small className="sub">{w.decisionNote}</small>}</> },
    { key: "x", header: "", cell: (w) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end", flexWrap: "wrap" }}>
        {w.canDecide && <Button size="sm" variant="primary" onClick={() => act(w, "decide", { approve: true }, t("Approved."))}>{t("Approve")}</Button>}
        {w.canDecide && <Button size="sm" variant="danger-outline" onClick={() => { setNote(""); setErr({}); setReject(w); }}>{t("Reject")}</Button>}
        {w.canDisburse && <Button size="sm" variant="primary" onClick={() => act(w, "disburse", {}, t("Marked as transferred."))}>{t("Mark as transferred")}</Button>}
        {w.canCancel && <Button size="sm" onClick={() => act(w, "cancel", {}, t("Request cancelled."))}>{t("Cancel")}</Button>}
      </div>) },
  ];
  return (
    <>
      <Panel title={scope === "mine" ? t("Early wage withdrawal") : undefined}
        description={scope === "mine" ? t("Take part of the wage you have already earned this month before payday. It is deducted from this month's pay.") : undefined}
        actions={scope === "mine" && a?.eligible ? <Button variant="primary" disabled={a.available <= 0} onClick={() => { setErr({}); setForm(""); setF({ amount: "", reason: "" }); setAsk(true); }}><Coins size={16} /> {t("Withdraw early")}</Button> : undefined}>
        {scope === "mine" && a && (
          <p className="sub" style={{ marginTop: 0 }}>
            {a.eligible ? t("Available now: {n} (up to {pct}% of the {earned} earned so far in {month}; {taken} already requested).", { n: idr(a.available), pct: String(Math.round(a.maxShare * 100)), earned: idr(a.earned), month: monthOf(a.year, a.month), taken: idr(a.alreadyTaken) }) : a.reason}
          </p>)}
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No withdrawals")} description={scope === "mine" ? t("Requests you make appear here.") : t("Requests appear here when employees make them.")} />
          : <DataTable label={t("Early wage withdrawals")} columns={cols} rows={rows} rowKey={(w) => w.id} loading={q.data === undefined && q.loading} error={(q.error as { code?: string } | undefined)?.code === "invariant" ? null : q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={ask} onClose={() => setAsk(false)} side="right" title={t("Withdraw early")} description={a ? t("Up to {n} now.", { n: idr(a.available) }) : undefined} footer={<><Button onClick={() => setAsk(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="wa-form">{t("Send")}</Button></>}>
        <form id="wa-form" className="stack" noValidate onSubmit={request}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Amount (IDR)")} htmlFor="wa-amt" required error={err.amount}><TextInput id="wa-amt" inputMode="decimal" data-autofocus value={f.amount} invalid={!!err.amount} onChange={(e) => setF({ ...f, amount: e.target.value })} /></Field>
          <Field label={t("Reason")} htmlFor="wa-reason" error={err.reason}><TextInput id="wa-reason" value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject request")} footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" onClick={() => reject && act(reject, "decide", { approve: false, note }, t("Request rejected."))}>{t("Reject")}</Button></>}>
        <Field label={t("Reason (the employee will see it)")} htmlFor="wa-note" required error={err.note}><TextInput id="wa-note" data-autofocus value={note} invalid={!!err.note} onChange={(e) => setNote(e.target.value)} /></Field>
      </Overlay>
    </>
  );
}

