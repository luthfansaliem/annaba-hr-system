"use client";

import { useState } from "react";
import { ArrowDown, ArrowUp, Copy, KeyRound, Plus, Trash2 } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { QueryBoundary } from "@/components/query-boundary";
import { del, get, post, put } from "@/lib/api/client";
import type { ApiKeyItem, BankLayout, JournalAccountItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDate, fmtDateTime } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const fieldsOf = (r: { fields?: Record<string, string> }) => r.fields ?? {};

// ============================================================ API keys
export function ApiKeys() {
  const run = useRun(); const q = useQuery(() => get<{ keys: ApiKeyItem[]; scopes: Record<string, string> }>("/api/integrations/api-keys"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ name: "", scopes: [] as string[], ips: "", days: "365" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [secret, setSecret] = useState("");
  async function create(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post<{ secret: string }>("/api/integrations/api-keys", { name: f.name, scopes: f.scopes, allowedIps: f.ips.split(/[\s,]+/).filter(Boolean), expiresInDays: f.days ? Number(f.days) : null }), undefined, { silent: true });
    if (r.ok) { setSecret(r.data.secret); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const origin = typeof window === "undefined" ? "" : window.location.origin;
  return <QueryBoundary q={q}>{(d) => {
    const cols: Column<ApiKeyItem>[] = [
      { key: "n", header: t("Key"), cell: (k) => <><b>{k.name}</b><small className="sub"><code>{k.prefix}…</code></small></> },
      { key: "s", header: t("May read"), cell: (k) => <div className="chips">{k.scopes.map((s) => <code key={s}>{s}</code>)}</div> },
      { key: "i", header: t("From"), className: "hide-sm", cell: (k) => (k.allowedIps.length ? k.allowedIps.join(", ") : t("Any address")) },
      { key: "u", header: t("Last used"), className: "hide-sm", cell: (k) => (k.lastUsedAt ? fmtDateTime(k.lastUsedAt) : t("Never")) },
      { key: "e", header: t("Expires"), className: "hide-sm", cell: (k) => (k.expiresAt ? fmtDate(k.expiresAt) : t("Never")) },
      { key: "st", header: t("Status"), cell: (k) => (k.active ? <Badge tone="green">{t("Active")}</Badge> : <Badge tone="gray">{t("Inactive")}</Badge>) },
      { key: "x", header: "", cell: (k) => (k.active ? <Button size="sm" variant="danger-outline" onClick={async () => { if ((await run(() => del(`/api/integrations/api-keys/${k.id}`), t("Key revoked. Systems using it are cut off at once."))).ok) q.reload(); }}>{t("Revoke")}</Button> : null) },
    ];
    return (
      <>
        <div className="row" style={{ justifyContent: "flex-end", marginBottom: 12 }}><Button variant="primary" onClick={() => { setErr({}); setForm(""); setSecret(""); setF({ name: "", scopes: [], ips: "", days: "365" }); setOpen(true); }}><KeyRound size={16} /> {t("New API key")}</Button></div>
        <Panel>
          {d.keys.length === 0 ? <EmptyState title={t("No API keys")} description={t("Give another system (access control, BI, an ERP) read-only access to employees, leave or attendance.")} />
            : <DataTable label={t("API keys")} columns={cols} rows={d.keys} rowKey={(k) => k.id} />}
        </Panel>
        <Panel title={t("Using the API")} description={t("Read-only, JSON, up to 600 calls per 10 minutes per key. Every call is recorded in the audit log.")}>
          <pre className="code-block">{`curl -H "Authorization: Bearer ak_…" ${origin}/api/public/v1/employees?page=1&pageSize=100
GET /api/public/v1/employees?updatedSince=2027-01-01T00:00:00Z   (employees.read)
GET /api/public/v1/org-units                                    (employees.read)
GET /api/public/v1/leave-requests?from=2027-01-01&to=2027-03-31 (leave.read_all)
GET /api/public/v1/attendance?from=2027-01-01&to=2027-01-31     (attendance.read_all, max 92 days)`}</pre>
          <small className="sub">{t("Salary, tax, ID numbers and bank data are never available through the API.")}</small>
        </Panel>
        <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New API key")} footer={secret ? <Button variant="primary" onClick={() => setOpen(false)}>{t("Done")}</Button> : <><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="ak-form">{t("Create key")}</Button></>}>
          {secret ? <div className="stack">
            <div className="form-alert" role="status">{t("Copy the key now. It is shown only once; if it is lost, revoke it and create a new one.")}</div>
            <code className="code-block" style={{ wordBreak: "break-all" }}>{secret}</code>
            <Button onClick={() => navigator.clipboard?.writeText(secret)}><Copy size={14} /> {t("Copy")}</Button>
          </div> : <form id="ak-form" className="stack" noValidate onSubmit={create}>
            {form && <div className="form-alert err" role="alert">{form}</div>}
            <Field label={t("System that uses it")} htmlFor="ak-name" required error={err.name}><TextInput id="ak-name" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("e.g. Door access system")} /></Field>
            <fieldset className="stack"><legend>{t("May read")}</legend>
              {Object.entries(d.scopes).map(([k, label]) => <label key={k} className="check"><Checkbox checked={f.scopes.includes(k)} onChange={(e) => setF({ ...f, scopes: e.target.checked ? [...f.scopes, k] : f.scopes.filter((x) => x !== k) })} /> {t(label)}</label>)}
              {err.scopes && <small className="err">{err.scopes}</small>}
            </fieldset>
            <Field label={t("Only from these IP addresses")} htmlFor="ak-ip" error={err.allowedIps} hint={t("Separated by commas. Empty = any address.")}><TextInput id="ak-ip" value={f.ips} onChange={(e) => setF({ ...f, ips: e.target.value })} placeholder="203.0.113.10" /></Field>
            <Field label={t("Expires after (days)")} htmlFor="ak-days" error={err.expiresInDays} hint={t("Empty = never.")}><TextInput id="ak-days" inputMode="numeric" value={f.days} onChange={(e) => setF({ ...f, days: e.target.value })} /></Field>
          </form>}
        </Overlay>
      </>);
  }}</QueryBoundary>;
}

// ============================================================ bank file layouts
const PRESETS: { name: string; delimiter: string; header: boolean; columns: string[]; amountDecimals: number; note: string }[] = [
  { name: "Generic CSV", delimiter: ",", header: true, columns: ["employee_no", "name", "bank", "account_no", "amount", "reference"], amountDecimals: 2, note: "Readable by most spreadsheet tools." },
  { name: "Bulk transfer (account, amount, name)", delimiter: ",", header: false, columns: ["account_no", "amount", "name", "reference"], amountDecimals: 0, note: "A common shape for bulk payroll uploads. Check the column order against your bank's current template." },
  { name: "Bulk transfer with company account", delimiter: ";", header: false, columns: ["source_account", "account_no", "name", "amount", "currency", "transfer_type", "reference", "email"], amountDecimals: 0, note: "For cash-management uploads that start each line with the debit account. Check against your bank's current template." },
];

export function BankLayouts() {
  const run = useRun(); const q = useQuery(() => get<{ layouts: BankLayout[]; tokens: Record<string, string> }>("/api/hr/integrations/bank-layouts"), []);
  const [edit, setEdit] = useState<BankLayout | "new" | null>(null); const [f, setF] = useState({ name: "", delimiter: ",", header: true, columns: [] as string[], amountDecimals: 2, dateFormat: "yyyyMMdd", sourceAccount: "", note: "" });
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState(""); const [add, setAdd] = useState("account_no");
  const open = (l: BankLayout | "new", preset?: (typeof PRESETS)[number]) => { setErr({}); setForm(""); setEdit(l); setF(l === "new" ? { name: preset?.name ?? "", delimiter: preset?.delimiter ?? ",", header: preset?.header ?? true, columns: preset?.columns ?? ["account_no", "name", "amount"], amountDecimals: preset?.amountDecimals ?? 2, dateFormat: "yyyyMMdd", sourceAccount: "", note: preset?.note ?? "" } : { name: l.name, delimiter: l.delimiter, header: l.header, columns: l.columns, amountDecimals: l.amountDecimals, dateFormat: l.dateFormat, sourceAccount: l.sourceAccount ?? "", note: l.note ?? "" }); };
  async function save(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => (edit === "new" ? post("/api/hr/integrations/bank-layouts", f) : put(`/api/hr/integrations/bank-layouts/${(edit as BankLayout).id}`, f)), t("Layout saved."), { silent: true });
    if (r.ok) { setEdit(null); q.reload(); } else { setErr(fieldsOf(r)); setForm(r.error); }
  }
  const move = (i: number, d: number) => { const c = [...f.columns]; const j = i + d; if (j < 0 || j >= c.length) return; [c[i], c[j]] = [c[j], c[i]]; setF({ ...f, columns: c }); };
  const DELIM: Record<string, string> = { ",": "Comma", ";": "Semicolon", "|": "Pipe", "\t": "Tab" };
  return <QueryBoundary q={q}>{(d) => (
    <>
      <div className="row" style={{ justifyContent: "flex-end", gap: 8, marginBottom: 12, flexWrap: "wrap" }}>
        <SelectInput aria-label={t("Start from")} value="" onChange={(e) => { const p = PRESETS.find((x) => x.name === e.target.value); if (p) open("new", p); }} options={[{ value: "", label: t("Start from a template…") }, ...PRESETS.map((p) => ({ value: p.name, label: t(p.name) }))]} />
        <Button variant="primary" onClick={() => open("new")}><Plus size={16} /> {t("New layout")}</Button>
      </div>
      <Panel description={t("Banks change their bulk-upload templates. Compare a layout with your bank's current template before the first real transfer, and test with a small file.")}>
        {d.layouts.length === 0 ? <EmptyState title={t("No bank layouts yet")} description={t("Without a layout the bank file is a generic CSV. Add your bank's bulk-upload layout so the file uploads as it is.")} />
          : <DataTable label={t("Bank file layouts")} rows={d.layouts} rowKey={(l) => l.id} columns={[
            { key: "n", header: t("Layout"), cell: (l) => <><b>{l.name}</b>{l.note && <small className="sub">{l.note}</small>}</> },
            { key: "c", header: t("Columns"), cell: (l) => <div className="chips">{l.columns.map((c, i) => <span key={i} className="tag">{t(d.tokens[c] ?? c)}</span>)}</div> },
            { key: "f", header: t("Format"), className: "hide-sm", cell: (l) => `${t(DELIM[l.delimiter] ?? l.delimiter)} · ${l.header ? t("header row") : t("no header")} · ${l.amountDecimals ? t("2 decimals") : t("whole rupiah")}` },
            { key: "x", header: "", cell: (l) => <div className="row" style={{ gap: 6 }}><Button size="sm" variant="soft" onClick={() => open(l)}>{t("Edit")}</Button><Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={async () => { if ((await run(() => del(`/api/hr/integrations/bank-layouts/${l.id}`), t("Removed."))).ok) q.reload(); }}><Trash2 size={14} /></Button></div> },
          ] as Column<BankLayout>[]} />}
      </Panel>
      <Overlay open={edit !== null} onClose={() => setEdit(null)} side="right" title={edit === "new" ? t("New layout") : t("Edit layout")} footer={<><Button onClick={() => setEdit(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="bl-form">{t("Save")}</Button></>}>
        <form id="bl-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Name")} htmlFor="bl-n" required error={err.name}><TextInput id="bl-n" value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder={t("e.g. BCA payroll upload")} /></Field>
          <div className="fgrid">
            <Field label={t("Separator")} htmlFor="bl-d" error={err.delimiter}><SelectInput id="bl-d" value={f.delimiter} onChange={(e) => setF({ ...f, delimiter: e.target.value })} options={Object.keys(DELIM).map((k) => ({ value: k, label: t(DELIM[k]) }))} /></Field>
            <Field label={t("Amounts")} htmlFor="bl-a" error={err.amountDecimals}><SelectInput id="bl-a" value={String(f.amountDecimals)} onChange={(e) => setF({ ...f, amountDecimals: Number(e.target.value) })} options={[{ value: "0", label: t("whole rupiah") }, { value: "2", label: t("2 decimals") }]} /></Field>
            <Field label={t("Date pattern")} htmlFor="bl-df" error={err.dateFormat}><TextInput id="bl-df" value={f.dateFormat} onChange={(e) => setF({ ...f, dateFormat: e.target.value })} /></Field>
            <Field label={t("Company account")} htmlFor="bl-src" error={err.sourceAccount}><TextInput id="bl-src" inputMode="numeric" value={f.sourceAccount} onChange={(e) => setF({ ...f, sourceAccount: e.target.value })} /></Field>
          </div>
          <label className="check"><Checkbox checked={f.header} onChange={(e) => setF({ ...f, header: e.target.checked })} /> {t("First line names the columns")}</label>
          <fieldset className="stack"><legend>{t("Columns, in order")}</legend>
            <ol className="col-order">{f.columns.map((c, i) => (
              <li key={i}><span>{i + 1}. {t(d.tokens[c] ?? c)}</span>
                <Button size="sm" variant="soft" aria-label={t("Up")} onClick={() => move(i, -1)}><ArrowUp size={14} /></Button><Button size="sm" variant="soft" aria-label={t("Down")} onClick={() => move(i, 1)}><ArrowDown size={14} /></Button>
                <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={() => setF({ ...f, columns: f.columns.filter((_, j) => j !== i) })}><Trash2 size={14} /></Button></li>))}</ol>
            <div className="row" style={{ gap: 6 }}><SelectInput aria-label={t("Column to add")} value={add} onChange={(e) => setAdd(e.target.value)} options={Object.entries(d.tokens).map(([k, v]) => ({ value: k, label: t(v) }))} /><Button size="sm" onClick={() => setF({ ...f, columns: [...f.columns, add] })}><Plus size={14} /> {t("Add")}</Button></div>
            {err.columns && <small className="err">{err.columns}</small>}
          </fieldset>
          <Field label={t("Note")} htmlFor="bl-note"><TextInput id="bl-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </>)}</QueryBoundary>;
}

// ============================================================ journal accounts
export function JournalAccounts() {
  const run = useRun(); const { can } = useAuth(); const q = useQuery(() => get<JournalAccountItem[]>("/api/hr/integrations/journal-accounts"), []);
  const [rows, setRows] = useState<JournalAccountItem[] | null>(null); const [form, setForm] = useState("");
  return <QueryBoundary q={q}>{(d) => { const r = rows ?? d; const editable = can("payroll.configure"); return (
    <Panel description={t("The account codes of your own chart of accounts, used in the payroll journal. Download the journal from a locked pay run as a generic file or in Xero's manual journal layout; Jurnal and Accurate import it by mapping the columns.")}>
      {form && <div className="form-alert err" role="alert">{form}</div>}
      <table className="table"><thead><tr><th>{t("Journal line")}</th><th>{t("Account code")}</th><th>{t("Account name")}</th></tr></thead>
        <tbody>{r.map((a, i) => <tr key={a.line}><td>{t(a.label)}</td>
          <td><TextInput aria-label={t("Account code")} value={a.code} disabled={!editable} onChange={(e) => setRows(r.map((x, j) => (j === i ? { ...x, code: e.target.value } : x)))} /></td>
          <td><TextInput aria-label={t("Account name")} value={a.name} disabled={!editable} onChange={(e) => setRows(r.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} /></td></tr>)}</tbody></table>
      {editable && <div className="row" style={{ justifyContent: "flex-end", marginTop: 12 }}><Button variant="primary" disabled={!rows} onClick={async () => { setForm(""); const res = await run(() => put<JournalAccountItem[]>("/api/hr/integrations/journal-accounts", r.map((x) => ({ line: x.line, code: x.code, name: x.name }))), t("Accounts saved."), { silent: true }); if (res.ok) { q.setData(res.data); setRows(null); } else setForm(res.error); }}>{t("Save")}</Button></div>}
    </Panel>); }}</QueryBoundary>;
}
