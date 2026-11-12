"use client";

import { useState } from "react";
import { Download, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { MyIssuedLetters } from "@/components/hr/admin-extras";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { LetterItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TONE: Record<string, ["orange" | "green" | "red" | "gray", string]> = { pending: ["orange", "Pending"], approved: ["green", "Issued"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"] };

/** ESS-003 — proof of employment (surat keterangan kerja): ask for one, HR approves, download the numbered PDF. */
export default function MyLettersPage() {
  const run = useRun(); const q = useQuery(() => get<LetterItem[]>("/api/hr/me/letters"), []);
  const [open, setOpen] = useState(false); const [purpose, setPurpose] = useState(""); const [err, setErr] = useState(""); 
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  async function submit(e: React.FormEvent) {
    e.preventDefault();
    const r = await run(() => post("/api/hr/me/letters", { purpose }), t("Request sent to HR."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else setErr(r.fields?.purpose ?? r.error);
  }
  const cols: Column<LetterItem>[] = [
    { key: "p", header: t("Purpose"), cell: (l) => <><b>{l.purpose}</b><small className="sub">{fmtDate(l.createdAt)}{l.number ? ` · ${l.number}` : ""}</small></> },
    { key: "s", header: t("Status"), cell: (l) => <><Badge tone={TONE[l.status]?.[0] ?? "gray"}>{t(TONE[l.status]?.[1] ?? l.status)}</Badge>{l.decisionNote && <small className="sub">{l.decisionNote}</small>}</> },
    { key: "a", header: "", cell: (l) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        {l.status === "approved" && <Button size="sm" onClick={() => run(() => download(`/api/hr/me/letters/${l.id}/pdf`), t("File downloaded."))}><Download size={14} /> {t("PDF")}</Button>}
        {l.canCancel && <Button size="sm" onClick={async () => { if ((await run(() => post(`/api/hr/me/letters/${l.id}/cancel`), t("Request cancelled."))).ok) q.reload(); }}>{t("Cancel")}</Button>}
      </div>) },
  ];
  return (
    <>
      <PageHeader title={t("Employment Letters")} description={t("Proof of employment for a bank, a visa or a landlord. HR approves each request; the letter states no salary.")} actions={!unlinked && <Button variant="primary" onClick={() => { setPurpose(""); setErr(""); setOpen(true); }}><Plus size={16} /> {t("Request a letter")}</Button>} />
      <Panel>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !q.loading && !q.error && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No letters yet")} description={t("Request one and it appears here once HR has approved it.")} />
          : <DataTable label={t("Employment Letters")} columns={cols} rows={q.data ?? []} rowKey={(l) => l.id} loading={q.data === undefined && q.loading} error={unlinked ? null : q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <div style={{ marginTop: 16 }}><MyIssuedLetters /></div>
      <Overlay open={open} onClose={() => setOpen(false)} title={t("Request a letter")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="letter-form">{t("Send request")}</Button></>}>
        <form id="letter-form" className="stack" noValidate onSubmit={submit}>
          <Field label={t("What is the letter for?")} htmlFor="lt-purpose" required error={err}><TextInput id="lt-purpose" data-autofocus value={purpose} invalid={!!err} onChange={(e) => setPurpose(e.target.value)} placeholder={t("Home loan application at …")} /></Field>
        </form>
      </Overlay>
    </>
  );
}
