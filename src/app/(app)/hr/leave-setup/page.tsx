"use client";

import { useState } from "react";
import { Plus, Trash2 } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { del, get, patch, post } from "@/lib/api/client";
import type { Holiday, LeaveType } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type TypeDraft = { id?: string; code: string; name: string; annualQuota: string; carryOverMax: string; maxDaysPerRequest: string; paid: boolean; allowHalfDay: boolean };
const blankType: TypeDraft = { code: "", name: "", annualQuota: "", carryOverMax: "0", maxDaysPerRequest: "", paid: true, allowHalfDay: true };

function Types() {
  const run = useRun(); const q = useQuery(() => get<LeaveType[]>("/api/hr/leave/types", { includeArchived: true }), []);
  const [d, setD] = useState<TypeDraft | null>(null); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (x: TypeDraft) => { setErr({}); setForm(""); setD(x); };
  const save = async (x: TypeDraft) => {
    const body = { code: x.code, name: x.name, unlimited: x.annualQuota === "", annualQuota: x.annualQuota === "" ? null : Number(x.annualQuota), carryOverMax: Number(x.carryOverMax || 0),
      maxDaysPerRequest: x.maxDaysPerRequest === "" ? null : Number(x.maxDaysPerRequest), paid: x.paid, allowHalfDay: x.allowHalfDay };
    const r = await run(() => (x.id ? patch(`/api/hr/leave/types/${x.id}`, body) : post("/api/hr/leave/types", body)), t("Leave type saved."), { silent: true });
    if (r.ok) { setD(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  };
  const archive = async (x: LeaveType) => { if ((await run(() => patch(`/api/hr/leave/types/${x.id}`, { archived: !x.archived }), x.archived ? t("Leave type restored.") : t("Leave type archived."))).ok) q.reload(); };
  const cols: Column<LeaveType>[] = [
    { key: "name", header: t("Leave type"), cell: (x) => <><b>{x.name}</b> <small className="sub">{x.code}{x.archived ? ` · ${t("Archived")}` : ""}</small></> },
    { key: "quota", header: t("Yearly days"), cell: (x) => x.annualQuota === null ? t("No balance") : x.annualQuota },
    { key: "max", header: t("Max per request"), className: "hide-sm", cell: (x) => x.maxDaysPerRequest ?? "—" },
    { key: "paid", header: t("Pay"), cell: (x) => <Badge tone={x.paid ? "green" : "gray"}>{x.paid ? t("Paid") : t("Unpaid")}</Badge> },
    { key: "a", header: "", cell: (x) => <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
      <Button size="sm" onClick={() => open({ id: x.id, code: x.code, name: x.name, annualQuota: x.annualQuota?.toString() ?? "", carryOverMax: String(x.carryOverMax), maxDaysPerRequest: x.maxDaysPerRequest?.toString() ?? "", paid: x.paid, allowHalfDay: x.allowHalfDay })}>{t("Edit")}</Button>
      <Button size="sm" onClick={() => archive(x)}>{x.archived ? t("Restore") : t("Archive")}</Button></div> },
  ];
  const set = (k: keyof TypeDraft) => (e: React.ChangeEvent<HTMLInputElement>) => d && setD({ ...d, [k]: e.target.value });
  return (
    <Panel title={t("Leave types")} description={t("Leave a yearly quota empty for types without a balance (sick, unpaid, event leave).")} actions={<Button variant="primary" onClick={() => open(blankType)}><Plus size={16} /> {t("Add leave type")}</Button>}>
      <DataTable label={t("Leave types")} columns={cols} rows={q.data ?? []} rowKey={(x) => x.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      <Overlay open={!!d} onClose={() => setD(null)} side="right" title={d?.id ? t("Edit leave type") : t("Add leave type")}
        footer={<><Button onClick={() => setD(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="lt-form">{t("Save")}</Button></>}>
        {d && <form id="lt-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); save(d); }}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Code")} htmlFor="lt-code" required error={err.code}><TextInput id="lt-code" data-autofocus value={d.code} invalid={!!err.code} onChange={set("code")} /></Field>
            <Field label={t("Name")} htmlFor="lt-name" required error={err.name}><TextInput id="lt-name" value={d.name} invalid={!!err.name} onChange={set("name")} /></Field>
            <Field label={t("Yearly days")} htmlFor="lt-quota" error={err.annualQuota}><TextInput id="lt-quota" inputMode="decimal" value={d.annualQuota} invalid={!!err.annualQuota} onChange={set("annualQuota")} placeholder={t("No balance")} /></Field>
            <Field label={t("Carry over (max days)")} htmlFor="lt-carry" error={err.carryOverMax}><TextInput id="lt-carry" inputMode="decimal" value={d.carryOverMax} invalid={!!err.carryOverMax} onChange={set("carryOverMax")} /></Field>
            <Field label={t("Max per request")} htmlFor="lt-max" error={err.maxDaysPerRequest}><TextInput id="lt-max" inputMode="numeric" value={d.maxDaysPerRequest} invalid={!!err.maxDaysPerRequest} onChange={set("maxDaysPerRequest")} /></Field>
          </div>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={d.paid} onChange={(e) => setD({ ...d, paid: e.target.checked })} /> {t("Paid leave")}</label>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={d.allowHalfDay} onChange={(e) => setD({ ...d, allowHalfDay: e.target.checked })} /> {t("Allow half days")}</label>
        </form>}
      </Overlay>
    </Panel>
  );
}

function Holidays() {
  const run = useRun(); const [year, setYear] = useState(new Date().getFullYear());
  const q = useQuery(() => get<Holiday[]>("/api/hr/leave/holidays", { year }), [year]);
  const [d, setD] = useState<{ date: string; name: string; collective: boolean } | null>(null); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const save = async (x: NonNullable<typeof d>) => {
    const r = await run(() => post("/api/hr/leave/holidays", x), t("Holiday added."), { silent: true });
    if (r.ok) { setD(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  };
  const remove = async (h: Holiday) => { if ((await run(() => del(`/api/hr/leave/holidays/${h.id}`), t("Holiday removed."))).ok) q.reload(); };
  const cols: Column<Holiday>[] = [
    { key: "date", header: t("Date"), cell: (h) => fmtDate(h.date) }, { key: "name", header: t("Holiday"), cell: (h) => h.name },
    { key: "c", header: t("Kind"), cell: (h) => <Badge tone={h.collective ? "orange" : "blue"}>{h.collective ? t("Collective leave") : t("Public holiday")}</Badge> },
    { key: "x", header: "", width: 50, cell: (h) => <Button size="sm" variant="danger-outline" aria-label={t("Remove {name}", { name: h.name })} onClick={() => remove(h)}><Trash2 size={14} /></Button> },
  ];
  const y = new Date().getFullYear();
  return (
    <Panel title={t("Holidays")} description={t("Public holidays and collective leave days are not working days.")}
      actions={<div className="row" style={{ gap: 8 }}><SelectInput aria-label={t("Year")} value={String(year)} onChange={(e) => setYear(Number(e.target.value))} options={[y - 1, y, y + 1].map((v) => String(v))} />
        <Button variant="primary" onClick={() => { setErr({}); setForm(""); setD({ date: "", name: "", collective: false }); }}><Plus size={16} /> {t("Add holiday")}</Button></div>}>
      <DataTable label={t("Holidays")} columns={cols} rows={q.data ?? []} rowKey={(h) => h.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      <Overlay open={!!d} onClose={() => setD(null)} side="right" title={t("Add holiday")}
        footer={<><Button onClick={() => setD(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="hd-form">{t("Save")}</Button></>}>
        {d && <form id="hd-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); save(d); }}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Date")} htmlFor="hd-date" required error={err.date}><TextInput id="hd-date" type="date" data-autofocus value={d.date} invalid={!!err.date} onChange={(e) => setD({ ...d, date: e.target.value })} /></Field>
          <Field label={t("Name")} htmlFor="hd-name" required error={err.name}><TextInput id="hd-name" value={d.name} invalid={!!err.name} onChange={(e) => setD({ ...d, name: e.target.value })} /></Field>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={d.collective} onChange={(e) => setD({ ...d, collective: e.target.checked })} /> {t("Collective leave day (cuti bersama)")}</label>
        </form>}
      </Overlay>
    </Panel>
  );
}

export default function LeaveSetupPage() {
  return <Require perm="leave.manage"><PageHeader title={t("Leave Setup")} description={t("Leave types, quotas and holidays for the whole organisation.")} /><div className="stack"><Types /><Holidays /></div></Require>;
}
