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
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, patch, post } from "@/lib/api/client";
import type { WorkLocation } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const blank = { name: "", latitude: "", longitude: "", radiusMeters: "200" };

/** ATT-002 — company sites. Once at least one exists, people clocking in "at the office" are asked for their position, and clock-ins outside every site are flagged in the attendance report. */
export default function WorkLocationsPage() {
  const { can } = useAuth(); const run = useRun(); const q = useQuery(() => get<WorkLocation[]>("/api/hr/attendance/work-locations", { includeArchived: true }), []);
  const [edit, setEdit] = useState<WorkLocation | "new" | null>(null); const [f, setF] = useState(blank); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const open = (w: WorkLocation | "new") => { setF(w === "new" ? blank : { name: w.name, latitude: String(w.latitude), longitude: String(w.longitude), radiusMeters: String(w.radiusMeters) }); setErr({}); setForm(""); setEdit(w); };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm(""); if (!edit) return;
    const body = { name: f.name, latitude: f.latitude === "" ? null : Number(f.latitude), longitude: f.longitude === "" ? null : Number(f.longitude), radiusMeters: Number(f.radiusMeters) };
    const r = await run(() => (edit === "new" ? post("/api/hr/attendance/work-locations", body) : patch(`/api/hr/attendance/work-locations/${edit.id}`, body)), t("Work location saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const here = () => navigator.geolocation?.getCurrentPosition((p) => setF((x) => ({ ...x, latitude: p.coords.latitude.toFixed(6), longitude: p.coords.longitude.toFixed(6) })), () => setForm(t("Your browser did not share a position.")));
  const manage = can("attendance.manage");
  const cols: Column<WorkLocation>[] = [
    { key: "n", header: t("Location"), cell: (w) => <><b>{w.name}</b> {w.archived && <Badge tone="gray" dot={false}>{t("Archived")}</Badge>}<small className="sub">{w.latitude}, {w.longitude}</small></> },
    { key: "r", header: t("Radius"), cell: (w) => `${w.radiusMeters} m` },
    { key: "a", header: "", cell: (w) => manage ? (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        <Button size="sm" onClick={() => open(w)}>{t("Edit")}</Button>
        <Button size="sm" onClick={async () => { if ((await run(() => patch(`/api/hr/attendance/work-locations/${w.id}`, { archived: !w.archived }), w.archived ? t("Work location restored.") : t("Work location archived."))).ok) q.reload(); }}>{w.archived ? t("Restore") : t("Archive")}</Button>
      </div>) : null },
  ];
  return (
    <Require perm="attendance.read_all">
      <PageHeader title={t("Work Locations")} description={t("Company sites with a radius. Office clock-ins outside every site are flagged for HR; nobody is locked out.")} actions={manage && <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("Add location")}</Button>} />
      <Panel>
        {!q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No work locations")} description={t("Without locations, clock-ins are not checked against a place.")} />
          : <DataTable label={t("Work Locations")} columns={cols} rows={q.data ?? []} rowKey={(w) => w.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!edit} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("Add location") : t("Edit location")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="wl-form">{t("Save")}</Button></>}>
        <form id="wl-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Name")} htmlFor="wl-name" required error={err.name}><TextInput id="wl-name" data-autofocus value={f.name} invalid={!!err.name} onChange={(e) => setF({ ...f, name: e.target.value })} /></Field>
          <div className="fgrid">
            <Field label={t("Latitude")} htmlFor="wl-lat" required error={err.latitude}><TextInput id="wl-lat" inputMode="decimal" value={f.latitude} invalid={!!err.latitude} onChange={(e) => setF({ ...f, latitude: e.target.value })} placeholder="-6.200000" /></Field>
            <Field label={t("Longitude")} htmlFor="wl-lon" required error={err.longitude}><TextInput id="wl-lon" inputMode="decimal" value={f.longitude} invalid={!!err.longitude} onChange={(e) => setF({ ...f, longitude: e.target.value })} placeholder="106.816666" /></Field>
          </div>
          <div><Button type="button" size="sm" onClick={here}>{t("Use my current position")}</Button></div>
          <Field label={t("Radius (metres)")} htmlFor="wl-rad" required error={err.radiusMeters}><TextInput id="wl-rad" inputMode="numeric" value={f.radiusMeters} invalid={!!err.radiusMeters} onChange={(e) => setF({ ...f, radiusMeters: e.target.value })} /></Field>
        </form>
      </Overlay>
    </Require>
  );
}
