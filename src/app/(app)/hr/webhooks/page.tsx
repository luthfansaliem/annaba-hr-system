"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { del, get, patch, post } from "@/lib/api/client";
import type { WebhookDelivery, WebhookItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Outbound webhooks: other systems (ERP, accounting, intranet) are told when someone is hired or leaves, leave is decided, or payroll is locked. Payloads carry ids and codes only. */
export default function WebhooksPage() {
  const run = useRun(); const q = useQuery(() => get<WebhookItem[]>("/api/hr/webhooks"), []); const events = useQuery(() => get<string[]>("/api/hr/webhooks/events"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ url: "", description: "", events: [] as string[] }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [secret, setSecret] = useState<string | null>(null); const [log, setLog] = useState<{ hook: WebhookItem; rows: WebhookDelivery[] } | null>(null);
  const rows = q.data ?? [];
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<{ secret: string }>("/api/hr/webhooks", { url: f.url, description: f.description || undefined, events: f.events }), undefined, { silent: true });
    if (r.ok) { setOpen(false); setSecret(r.data.secret); setF({ url: "", description: "", events: [] }); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<WebhookItem>[] = [
    { key: "u", header: t("Address"), cell: (w) => <><b style={{ wordBreak: "break-all" }}>{w.url}</b><small className="sub">{w.description ?? ""}</small></> },
    { key: "e", header: t("Events"), className: "hide-sm", cell: (w) => <div className="chips">{w.events.map((x) => <Badge key={x} tone="blue" dot={false}>{x}</Badge>)}</div> },
    { key: "s", header: t("Status"), cell: (w) => <><Badge tone={w.active ? "green" : "gray"}>{w.active ? t("Active") : t("Off")}</Badge>{w.pending > 0 && <small className="sub">{t("{n} waiting", { n: w.pending })}</small>}{w.dead > 0 && <small className="sub" style={{ color: "var(--red, #e5484d)" }}>{t("{n} failed for good", { n: w.dead })}</small>}</> },
    { key: "d", header: t("Last delivered"), className: "hide-md", cell: (w) => (w.lastDeliveredAt ? fmtDateTime(w.lastDeliveredAt) : "—") },
    { key: "a", header: "", cell: (w) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end", flexWrap: "wrap" }}>
        <Button size="sm" onClick={async () => { if ((await run(() => post(`/api/hr/webhooks/${w.id}/test`), t("Test event queued."))).ok) q.reload(); }}>{t("Send test")}</Button>
        <Button size="sm" onClick={async () => { const r = await run(() => get<WebhookDelivery[]>(`/api/hr/webhooks/${w.id}/deliveries`)); if (r.ok) setLog({ hook: w, rows: r.data }); }}>{t("Deliveries")}</Button>
        <Button size="sm" onClick={async () => { if ((await run(() => patch(`/api/hr/webhooks/${w.id}`, { active: !w.active }), w.active ? t("Webhook switched off.") : t("Webhook switched on."))).ok) q.reload(); }}>{w.active ? t("Switch off") : t("Switch on")}</Button>
        <Button size="sm" onClick={async () => { const r = await run(() => post<{ secret: string }>(`/api/hr/webhooks/${w.id}/rotate-secret`)); if (r.ok) setSecret(r.data.secret); }}>{t("New secret")}</Button>
        <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/hr/webhooks/${w.id}`), t("Webhook deleted."))).ok) q.reload(); }}>{t("Delete")}</Button>
      </div>) },
  ];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Webhooks")} description={t("Tell other systems when something happens in HR. Only https addresses on the public internet; every call is signed.")} actions={<Button variant="primary" onClick={() => { setErr({}); setForm(""); setOpen(true); }}><Plus size={16} /> {t("Add webhook")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No webhooks")} description={t("Add an address to start receiving events.")} />
          : <DataTable label={t("Webhooks")} columns={cols} rows={rows} rowKey={(w) => w.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Add webhook")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="wh-form">{t("Save")}</Button></>}>
        <form id="wh-form" className="stack" noValidate onSubmit={create}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Address (https://…)")} htmlFor="wh-url" required error={err.url}><TextInput id="wh-url" data-autofocus value={f.url} invalid={!!err.url} onChange={(e) => setF({ ...f, url: e.target.value })} placeholder="https://erp.example.com/hooks/annaba" /></Field>
          <Field label={t("Description")} htmlFor="wh-desc" error={err.description}><TextInput id="wh-desc" value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} placeholder={t("Optional")} /></Field>
          <fieldset className="fl"><legend>{t("Events")} <em>*</em></legend>{err.events && <p className="field-error" role="alert">{err.events}</p>}
            {(events.data ?? []).map((ev) => <label key={ev} className="row" style={{ gap: 8 }}><Checkbox checked={f.events.includes(ev)} onChange={(e) => setF({ ...f, events: e.target.checked ? [...f.events, ev] : f.events.filter((x) => x !== ev) })} /> <code>{ev}</code></label>)}</fieldset>
        </form>
      </Overlay>
      <Overlay open={!!secret} onClose={() => setSecret(null)} title={t("Signing secret")} description={t("Copy it now: it is shown only once. Use it to check the X-Annaba-Signature header (HMAC-SHA256 of \"timestamp.body\").")} footer={<Button variant="primary" onClick={() => setSecret(null)}>{t("I have copied it")}</Button>}>
        <code style={{ wordBreak: "break-all", display: "block", padding: 12 }} aria-label={t("Signing secret")}>{secret}</code>
      </Overlay>
      <Overlay open={!!log} onClose={() => setLog(null)} side="right" title={t("Deliveries")} description={log?.hook.url} footer={<Button onClick={() => setLog(null)}>{t("Close")}</Button>}>
        {log && (log.rows.length === 0 ? <p className="tab-placeholder">{t("Nothing has been sent yet.")}</p> : (
          <table className="tbl" aria-label={t("Deliveries")}><thead><tr><th>{t("Event")}</th><th>{t("Status")}</th><th>{t("Tries")}</th><th>{t("When")}</th></tr></thead>
            <tbody>{log.rows.map((d) => <tr key={d.id}><td><code>{d.event}</code></td><td><Badge tone={d.status === "delivered" ? "green" : d.status === "dead" ? "red" : "orange"}>{d.status === "delivered" ? t("Delivered") : d.status === "dead" ? t("Failed") : t("Waiting")}</Badge>{d.lastError && <small className="sub">{d.lastError}</small>}</td><td>{d.attempts}</td><td>{fmtDateTime(d.deliveredAt ?? d.createdAt)}</td></tr>)}</tbody></table>))}
      </Overlay>
    </Require>
  );
}
