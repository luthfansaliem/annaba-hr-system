"use client";

import { useEffect, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { TYPE_OPTIONS } from "./employee-status";
import { get, post } from "@/lib/api/client";
import type { EmployeeDetail, EmployeeListItem, OrgUnitItem, Paged } from "@/lib/api/types";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const today = () => new Date().toISOString().slice(0, 10);

/** Reads server field errors into the form: known fields beside their input, the rest in the alert so nothing is invisible. */
function useFormErrors(known: string[]) {
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const clear = () => { setErr({}); setForm(""); };
  const fromServer = (r: { error: string; fields?: Record<string, string> }) => {
    const fe = r.fields ?? {}; setErr(Object.fromEntries(known.map((k) => [k, fe[k] ?? ""])));
    const other = Object.entries(fe).filter(([k]) => !known.includes(k)).map(([, v]) => v);
    setForm(other.length ? `${r.error} ${other.join(" ")}` : r.error);
  };
  return { err, setErr, form, setForm, clear, fromServer };
}

/** Change position, department, manager, grade or contract. Creates a new effective-dated period; the old one stays in the history. */
export function ChangeJobDrawer({ emp, open, onClose, onDone }: { emp: EmployeeDetail; open: boolean; onClose: () => void; onDone: (d: EmployeeDetail) => void }) {
  const run = useRun(); const cur = emp.current;
  const [f, setF] = useState({ effectiveFrom: today(), jobTitle: "", grade: "", location: "", type: "permanent", contractEnd: "", orgUnitId: "", managerId: "", reason: "" });
  const e = useFormErrors(Object.keys(f)); const [busy, setBusy] = useState(false);
  const [units, setUnits] = useState<OrgUnitItem[]>([]); const [people, setPeople] = useState<EmployeeListItem[]>([]);
  useEffect(() => {
    if (!open || !cur) return;
    e.clear();
    setF({ effectiveFrom: today(), jobTitle: cur.jobTitle, grade: cur.grade ?? "", location: cur.location ?? "", type: cur.type, contractEnd: cur.contractEnd ?? "", orgUnitId: cur.orgUnitId ?? "", managerId: cur.managerId ?? "", reason: "" });
    get<OrgUnitItem[]>("/api/hr/org-units").then(setUnits).catch(() => {});
    get<Paged<EmployeeListItem>>("/api/hr/employees", { status: "active", pageSize: 100, sort: "name" }).then((r) => setPeople(r.data.filter((p) => p.id !== emp.id))).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  const set = (k: keyof typeof f) => (ev: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: ev.target.value });
  async function submit(ev: React.FormEvent) {
    ev.preventDefault(); setBusy(true); e.setForm("");
    const r = await run(() => post<EmployeeDetail>(`/api/hr/employees/${emp.id}/employment`, {
      effectiveFrom: f.effectiveFrom, jobTitle: f.jobTitle, grade: f.grade, location: f.location, type: f.type, contractEnd: f.type === "permanent" ? null : f.contractEnd || null,
      orgUnitId: f.orgUnitId || null, managerId: f.managerId || null, reason: f.reason || null }), t("Job details updated."), { silent: true });
    setBusy(false);
    if (r.ok) { onClose(); onDone(r.data); } else e.fromServer(r);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Change job details")} description={t("The previous position is kept in the job history.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="change-job-form" disabled={busy}>{busy ? t("Saving…") : t("Save change")}</Button></>}>
      <form id="change-job-form" className="stack" noValidate onSubmit={submit}>
        {e.form && <div className="form-alert err" role="alert">{e.form}</div>}
        <div className="fgrid">
          <Field label={t("Effective from")} htmlFor="cj-from" required error={e.err.effectiveFrom}><TextInput id="cj-from" type="date" value={f.effectiveFrom} invalid={!!e.err.effectiveFrom} onChange={set("effectiveFrom")} /></Field>
          <Field label={t("Employment type")} htmlFor="cj-type" error={e.err.type}><SelectInput id="cj-type" options={TYPE_OPTIONS} value={f.type} onChange={set("type")} /></Field>
          {f.type !== "permanent" && <Field label={t("Contract end")} htmlFor="cj-end" required error={e.err.contractEnd}><TextInput id="cj-end" type="date" value={f.contractEnd} invalid={!!e.err.contractEnd} onChange={set("contractEnd")} /></Field>}
          <Field label={t("Job title")} htmlFor="cj-title" required full error={e.err.jobTitle}><TextInput id="cj-title" data-autofocus value={f.jobTitle} invalid={!!e.err.jobTitle} onChange={set("jobTitle")} /></Field>
          <Field label={t("Grade")} htmlFor="cj-grade"><TextInput id="cj-grade" value={f.grade} onChange={set("grade")} /></Field>
          <Field label={t("Location")} htmlFor="cj-loc"><TextInput id="cj-loc" value={f.location} onChange={set("location")} /></Field>
          <Field label={t("Department")} htmlFor="cj-unit" error={e.err.orgUnitId}><SelectInput id="cj-unit" value={f.orgUnitId} onChange={set("orgUnitId")} options={[{ value: "", label: "—" }, ...units.map((u) => ({ value: u.id, label: u.name }))]} /></Field>
          <Field label={t("Manager")} htmlFor="cj-mgr" error={e.err.managerId}><SelectInput id="cj-mgr" value={f.managerId} onChange={set("managerId")} options={[{ value: "", label: "—" }, ...people.map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeNo})` }))]} /></Field>
          <Field label={t("Reason")} htmlFor="cj-reason" full><TextInput id="cj-reason" value={f.reason} onChange={set("reason")} placeholder={t("Promotion, transfer, contract renewal…")} /></Field>
        </div>
      </form>
    </Overlay>
  );
}

/** ONB-003 — ends employment. If the person has a sign-in account it is deactivated and signed out, which is said plainly before confirming. */
export function EndEmploymentDrawer({ emp, open, onClose, onDone }: { emp: EmployeeDetail; open: boolean; onClose: () => void; onDone: (d: EmployeeDetail) => void }) {
  const run = useRun(); const [f, setF] = useState({ date: today(), reason: "" });
  const e = useFormErrors(["date", "reason"]); const [busy, setBusy] = useState(false);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { if (open) { setF({ date: today(), reason: "" }); e.clear(); } }, [open]);
  async function submit(ev: React.FormEvent) {
    ev.preventDefault(); setBusy(true); e.setForm("");
    const r = await run(() => post<EmployeeDetail>(`/api/hr/employees/${emp.id}/terminate`, f), t("Employment ended."), { silent: true });
    setBusy(false);
    if (r.ok) { onClose(); onDone(r.data); } else e.fromServer(r);
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("End employment")} description={t("{name} will no longer be active. This cannot be undone.", { name: emp.fullName })}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="end-emp-form" disabled={busy}>{busy ? t("Saving…") : t("End employment")}</Button></>}>
      <form id="end-emp-form" className="stack" noValidate onSubmit={submit}>
        {e.form && <div className="form-alert err" role="alert">{e.form}</div>}
        {emp.userId && <div className="form-alert" role="note">{t("Their sign-in account will be deactivated and signed out of every device.")}</div>}
        <Field label={t("Last working day")} htmlFor="ee-date" required error={e.err.date}><TextInput id="ee-date" type="date" data-autofocus value={f.date} invalid={!!e.err.date} onChange={(ev) => setF({ ...f, date: ev.target.value })} /></Field>
        <Field label={t("Reason")} htmlFor="ee-reason" required error={e.err.reason}><TextInput id="ee-reason" value={f.reason} invalid={!!e.err.reason} onChange={(ev) => setF({ ...f, reason: ev.target.value })} placeholder={t("Resignation, end of contract, retirement…")} /></Field>
      </form>
    </Overlay>
  );
}
