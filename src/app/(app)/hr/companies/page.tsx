"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { Require } from "@/components/page-guard";
import { get, patch, post } from "@/lib/api/client";
import type { LegalEntityItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const blank = { name: "", taxId: "", address: "" };
const fmtNpwp = (v: string | null) => (v ? (v.length === 15 ? v.replace(/^(\d{2})(\d{3})(\d{3})(\d)(\d{3})(\d{3})$/, "$1.$2.$3.$4-$5.$6") : v) : "—");

/** Companies under one organisation. Each has its own tax number and its own payroll: pay runs, BPJS reports, the journal and the annual tax file are per company. */
export default function CompaniesPage() {
  const run = useRun(); const q = useQuery(() => get<LegalEntityItem[]>("/api/hr/legal-entities", { includeArchived: true }), []);
  const [edit, setEdit] = useState<LegalEntityItem | "new" | null>(null); const [f, setF] = useState(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const rows = q.data ?? [];
  function open(c: LegalEntityItem | "new") { setF(c === "new" ? blank : { name: c.name, taxId: c.taxId ?? "", address: c.address ?? "" }); setErr({}); setForm(""); setEdit(c); }
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm(""); if (!edit) return;
    const body = { name: f.name, taxId: f.taxId.trim() || null, address: f.address.trim() || null };
    const r = await run(() => (edit === "new" ? post("/api/hr/legal-entities", body) : patch(`/api/hr/legal-entities/${edit.id}`, body)), t("Company saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const act = async (c: LegalEntityItem, body: object, msg: string) => { if ((await run(() => patch(`/api/hr/legal-entities/${c.id}`, { name: c.name, taxId: c.taxId, address: c.address, ...body }), msg)).ok) q.reload(); };
  const cols: Column<LegalEntityItem>[] = [
    { key: "n", header: t("Company"), cell: (c) => <><b>{c.name}</b> {c.isDefault && <Badge tone="blue" dot={false}>{t("Default")}</Badge>} {c.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{c.address ?? ""}</small></> },
    { key: "t", header: t("Tax number (NPWP)"), className: "hide-sm", cell: (c) => fmtNpwp(c.taxId) },
    { key: "e", header: t("Employees"), className: "hide-md", cell: (c) => c.activeEmployees }, { key: "r", header: t("Pay runs"), className: "hide-md", cell: (c) => c.payRuns },
    { key: "a", header: "", cell: (c) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end", flexWrap: "wrap" }}>
        <Button size="sm" onClick={() => open(c)}>{t("Edit")}</Button>
        {!c.isDefault && !c.archived && <Button size="sm" onClick={() => act(c, { isDefault: true }, t("Default company changed."))}>{t("Make default")}</Button>}
        {!c.isDefault && <Button size="sm" onClick={() => act(c, { archived: !c.archived }, c.archived ? t("Company restored.") : t("Company archived."))}>{c.archived ? t("Restore") : t("Archive")}</Button>}
      </div>) },
  ];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Companies")} description={t("Legal entities with their own tax number. Each runs its own payroll; the default company receives employees when none is chosen.")} actions={<Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add company")}</Button>} />
      <Panel><DataTable label={t("Companies")} columns={cols} rows={rows} rowKey={(c) => c.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} /></Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add company") : t("Edit company")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="co-form">{t("Save")}</Button></>}>
        <form id="co-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Company name")} htmlFor="co-name" required error={err.name}><TextInput id="co-name" data-autofocus value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
          <Field label={t("Tax number (NPWP)")} htmlFor="co-tax" error={err.taxId}><TextInput id="co-tax" inputMode="numeric" value={f.taxId} invalid={!!err.taxId} onChange={(e) => setF({ ...f, taxId: e.target.value })} placeholder="00.000.000.0-000.000" /><small className="sub">{t("15 or 16 digits. Shown on payslips and the annual tax file.")}</small></Field>
          <Field label={t("Address")} htmlFor="co-addr" error={err.address}><TextInput id="co-addr" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
