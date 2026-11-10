"use client";

import { useRef, useState } from "react";
import { Download, History, Replace, Trash2, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { Overlay } from "@/components/ui/overlay";
import { DataTable, type Column } from "@/components/data/data-table";
import { del, get, post } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { DocumentItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const CATS = ["contract", "id_card", "tax", "certificate", "medical", "other"];
const LABEL: Record<string, string> = { contract: "Contract", id_card: "ID card", tax: "Tax document", certificate: "Certificate", medical: "Medical", other: "Other" };
const MAX = 4 * 1024 * 1024;

function toBase64(buf: ArrayBuffer): string {
  let s = ""; const bytes = new Uint8Array(buf); const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) s += String.fromCharCode(...bytes.subarray(i, i + chunk));
  return btoa(s);
}

/** EMP-005 — contracts, ID cards and certificates. The server decides the file type from the bytes; this form only pre-checks size so people get a quick message. */
export function DocumentsTab({ employeeId, canManage }: { employeeId: string; canManage: boolean }) {
  const run = useRun(); const [history, setHistory] = useState(false); const [replaces, setReplaces] = useState<DocumentItem | null>(null);
  const q = useQuery(() => get<DocumentItem[]>(`/api/hr/employees/${employeeId}/documents`, history ? { history: true } : undefined), [employeeId, history]); const file = useRef<HTMLInputElement>(null);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ category: "contract", expiresOn: "" }); const [name, setName] = useState(""); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [busy, setBusy] = useState(false);
  const cols: Column<DocumentItem>[] = [
    { key: "n", header: t("File"), cell: (d) => <><b>{d.fileName}</b> {(d.version ?? 1) > 1 && <Badge tone="blue" dot={false}>v{d.version}</Badge>} {d.superseded && <Badge tone="gray" dot={false}>{t("Older version")}</Badge>}<small className="sub">{t(LABEL[d.category] ?? d.category)} · {(d.sizeBytes / 1024).toFixed(0)} KB{d.scanStatus === "clean" ? ` · ${t("Virus-scanned")}` : ""}</small></> },
    { key: "e", header: t("Expires"), className: "hide-sm", cell: (d) => d.expiresOn ? <Badge tone={new Date(d.expiresOn) < new Date() ? "red" : "gray"}>{fmtDate(d.expiresOn)}</Badge> : "—" },
    { key: "u", header: t("Added"), className: "hide-md", cell: (d) => fmtDate(d.uploadedAt) },
    { key: "a", header: "", cell: (d) => (
      <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
        <Button size="sm" aria-label={t("Download {name}", { name: d.fileName })} onClick={() => run(() => download(`/api/hr/employees/documents/${d.id}/download`))}><Download size={14} /></Button>
        {canManage && !d.superseded && <Button size="sm" aria-label={t("Upload a new version of {name}", { name: d.fileName })} onClick={() => { setErr({}); setForm(""); setName(""); setF({ category: d.category, expiresOn: "" }); setReplaces(d); setOpen(true); }}><Replace size={14} /></Button>}
        {canManage && <Button size="sm" variant="danger-outline" aria-label={t("Remove {name}", { name: d.fileName })} onClick={async () => { if ((await run(() => del(`/api/hr/employees/documents/${d.id}`), t("Document removed."))).ok) q.reload(); }}><Trash2 size={14} /></Button>}
      </div>) },
  ];
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm(""); const picked = file.current?.files?.[0];
    if (!picked) { setErr({ file: t("Choose a file.") }); return; } if (picked.size > MAX) { setErr({ file: t("The file is larger than 4 MB.") }); return; }
    setBusy(true);
    const r = await run(async () => post(`/api/hr/employees/${employeeId}/documents`, { fileName: picked.name, category: f.category, expiresOn: f.expiresOn || null, contentBase64: toBase64(await picked.arrayBuffer()), replacesId: replaces?.id }), t("Document added."), { silent: true });
    setBusy(false);
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  return (
    <div className="stack">
      <div className="row" style={{ gap: 12, alignItems: "center", flexWrap: "wrap" }}>
        {canManage && <Button onClick={() => { setErr({}); setForm(""); setName(""); setReplaces(null); setOpen(true); }}><Upload size={14} /> {t("Upload document")}</Button>}
        <label className="row" style={{ gap: 6, alignItems: "center" }}><Checkbox checked={history} onChange={(e) => setHistory(e.target.checked)} /> <History size={14} aria-hidden="true" /> {t("Show older versions")}</label>
      </div>
      <DataTable label={t("Documents")} columns={cols} rows={q.data ?? []} rowKey={(d) => d.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />
      <small className="sub">{t("Opening or changing documents asks for your password and is recorded in the audit log. PDF, PNG or JPEG, up to 4 MB.")}</small>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={replaces ? t("New version of {name}", { name: replaces.fileName }) : t("Upload document")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="doc-form" disabled={busy}>{busy ? t("Uploading…") : t("Upload")}</Button></>}>
        <form id="doc-form" className="stack" noValidate onSubmit={submit}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("File")} htmlFor="doc-file" required error={err.file}><input id="doc-file" ref={file} type="file" accept="application/pdf,image/png,image/jpeg" onChange={(e) => setName(e.target.files?.[0]?.name ?? "")} />{name && <small className="sub">{name}</small>}</Field>
          <Field label={t("Category")} htmlFor="doc-cat" error={err.category}><SelectInput id="doc-cat" value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })} options={CATS.map((c) => ({ value: c, label: t(LABEL[c]) }))} /></Field>
          <Field label={t("Expires on")} htmlFor="doc-exp" error={err.expiresOn}><TextInput id="doc-exp" type="date" value={f.expiresOn} onChange={(e) => setF({ ...f, expiresOn: e.target.value })} /></Field>
        </form>
      </Overlay>
    </div>
  );
}
