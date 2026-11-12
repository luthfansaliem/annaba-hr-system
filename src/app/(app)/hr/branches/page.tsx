"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import type { BranchItem, LegalEntityItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Form = { legalEntityId: string; code: string; name: string; kind: string; address: string; archived: boolean };
const blank: Form = { legalEntityId: "", code: "", name: "", kind: "branch", address: "", archived: false };

/** Branches (locations) and strategic business units within each company. Employees are placed in one under their More tab. */
export default function BranchesPage() {
  const run = useRun(); const q = useQuery(() => get<BranchItem[]>("/api/hr/branches"), []); const cos = useQuery(() => get<LegalEntityItem[]>("/api/hr/legal-entities"), []);
  const [edit, setEdit] = useState<BranchItem | "new" | null>(null); const [f, setF] = useState<Form>(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  function open(b: BranchItem | "new") { setF(b === "new" ? { ...blank, legalEntityId: cos.data?.find((c) => c.isDefault)?.id ?? "" } : { legalEntityId: b.legalEntityId, code: b.code, name: b.name, kind: b.kind, address: b.address ?? "", archived: b.archived }); setErr({}); setForm(""); setEdit(b); }
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!edit) return; setForm("");
    const body = { ...f, legalEntityId: f.legalEntityId || undefined, address: f.address };
    const r = await run(() => (edit === "new" ? post("/api/hr/branches", body) : put(`/api/hr/branches/${edit.id}`, body)), t("Branch saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<BranchItem>[] = [
    { key: "n", header: t("Name"), cell: (b) => <><b>{b.name}</b> <Badge tone={b.kind === "unit" ? "blue" : "gray"} dot={false}>{b.kind === "unit" ? t("Business unit") : t("Branch")}</Badge> {b.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{b.code}{b.address ? ` · ${b.address}` : ""}</small></> },
    { key: "c", header: t("Company"), className: "hide-sm", cell: (b) => b.company },
    { key: "e", header: t("Employees"), className: "hide-sm", cell: (b) => b.employees },
    { key: "x", header: "", cell: (b) => <Button size="sm" onClick={() => open(b)}>{t("Edit")}</Button> },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Branches & Units")} description={t("Locations and strategic business units within each company. Lists, the directory and reports can be filtered by them.")}
        actions={<Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add branch")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No branches")} description={t("Add your branches or business units, then place employees in them.")} />
          : <DataTable label={t("Branches")} columns={cols} rows={rows} rowKey={(b) => b.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add branch") : t("Edit branch")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="br-form">{t("Save")}</Button></>}>
        <form id="br-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Name")} htmlFor="br-name" required error={err.name}><TextInput id="br-name" data-autofocus value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("Surabaya branch")} /></Field>
            <Field label={t("Code")} htmlFor="br-code" required error={err.code}><TextInput id="br-code" value={f.code} invalid={!!err.code} onChange={(e) => setF({ ...f, code: e.target.value })} placeholder="SBY" /></Field>
            <Field label={t("Kind")} htmlFor="br-kind" error={err.kind}><SelectInput id="br-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })} options={[{ value: "branch", label: t("Branch (a location)") }, { value: "unit", label: t("Strategic business unit") }]} /></Field>
            <Field label={t("Company")} htmlFor="br-co" error={err.legalEntityId}><SelectInput id="br-co" value={f.legalEntityId} onChange={(e) => setF({ ...f, legalEntityId: e.target.value })} options={(cos.data ?? []).filter((c) => !c.archived).map((c) => ({ value: c.id, label: c.name }))} /></Field>
          </div>
          <Field label={t("Address")} htmlFor="br-addr"><TextInput id="br-addr" value={f.address} onChange={(e) => setF({ ...f, address: e.target.value })} placeholder={t("Optional")} /></Field>
          {edit !== "new" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived")}</label>}
        </form>
      </Overlay>
    </Require>
  );
}
