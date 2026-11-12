"use client";

import { useState } from "react";
import { Copy, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { del, get, post } from "@/lib/api/client";
import type { KioskDeviceItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { useToast } from "@/lib/toast";
import { t } from "@/i18n";

/** Shared attendance tablets. Registering one gives a link that is shown once; open it on the tablet and keep it there. */
export default function KiosksPage() {
  const run = useRun(); const { notify } = useToast(); const q = useQuery(() => get<KioskDeviceItem[]>("/api/hr/attendance/kiosks"), []);
  const [open, setOpen] = useState(false); const [name, setName] = useState(""); const [link, setLink] = useState(""); const [err, setErr] = useState("");
  async function create(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    const r = await run(() => post<KioskDeviceItem>("/api/hr/attendance/kiosks", { name }), t("Device registered."), { silent: true });
    if (r.ok) { setLink(`${window.location.origin}/kiosk#${r.data.token}`); q.reload(); } else setErr(r.fields?.name ?? r.error);
  }
  const cols: Column<KioskDeviceItem>[] = [
    { key: "n", header: t("Device"), cell: (k) => <><b>{k.name}</b> {k.active ? <Badge tone="green">{t("Active")}</Badge> : <Badge tone="gray">{t("Switched off")}</Badge>}</> },
    { key: "s", header: t("Last used"), cell: (k) => (k.lastSeenAt ? fmtDateTime(k.lastSeenAt) : "—") },
    { key: "x", header: "", cell: (k) => (k.active ? <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/hr/attendance/kiosks/${k.id}`), t("Device switched off."))).ok) q.reload(); }}>{t("Switch off")}</Button> : null) },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="attendance.manage">
      <PageHeader title={t("Attendance Tablets")} description={t("A tablet at the entrance where people clock in with their employee number and PIN, for staff without their own phone.")}
        actions={<Button variant="primary" onClick={() => { setName(""); setLink(""); setErr(""); setOpen(true); }}><Plus size={16} /> {t("Register a tablet")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No tablets")} description={t("Register a tablet, open its link on the device, and let people set their PIN under My Attendance.")} />
          : <DataTable label={t("Attendance tablets")} columns={cols} rows={rows} rowKey={(k) => k.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Register a tablet")} footer={link ? <Button variant="primary" onClick={() => setOpen(false)}>{t("Done")}</Button> : <><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="kd-form">{t("Register")}</Button></>}>
        {link ? (
          <div className="stack">
            <div className="form-alert" role="status">{t("Open this link on the tablet now. It is shown only once; if it is lost, switch the device off and register it again.")}</div>
            <TextInput aria-label={t("Tablet link")} readOnly value={link} onFocus={(e) => e.currentTarget.select()} />
            <div><Button onClick={async () => { await navigator.clipboard.writeText(link); notify("success", t("Link copied.")); }}><Copy size={14} /> {t("Copy link")}</Button></div>
          </div>) : (
          <form id="kd-form" className="stack" noValidate onSubmit={create}>
            <Field label={t("Name")} htmlFor="kd-name" required error={err}><TextInput id="kd-name" data-autofocus value={name} onChange={(e) => setName(e.target.value)} placeholder={t("Lobby tablet")} /></Field>
          </form>)}
      </Overlay>
    </Require>
  );
}
