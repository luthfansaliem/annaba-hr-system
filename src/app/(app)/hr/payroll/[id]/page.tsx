"use client";

import { use, useState } from "react";
import Link from "next/link";
import { Banknote, Building2, ReceiptText, Users, Wallet, AlertTriangle, ArrowLeft, Download, Plus, Trash2 } from "lucide-react";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { QueryBoundary } from "@/components/query-boundary";
import { PayStatusBadge } from "@/components/hr/pay-status";
import { SeveranceDrawer } from "@/components/hr/severance-drawer";
import { ParallelCompare } from "@/components/hr/parallel-compare";
import { Require } from "@/components/page-guard";
import { del, get, post } from "@/lib/api/client";
import { download } from "@/lib/download";
import type { PayItem, PayLine, PayRunDetail } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime, idr, monthName } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { useToast } from "@/lib/toast";
import { t } from "@/i18n";


function AddLine({ id, items, open, onClose, onDone }: { id: string; items: PayItem[]; open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun(); const [f, setF] = useState({ employeeId: "", kind: "earning", label: "", amount: "", taxable: true }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const r = await run(() => post(`/api/hr/payroll/runs/${id}/lines`, { ...f, employeeId: f.employeeId || items[0]?.employeeId, amount: Number(f.amount) }), t("Line added. Calculate again to include it."), { silent: true });
    if (r.ok) { onClose(); onDone(); setF({ ...f, label: "", amount: "" }); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Add earning or deduction")} description={t("A bonus, THR, reimbursement, loan instalment… for one employee in this run.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="line-form">{t("Add")}</Button></>}>
      <form id="line-form" className="stack" noValidate onSubmit={submit}>
        {form && <div className="form-alert err" role="alert">{form}</div>}
        <Field label={t("Employee")} htmlFor="ln-emp" error={err.employeeId}><SelectInput id="ln-emp" value={f.employeeId || items[0]?.employeeId || ""} onChange={(e) => setF({ ...f, employeeId: e.target.value })} options={items.map((i) => ({ value: i.employeeId, label: `${i.employeeName} (${i.employeeNo})` }))} /></Field>
        <div className="fgrid">
          <Field label={t("Type")} htmlFor="ln-kind"><SelectInput id="ln-kind" value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })} options={[{ value: "earning", label: t("Earning") }, { value: "deduction", label: t("Deduction") }]} /></Field>
          <Field label={t("Amount (IDR)")} htmlFor="ln-amt" required error={err.amount}><TextInput id="ln-amt" inputMode="decimal" value={f.amount} invalid={!!err.amount} onChange={(e) => setF({ ...f, amount: e.target.value })} /></Field>
        </div>
        <Field label={t("Label")} htmlFor="ln-label" required error={err.label}><TextInput id="ln-label" value={f.label} invalid={!!err.label} onChange={(e) => setF({ ...f, label: e.target.value })} placeholder={t("Performance bonus")} /></Field>
        {f.kind === "earning" && <label className="row" style={{ gap: 8 }}><Checkbox checked={f.taxable} onChange={(e) => setF({ ...f, taxable: e.target.checked })} /> {t("Taxable income (a bonus is; a travel reimbursement is not)")}</label>}
      </form>
    </Overlay>
  );
}

