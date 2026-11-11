"use client";

import { useEffect, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { post } from "@/lib/api/client";
import type { PayItem, SeverancePreview } from "@/lib/api/types";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const REASONS: [string, string][] = [["resignation", "Resignation"], ["end_of_contract", "End of fixed-term contract"], ["layoff_efficiency", "Layoff (efficiency)"], ["layoff_other", "Layoff (other)"], ["retirement", "Retirement"], ["death", "Death"], ["misconduct", "Serious misconduct"]];

/** PP 35/2021 termination pay: preview the amounts first, then add them to the pay run as lines. The rules are baseline values to be confirmed by a labour/tax consultant. */
export function SeveranceDrawer({ runId, items, open, onClose, onDone }: { runId: string; items: PayItem[]; open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun(); const [f, setF] = useState({ employeeId: "", reason: "layoff_other", lastDay: new Date().toISOString().slice(0, 10) }); const [pv, setPv] = useState<SeverancePreview | null>(null); const [err, setErr] = useState("");
  useEffect(() => { if (open) { setPv(null); setErr(""); } }, [open]);
  const body = () => ({ ...f, employeeId: f.employeeId || items[0]?.employeeId });
  async function preview() { setErr(""); const r = await run(() => post<SeverancePreview>("/api/hr/payroll/severance/preview", body()), undefined, { silent: true }); if (r.ok) setPv(r.data); else { setPv(null); setErr(r.error); } }
  async function apply() { const r = await run(() => post(`/api/hr/payroll/runs/${runId}/severance`, body()), t("Severance added. Calculate again to include it."), { silent: true }); if (r.ok) { onClose(); onDone(); } else setErr(r.error); }
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Severance and termination pay")} description={t("Pesangon, UPMK, unused-leave pay and fixed-term compensation per PP 35/2021.")}
      footer={<><Button onClick={onClose}>{t("Cancel")}</Button><Button onClick={preview}>{t("Preview")}</Button><Button variant="primary" disabled={!pv || pv.lines.length === 0} onClick={apply}>{t("Add to pay run")}</Button></>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        <Field label={t("Employee")} htmlFor="sv-emp"><SelectInput id="sv-emp" value={f.employeeId || items[0]?.employeeId || ""} onChange={(e) => { setF({ ...f, employeeId: e.target.value }); setPv(null); }} options={items.map((i) => ({ value: i.employeeId, label: `${i.employeeName} (${i.employeeNo})` }))} /></Field>
        <Field label={t("Reason")} htmlFor="sv-reason"><SelectInput id="sv-reason" value={f.reason} onChange={(e) => { setF({ ...f, reason: e.target.value }); setPv(null); }} options={REASONS.map(([value, label]) => ({ value, label: t(label) }))} /></Field>
        <Field label={t("Last working day")} htmlFor="sv-last"><TextInput id="sv-last" type="date" value={f.lastDay} onChange={(e) => { setF({ ...f, lastDay: e.target.value }); setPv(null); }} /></Field>
        {pv && (
          <div className="stack">
            <p className="sub">{t("{y} full year(s) of service, {m} month(s) in total. Monthly wage {w}.", { y: pv.years, m: pv.months, w: idr(pv.wage) })}</p>
            {pv.lines.length === 0 ? <p className="tab-placeholder">{t("Nothing is owed for this reason.")}</p> : (
              <table className="tbl" aria-label={t("Severance preview")}><tbody>{pv.lines.map((l) => <tr key={l.label}><td>{l.label.replace("Severance: ", "")}<small className="sub">{l.basis}</small></td><td style={{ textAlign: "right" }}>{l.kind === "deduction" ? "−" : ""}{idr(l.amount)}</td></tr>)}
                <tr><td><b>{t("Net amount")}</b></td><td style={{ textAlign: "right" }}><b>{idr(pv.netTotal)}</b></td></tr></tbody></table>)}
          </div>)}
      </div>
    </Overlay>
  );
}
