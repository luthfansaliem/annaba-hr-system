"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Tabs } from "@/components/ui/tabs";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get, patch, post } from "@/lib/api/client";
import type { AckReport, AnnouncementItem, OrgUnitItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** COM-001/003 — announcements and policies. Everyone reads their own; people with HR configuration rights publish and see who has not acknowledged. */
export default function AnnouncementsPage() {
  const { can } = useAuth(); const run = useRun(); const manage = can("hrconfig.manage"); const [tab, setTab] = useState<"mine" | "manage">("mine");
  const mine = useQuery(() => get<AnnouncementItem[]>("/api/hr/announcements"), []); const all = useQuery(() => (manage ? get<AnnouncementItem[]>("/api/hr/announcements/manage") : Promise.resolve([] as AnnouncementItem[])), [manage]);
  const units = useQuery(() => (manage ? get<OrgUnitItem[]>("/api/hr/org-units") : Promise.resolve([] as OrgUnitItem[])), [manage]);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ title: "", body: "", orgUnitId: "", requiresAck: false, expiresOn: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [report, setReport] = useState<AckReport | null>(null);
  const unlinked = (mine.error as { code?: string } | undefined)?.code === "invariant";
  const openIt = (a: AnnouncementItem) => { if (!a.read) run(() => post(`/api/hr/announcements/${a.id}/read`)).then((r) => r.ok && mine.reload()); };
  const ack = async (a: AnnouncementItem) => { if ((await run(() => post(`/api/hr/announcements/${a.id}/ack`), t("Thank you. Your acknowledgement is recorded."))).ok) mine.reload(); };
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post("/api/hr/announcements", { title: f.title, body: f.body, orgUnitId: f.orgUnitId || null, requiresAck: f.requiresAck, expiresOn: f.expiresOn || null }), t("Announcement published."), { silent: true });
    if (r.ok) { setOpen(false); setF({ title: "", body: "", orgUnitId: "", requiresAck: false, expiresOn: "" }); all.reload(); mine.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const List = ({ items, admin }: { items: AnnouncementItem[]; admin: boolean }) => items.length === 0 ? <EmptyState title={t("No announcements")} description={t("Nothing has been published for you.")} /> : (
    <ul className="stack" style={{ listStyle: "none", padding: 0, margin: 0 }}>{items.map((a) => (
      <li key={a.id}><Panel title={<>{a.title} {a.requiresAck && <Badge tone={a.acked || admin ? "gray" : "orange"}>{a.requiresAck && !admin && a.acked ? t("Acknowledged") : t("Acknowledgement needed")}</Badge>} {a.archived && <Badge tone="gray">{t("Archived")}</Badge>}</>}
        description={`${fmtDate(a.publishedAt)}${a.orgUnit ? ` · ${a.orgUnit}` : ` · ${t("Everyone")}`}${a.expiresOn ? ` · ${t("until")} ${fmtDate(a.expiresOn)}` : ""}`}
        actions={admin ? <div className="row" style={{ gap: 6 }}><Button size="sm" onClick={async () => { const r = await run(() => get<AckReport>(`/api/hr/announcements/${a.id}/report`)); if (r.ok) setReport(r.data); }}>{t("Who read it")}</Button>
          <Button size="sm" onClick={async () => { if ((await run(() => patch(`/api/hr/announcements/${a.id}`, { archived: !a.archived }), a.archived ? t("Announcement restored.") : t("Announcement archived."))).ok) { all.reload(); mine.reload(); } }}>{a.archived ? t("Restore") : t("Archive")}</Button></div>
          : <div className="row" style={{ gap: 6 }}>{!a.read && <Button size="sm" onClick={() => openIt(a)}>{t("Mark as read")}</Button>}{a.requiresAck && !a.acked && <Button size="sm" variant="primary" onClick={() => ack(a)}>{t("I have read and understood")}</Button>}</div>}>
        <p style={{ whiteSpace: "pre-wrap" }}>{a.body}</p></Panel></li>))}</ul>);
  return (
    <>
      <PageHeader title={t("Announcements")} description={t("Company news and policies.")} actions={manage && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setOpen(true); }}><Plus size={16} /> {t("New announcement")}</Button>} />
      {manage && <div style={{ marginBottom: 12 }}><Tabs variant="segmented" items={[{ key: "mine", label: t("For me") }, { key: "manage", label: t("Manage") }]} value={tab} onChange={setTab} label={t("View")} /></div>}
      {tab === "mine" ? (unlinked ? <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} /></Panel> : <List items={mine.data ?? []} admin={false} />) : <List items={all.data ?? []} admin />}
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New announcement")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="ann-form">{t("Publish")}</Button></>}>
        <form id="ann-form" className="stack" noValidate onSubmit={create}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Title")} htmlFor="an-title" required error={err.title}><TextInput id="an-title" data-autofocus value={f.title} invalid={!!err.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></Field>
          <Field label={t("Text")} htmlFor="an-body" required error={err.body}><textarea id="an-body" className="ctl" rows={6} value={f.body} onChange={(e) => setF({ ...f, body: e.target.value })} style={{ width: "100%" }} /></Field>
          <div className="fgrid">
            <Field label={t("For")} htmlFor="an-unit" error={err.orgUnitId}><SelectInput id="an-unit" value={f.orgUnitId} onChange={(e) => setF({ ...f, orgUnitId: e.target.value })} options={[{ value: "", label: t("Everyone") }, ...(units.data ?? []).filter((u) => !u.archived).map((u) => ({ value: u.id, label: u.name }))]} /></Field>
            <Field label={t("Expires on")} htmlFor="an-exp" error={err.expiresOn}><TextInput id="an-exp" type="date" value={f.expiresOn} invalid={!!err.expiresOn} onChange={(e) => setF({ ...f, expiresOn: e.target.value })} /></Field>
          </div>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.requiresAck} onChange={(e) => setF({ ...f, requiresAck: e.target.checked })} /> {t("Everyone must acknowledge it (a policy)")}</label>
        </form>
      </Overlay>
      <Overlay open={!!report} onClose={() => setReport(null)} side="right" title={report?.title ?? ""} description={report ? t("{a} of {n} acknowledged · {r} read", { a: report.acked, n: report.audience, r: report.read }) : undefined} footer={<Button onClick={() => setReport(null)}>{t("Close")}</Button>}>
        {report && <table className="tbl" aria-label={t("Who read it")}><thead><tr><th>{t("Employee")}</th><th>{t("Read")}</th><th>{t("Acknowledged")}</th></tr></thead>
          <tbody>{report.people.map((p) => <tr key={p.employeeId}><td>{p.employeeName}<small className="sub">{p.employeeNo}</small></td><td>{p.read ? "✓" : "—"}</td><td>{p.ackedAt ? fmtDate(p.ackedAt) : "—"}</td></tr>)}</tbody></table>}
      </Overlay>
    </>
  );
}
