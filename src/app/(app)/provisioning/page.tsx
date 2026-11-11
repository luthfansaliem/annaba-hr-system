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
import { del, get, post, put } from "@/lib/api/client";
import type { ScimRoleItem, ScimTokenItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime, rel } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** ADM-005 — lets an identity provider (Entra ID, Okta, Google) create, update and deactivate user accounts through SCIM 2.0. */
export default function ProvisioningPage() {
  const run = useRun(); const q = useQuery(() => get<ScimTokenItem[]>("/api/scim-tokens"), []);
  const roles = useQuery(() => get<ScimRoleItem[]>("/api/scim-tokens/roles"), []);
  const [open, setOpen] = useState(false); const [name, setName] = useState(""); const [err, setErr] = useState(""); const [secret, setSecret] = useState<string | null>(null);
  const base = typeof window === "undefined" ? "" : `${window.location.origin}/api/scim/v2`;
  async function create(e: React.FormEvent) {
    e.preventDefault();
    const r = await run(() => post<{ secret: string }>("/api/scim-tokens", { name }), undefined, { silent: true });
    if (r.ok) { setOpen(false); setSecret(r.data.secret); q.reload(); } else setErr(r.fields?.name ?? r.error);
  }
  const cols: Column<ScimTokenItem>[] = [
    { key: "n", header: t("Identity provider"), cell: (x) => <><b>{x.name}</b><small className="sub">{t("Created")} {fmtDateTime(x.createdAt)}</small></> },
    { key: "u", header: t("Last used"), className: "hide-sm", cell: (x) => (x.lastUsedAt ? rel(x.lastUsedAt) : t("Never")) },
    { key: "s", header: t("Status"), cell: (x) => <Badge tone={x.revoked ? "gray" : "green"}>{x.revoked ? t("Revoked") : t("Active")}</Badge> },
    { key: "a", header: "", cell: (x) => x.revoked ? null : <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/scim-tokens/${x.id}`), t("Token revoked."))).ok) q.reload(); }}>{t("Revoke")}</Button> },
  ];
  return (
    <Require perm="settings.manage">
      <PageHeader title={t("User Provisioning (SCIM)")} description={t("Let your identity provider create, update and deactivate accounts. Provisioned users get the plain User role and sign in through single sign-on.")} actions={<Button variant="primary" onClick={() => { setName(""); setErr(""); setOpen(true); }}><Plus size={16} /> {t("New token")}</Button>} />
      <Panel title={t("Connection details")}>
        <dl className="kvs one"><div className="kv"><dt>{t("SCIM base URL")}</dt><dd><code style={{ wordBreak: "break-all" }}>{base}</code></dd></div><div className="kv"><dt>{t("Authentication")}</dt><dd>{t("Bearer token (create one below)")}</dd></div><div className="kv"><dt>{t("Supported")}</dt><dd>{t("Users: create, read, filter by userName or externalId, update, deactivate. Groups: read and change the members of the roles you open up below.")}</dd></div></dl>
      </Panel>
      <Panel title={t("Provisioning tokens")}>
        {!q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No tokens")} description={t("Create a token and paste it into your identity provider.")} />
          : <DataTable label={t("Provisioning tokens")} columns={cols} rows={q.data ?? []} rowKey={(x) => x.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Panel title={t("Roles managed by the identity provider")}>
        <p className="sub" style={{ marginTop: 0 }}>{t("A role you open up appears to the identity provider as a group, and it can then add and remove members. Protected roles can never be opened up. Asks for your password again.")}</p>
        <ul className="stack" style={{ listStyle: "none", padding: 0, margin: 0, gap: 6 }}>
          {(roles.data ?? []).map((r) => (
            <li key={r.id}><label style={{ display: "flex", gap: 8, alignItems: "center" }}>
              <input type="checkbox" checked={r.scimAssignable} disabled={r.protected} onChange={async (e) => { await run(() => put(`/api/scim-tokens/roles/${r.id}`, { assignable: e.target.checked }), e.target.checked ? t("Role opened up for provisioning.") : t("Role closed for provisioning.")); roles.reload(); }} />
              <span>{r.name}</span>{r.protected && <Badge tone="gray">{t("Protected")}</Badge>}
            </label></li>
          ))}
        </ul>
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} title={t("New token")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="scim-form">{t("Create")}</Button></>}>
        <form id="scim-form" className="stack" noValidate onSubmit={create}>
          <Field label={t("Which identity provider is it for?")} htmlFor="scim-name" required error={err}><TextInput id="scim-name" data-autofocus value={name} invalid={!!err} onChange={(e) => setName(e.target.value)} placeholder="Microsoft Entra ID" /></Field>
        </form>
      </Overlay>
      <Overlay open={!!secret} onClose={() => setSecret(null)} title={t("Provisioning token")} description={t("Copy it now: it is shown only once. Anyone holding it can create and deactivate accounts in your organisation.")} footer={<Button variant="primary" onClick={() => setSecret(null)}>{t("I have copied it")}</Button>}>
        <code style={{ wordBreak: "break-all", display: "block", padding: 12 }} aria-label={t("Provisioning token")}>{secret}</code>
      </Overlay>
    </Require>
  );
}
