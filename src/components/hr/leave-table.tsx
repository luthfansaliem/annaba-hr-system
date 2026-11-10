"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { LeaveStatusBadge, days } from "./leave-status";
import { post } from "@/lib/api/client";
import type { LeaveRequestItem } from "@/lib/api/types";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Requests table shared by My Leave, Approvals and All Leave. Approve/reject/cancel buttons only appear where the server says the caller may (canDecide / canCancel). */
export function LeaveTable({ rows, showEmployee, loading, error, onRetry, onChanged }: {
  rows: LeaveRequestItem[]; showEmployee: boolean; loading?: boolean; error?: string | null; onRetry?: () => void; onChanged: () => void;
}) {
  const run = useRun();
  const [reject, setReject] = useState<LeaveRequestItem | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState("");
  const decide = async (r: LeaveRequestItem, kind: "approve" | "reject", n?: string) => {
    const res = await run(() => post(`/api/hr/leave/requests/${r.id}/${kind}`, { note: n }), kind === "approve" ? t("Leave approved.") : t("Leave rejected."), { silent: kind === "reject" });
    if (res.ok) { setReject(null); setNote(""); onChanged(); } else if (kind === "reject") setErr(res.fields?.note ?? res.error);
  };
  const cancel = async (r: LeaveRequestItem) => { if ((await run(() => post(`/api/hr/leave/requests/${r.id}/cancel`), t("Request cancelled."))).ok) onChanged(); };

  const columns: Column<LeaveRequestItem>[] = [
    ...(showEmployee ? [{ key: "emp", header: t("Employee"), cell: (r: LeaveRequestItem) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo}</small></> } as Column<LeaveRequestItem>] : []),
    { key: "type", header: t("Type"), cell: (r) => <><b>{r.leaveType}</b>{r.reason && <small className="sub">{r.reason}</small>}</> },
    { key: "dates", header: t("Dates"), cell: (r) => <>{fmtDate(r.startDate)}{r.startDate !== r.endDate && <> – {fmtDate(r.endDate)}</>}{r.halfDay && <small className="sub">{t("Half day")}</small>}</> },
    { key: "days", header: t("Days"), cell: (r) => days(r.days) },
    { key: "status", header: t("Status"), cell: (r) => <><LeaveStatusBadge status={r.status} />{r.decisionNote && <small className="sub">{r.decisionNote}</small>}</> },
    { key: "actions", header: "", cell: (r) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        {r.canDecide && <Button size="sm" variant="primary" onClick={() => decide(r, "approve")}>{t("Approve")}</Button>}
        {r.canDecide && <Button size="sm" variant="danger-outline" onClick={() => { setErr(""); setNote(""); setReject(r); }}>{t("Reject")}</Button>}
        {r.canCancel && <Button size="sm" onClick={() => cancel(r)}>{t("Cancel request")}</Button>}
      </div>) },
  ];
  return (
    <>
      <DataTable label={t("Leave requests")} columns={columns} rows={rows} rowKey={(r) => r.id} loading={loading} error={error ?? null} onRetry={onRetry} />
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject leave request")} description={reject ? `${reject.employeeName} · ${fmtDate(reject.startDate)}` : undefined}
        footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="reject-form">{t("Reject")}</Button></>}>
        <form id="reject-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); if (reject) decide(reject, "reject", note); }}>
          <Field label={t("Reason")} htmlFor="rj-note" required error={err}><TextInput id="rj-note" data-autofocus value={note} invalid={!!err} onChange={(e) => setNote(e.target.value)} placeholder={t("Tell the employee why")} /></Field>
        </form>
      </Overlay>
    </>
  );
}
