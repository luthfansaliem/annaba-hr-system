"use client";

import { useMemo, useState } from "react";
import { Check, X } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import type { ApprovalInbox, ApprovalBulkResult } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Row = { key: string; kind: "leave" | "correction" | "overtime" | "change"; id: string; who: string; no: string; what: string; detail: string; when: string };

/** MSS-001 — everything that waits for your decision in one list: leave, attendance corrections, overtime and profile changes. Decide one by one or several at once. */
export default function ApprovalsPage() {
  const run = useRun(); const q = useQuery(() => get<ApprovalInbox>("/api/hr/approvals"), []);
  const [picked, setPicked] = useState<Set<string>>(new Set()); const [reject, setReject] = useState<Row[] | null>(null); const [note, setNote] = useState(""); const [noteErr, setNoteErr] = useState(""); const [failed, setFailed] = useState<string[]>([]);
  const rows = useMemo<Row[]>(() => {
    const d = q.data; if (!d) return [];
    return [
      ...d.leave.map((l): Row => ({ key: `leave:${l.id}`, kind: "leave", id: l.id, who: l.employeeName, no: l.employeeNo, what: t("Leave"), detail: `${l.leaveType} · ${fmtDate(l.startDate)}${l.endDate !== l.startDate ? ` – ${fmtDate(l.endDate)}` : ""} · ${l.days} ${t("day(s)")}${l.reason ? ` · ${l.reason}` : ""}`, when: l.createdAt })),
      ...d.corrections.map((c): Row => ({ key: `correction:${c.id}`, kind: "correction", id: c.id, who: c.employeeName, no: c.employeeNo, what: t("Attendance correction"), detail: `${fmtDate(c.date)} · ${c.clockIn}–${c.clockOut} · ${c.reason}`, when: c.createdAt })),
      ...d.overtime.map((o): Row => ({ key: `overtime:${o.id}`, kind: "overtime", id: o.id, who: o.employeeName, no: o.employeeNo, what: t("Overtime"), detail: `${fmtDate(o.date)} · ${o.hours} ${t("hour(s)")} · ${o.reason}`, when: o.createdAt })),
      ...d.changeRequests.map((c): Row => ({ key: `change:${c.id}`, kind: "change", id: c.id, who: c.employeeName, no: c.employeeNo, what: t("Profile change"), detail: `${c.field.replace(/_/g, " ")}${c.valueHidden ? "" : ` → ${c.value ?? ""}`}${c.reason ? ` · ${c.reason}` : ""}`, when: c.createdAt })),
    ];
  }, [q.data]);
  const chosen = rows.filter((r) => picked.has(r.key)); const all = rows.length > 0 && chosen.length === rows.length;
  const toggle = (k: string) => { const n = new Set(picked); if (n.has(k)) n.delete(k); else n.add(k); setPicked(n); };
  async function decide(items: Row[], approve: boolean, why?: string) {
    const r = await run(() => post<ApprovalBulkResult[]>("/api/hr/approvals/bulk", { approve, note: why, items: items.map((i) => ({ kind: i.kind, id: i.id })) }), undefined, { silent: true });
    if (!r.ok) { if (!approve) setNoteErr(r.fields?.note ?? r.error); else setFailed([r.error]); return; }
    const bad = r.data.filter((x) => !x.ok);
    setFailed(bad.map((x) => `${rows.find((y) => y.id === x.id)?.who ?? x.id}: ${x.error}`)); setReject(null); setNote(""); setNoteErr(""); setPicked(new Set()); q.reload();
  }
  return (
    <>
      <PageHeader title={t("Approvals Inbox")} description={t("Everything waiting for your decision: leave, attendance corrections, overtime and profile changes.")} />
      {failed.length > 0 && <div className="form-alert err" role="alert"><b>{t("Some requests could not be decided:")}</b><ul>{failed.map((f) => <li key={f}>{f}</li>)}</ul></div>}
      <Panel>
        {q.error ? <div className="form-alert err" role="alert">{q.error.message}</div>
          : !q.loading && rows.length === 0 ? <EmptyState title={t("Nothing is waiting for you")} description={t("New requests from your team appear here.")} />
          : <>
            <div className="row" style={{ gap: 8, flexWrap: "wrap", marginBottom: 12, alignItems: "center" }}>
              <label className="row" style={{ gap: 8 }}><Checkbox checked={all} onChange={() => setPicked(all ? new Set() : new Set(rows.map((r) => r.key)))} /> {t("Select all ({n})", { n: rows.length })}</label>
              <Button size="sm" variant="primary" disabled={chosen.length === 0} onClick={() => decide(chosen, true)}><Check size={14} /> {t("Approve selected ({n})", { n: chosen.length })}</Button>
              <Button size="sm" variant="danger-outline" disabled={chosen.length === 0} onClick={() => { setNote(""); setNoteErr(""); setReject(chosen); }}><X size={14} /> {t("Reject selected")}</Button>
            </div>
            <div style={{ overflowX: "auto" }}><table className="tbl" aria-label={t("Approvals Inbox")}>
              <thead><tr><th style={{ width: 36 }}><span className="sr-only">{t("Select")}</span></th><th>{t("Employee")}</th><th>{t("Request")}</th><th className="hide-sm">{t("Asked")}</th><th /></tr></thead>
              <tbody>{rows.map((r) => (
                <tr key={r.key}>
                  <td><Checkbox aria-label={`${t("Select")} ${r.who} ${r.what}`} checked={picked.has(r.key)} onChange={() => toggle(r.key)} /></td>
                  <td><b>{r.who}</b><small className="sub">{r.no}</small></td>
                  <td><Badge tone={r.kind === "leave" ? "blue" : r.kind === "change" ? "gray" : "orange"} dot={false}>{r.what}</Badge><small className="sub">{r.detail}</small></td>
                  <td className="hide-sm">{fmtDate(r.when)}</td>
                  <td><div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
                    <Button size="sm" variant="primary" aria-label={`${t("Approve")} ${r.who} ${r.what}`} onClick={() => decide([r], true)}>{t("Approve")}</Button>
                    <Button size="sm" variant="danger-outline" aria-label={`${t("Reject")} ${r.who} ${r.what}`} onClick={() => { setNote(""); setNoteErr(""); setReject([r]); }}>{t("Reject")}</Button></div></td>
                </tr>))}</tbody>
            </table></div>
          </>}
      </Panel>
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject {n} request(s)", { n: reject?.length ?? 0 })} footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="bulk-reject">{t("Reject")}</Button></>}>
        <form id="bulk-reject" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); if (reject) void decide(reject, false, note); }}>
          <Field label={t("Reason (the employees will see it)")} htmlFor="br-note" required error={noteErr}><TextInput id="br-note" data-autofocus value={note} invalid={!!noteErr} onChange={(e) => setNote(e.target.value)} /></Field>
        </form>
      </Overlay>
    </>
  );
}
