"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { RowMenu } from "@/components/ui/menu";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, patch, post } from "@/lib/api/client";
import type { OrgUnitItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

type Draft = { id?: string; code: string; name: string; parentId: string; archived: boolean };
const blank: Draft = { code: "", name: "", parentId: "", archived: false };

export default function DepartmentsPage() {
  const { can } = useAuth(); const run = useRun(); const manage = can("hrconfig.manage");
  const [showArchived, setShowArchived] = useState(false);
  const list = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units", { includeArchived: showArchived }), [showArchived]);
  const [draft, setDraft] = useState<Draft | null>(null); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const rows = list.data ?? [];

  // The server treats a missing parent as "top level", so every edit sends the full form (archive/restore included).
  const save = async (d: Draft) => {
    const body = { code: d.code, name: d.name, parentId: d.parentId || null, archived: d.archived };
    const r = await run(() => (d.id ? patch(`/api/hr/org-units/${d.id}`, body) : post("/api/hr/org-units", body)), d.id ? t("Department saved.") : t("Department added."), { silent: true });
    if (r.ok) { setDraft(null); list.reload(); return; }
    setErr(r.fields ?? {}); setForm(r.error);
  };
  const open = (d: Draft) => { setErr({}); setForm(""); setDraft(d); };
  const toggle = async (u: OrgUnitItem) => { const r = await run(() => patch(`/api/hr/org-units/${u.id}`, { parentId: u.parentId, archived: !u.archived }), u.archived ? t("Department restored.") : t("Department archived.")); if (r.ok) list.reload(); };

  const columns: Column<OrgUnitItem>[] = [
    { key: "code", header: t("Code"), cell: (u) => <b>{u.code}</b> },
    { key: "name", header: t("Department"), cell: (u) => <>{u.name}{u.archived && <> <Badge tone="gray">{t("Archived")}</Badge></>}</> },
    { key: "parent", header: t("Parent"), className: "hide-sm", cell: (u) => u.parentName ?? "—" },
    { key: "employees", header: t("Employees"), cell: (u) => u.employees },
    ...(manage ? [{ key: "actions", header: "", width: 44, cell: (u: OrgUnitItem) => (
      <RowMenu label={t("Actions for {name}", { name: u.name })} items={[
        { label: t("Edit"), onSelect: () => open({ id: u.id, code: u.code, name: u.name, parentId: u.parentId ?? "", archived: u.archived }), hidden: u.archived },
        { label: u.archived ? t("Restore") : t("Archive"), danger: !u.archived, onSelect: () => toggle(u) },
      ]} />) } as Column<OrgUnitItem>] : []),
  ];
  return (
    <Require perm="employees.read">
      <PageHeader title={t("Departments")} description={t("Organisation units that employees belong to.")}
        actions={manage && <Button variant="primary" onClick={() => open(blank)}><Plus size={16} /> {t("Add Department")}</Button>} />
      <Panel>
        <label className="row" style={{ gap: 8, padding: "0 0 12px" }}><input type="checkbox" className="cb" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} /> {t("Show archived")}</label>
        {!list.loading && !list.error && rows.length === 0
          ? <EmptyState title={t("No departments yet")} description={t("Add the first department to organise your employees.")} />
          : <DataTable label={t("Departments")} columns={columns} rows={rows} rowKey={(u) => u.id} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />}
      </Panel>
      <Overlay open={!!draft} onClose={() => setDraft(null)} side="right" title={draft?.id ? t("Edit Department") : t("Add Department")}
        footer={<><Button onClick={() => setDraft(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="dept-form">{t("Save")}</Button></>}>
        {draft && <form id="dept-form" className="stack" noValidate onSubmit={(e) => { e.preventDefault(); save(draft); }}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Code")} htmlFor="dp-code" required error={err.code}><TextInput id="dp-code" data-autofocus value={draft.code} invalid={!!err.code} onChange={(e) => setDraft({ ...draft, code: e.target.value })} /></Field>
          <Field label={t("Name")} htmlFor="dp-name" required error={err.name}><TextInput id="dp-name" value={draft.name} invalid={!!err.name} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></Field>
          <Field label={t("Parent department")} htmlFor="dp-parent" error={err.parentId}><SelectInput id="dp-parent" value={draft.parentId} onChange={(e) => setDraft({ ...draft, parentId: e.target.value })}
            options={[{ value: "", label: t("None (top level)") }, ...rows.filter((u) => !u.archived && u.id !== draft.id).map((u) => ({ value: u.id, label: u.name }))]} /></Field>
        </form>}
      </Overlay>
    </Require>
  );
}
