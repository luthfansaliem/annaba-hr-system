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
import { del, get, patch, post } from "@/lib/api/client";
import type { ApprovalRule, LeaveType } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const CHAINS = ["manager", "manager,hr", "manager,manager2", "manager,manager2,hr", "hr"];
const STEP: Record<string, string> = { manager: "Manager", manager2: "Manager's manager", hr: "HR / claims managers" };
const chainLabel = (steps: string[]) => steps.map((s) => t(STEP[s] ?? s)).join(" → ");

/** ADM-002 — who approves what, in which order. Without a rule: the manager alone (plus HR for leave types that ask for it). */
export default function ApprovalFlowsPage() {
  const run = useRun(); const q = useQuery(() => get<ApprovalRule[]>("/api/hr/approval-rules"), []); const types = useQuery(() => get<LeaveType[]>("/api/hr/leave/types"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ kind: "leave", leaveTypeId: "", minAmount: "0", steps: "manager,hr" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/approval-rules", { kind: f.kind, leaveTypeId: f.kind === "leave" && f.leaveTypeId ? f.leaveTypeId : null, minAmount: Number(f.minAmount), steps: f.steps.split(",") }), t("Approval rule saved."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<ApprovalRule>[] = [
    { key: "k", header: t("Applies to"), cell: (r) => <><b>{r.kind === "leave" ? t("Leave") : t("Expense claim")}</b><small className="sub">{r.kind === "leave" ? (r.leaveType ?? t("Every leave type")) : ""}</small></> },
    { key: "m", header: t("From"), cell: (r) => (r.kind === "leave" ? `${r.minAmount} ${t("day(s)")}` : idr(r.minAmount)) },
    { key: "s", header: t("Approved by, in order"), cell: (r) => chainLabel(r.steps) },
    { key: "a", header: t("Status"), className: "hide-sm", cell: (r) => <Badge tone={r.active ? "green" : "gray"}>{r.active ? t("Active") : t("Off")}</Badge> },
    { key: "x", header: "", cell: (r) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end", flexWrap: "wrap" }}>
        <Button size="sm" onClick={async () => { if ((await run(() => patch(`/api/hr/approval-rules/${r.id}`, { active: !r.active }), r.active ? t("Rule switched off.") : t("Rule switched on."))).ok) q.reload(); }}>{r.active ? t("Switch off") : t("Switch on")}</Button>
        <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/hr/approval-rules/${r.id}`), t("Rule deleted."))).ok) q.reload(); }}>{t("Delete")}</Button>
      </div>) },
  ];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Approval Flows")} description={t("Who approves leave and expense claims, in which order, from which size. The rule with the highest threshold a request reaches applies; requests already under way keep their chain.")} actions={<Button variant="primary" onClick={() => { setErr({}); setForm(""); setOpen(true); }}><Plus size={16} /> {t("New rule")}</Button>} />
      <Panel>
        {!q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No rules")} description={t("Without rules the direct manager decides alone; people with the manage permission can always decide.")} />
          : <DataTable label={t("Approval Flows")} columns={cols} rows={q.data ?? []} rowKey={(r) => r.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New rule")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="af-form">{t("Save")}</Button></>}>
        <form id="af-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Applies to")} htmlFor="af-kind" error={err.kind}><SelectInput id="af-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })} options={[{ value: "leave", label: t("Leave") }, { value: "claim", label: t("Expense claim") }]} /></Field>
          {f.kind === "leave" && <Field label={t("Leave type")} htmlFor="af-type" error={err.leaveTypeId}><SelectInput id="af-type" value={f.leaveTypeId} onChange={(e) => setF({ ...f, leaveTypeId: e.target.value })} options={[{ value: "", label: t("Every leave type") }, ...(types.data ?? []).map((x) => ({ value: x.id, label: x.name }))]} /></Field>}
          <Field label={f.kind === "leave" ? t("From how many days") : t("From what amount (IDR)")} htmlFor="af-min" error={err.minAmount}><TextInput id="af-min" inputMode="decimal" value={f.minAmount} invalid={!!err.minAmount} onChange={(e) => setF({ ...f, minAmount: e.target.value })} /></Field>
          <Field label={t("Approved by, in order")} htmlFor="af-steps" error={err.steps}><SelectInput id="af-steps" value={f.steps} onChange={(e) => setF({ ...f, steps: e.target.value })} options={CHAINS.map((c) => ({ value: c, label: chainLabel(c.split(",")) }))} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
