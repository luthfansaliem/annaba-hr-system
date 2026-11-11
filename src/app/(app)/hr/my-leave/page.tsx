"use client";

import { useEffect, useState } from "react";
import { CalendarDays, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { LeaveTable } from "@/components/hr/leave-table";
import { days } from "@/components/hr/leave-status";
import { get, post } from "@/lib/api/client";
import type { LeaveBalance, LeaveRequestItem, LeaveType, Paged } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const today = () => new Date().toISOString().slice(0, 10);

function RequestDrawer({ open, onClose, onDone, types }: { open: boolean; onClose: () => void; onDone: () => void; types: LeaveType[] }) {
  const run = useRun();
  const [f, setF] = useState({ leaveTypeId: "", startDate: today(), endDate: today(), halfDay: false, reason: "" });
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [busy, setBusy] = useState(false);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (open) { setF({ leaveTypeId: types[0]?.id ?? "", startDate: today(), endDate: today(), halfDay: false, reason: "" }); setErr({}); setForm(""); } }, [open]);
  const type = types.find((x) => x.id === f.leaveTypeId);
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm(""); setBusy(true);
    const r = await run(() => post("/api/hr/leave/requests", { ...f, reason: f.reason || null }), t("Leave requested."), { silent: true });
    setBusy(false);
    if (r.ok) { onClose(); onDone(); return; }
    const fe = r.fields ?? {}; setErr(Object.fromEntries(Object.keys(f).map((k) => [k, fe[k] ?? ""]))); setForm(r.error);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Request leave")} description={t("Your manager is asked to approve. Weekends and public holidays are not counted.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="leave-form" disabled={busy}>{busy ? t("Sending…") : t("Submit request")}</Button></>}>
      <form id="leave-form" className="stack" noValidate onSubmit={submit}>
        {form && <div className="form-alert err" role="alert">{form}</div>}
        <Field label={t("Leave type")} htmlFor="lv-type" required error={err.leaveTypeId}>
          <SelectInput id="lv-type" value={f.leaveTypeId} onChange={(e) => setF({ ...f, leaveTypeId: e.target.value, halfDay: false })} options={types.map((x) => ({ value: x.id, label: x.name }))} /></Field>
        <div className="fgrid">
          <Field label={t("First day")} htmlFor="lv-start" required error={err.startDate}><TextInput id="lv-start" type="date" value={f.startDate} invalid={!!err.startDate} onChange={(e) => setF({ ...f, startDate: e.target.value, endDate: f.endDate < e.target.value ? e.target.value : f.endDate })} /></Field>
          <Field label={t("Last day")} htmlFor="lv-end" required error={err.endDate}><TextInput id="lv-end" type="date" min={f.startDate} value={f.endDate} invalid={!!err.endDate} onChange={(e) => setF({ ...f, endDate: e.target.value })} /></Field>
        </div>
        {type?.allowHalfDay && f.startDate === f.endDate && (
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.halfDay} onChange={(e) => setF({ ...f, halfDay: e.target.checked })} /> {t("Half day")}{err.halfDay && <span className="field-error">{err.halfDay}</span>}</label>)}
        <Field label={t("Reason")} htmlFor="lv-reason" error={err.reason}><TextInput id="lv-reason" value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} placeholder={t("Optional")} /></Field>
      </form>
    </Overlay>
  );
}

export default function MyLeavePage() {
  const [open, setOpen] = useState(false);
  const types = useQuery(() => get<LeaveType[]>("/api/hr/leave/types"), []);
  const bal = useQuery(() => get<LeaveBalance[]>("/api/hr/leave/balances"), []);
  const reqs = useQuery(() => get<Paged<LeaveRequestItem>>("/api/hr/leave/requests", { scope: "mine", pageSize: 100 }), []);
  const reload = () => { bal.reload(); reqs.reload(); };
  const unlinked = (bal.error as { code?: string } | undefined)?.code === "invariant";
  return (
    <>
      <PageHeader title={t("My Leave")} description={t("Your balances and requests.")} actions={!unlinked && <Button variant="primary" onClick={() => setOpen(true)}><Plus size={16} /> {t("Request leave")}</Button>} />
      {unlinked
        ? <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account so you can request leave.")} /></Panel>
        : <>
          <KpiRow wide label={t("My leave balances")}>
            {(bal.data ?? []).map((b) => (
              <Kpi key={b.typeId} icon={<CalendarDays size={20} />} tone="purple" label={`${b.name} · ${b.year}`} value={days(b.available)}
                hint={<>{t("days available")} · {t("{n} used", { n: days(b.used) })}{b.pending > 0 ? ` · ${t("{n} pending", { n: days(b.pending) })}` : ""}{(b.expiring ?? 0) > 0 && b.expiresOn && <span className="kpi-warn">{t("{n} carried-over day(s) expire on {date}", { n: days(b.expiring!), date: new Date(b.expiresOn).toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" }) })}</span>}</>} />))}
          </KpiRow>
          <Panel title={t("My requests")}>
            {!reqs.loading && (reqs.data?.data.length ?? 0) === 0
              ? <EmptyState title={t("No leave requests yet")} description={t("Requests you make will appear here.")} />
              : <LeaveTable rows={reqs.data?.data ?? []} showEmployee={false} loading={reqs.data === undefined && reqs.loading} error={reqs.error?.message ?? null} onRetry={reqs.reload} onChanged={reload} />}
          </Panel></>}
      <RequestDrawer open={open} onClose={() => setOpen(false)} onDone={reload} types={types.data ?? []} />
    </>
  );
}
