"use client";

import { use, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Save } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { EmployeeStatusBadge, typeLabel } from "@/components/hr/employee-status";
import { ChangeJobDrawer, EndEmploymentDrawer } from "@/components/hr/job-drawers";
import { ChecklistTab } from "@/components/hr/checklist-tab";
import { DocumentsTab } from "@/components/hr/documents-tab";
import { get, patch, post } from "@/lib/api/client";
import { Overlay } from "@/components/ui/overlay";
import type { EmployeeDetail, EmploymentView, LegalEntityItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const tabs = [["overview", "Overview"], ["job", "Job & history"], ["checklist", "Checklist"], ["documents", "Documents"], ["protected", "Protected details"], ["team", "Team"]] as const;
type Tab = (typeof tabs)[number][0];
const dash = (v: string | null | undefined) => v || "—";
const Row = ({ k, v }: { k: string; v: React.ReactNode }) => <div className="kv"><dt>{k}</dt><dd>{v}</dd></div>;

function Overview({ emp, onSaved, canEdit }: { emp: EmployeeDetail; onSaved: (d: EmployeeDetail) => void; canEdit: boolean }) {
  const run = useRun(); const companies = useQuery(() => get<LegalEntityItem[]>("/api/hr/legal-entities"), []);
  const init = () => ({ legalEntityId: emp.legalEntityId, fullName: emp.fullName, workEmail: emp.workEmail ?? "", phone: emp.phone ?? "", birthDate: emp.birthDate ?? "", gender: emp.gender ?? "", maritalStatus: emp.maritalStatus ?? "", address: emp.address ?? "", emergencyContactName: emp.emergencyContactName ?? "", emergencyContactPhone: emp.emergencyContactPhone ?? "" });
  const [f, setF] = useState(init); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { setF(init()); setErr({}); }, [emp.version]);
  const editable = canEdit && emp.status === "active";
  const set = (k: keyof ReturnType<typeof init>) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body: Record<string, unknown> = { version: emp.version, ...f, birthDate: f.birthDate || undefined, gender: f.gender || undefined, maritalStatus: f.maritalStatus || undefined };
    const r = await run(() => patch<EmployeeDetail>(`/api/hr/employees/${emp.id}`, body), t("Employee saved."), { silent: true });
    if (r.ok) { onSaved(r.data); return; }
    setErr(r.fields ?? {}); setForm(r.error);
  }
  if (!editable) return (
    <dl className="kvs">
      <Row k={t("Company")} v={dash(emp.legalEntityName)} /><Row k={t("Work email")} v={dash(emp.workEmail)} /><Row k={t("Phone")} v={dash(emp.phone)} /><Row k={t("Date of birth")} v={emp.birthDate ? fmtDate(emp.birthDate) : "—"} />
      <Row k={t("Gender")} v={dash(emp.gender)} /><Row k={t("Marital status")} v={dash(emp.maritalStatus)} /><Row k={t("Address")} v={dash(emp.address)} />
      <Row k={t("Emergency contact")} v={emp.emergencyContactName ? `${emp.emergencyContactName} · ${dash(emp.emergencyContactPhone)}` : "—"} />
      {emp.status === "terminated" && <Row k={t("Employment ended")} v={`${fmtDate(emp.terminationDate)} · ${dash(emp.terminationReason)}`} />}
    </dl>);
  return (
    <form className="stack" noValidate onSubmit={save}>
      {form && <div className="form-alert err" role="alert">{form}</div>}
      <div className="fgrid">
        {(companies.data?.length ?? 0) > 1 && <Field label={t("Company")} htmlFor="ov-co" error={err.legalEntityId}><SelectInput id="ov-co" value={f.legalEntityId} onChange={set("legalEntityId")} options={companies.data!.map((c) => ({ value: c.id, label: c.name }))} /></Field>}
        <Field label={t("Full name")} htmlFor="ov-name" required error={err.fullName}><TextInput id="ov-name" value={f.fullName} invalid={!!err.fullName} onChange={set("fullName")} /></Field>
        <Field label={t("Work email")} htmlFor="ov-email" error={err.workEmail}><TextInput id="ov-email" type="email" value={f.workEmail} invalid={!!err.workEmail} onChange={set("workEmail")} /></Field>
        <Field label={t("Phone")} htmlFor="ov-phone" error={err.phone}><TextInput id="ov-phone" value={f.phone} onChange={set("phone")} /></Field>
        <Field label={t("Date of birth")} htmlFor="ov-bd" error={err.birthDate}><TextInput id="ov-bd" type="date" value={f.birthDate} invalid={!!err.birthDate} onChange={set("birthDate")} /></Field>
        <Field label={t("Gender")} htmlFor="ov-g"><SelectInput id="ov-g" value={f.gender} onChange={set("gender")} options={[{ value: "", label: "—" }, { value: "female", label: t("Female") }, { value: "male", label: t("Male") }]} /></Field>
        <Field label={t("Marital status")} htmlFor="ov-m"><SelectInput id="ov-m" value={f.maritalStatus} onChange={set("maritalStatus")} options={[{ value: "", label: "—" }, ...["single", "married", "divorced", "widowed"].map((v) => ({ value: v, label: t(v[0].toUpperCase() + v.slice(1)) }))]} /></Field>
        <Field label={t("Address")} htmlFor="ov-addr" full><TextInput id="ov-addr" value={f.address} onChange={set("address")} /></Field>
        <Field label={t("Emergency contact")} htmlFor="ov-ecn"><TextInput id="ov-ecn" value={f.emergencyContactName} onChange={set("emergencyContactName")} /></Field>
        <Field label={t("Emergency phone")} htmlFor="ov-ecp"><TextInput id="ov-ecp" value={f.emergencyContactPhone} onChange={set("emergencyContactPhone")} /></Field>
      </div>
      <div><Button variant="primary" type="submit"><Save size={15} /> {t("Save changes")}</Button></div>
    </form>
  );
}

