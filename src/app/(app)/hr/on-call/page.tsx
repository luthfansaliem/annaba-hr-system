"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { del, get, post } from "@/lib/api/client";
import type { DirectoryEntry, OnCallItem, Paged } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate, idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** On-call duty days: reachable outside working hours, with an allowance paid in that month's pay run. */
export default function OnCallPage() {
  const { can } = useAuth(); const run = useRun(); const now = new Date(); const [ym, setYm] = useState(`${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`);
  const [y, m] = ym.split("-").map(Number);
  const q = useQuery(() => get<OnCallItem[]>("/api/hr/attendance/on-call", { year: y, month: m }), [ym]);
  const people = useQuery(() => get<Paged<DirectoryEntry>>("/api/hr/directory", { pageSize: 100 }), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ employeeId: "", dates: "", allowance: "", note: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const dates = f.dates.split(/[\s,]+/).map((x) => x.trim()).filter(Boolean);
    const r = await run(() => post("/api/hr/attendance/on-call", { employeeId: f.employeeId || people.data?.data[0]?.employeeId, dates, allowance: Number(f.allowance || 0), note: f.note || undefined }), t("On-call days added."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const months = Array.from({ length: 6 }, (_, i) => { const d = new Date(now.getFullYear(), now.getMonth() + 1 - i, 1); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`; });
  const cols: Column<OnCallItem>[] = [
    { key: "d", header: t("Day"), cell: (o) => fmtDate(o.date) }, { key: "e", header: t("Employee"), cell: (o) => <b>{o.employeeName}</b> },
    { key: "a", header: t("Allowance"), cell: (o) => idr(o.allowance) }, { key: "n", header: t("Note"), className: "hide-sm", cell: (o) => o.note ?? "—" },
    { key: "x", header: "", cell: (o) => (can("attendance.manage") ? <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={async () => { if ((await run(() => del(`/api/hr/attendance/on-call/${o.id}`), t("Removed."))).ok) q.reload(); }}><Trash2 size={14} /></Button> : null) },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="attendance.read_all">
      <PageHeader title={t("On-call Duty")} description={t("Days someone must be reachable outside working hours. The allowance is paid as taxable income in that month's pay run.")}
        actions={<div className="row" style={{ gap: 8 }}><SelectInput aria-label={t("Month")} value={ym} onChange={(e) => setYm(e.target.value)} options={months} />
          {can("attendance.manage") && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ employeeId: "", dates: "", allowance: "", note: "" }); setOpen(true); }}><Plus size={16} /> {t("Add on-call days")}</Button>}</div>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No on-call duty this month")} description={t("Add the days someone is on call, with the allowance per day.")} />
          : <DataTable label={t("On-call duty")} columns={cols} rows={rows} rowKey={(o) => o.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Add on-call days")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="oc-form">{t("Save")}</Button></>}>
        <form id="oc-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Employee")} htmlFor="oc-who" error={err.employeeId}><SelectInput id="oc-who" value={f.employeeId || people.data?.data[0]?.employeeId || ""} onChange={(e) => setF({ ...f, employeeId: e.target.value })} options={(people.data?.data ?? []).map((p) => ({ value: p.employeeId, label: `${p.fullName} (${p.employeeNo})` }))} /></Field>
          <Field label={t("Days (YYYY-MM-DD, separated by commas)")} htmlFor="oc-dates" required error={err.dates}><TextInput id="oc-dates" value={f.dates} onChange={(e) => setF({ ...f, dates: e.target.value })} placeholder="2027-01-04, 2027-01-05" /></Field>
          <Field label={t("Allowance per day (IDR)")} htmlFor="oc-amt" error={err.allowance}><TextInput id="oc-amt" inputMode="decimal" value={f.allowance} onChange={(e) => setF({ ...f, allowance: e.target.value })} /></Field>
          <Field label={t("Note")} htmlFor="oc-note"><TextInput id="oc-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
