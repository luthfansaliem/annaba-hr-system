"use client";

import { useEffect, useState } from "react";
import { Download, Save } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { MyCustomFields } from "@/components/hr/admin-extras";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { ChangeRequestsTable, FIELD_LABEL } from "@/components/hr/change-requests-table";
import { get, patch, post } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { ChangeRequestItem, SelfProfile } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const Row = ({ k, v }: { k: string; v: React.ReactNode }) => <div className="kv"><dt>{k}</dt><dd>{v || "—"}</dd></div>;

/** My HR record: contact details change right away; name, marital status, bank and tax details are requests that HR approves. Also the place to download everything the system holds about you. */
export default function MyHrProfilePage() {
  const run = useRun(); const q = useQuery(() => get<SelfProfile>("/api/hr/me"), []); const reqs = useQuery(() => get<ChangeRequestItem[]>("/api/hr/me/change-requests", { status: "all" }), []);
  const [f, setF] = useState({ phone: "", address: "", emergencyContactName: "", emergencyContactPhone: "" }); const [ferr, setFerr] = useState("");
  const [open, setOpen] = useState(false); const [c, setC] = useState({ field: "bank_account", value: "", reason: "" }); const [cerr, setCerr] = useState<Record<string, string>>({}); const [cform, setCform] = useState("");
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant"; const p = q.data;
  useEffect(() => { if (p) setF({ phone: p.phone ?? "", address: p.address ?? "", emergencyContactName: p.emergencyContactName ?? "", emergencyContactPhone: p.emergencyContactPhone ?? "" }); }, [p]);
  async function save(e: React.FormEvent) { e.preventDefault(); setFerr(""); const r = await run(() => patch<SelfProfile>("/api/hr/me", f), t("Details saved."), { silent: true }); if (r.ok) q.setData(r.data); else setFerr(r.error); }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setCform("");
    const r = await run(() => post("/api/hr/me/change-requests", c), t("Request sent to HR."), { silent: true });
    if (r.ok) { setOpen(false); setC({ ...c, value: "", reason: "" }); reqs.reload(); } else { setCerr(r.fields ?? {}); setCform(r.error); }
  }
  return (
    <>
      <PageHeader title={t("My HR Profile")} description={t("Your employee record.")} actions={p && <Button onClick={() => run(() => download("/api/hr/me/export"), t("File downloaded."))}><Download size={16} /> {t("Download my data")}</Button>} />
      {unlinked ? <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} /></Panel> : p && (
        <div className="stack">
          <Panel title={`${p.fullName} · ${p.employeeNo}`}>
            <dl className="kvs"><Row k={t("Job title")} v={p.jobTitle} /><Row k={t("Department")} v={p.orgUnit} /><Row k={t("Manager")} v={p.managerName} /><Row k={t("Hired")} v={fmtDate(p.hireDate)} /><Row k={t("Work email")} v={p.workEmail} />
              <Row k={t("Marital status")} v={p.maritalStatus} /><Row k={t("Bank")} v={p.bankName} /><Row k={t("Bank account")} v={p.hasBankAccount ? "••••••••" : ""} /><Row k={t("Tax ID (NPWP)")} v={p.hasTaxId ? "••••••••" : ""} /><Row k={t("National ID (NIK)")} v={p.hasNationalId ? "••••••••" : ""} /></dl>
          </Panel>
          <Panel title={t("Contact details")} description={t("Saved straight away.")}>
            <form className="stack" noValidate onSubmit={save}>
              {ferr && <div className="form-alert err" role="alert">{ferr}</div>}
              <div className="fgrid">
                <Field label={t("Phone")} htmlFor="mp-phone"><TextInput id="mp-phone" value={f.phone} onChange={(e) => setF({ ...f, phone: e.target.value })} /></Field>
                <Field label={t("Address")} htmlFor="mp-addr"><TextInput id="mp-addr" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} /></Field>
                <Field label={t("Emergency contact")} htmlFor="mp-ecn"><TextInput id="mp-ecn" value={f.emergencyContactName} onChange={(e) => setF({ ...f, emergencyContactName: e.target.value })} /></Field>
                <Field label={t("Emergency phone")} htmlFor="mp-ecp"><TextInput id="mp-ecp" value={f.emergencyContactPhone} onChange={(e) => setF({ ...f, emergencyContactPhone: e.target.value })} /></Field>
              </div>
              <div><Button variant="primary" type="submit"><Save size={15} /> {t("Save changes")}</Button></div>
            </form>
          </Panel>
          <MyCustomFields />
          <Panel title={t("Change requests")} description={t("Name, marital status, bank and tax details change after HR approves.")} actions={<Button onClick={() => { setCerr({}); setCform(""); setOpen(true); }}>{t("Request a change")}</Button>}>
            {!reqs.loading && (reqs.data?.length ?? 0) === 0 ? <EmptyState title={t("No requests yet")} description={t("Requests you make will appear here.")} />
              : <ChangeRequestsTable rows={reqs.data ?? []} base="/api/hr/me/change-requests" showEmployee={false} loading={reqs.data === undefined && reqs.loading} error={reqs.error?.message ?? null} onRetry={reqs.reload} onChanged={reqs.reload} />}
          </Panel>
        </div>)}
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Request a change")} description={t("HR checks and applies it. Bank and tax details ask for your password first.")}
        footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="chg-form">{t("Send request")}</Button></>}>
        <form id="chg-form" className="stack" noValidate onSubmit={submit}>
          {cform && <div className="form-alert err" role="alert">{cform}</div>}
          <Field label={t("What to change")} htmlFor="ch-field" error={cerr.field}><SelectInput id="ch-field" value={c.field} onChange={(e) => setC({ ...c, field: e.target.value })} options={Object.keys(FIELD_LABEL).map((k) => ({ value: k, label: t(FIELD_LABEL[k]) }))} /></Field>
          <Field label={t("New value")} htmlFor="ch-value" required error={cerr.value}>{c.field === "marital_status"
            ? <SelectInput id="ch-value" value={c.value || "single"} onChange={(e) => setC({ ...c, value: e.target.value })} options={["single", "married", "divorced", "widowed"].map((v) => ({ value: v, label: t(v[0].toUpperCase() + v.slice(1)) }))} />
            : <TextInput id="ch-value" autoComplete="off" value={c.value} invalid={!!cerr.value} onChange={(e) => setC({ ...c, value: e.target.value })} />}</Field>
          <Field label={t("Reason")} htmlFor="ch-reason" error={cerr.reason}><TextInput id="ch-reason" value={c.reason} onChange={(e) => setC({ ...c, reason: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </>
  );
}