function Job({ emp }: { emp: EmployeeDetail }) {
  const c = emp.current;
  return (
    <div className="stack">
      {c && <dl className="kvs">
        <Row k={t("Job title")} v={c.jobTitle} /><Row k={t("Department")} v={dash(c.orgUnit)} /><Row k={t("Manager")} v={c.managerId ? <Link href={`/hr/employees/${c.managerId}`}>{c.managerName}</Link> : "—"} />
        <Row k={t("Type")} v={typeLabel(c.type)} /><Row k={t("Grade")} v={dash(c.grade)} /><Row k={t("Location")} v={dash(c.location)} />
        <Row k={t("Hired")} v={fmtDate(emp.hireDate)} />{c.contractEnd && <Row k={t("Contract end")} v={fmtDate(c.contractEnd)} />}
      </dl>}
      <h3>{t("Job history")}</h3>
      <ol className="timeline" aria-label={t("Job history")}>
        {emp.history.map((h: EmploymentView) => (
          <li key={h.id}><b>{h.jobTitle}</b> {h.effectiveTo === null && emp.status === "active" && <Badge tone="green">{t("Current")}</Badge>}
            <small className="sub">{fmtDate(h.effectiveFrom)} – {h.effectiveTo ? fmtDate(h.effectiveTo) : t("now")} · {dash(h.orgUnit)} · {typeLabel(h.type)}{h.changeReason ? ` · ${h.changeReason}` : ""}</small></li>))}
      </ol>
    </div>
  );
}

function Protected({ emp, canEdit, onSaved }: { emp: EmployeeDetail; canEdit: boolean; onSaved: (d: EmployeeDetail) => void }) {
  const run = useRun(); const s = emp.sensitive;
  const [f, setF] = useState({ nationalId: "", taxId: "", bankName: emp.bankName ?? "", bankAccount: "" }); const [form, setForm] = useState("");
  useEffect(() => setF({ nationalId: s.nationalId ?? "", taxId: s.taxId ?? "", bankName: emp.bankName ?? "", bankAccount: s.bankAccount ?? "" }), [emp.version, s.visible, s.nationalId, s.taxId, s.bankAccount, emp.bankName]);
  if (!s.visible) return (
    <div className="stack">
      <p>{t("These details are encrypted. Seeing them needs the Employees: sensitive permission and a recent password confirmation (reload this page after confirming).")}</p>
      <dl className="kvs"><Row k={t("National ID (NIK)")} v={s.hasNationalId ? "••••••••" : "—"} /><Row k={t("Tax ID (NPWP)")} v={s.hasTaxId ? "••••••••" : "—"} /><Row k={t("Bank account")} v={s.hasBankAccount ? "••••••••" : "—"} /></dl>
    </div>);
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => patch<EmployeeDetail>(`/api/hr/employees/${emp.id}`, { version: emp.version, nationalId: f.nationalId, taxId: f.taxId, bankName: f.bankName, bankAccount: f.bankAccount }), t("Protected details saved."), { silent: true });
    if (r.ok) onSaved(r.data); else setForm(r.error);
  }
  const ro = !canEdit || emp.status !== "active";
  return (
    <form className="stack" noValidate onSubmit={save}>
      {form && <div className="form-alert err" role="alert">{form}</div>}
      <div className="fgrid">
        <Field label={t("National ID (NIK)")} htmlFor="pd-nik"><TextInput id="pd-nik" inputMode="numeric" autoComplete="off" readOnly={ro} value={f.nationalId} onChange={(e) => setF({ ...f, nationalId: e.target.value })} /></Field>
        <Field label={t("Tax ID (NPWP)")} htmlFor="pd-npwp"><TextInput id="pd-npwp" autoComplete="off" readOnly={ro} value={f.taxId} onChange={(e) => setF({ ...f, taxId: e.target.value })} /></Field>
        <Field label={t("Bank")} htmlFor="pd-bank"><TextInput id="pd-bank" readOnly={ro} value={f.bankName} onChange={(e) => setF({ ...f, bankName: e.target.value })} /></Field>
        <Field label={t("Bank account")} htmlFor="pd-acct"><TextInput id="pd-acct" autoComplete="off" readOnly={ro} value={f.bankAccount} onChange={(e) => setF({ ...f, bankAccount: e.target.value })} /></Field>
      </div>
      {!ro && <div><Button variant="primary" type="submit"><Save size={15} /> {t("Save changes")}</Button></div>}
      <small className="sub">{t("Every time these details are shown, it is recorded in the audit log.")}</small>
    </form>
  );
}

