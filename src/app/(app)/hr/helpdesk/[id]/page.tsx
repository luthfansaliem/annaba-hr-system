"use client";

import { use, useState } from "react";
import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox } from "@/components/ui/field";
import { QueryBoundary } from "@/components/query-boundary";
import { get, post } from "@/lib/api/client";
import type { TicketDetailData } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { TICKET_STATUS, TICKET_TOPICS } from "@/components/hr/ticket-labels";
import { t } from "@/i18n";

function Thread({ id }: { id: string }) {
  const run = useRun(); const q = useQuery(() => get<TicketDetailData>(`/api/hr/helpdesk/${id}`), [id]);
  const [body, setBody] = useState(""); const [internal, setInternal] = useState(false); const [err, setErr] = useState("");
  async function reply(e: React.FormEvent) {
    e.preventDefault(); setErr("");
    const r = await run(() => post(`/api/hr/helpdesk/${id}/messages`, { body, internal }), internal ? t("Note added.") : t("Message sent."), { silent: true });
    if (r.ok) { setBody(""); setInternal(false); q.reload(); } else setErr(r.fields?.body ?? r.error);
  }
  const status = async (s: string, assignToMe?: boolean) => { if ((await run(() => post(`/api/hr/helpdesk/${id}/status`, { status: s, assignToMe }), t("Updated."))).ok) q.reload(); };
  return (
    <QueryBoundary q={q} rows={4}>{({ ticket: x, messages, canManage }) => (
      <>
        <PageHeader title={`#${x.number} ${x.subject}`} description={`${t(TICKET_TOPICS[x.category] ?? x.category)} · ${x.employeeName}`}
          actions={<div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
            <Badge tone={TICKET_STATUS[x.status][0]}>{t(TICKET_STATUS[x.status][1])}</Badge>
            {canManage && !x.assignedTo && <Button onClick={() => status("", true)}>{t("Take it")}</Button>}
            {canManage && x.status !== "resolved" && x.status !== "closed" && <Button variant="primary" onClick={() => status("resolved")}>{t("Mark answered")}</Button>}
            {x.status !== "closed" && <Button onClick={() => status("closed")}>{t("Close")}</Button>}
          </div>} />
        <Panel>
          <div className="thread">
            {messages.map((m) => (
              <div key={m.id} className={`msg ${m.internal ? "note" : m.fromHr ? "hr" : ""}`}>
                <small className="sub">{m.author}{m.fromHr ? ` · HR` : ""}{m.internal ? ` · ${t("internal note, not shown to the employee")}` : ""} · {fmtDateTime(m.createdAt)}</small>
                <p>{m.body}</p>
              </div>))}
          </div>
          {x.status !== "closed" && <form className="stack" style={{ marginTop: 16 }} noValidate onSubmit={reply}>
            <textarea aria-label={t("Your reply")} className="input" rows={4} value={body} onChange={(e) => setBody(e.target.value)} placeholder={t("Write a reply…")} />
            {err && <p className="field-error" role="alert">{err}</p>}
            <div className="row" style={{ justifyContent: "space-between", gap: 8, flexWrap: "wrap" }}>
              {canManage ? <label className="row" style={{ gap: 8 }}><Checkbox checked={internal} onChange={(e) => setInternal(e.target.checked)} /> {t("Internal note (only HR sees it)")}</label> : <span />}
              <Button variant="primary" type="submit">{t("Send")}</Button>
            </div>
          </form>}
        </Panel>
      </>)}
    </QueryBoundary>
  );
}

export default function TicketPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <><p style={{ marginBottom: 12 }}><Link href="/hr/helpdesk" className="link-btn"><ArrowLeft size={14} /> {t("All questions")}</Link></p><Thread id={id} /></>;
}
