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
import type { DelegateCandidate, DelegationItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const today = () => new Date().toISOString().slice(0, 10);

/** LVE-004 — hand your approvals to a colleague while you are away. They must already be an approver; nothing else is granted. */
export default function DelegationsPage() {
  const run = useRun(); const q = useQuery(() => get<DelegationItem[]>("/api/hr/delegations"), []); const cands = useQuery(() => get<DelegateCandidate[]>("/api/hr/delegations/candidates"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ delegateEmployeeId: "", startDate: today(), endDate: today() }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const rows = q.data ?? [];
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/delegations", { ...f, delegateEmployeeId: f.delegateEmployeeId || cands.data?.[0]?.employeeId }), t("Delegation saved."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<DelegationItem>[] = [
    { key: "a", header: t("Approver away"), cell: (d) => d.delegator }, { key: "b", header: t("Approves instead"), cell: (d) => d.delegate },
    { key: "p", header: t("Period"), cell: (d) => <>{fmtDate(d.startDate)} – {fmtDate(d.endDate)} {d.active && <Badge tone="green">{t("Active")}</Badge>}</> },
    { key: "x", header: "", cell: (d) => d.canRevoke ? <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/hr/delegations/${d.id}`), t("Delegation revoked."))).ok) q.reload(); }}>{t("Revoke")}</Button> : null },
  ];
  return (
    <Require perm="leave.approve">
      <PageHeader title={t("Delegate Approvals")} description={t("While you are away, a colleague who already approves requests can decide for your team.")}
        actions={<Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ delegateEmployeeId: "", startDate: today(), endDate: today() }); setOpen(true); }}><Plus size={16} /> {t("New delegation")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No delegations")} description={t("Nobody is approving in your place right now.")} />
          : <DataTable label={t("Delegations")} columns={cols} rows={rows} rowKey={(d) => d.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New delegation")} description={t("Up to 90 days. You can revoke it at any time.")}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="deleg-form">{t("Save")}</Button></>}>
        <form id="deleg-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Colleague")} htmlFor="dg-who" required error={err.delegateEmployeeId}><SelectInput id="dg-who" value={f.delegateEmployeeId || cands.data?.[0]?.employeeId || ""} onChange={(e) => setF({ ...f, delegateEmployeeId: e.target.value })} options={(cands.data ?? []).map((c) => ({ value: c.employeeId, label: `${c.name} (${c.employeeNo})` }))} /></Field>
          <div className="fgrid">
            <Field label={t("From")} htmlFor="dg-from" error={err.startDate}><TextInput id="dg-from" type="date" min={today()} value={f.startDate} invalid={!!err.startDate} onChange={(e) => setF({ ...f, startDate: e.target.value })} /></Field>
            <Field label={t("Until")} htmlFor="dg-to" required error={err.endDate}><TextInput id="dg-to" type="date" min={f.startDate} value={f.endDate} invalid={!!err.endDate} onChange={(e) => setF({ ...f, endDate: e.target.value })} /></Field>
          </div>
        </form>
      </Overlay>
    </Require>
  );
}
