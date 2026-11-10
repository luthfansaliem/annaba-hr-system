"use client";

import { useEffect, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { TYPE_OPTIONS } from "./employee-status";
import { get, post } from "@/lib/api/client";
import type { EmployeeListItem, LegalEntityItem, OrgUnitItem, Paged, UserOption } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const today = () => new Date().toISOString().slice(0, 10);
const blank = () => ({ userId: "", fullName: "", workEmail: "", phone: "", hireDate: today(), jobTitle: "", type: "permanent", contractEnd: "", orgUnitId: "", managerId: "", legalEntityId: "", nationalId: "", taxId: "", bankName: "", bankAccount: "" });

/** EMP-001/002/003 — "+ Add Employee" on the Employees list. Sensitive identifiers only appear for people allowed to enter them. */
export function AddEmployeeDrawer({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: (id: string) => void }) {
  const run = useRun(); const { can } = useAuth();
  const [f, setF] = useState(blank()); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [busy, setBusy] = useState(false);
  const [companies, setCompanies] = useState<LegalEntityItem[]>([]); const [units, setUnits] = useState<OrgUnitItem[]>([]); const [people, setPeople] = useState<EmployeeListItem[]>([]); const [users, setUsers] = useState<UserOption[]>([]);
  useEffect(() => {
    if (!open) return;
    setF(blank()); setErr({}); setForm("");
    get<LegalEntityItem[]>("/api/hr/legal-entities").then(setCompanies).catch(() => {});
    get<OrgUnitItem[]>("/api/hr/org-units").then(setUnits).catch(() => setForm(t("Could not load departments.")));
    get<Paged<EmployeeListItem>>("/api/hr/employees", { status: "active", pageSize: 100, sort: "name" }).then((r) => setPeople(r.data)).catch(() => {});
    // Optional link to a sign-in account (lets the person use My Leave, My Attendance and My Payslips). Hidden when the caller may not list users.
    if (can("users.read")) get<Paged<UserOption>>("/api/users", { pageSize: 100, sort: "name" }).then((r) => setUsers(r.data)).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);
  const set = (k: keyof ReturnType<typeof blank>) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  const fixedTerm = f.type !== "permanent";

  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const n: Record<string, string> = {};
    if (f.fullName.trim().length < 2) n.fullName = t("Enter the full name.");
    if (!f.hireDate) n.hireDate = t("Choose the hire date.");
    if (!f.jobTitle.trim()) n.jobTitle = t("Enter the job title.");
    if (fixedTerm && !f.contractEnd) n.contractEnd = t("A fixed-term position needs an end date.");
    setErr(n); if (Object.keys(n).length) return;
    setBusy(true);
    const nz = (v: string) => v.trim() || null;
    const r = await run(() => post<{ id: string }>("/api/hr/employees", {
      fullName: f.fullName, workEmail: nz(f.workEmail), phone: nz(f.phone), hireDate: f.hireDate, jobTitle: f.jobTitle, type: f.type, contractEnd: fixedTerm ? f.contractEnd : null,
      orgUnitId: nz(f.orgUnitId), managerId: nz(f.managerId), legalEntityId: nz(f.legalEntityId), userId: nz(f.userId), nationalId: nz(f.nationalId), taxId: nz(f.taxId), bankName: nz(f.bankName), bankAccount: nz(f.bankAccount),
    }), t("Employee added."), { silent: true });
    setBusy(false);
    if (r.ok) { onClose(); onDone(r.data.id); return; }
    const known = Object.keys(f); const fe = r.fields ?? {};
    setErr(Object.fromEntries(known.map((k) => [k, fe[k] ?? ""])));
    const other = Object.entries(fe).filter(([k]) => !known.includes(k)).map(([, v]) => v);
    setForm(other.length ? `${r.error} ${other.join(" ")}` : r.error);
  }

  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Add Employee")} description={t("Creates the employee record. Sign-in access is separate: invite them from Users.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="add-emp-form" disabled={busy}>{busy ? t("Saving…") : t("Add Employee")}</Button></>}>
      <form id="add-emp-form" className="stack" noValidate onSubmit={submit}>
        {form && <div className="form-alert err" role="alert">{form}</div>}
        <Field label={t("Full name")} htmlFor="ae-name" required error={err.fullName}><TextInput id="ae-name" data-autofocus value={f.fullName} invalid={!!err.fullName} onChange={set("fullName")} /></Field>
        <div className="fgrid">
          <Field label={t("Work email")} htmlFor="ae-email" error={err.workEmail}><TextInput id="ae-email" type="email" value={f.workEmail} invalid={!!err.workEmail} onChange={set("workEmail")} /></Field>
          <Field label={t("Phone")} htmlFor="ae-phone"><TextInput id="ae-phone" value={f.phone} onChange={set("phone")} placeholder="+62…" /></Field>
          <Field label={t("Hire date")} htmlFor="ae-hire" required error={err.hireDate}><TextInput id="ae-hire" type="date" value={f.hireDate} invalid={!!err.hireDate} onChange={set("hireDate")} /></Field>
          <Field label={t("Employment type")} htmlFor="ae-type"><SelectInput id="ae-type" options={TYPE_OPTIONS} value={f.type} onChange={set("type")} /></Field>
          {fixedTerm && <Field label={t("Contract end")} htmlFor="ae-end" required error={err.contractEnd}><TextInput id="ae-end" type="date" value={f.contractEnd} invalid={!!err.contractEnd} onChange={set("contractEnd")} /></Field>}
          <Field label={t("Job title")} htmlFor="ae-title" required full error={err.jobTitle}><TextInput id="ae-title" value={f.jobTitle} invalid={!!err.jobTitle} onChange={set("jobTitle")} /></Field>
          {companies.length > 1 && <Field label={t("Company")} htmlFor="ae-co" error={err.legalEntityId}><SelectInput id="ae-co" value={f.legalEntityId} onChange={set("legalEntityId")} options={[{ value: "", label: t("Default company") }, ...companies.map((c) => ({ value: c.id, label: c.name }))]} /></Field>}
          <Field label={t("Department")} htmlFor="ae-unit" error={err.orgUnitId}><SelectInput id="ae-unit" value={f.orgUnitId} onChange={set("orgUnitId")} options={[{ value: "", label: "—" }, ...units.map((u) => ({ value: u.id, label: u.name }))]} /></Field>
          <Field label={t("Manager")} htmlFor="ae-mgr" error={err.managerId}><SelectInput id="ae-mgr" value={f.managerId} onChange={set("managerId")} options={[{ value: "", label: "—" }, ...people.map((p) => ({ value: p.id, label: `${p.fullName} (${p.employeeNo})` }))]} /></Field>
        </div>
        {users.length > 0 && (
          <Field label={t("Sign-in account")} htmlFor="ae-user" error={err.userId}>
            <SelectInput id="ae-user" value={f.userId} onChange={set("userId")} options={[{ value: "", label: t("None yet") }, ...users.map((u) => ({ value: u.id, label: `${u.name} (${u.email})` }))]} />
            <small className="sub">{t("Links this person to a login so they can request leave, clock in and read their payslips.")}</small></Field>)}
        {can("employees.read_sensitive") && (
          <fieldset className="fl"><legend>{t("Protected details")}</legend>
            <p className="sub">{t("Encrypted at rest. You will be asked to confirm your password.")}</p>
            <div className="fgrid">
              <Field label={t("National ID (NIK)")} htmlFor="ae-nik" error={err.nationalId}><TextInput id="ae-nik" inputMode="numeric" autoComplete="off" value={f.nationalId} onChange={set("nationalId")} /></Field>
              <Field label={t("Tax ID (NPWP)")} htmlFor="ae-npwp" error={err.taxId}><TextInput id="ae-npwp" autoComplete="off" value={f.taxId} onChange={set("taxId")} /></Field>
              <Field label={t("Bank")} htmlFor="ae-bank"><TextInput id="ae-bank" value={f.bankName} onChange={set("bankName")} /></Field>
              <Field label={t("Bank account")} htmlFor="ae-acct" error={err.bankAccount}><TextInput id="ae-acct" autoComplete="off" value={f.bankAccount} onChange={set("bankAccount")} /></Field>
            </div>
          </fieldset>
        )}
      </form>
    </Overlay>
  );
}