function Detail({ id }: { id: string }) {
  const { can } = useAuth();
  const q = useQuery(() => get<EmployeeDetail>(`/api/hr/employees/${id}`), [id]);
  const [tab, setTab] = useState<Tab>("overview"); const [job, setJob] = useState(false); const [end, setEnd] = useState(false); const [anon, setAnon] = useState(false); const [ar, setAr] = useState({ reason: "", force: false }); const [aerr, setAerr] = useState(""); const arun = useRun();
  const saved = (d: EmployeeDetail) => q.setData(d);
  return (
    <QueryBoundary q={q} rows={6}>{(emp) => (
      <>
        <div className="crud-top">
          <div className="row" style={{ gap: 14, alignItems: "center" }}>
            <Avatar name={emp.fullName} size={52} />
            <div><h1>{emp.fullName}</h1><p>{emp.employeeNo} · {emp.current?.jobTitle ?? "—"} <EmployeeStatusBadge status={emp.status} /></p></div>
          </div>
          {emp.status === "active" && can("employees.update") && <div className="row" style={{ gap: 8 }}>
            <Button onClick={() => setJob(true)}>{t("Change job details")}</Button>
            <Button variant="danger-outline" onClick={() => setEnd(true)}>{t("End employment")}</Button></div>}
        </div>
        {emp.status === "terminated" && can("hrconfig.manage") && !emp.fullName.startsWith("Former employee") && <div style={{ marginBottom: 12 }}><Button variant="danger-outline" onClick={() => { setAerr(""); setAr({ reason: "", force: false }); setAnon(true); }}>{t("Anonymise personal data")}</Button></div>}
        <Panel>
          <Tabs items={tabs.filter(([key]) => key !== "documents" || can("documents.read")).map(([key, label]) => ({ key, label: t(label) }))} value={tab} onChange={setTab} label={t("Employee sections")} />
          <div style={{ paddingTop: 16 }}>
            {tab === "overview" && <Overview emp={emp} canEdit={can("employees.update")} onSaved={saved} />}
            {tab === "job" && <Job emp={emp} />}
            {tab === "checklist" && <ChecklistTab employeeId={emp.id} canEdit={can("employees.update")} />}
            {tab === "documents" && <DocumentsTab employeeId={emp.id} canManage={can("documents.manage")} />}
            {tab === "protected" && <Protected emp={emp} canEdit={can("employees.update")} onSaved={saved} />}
            {tab === "team" && (emp.directReports.length
              ? <ul className="mini-list">{emp.directReports.map((r) => <li key={r.id}><div><strong><Link href={`/hr/employees/${r.id}`}>{r.fullName}</Link></strong><small>{r.employeeNo}</small></div></li>)}</ul>
              : <p className="tab-placeholder">{t("No one reports to this person.")}</p>)}
          </div>
        </Panel>
        <Overlay open={anon} onClose={() => setAnon(false)} side="right" title={t("Anonymise personal data")} description={t("Erases this former employee's name, contact, identifiers, bank details and documents. Payroll amounts stay. This cannot be undone.")}
          footer={<><Button onClick={() => setAnon(false)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="anon-form">{t("Anonymise")}</Button></>}>
          <form id="anon-form" className="stack" noValidate onSubmit={async (e) => { e.preventDefault(); const r = await arun(() => post(`/api/hr/employees/${emp.id}/anonymize`, ar), t("Personal data anonymised."), { silent: true }); if (r.ok) { setAnon(false); q.reload(); } else setAerr(r.fields?.reason ?? r.error); }}>
            {aerr && <div className="form-alert err" role="alert">{aerr}</div>}
            <Field label={t("Reason")} htmlFor="an-reason" required><TextInput id="an-reason" data-autofocus value={ar.reason} onChange={(e) => setAr({ ...ar, reason: e.target.value })} placeholder={t("Retention period passed, erasure request…")} /></Field>
            <label className="row" style={{ gap: 8 }}><input type="checkbox" className="cb" checked={ar.force} onChange={(e) => setAr({ ...ar, force: e.target.checked })} /> {t("Erase before the retention period ends (granted erasure request, after legal sign-off)")}</label>
          </form>
        </Overlay>
        <ChangeJobDrawer emp={emp} open={job} onClose={() => setJob(false)} onDone={saved} />
        <EndEmploymentDrawer emp={emp} open={end} onClose={() => setEnd(false)} onDone={saved} />
      </>)}</QueryBoundary>
  );
}

export default function EmployeePage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="employees.read"><p style={{ marginBottom: 12 }}><Link href="/hr/employees" className="link-btn"><ArrowLeft size={14} /> {t("All employees")}</Link></p><Detail id={id} /></Require>;
}
