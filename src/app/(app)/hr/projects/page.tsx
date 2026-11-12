"use client";

import { useState } from "react";
import { Download, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post, put } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { ProjectItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Projects and clients that people book their time against; the monthly report shows approved hours per project and person. */
export default function ProjectsPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<ProjectItem[]>("/api/hr/projects", { includeArchived: true }), []);
  const [edit, setEdit] = useState<ProjectItem | "new" | null>(null); const [f, setF] = useState({ code: "", name: "", client: "", archived: false }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  function open(p: ProjectItem | "new") { setF(p === "new" ? { code: "", name: "", client: "", archived: false } : { code: p.code, name: p.name, client: p.client ?? "", archived: p.archived }); setErr({}); setForm(""); setEdit(p); }
  async function save(e: React.FormEvent) { e.preventDefault(); if (!edit) return; setForm(""); const r = await run(() => (edit === "new" ? post("/api/hr/projects", f) : put(`/api/hr/projects/${edit.id}`, f)), t("Project saved."), { silent: true }); if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); } }
  const now = new Date();
  const cols: Column<ProjectItem>[] = [
    { key: "c", header: t("Project"), cell: (p) => <><b>{p.code} · {p.name}</b> {p.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{p.client ?? "—"}</small></> },
    { key: "h", header: t("Hours this month"), cell: (p) => p.hoursThisMonth },
    { key: "x", header: "", cell: (p) => (can("attendance.manage") ? <Button size="sm" onClick={() => open(p)}>{t("Edit")}</Button> : null) },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="attendance.read_all">
      <PageHeader title={t("Projects")} description={t("Projects and clients people book their hours against in their weekly timesheet.")}
        actions={<div className="row" style={{ gap: 8 }}>
          <Button onClick={() => run(() => download(`/api/hr/timesheets/report?year=${now.getFullYear()}&month=${now.getMonth() + 1}`), t("File downloaded."))}><Download size={16} /> {t("This month's hours (CSV)")}</Button>
          {can("attendance.manage") && <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add project")}</Button>}</div>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No projects")} description={t("Add the projects or clients people work on; they then appear in everyone's timesheet.")} />
          : <DataTable label={t("Projects")} columns={cols} rows={rows} rowKey={(p) => p.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add project") : t("Edit project")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="pj-form">{t("Save")}</Button></>}>
        <form id="pj-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Code")} htmlFor="pj-code" required error={err.code}><TextInput id="pj-code" data-autofocus value={f.code} onChange={(e) => setF({ ...f, code: e.target.value })} placeholder="WEB-01" /></Field>
            <Field label={t("Name")} htmlFor="pj-name" required error={err.name}><TextInput id="pj-name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
          </div>
          <Field label={t("Client")} htmlFor="pj-client"><TextInput id="pj-client" value={f.client} onChange={(e) => setF({ ...f, client: e.target.value })} placeholder={t("Optional")} /></Field>
          {edit !== "new" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.archived} onChange={(e) => setF({ ...f, archived: e.target.checked })} /> {t("Archived (no new hours)")}</label>}
        </form>
      </Overlay>
    </Require>
  );
}
