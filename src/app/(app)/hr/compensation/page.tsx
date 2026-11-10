"use client";

import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, put } from "@/lib/api/client";
import type { CompensationRow, CompensationView } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const PTKP = ["TK0", "TK1", "TK2", "TK3", "K0", "K1", "K2", "K3"];
const today = () => new Date().toISOString().slice(0, 10);
type Form = { effectiveFrom: string; basic: string; allowance: string; ptkpStatus: string; hasNpwp: boolean; kesEnrolled: boolean; tkEnrolled: boolean; jkkRate: string; reason: string; openingYear: string; openingMonths: string; openingGross: string; openingPension: string; openingTax: string };

function Editor({ row, onClose, onDone }: { row: CompensationRow | null; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [f, setF] = useState<Form | null>(null); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  // load the current values when a row is opened (the server asks for the password and the client replays the request when it is needed)
  const employeeId = row?.employeeId;
  useEffect(() => {
    if (!employeeId) return;
    let live = true; setF(null); setErr({}); setForm("");
    get<CompensationView>(`/api/hr/payroll/compensation/${employeeId}`).then((c) => { if (live) setF({
      effectiveFrom: today(), basic: c.basic?.toString() ?? "", allowance: c.allowance?.toString() ?? "0", ptkpStatus: c.ptkpStatus ?? "TK0", hasNpwp: c.hasNpwp, kesEnrolled: c.kesEnrolled, tkEnrolled: c.tkEnrolled,
      jkkRate: String(c.jkkRate), reason: "", openingYear: c.openingYear?.toString() ?? "", openingMonths: String(c.openingMonths), openingGross: String(c.openingGross), openingPension: String(c.openingPension), openingTax: String(c.openingTax),
    }); }).catch((e) => { if (live) setForm((e as Error).message); });
    return () => { live = false; };
  }, [employeeId]);
  const set = (k: keyof Form) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => f && setF({ ...f, [k]: e.target.value });
  async function submit(e: React.FormEvent) {
    e.preventDefault(); if (!f || !row) return; setForm("");
    const n = (v: string) => (v === "" ? undefined : Number(v));
    const body = { effectiveFrom: f.effectiveFrom, basic: n(f.basic), allowance: n(f.allowance) ?? 0, ptkpStatus: f.ptkpStatus, hasNpwp: f.hasNpwp, kesEnrolled: f.kesEnrolled, tkEnrolled: f.tkEnrolled, jkkRate: n(f.jkkRate), reason: f.reason || undefined,
      ...(f.openingYear ? { openingYear: n(f.openingYear), openingMonths: n(f.openingMonths) ?? 0, openingGross: n(f.openingGross) ?? 0, openingPension: n(f.openingPension) ?? 0, openingTax: n(f.openingTax) ?? 0 } : {}) };
    const r = await run(() => put(`/api/hr/payroll/compensation/${row.employeeId}`, body), t("Compensation saved."), { silent: true });
    if (r.ok) { onClose(); onDone(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  return (
    <Overlay open={!!row} onClose={onClose} side="right" title={row ? `${t("Compensation")}: ${row.employeeName}` : ""} description={t("Salary is encrypted at rest. A change starts a new period; earlier periods are kept.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="comp-form" disabled={!f}>{t("Save")}</Button></>}>
      {!f ? <p className="sub">{form || t("Loading…")}</p> : (
        <form id="comp-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Effective from")} htmlFor="cp-from" required error={err.effectiveFrom}><TextInput id="cp-from" type="date" value={f.effectiveFrom} invalid={!!err.effectiveFrom} onChange={set("effectiveFrom")} /></Field>
            <Field label={t("Tax status (PTKP)")} htmlFor="cp-ptkp" error={err.ptkpStatus}><SelectInput id="cp-ptkp" value={f.ptkpStatus} onChange={set("ptkpStatus")} options={PTKP} /></Field>
            <Field label={t("Basic salary (IDR / month)")} htmlFor="cp-basic" required error={err.basic}><TextInput id="cp-basic" inputMode="decimal" data-autofocus value={f.basic} invalid={!!err.basic} onChange={set("basic")} /></Field>
            <Field label={t("Fixed allowance (IDR / month)")} htmlFor="cp-allow" error={err.allowance}><TextInput id="cp-allow" inputMode="decimal" value={f.allowance} invalid={!!err.allowance} onChange={set("allowance")} /></Field>
            <Field label={t("JKK rate")} htmlFor="cp-jkk" error={err.jkkRate}><TextInput id="cp-jkk" inputMode="decimal" value={f.jkkRate} invalid={!!err.jkkRate} onChange={set("jkkRate")} /></Field>
            <Field label={t("Reason")} htmlFor="cp-reason"><TextInput id="cp-reason" value={f.reason} onChange={set("reason")} placeholder={t("Annual review, promotion…")} /></Field>
          </div>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.hasNpwp} onChange={(e) => setF({ ...f, hasNpwp: e.target.checked })} /> {t("Has NPWP (without one, tax is 20% higher)")}</label>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.kesEnrolled} onChange={(e) => setF({ ...f, kesEnrolled: e.target.checked })} /> {t("Enrolled in BPJS Kesehatan")}</label>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.tkEnrolled} onChange={(e) => setF({ ...f, tkEnrolled: e.target.checked })} /> {t("Enrolled in BPJS Ketenagakerjaan")}</label>
          <fieldset className="fl"><legend>{t("Year-to-date from the previous system (optional)")}</legend>
            <p className="sub">{t("Needed when payroll starts in the middle of a year, so the year-end tax settlement is correct.")}</p>
            <div className="fgrid">
              <Field label={t("Year")} htmlFor="cp-oy" error={err.openingYear}><TextInput id="cp-oy" inputMode="numeric" value={f.openingYear} onChange={set("openingYear")} /></Field>
              <Field label={t("Months already paid")} htmlFor="cp-om" error={err.openingMonths}><TextInput id="cp-om" inputMode="numeric" value={f.openingMonths} onChange={set("openingMonths")} /></Field>
              <Field label={t("Taxable income so far")} htmlFor="cp-og"><TextInput id="cp-og" inputMode="decimal" value={f.openingGross} onChange={set("openingGross")} /></Field>
              <Field label={t("JHT + JP paid by employee so far")} htmlFor="cp-op"><TextInput id="cp-op" inputMode="decimal" value={f.openingPension} onChange={set("openingPension")} /></Field>
              <Field label={t("PPh 21 withheld so far")} htmlFor="cp-ot"><TextInput id="cp-ot" inputMode="decimal" value={f.openingTax} onChange={set("openingTax")} /></Field>
            </div>
          </fieldset>
        </form>)}
    </Overlay>
  );
}

export default function CompensationPage() {
  const { can } = useAuth(); const q = useQuery(() => get<CompensationRow[]>("/api/hr/payroll/compensation"), []);
  const [edit, setEdit] = useState<CompensationRow | null>(null);
  const cols: Column<CompensationRow>[] = [
    { key: "e", header: t("Employee"), cell: (r) => <><b>{r.employeeName}</b><small className="sub">{r.employeeNo} · {r.jobTitle ?? "—"}</small></> },
    { key: "s", header: t("Status"), cell: (r) => r.hasCompensation ? <Badge tone="green">{t("Set")}</Badge> : <Badge tone="orange">{t("Missing")}</Badge> },
    { key: "p", header: t("Tax status (PTKP)"), className: "hide-sm", cell: (r) => r.ptkpStatus ?? "—" }, { key: "f", header: t("Since"), className: "hide-sm", cell: (r) => (r.effectiveFrom ? fmtDate(r.effectiveFrom) : "—") },
    { key: "a", header: "", cell: (r) => can("payroll.configure") ? <Button size="sm" onClick={() => setEdit(r)}>{r.hasCompensation ? t("Change") : t("Set")}</Button> : null },
  ];
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Compensation")} description={t("Salary and tax status of each employee. Amounts are only shown inside the editor and every view is recorded.")} />
      <Panel><DataTable label={t("Compensation")} columns={cols} rows={q.data ?? []} rowKey={(r) => r.employeeId} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} /></Panel>
      <Editor row={edit} onClose={() => setEdit(null)} onDone={q.reload} />
    </Require>
  );
}
