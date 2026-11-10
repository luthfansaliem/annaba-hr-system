"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { LeaveStatusBadge } from "./leave-status";
import { post } from "@/lib/api/client";
import type { ChangeRequestItem } from "@/lib/api/types";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

export const FIELD_LABEL: Record<string, string> = { full_name: "Full name", marital_status: "Marital status", bank_name: "Bank", bank_account: "Bank account", tax_id: "Tax ID (NPWP)", national_id: "National ID (NIK)" };

/** Change requests for My Profile (own requests: cancel) and for HR (decide). Protected values stay hidden until the viewer has the permission and a fresh password check. */
export function ChangeRequestsTable({ rows, base, showEmployee, loading, error, onRetry, onChanged }: { rows: ChangeRequestItem[]; base: string; showEmployee: boolean; loading?: boolean; error?: string | null; onRetry?: () => void; onChanged: () => void }) {
  const run = useRun(); const [reject, setReject] = useState<ChangeRequestItem | null>(null); const [note, setNote] = useState(""); const [err, setErr] = useState("");
  const decide = async (r: ChangeRequestItem, kind: "approve" | "reject", n?: string) => {
    const res = await run(() => post(`/api/hr/change-requests/${r.id}/${kind}`, { note: n }), kind === "approve" ? t("Change approved and applied.") : t("Change rejected."), { silent: kind === "reject" });
    if (res.ok) { setReject(null); setNote(""); onChanged(); } else if (kind === "reject") setErr(res.fields?.note ?? res.error);
  };
  const cols: Column<ChangeRequestItem>[] = [
    ...(showEmployee ? [{ key: "e", header: t("Employee"), cell: (r: ChangeRequestItem) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo}</small></> } as Column<ChangeRequestItem>] : []),
    { key: "f", header: t("Change"), cell: (r) => <><b>{t(FIELD_LABEL[r.field] ?? r.field)}</b><small className="sub">{r.reason ?? ""}</small></> },
    { key: "v", header: t("New value"), cell: (r) => r.valueHidden ? <span className="sub">{t("Hidden: confirm your password and open again")}</span> : r.value },
    { key: "d", header: t("Requested"), className: "hide-sm", cell: (r) => fmtDate(r.createdAt) },
    { key: "s", header: t("Status"), cell: (r) => <><LeaveStatusBadge status={r.status} />{r.decisionNote && <small className="sub">{r.decisionNote}</small>}</> },
    { key: "a", header: "", cell: (r) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        {r.canDecide && <Button size="sm" variant="primary" onClick={() => decide(r, "approve")}>{t("Approve")}</Button>}
        {r.canDecide && <Button size="sm" variant="danger-outline" onClick={() => { setErr(""); setNote(""); setReject(r); }}>{t("Reject")}</Button>}
        {r.canCancel && <Button size="sm" onClick={async () => { if ((await run(() => post(`${base}/${r.id}/cancel`), t("Request cancelled."))).ok) onChanged(); }}>{t("Cancel request")}</Button>}
      </div>) },
  ];
  return (
    <>
      <DataTable label={t("Change requests")} columns={cols} rows={rows} rowKey={(r) => r.id} loading={loading} error={error ?? null} onRetry={onRetry} />
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject change")} footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="crej-form">{t("Reject")}</Button></>}>
        <form id="crej-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); if (reject) decide(reject, "reject", note); }}>
          <Field label={t("Reason")} htmlFor="crej-note" required error={err}><TextInput id="crej-note" data-autofocus value={note} invalid={!!err} onChange={(e) => setNote(e.target.value)} placeholder={t("Tell the employee why")} /></Field>
        </form>
      </Overlay>
    </>
  );
}
