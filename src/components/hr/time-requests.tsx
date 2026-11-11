"use client";

import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { LeaveStatusBadge } from "./leave-status";
import { post } from "@/lib/api/client";
import type { TimeRequest } from "@/lib/api/types";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const seg = (kind: "correction" | "overtime") => (kind === "correction" ? "corrections" : "overtime");

/** Attendance corrections and overtime requests in one table; the buttons follow what the server says the caller may do (canDecide / canCancel). */
export function TimeRequestTable({ rows, showEmployee, loading, error, onRetry, onChanged }: {
  rows: TimeRequest[]; showEmployee: boolean; loading?: boolean; error?: string | null; onRetry?: () => void; onChanged: () => void;
}) {
  const run = useRun(); const [reject, setReject] = useState<TimeRequest | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState("");
  const decide = async (r: TimeRequest, kind: "approve" | "reject", n?: string) => {
    const res = await run(() => post(`/api/hr/attendance/${seg(r.kind)}/${r.id}/${kind}`, { note: n }), kind === "approve" ? t("Approved.") : t("Rejected."), { silent: kind === "reject" });
    if (res.ok) { setReject(null); setNote(""); onChanged(); } else if (kind === "reject") setErr(res.fields?.note ?? res.error);
  };
  const cancel = async (r: TimeRequest) => { if ((await run(() => post(`/api/hr/attendance/${seg(r.kind)}/${r.id}/cancel`), t("Request cancelled."))).ok) onChanged(); };
  const columns: Column<TimeRequest>[] = [
    ...(showEmployee ? [{ key: "e", header: t("Employee"), cell: (r: TimeRequest) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo}</small></> } as Column<TimeRequest>] : []),
    { key: "k", header: t("Type"), cell: (r) => <><b>{r.kind === "correction" ? t("Attendance correction") : t("Overtime")}</b><small className="sub">{r.reason}</small></> },
    { key: "d", header: t("Day"), cell: (r) => fmtDate(r.date) },
    { key: "w", header: t("Detail"), cell: (r) => r.kind === "correction" ? `${r.clockIn} – ${r.clockOut}` : `${r.hours} ${t("hours")}` },
    { key: "s", header: t("Status"), cell: (r) => <><LeaveStatusBadge status={r.status} />{r.decisionNote && <small className="sub">{r.decisionNote}</small>}</> },
    { key: "a", header: "", cell: (r) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        {r.canDecide && <Button size="sm" variant="primary" onClick={() => decide(r, "approve")}>{t("Approve")}</Button>}
        {r.canDecide && <Button size="sm" variant="danger-outline" onClick={() => { setErr(""); setNote(""); setReject(r); }}>{t("Reject")}</Button>}
        {r.canCancel && <Button size="sm" onClick={() => cancel(r)}>{t("Cancel request")}</Button>}
      </div>) },
  ];
  useEffect(() => { if (!reject) setErr(""); }, [reject]);
  return (
    <>
      <DataTable label={t("Requests")} columns={columns} rows={rows} rowKey={(r) => r.id} loading={loading} error={error ?? null} onRetry={onRetry} />
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject request")} description={reject ? `${reject.employeeName} · ${fmtDate(reject.date)}` : undefined}
        footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="trej-form">{t("Reject")}</Button></>}>
        <form id="trej-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); if (reject) decide(reject, "reject", note); }}>
          <Field label={t("Reason")} htmlFor="trej-note" required error={err}><TextInput id="trej-note" data-autofocus value={note} invalid={!!err} onChange={(e) => setNote(e.target.value)} placeholder={t("Tell the employee why")} /></Field>
        </form>
      </Overlay>
    </>
  );
}

const today = () => new Date().toISOString().slice(0, 10);

/** "I forgot to clock in/out" and "I will work overtime" in one drawer. */
export function RequestTimeDrawer({ kind, open, onClose, onDone }: { kind: "correction" | "overtime"; open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun(); const [f, setF] = useState({ date: today(), clockIn: "09:00", clockOut: "17:00", hours: "1", reason: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { if (open) { setF({ date: today(), clockIn: "09:00", clockOut: "17:00", hours: "1", reason: "" }); setErr({}); setForm(""); } }, [open]);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm(""); setBusy(true);
    const body = kind === "correction" ? { date: f.date, clockIn: f.clockIn, clockOut: f.clockOut, reason: f.reason } : { date: f.date, hours: Number(f.hours), reason: f.reason };
    const r = await run(() => post(`/api/hr/attendance/${seg(kind)}`, body), t("Request sent."), { silent: true });
    setBusy(false);
    if (r.ok) { onClose(); onDone(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={kind === "correction" ? t("Request attendance correction") : t("Request overtime")}
      description={kind === "correction" ? t("For a day you forgot to clock in or out. Your manager decides.") : t("Up to 4 hours a day and 18 a week. Ask beforehand, or within three days after.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="time-form" disabled={busy}>{busy ? t("Sending…") : t("Send request")}</Button></>}>
      <form id="time-form" className="stack" noValidate onSubmit={submit}>
        {form && <div className="form-alert err" role="alert">{form}</div>}
        <Field label={t("Day")} htmlFor="tm-date" required error={err.date}><TextInput id="tm-date" type="date" data-autofocus value={f.date} invalid={!!err.date} onChange={(e) => setF({ ...f, date: e.target.value })} /></Field>
        {kind === "correction"
          ? <div className="fgrid">
              <Field label={t("Clock in")} htmlFor="tm-in" required error={err.clockIn}><TextInput id="tm-in" type="time" value={f.clockIn} invalid={!!err.clockIn} onChange={(e) => setF({ ...f, clockIn: e.target.value })} /></Field>
              <Field label={t("Clock out")} htmlFor="tm-out" required error={err.clockOut}><TextInput id="tm-out" type="time" value={f.clockOut} invalid={!!err.clockOut} onChange={(e) => setF({ ...f, clockOut: e.target.value })} /></Field>
            </div>
          : <Field label={t("Hours")} htmlFor="tm-hours" required error={err.hours}><TextInput id="tm-hours" inputMode="decimal" value={f.hours} invalid={!!err.hours} onChange={(e) => setF({ ...f, hours: e.target.value })} /></Field>}
        <Field label={t("Reason")} htmlFor="tm-reason" required error={err.reason}><TextInput id="tm-reason" value={f.reason} invalid={!!err.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} /></Field>
      </form>
    </Overlay>
  );
}
