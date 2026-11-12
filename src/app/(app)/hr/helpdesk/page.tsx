"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { TICKET_STATUS, TICKET_TOPICS } from "@/components/hr/ticket-labels";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import type { TicketItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";


/** Ask HR: employees see their own questions; HR switches to everyone's. */
export default function HelpdeskPage() {
  const { can } = useAuth(); const run = useRun(); const router = useRouter();
  const [scope, setScope] = useState<"mine" | "all">(can("hrconfig.manage") ? "all" : "mine"); const [status, setStatus] = useState("open");
  const q = useQuery(() => get<TicketItem[]>("/api/hr/helpdesk", { scope, status }), [scope, status]);
  const [ask, setAsk] = useState(false); const [f, setF] = useState({ category: "payroll", subject: "", body: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function send(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<TicketItem>("/api/hr/helpdesk", f), t("Question sent to HR."), { silent: true });
    if (r.ok) router.push(`/hr/helpdesk/${r.data.id}`); else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const cols: Column<TicketItem>[] = [
    { key: "s", header: t("Question"), cell: (x) => <><Link href={`/hr/helpdesk/${x.id}`}><b>#{x.number} {x.subject}</b></Link><small className="sub">{t(TICKET_TOPICS[x.category] ?? x.category)}{scope === "all" ? ` · ${x.employeeName}` : ""}</small></> },
    { key: "u", header: t("Last update"), className: "hide-sm", cell: (x) => fmtDate(x.updatedAt) },
    { key: "a", header: t("Handled by"), className: "hide-md", cell: (x) => x.assignedTo ?? "—" },
    { key: "t", header: t("Status"), cell: (x) => <Badge tone={TICKET_STATUS[x.status][0]}>{t(TICKET_STATUS[x.status][1])}</Badge> },
  ];
  const rows = q.data ?? []; const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  return (
    <>
      <PageHeader title={t("Ask HR")} description={t("Questions about pay, leave, documents or your data go to HR here, with the whole conversation kept in one place.")}
        actions={<div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
          {can("hrconfig.manage") && <SelectInput aria-label={t("Show")} value={scope} onChange={(e) => setScope(e.target.value as "mine" | "all")} options={[{ value: "all", label: t("Everyone's questions") }, { value: "mine", label: t("My questions") }]} />}
          <SelectInput aria-label={t("Status")} value={status} onChange={(e) => setStatus(e.target.value)} options={[{ value: "open", label: t("Open") }, { value: "closed", label: t("Answered and closed") }, { value: "all", label: t("All") }]} />
          {!unlinked && <Button variant="primary" onClick={() => { setErr({}); setForm(""); setF({ category: "payroll", subject: "", body: "" }); setAsk(true); }}><Plus size={16} /> {t("Ask a question")}</Button>}
        </div>} />
      <Panel>
        {unlinked && scope === "mine" ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No questions")} description={scope === "all" ? t("Questions from employees appear here.") : t("Ask HR anything about your work: you get an answer here and a notification.")} />
          : <DataTable label={t("Questions")} columns={cols} rows={rows} rowKey={(x) => x.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={ask} onClose={() => setAsk(false)} side="right" title={t("Ask a question")} footer={<><Button onClick={() => setAsk(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="tk-form">{t("Send")}</Button></>}>
        <form id="tk-form" className="stack" noValidate onSubmit={send}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Topic")} htmlFor="tk-cat" error={err.category}><SelectInput id="tk-cat" value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })} options={Object.entries(TICKET_TOPICS).map(([k, v]) => ({ value: k, label: t(v) }))} /></Field>
          <Field label={t("Subject")} htmlFor="tk-subj" required error={err.subject}><TextInput id="tk-subj" data-autofocus value={f.subject} invalid={!!err.subject} onChange={(e) => setF({ ...f, subject: e.target.value })} /></Field>
          <Field label={t("Your question")} htmlFor="tk-body" required error={err.body}><textarea id="tk-body" className="input" rows={6} value={f.body} onChange={(e) => setF({ ...f, body: e.target.value })} /></Field>
        </form>
      </Overlay>
    </>
  );
}