function Detail({ id }: { id: string }) {
  const { can } = useAuth(); const run = useRun(); const { notify } = useToast(); const q = useQuery(() => get<PayRunDetail>(`/api/hr/payroll/runs/${id}`), [id]);
  const [add, setAdd] = useState(false); const [sev, setSev] = useState(false); const [compare, setCompare] = useState(false); const [reject, setReject] = useState(false); const [note, setNote] = useState(""); const [rerr, setRerr] = useState("");
  const act = async (path: string, ok: string) => { if ((await run(() => post(`/api/hr/payroll/runs/${id}/${path}`), ok)).ok) q.reload(); };
  const [xlsx, setXlsx] = useState(false);
  const [layout, setLayout] = useState(""); const [style, setStyle] = useState("generic");
  const layouts = useQuery(() => (can("payroll.lock") ? get<{ layouts: { id: string; name: string }[] }>("/api/hr/integrations/bank-layouts").then((x) => x.layouts).catch(() => []) : Promise.resolve([])), []);
  const file = (path: string) => {
    const q = new URLSearchParams(); if (xlsx && path !== "bank-file") q.set("format", "xlsx"); if (path === "bank-file" && layout) q.set("layoutId", layout); if (path === "journal" && style !== "generic") { q.set("style", style); q.delete("format"); }
    return run(() => download(`/api/hr/payroll/runs/${id}/${path}${q.size ? `?${q}` : ""}`), t("File downloaded."));
  };
  const addThr = async () => { const r = await run(() => post<{ added: number }>(`/api/hr/payroll/runs/${id}/thr`)); if (r.ok) { notify("success", r.data.added > 0 ? t("THR added for {n} employee(s). Calculate again to include it.", { n: r.data.added }) : t("No new THR to add.")); q.reload(); } };
  const removeLine = async (l: PayLine) => { if ((await run(() => del(`/api/hr/payroll/runs/${id}/lines/${l.id}`), t("Line removed."))).ok) q.reload(); };

  return (
    <QueryBoundary q={q} rows={6}>{({ run: r, items, lines, missing, attendanceClosed }) => {
      const warned = items.filter((i) => i.warnings).length;
      const cols: Column<PayItem>[] = [
        { key: "e", header: t("Employee"), cell: (i) => <><b>{i.employeeName}</b><small className="sub">{i.employeeNo} · {i.ptkp}</small></> },
        { key: "g", header: t("Gross"), cell: (i) => idr(i.gross) },
        { key: "b", header: t("BPJS (employee)"), className: "hide-md", cell: (i) => idr(i.kesEmployee + i.jhtEmployee + i.jpEmployee) },
        { key: "t", header: t("Income tax"), className: "hide-sm", cell: (i) => <>{idr(i.tax)} <small className="sub">{i.taxMethod === "PPH26" ? "PPh 26" : `PPh 21 · ${i.taxMethod}`}{i.taxTreatment === "gross-up" ? ` · ${t("gross-up")}` : i.taxTreatment === "nett" ? ` · ${t("paid by the company")}` : ""}</small></> },
        { key: "n", header: t("Net pay"), cell: (i) => <b>{idr(i.net)}</b> },
        { key: "w", header: "", cell: (i) => i.warnings ? <span title={i.warnings} aria-label={i.warnings}><Badge tone="orange"><AlertTriangle size={12} /> {i.warnings.split("; ").length}</Badge></span> : null },
      ];
      const lcols: Column<PayLine>[] = [
        { key: "e", header: t("Employee"), cell: (l) => l.employeeName }, { key: "l", header: t("Label"), cell: (l) => <>{l.label}{l.auto && <small className="sub">{t("Generated by the system")}</small>}{l.kind === "earning" && !l.taxable && <small className="sub">{t("Not taxable")}</small>}</> },
        { key: "k", header: t("Type"), cell: (l) => l.kind === "earning" ? t("Earning") : t("Deduction") }, { key: "a", header: t("Amount"), cell: (l) => idr(l.amount) },
        { key: "x", header: "", width: 50, cell: (l) => can("payroll.run") && !l.auto && (r.status === "draft" || r.status === "calculated") ? <Button size="sm" variant="danger-outline" aria-label={t("Remove {name}", { name: l.label })} onClick={() => removeLine(l)}><Trash2 size={14} /></Button> : null },
      ];
      return (
        <>
          <div className="crud-top">
            <div><h1>{monthName(r.year, r.month)}</h1><p><PayStatusBadge status={r.status} /> · {t("Rules")} {r.rulesVersion}{r.calculatedAt ? ` · ${t("Calculated")} ${fmtDateTime(r.calculatedAt)}` : ""}</p></div>
            <div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
              {r.canCalculate && <Button variant="primary" onClick={() => act("calculate", t("Calculated."))}>{r.status === "draft" ? t("Calculate") : t("Recalculate")}</Button>}
              {r.status === "calculated" && can("payroll.approve") && <Button variant="danger-outline" onClick={() => { setRerr(""); setNote(""); setReject(true); }}>{t("Send back")}</Button>}
              {r.canApprove && <Button variant="primary" onClick={() => act("approve", t("Approved."))}>{t("Approve")}</Button>}
              {r.canLock && <Button variant="primary" onClick={() => act("lock", t("Locked."))}>{t("Lock")}</Button>}
              {r.canMarkPaid && <Button variant="primary" onClick={() => act("paid", t("Marked as paid."))}>{t("Mark as paid")}</Button>}
              {(r.status === "locked" || r.status === "paid") && can("payroll.lock") && <span className="row" style={{ gap: 4 }}>{(layouts.data ?? []).length > 0 && <SelectInput aria-label={t("Bank layout")} value={layout} onChange={(e) => setLayout(e.target.value)} options={[{ value: "", label: t("Generic CSV") }, ...(layouts.data ?? []).map((l) => ({ value: l.id, label: l.name }))]} />}<Button onClick={() => file("bank-file")}><Download size={15} /> {t("Bank file")}</Button></span>}
              {r.employeeCount > 0 && <Button onClick={() => file("register")}><Download size={15} /> {t("Register")}</Button>}
              {r.employeeCount > 0 && <Button onClick={() => setCompare(true)}>{t("Compare with the old payroll")}</Button>}
              {r.employeeCount > 0 && <Button onClick={() => file("bpjs")}><Download size={15} /> {t("BPJS report")}</Button>}
              {(r.status === "locked" || r.status === "paid") && <span className="row" style={{ gap: 4 }}><SelectInput aria-label={t("Journal layout")} value={style} onChange={(e) => setStyle(e.target.value)} options={[{ value: "generic", label: t("Generic journal") }, { value: "xero", label: "Xero" }]} /><Button onClick={() => file("journal")}><Download size={15} /> {t("Journal")}</Button></span>}
              {r.employeeCount > 0 && <label className="row" style={{ gap: 6, alignItems: "center" }}><Checkbox checked={xlsx} onChange={(e) => setXlsx(e.target.checked)} /> {t("As Excel (.xlsx)")}</label>}
            </div>
          </div>
          {r.status === "calculated" && !r.canApprove && <div className="form-alert" role="note" style={{ marginBottom: 12 }}>{t("Another person with the approve permission must check this run: the person who calculated it cannot approve it.")}</div>}
          {r.note && <div className="form-alert" role="note" style={{ marginBottom: 12 }}>{r.note}</div>}
          <KpiRow wide label={t("Totals of this pay run")}>
            <Kpi icon={<Users size={20} />} label={t("Employees")} value={r.employeeCount} /><Kpi icon={<Banknote size={20} />} tone="blue" label={t("Gross")} value={idr(r.totalGross)} /><Kpi icon={<ReceiptText size={20} />} tone="orange" label={t("PPh 21")} value={idr(r.totalTax)} />
            <Kpi icon={<Wallet size={20} />} tone="green" label={t("Net pay")} value={idr(r.totalNet)} /><Kpi icon={<Building2 size={20} />} tone="purple" label={t("Employer cost")} value={idr(r.totalEmployerCost)} />
          </KpiRow>
          {!attendanceClosed && (r.status === "draft" || r.status === "calculated") && <div className="form-alert" role="note" style={{ marginBottom: 12 }}>{t("Attendance for this month is not closed yet. Overtime approved later will not be in this run unless you calculate again.")}</div>}
          {missing.length > 0 && <div className="form-alert err" role="alert" style={{ marginBottom: 12 }}>{t("Not in this run (no compensation set):")} {missing.join(", ")}</div>}
          {warned > 0 && <div className="form-alert" role="note" style={{ marginBottom: 12 }}>{t("{n} employee(s) have warnings. Hover the badge to read them before approving.", { n: warned })}</div>}
          <Panel title={t("Employees")}>
            {items.length === 0 ? <p className="tab-placeholder">{r.status === "draft" ? t("Not calculated yet.") : t("No employees in this run.")}</p>
              : <DataTable label={t("Pay run employees")} columns={cols} rows={items} rowKey={(i) => i.id} />}
          </Panel>
          <div style={{ height: 16 }} />
          <Panel title={t("Extra earnings and deductions")} actions={can("payroll.run") && (r.status === "draft" || r.status === "calculated") && items.length > 0 ? <div className="row" style={{ gap: 8 }}><Button size="sm" onClick={addThr}>{t("Add THR")}</Button><Button size="sm" onClick={() => setSev(true)}>{t("Severance")}</Button><Button size="sm" onClick={() => setAdd(true)}><Plus size={14} /> {t("Add")}</Button></div> : undefined}>
            {lines.length === 0 ? <p className="tab-placeholder">{t("None.")}</p> : <DataTable label={t("Extra lines")} columns={lcols} rows={lines} rowKey={(l) => l.id} />}
          </Panel>
          <SeveranceDrawer runId={id} items={items} open={sev} onClose={() => setSev(false)} onDone={q.reload} />
          <ParallelCompare id={id} open={compare} onClose={() => setCompare(false)} />
          <AddLine id={id} items={items} open={add} onClose={() => setAdd(false)} onDone={q.reload} />
          <Overlay open={reject} onClose={() => setReject(false)} title={t("Send back for changes")} footer={<><Button onClick={() => setReject(false)}>{t("Cancel")}</Button><Button variant="danger" type="submit" form="rej-form">{t("Send back")}</Button></>}>
            <form id="rej-form" className="stack" noValidate onSubmit={async (e) => { e.preventDefault(); const res = await run(() => post(`/api/hr/payroll/runs/${id}/reject`, { note }), t("Sent back."), { silent: true }); if (res.ok) { setReject(false); q.reload(); } else setRerr(res.fields?.note ?? res.error); }}>
              <Field label={t("What should be fixed?")} htmlFor="rj-note" required error={rerr}><TextInput id="rj-note" data-autofocus value={note} invalid={!!rerr} onChange={(e) => setNote(e.target.value)} /></Field>
            </form>
          </Overlay>
        </>);
    }}</QueryBoundary>
  );
}

export default function PayRunPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="payroll.read"><p style={{ marginBottom: 12 }}><Link href="/hr/payroll" className="link-btn"><ArrowLeft size={14} /> {t("All pay runs")}</Link></p><Detail id={id} /></Require>;
}
