"use client";

import { useState } from "react";
import { Pencil } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { PayrollRulesOverview } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useAuth } from "@/lib/auth";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const RATES = [
  ["kesEmployeeRate", "Health (BPJS Kes) – employee"], ["kesEmployerRate", "Health (BPJS Kes) – employer"], ["jhtEmployeeRate", "Old-age (JHT) – employee"], ["jhtEmployerRate", "Old-age (JHT) – employer"],
  ["jpEmployeeRate", "Pension (JP) – employee"], ["jpEmployerRate", "Pension (JP) – employer"], ["jkmEmployerRate", "Death (JKM) – employer"], ["defaultJkkRate", "Work accident (JKK) – default"],
  ["positionCostRate", "Position cost rate"], ["noNpwpSurcharge", "No-NPWP surcharge"], ["pph26Rate", "PPh 26 rate (non-residents)"],
] as const;
const CAPS = [["kesWageCap", "Health wage cap"], ["jpWageCap", "Pension wage cap"], ["positionCostMonthlyCap", "Position cost monthly cap"]] as const;
type Bracket = { upto: number; pct: number };
const bracketsText = (b: Bracket[]) => b.map((x) => `${x.upto >= 1e15 ? "max" : x.upto};${x.pct}`).join("\n");
function parseBrackets(s: string): Bracket[] | null {
  const out: Bracket[] = [];
  for (const line of s.split("\n").map((l) => l.trim()).filter(Boolean)) {
    const [u, p] = line.split(";").map((x) => x.trim()); const upto = u.toLowerCase() === "max" ? 1e15 : Number(u); const pct = Number(p);
    if (!Number.isFinite(upto) || !Number.isFinite(pct)) return null; out.push({ upto, pct });
  }
  return out;
}
const pctText = (r: number) => `${+(r * 100).toFixed(4)}%`;

/** The payroll rates as data: contribution rates and caps, tax-free allowances (PTKP) and the monthly tax tables (TER). A change is a new version from the first of a month; pay runs keep the version they used. */
export default function PayrollRulesPage() {
  const run = useRun(); const { can } = useAuth(); const q = useQuery(() => get<PayrollRulesOverview>("/api/hr/payroll/rules"), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState<Record<string, string>>({}); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const o = q.data;
  function begin() {
    if (!o) return; const r = o.rules as unknown as Record<string, number>; const next = new Date(); next.setUTCMonth(next.getUTCMonth() + 1, 1);
    const v: Record<string, string> = { effectiveFrom: next.toISOString().slice(0, 10), reason: "", terA: bracketsText(o.rules.terA), terB: bracketsText(o.rules.terB), terC: bracketsText(o.rules.terC) };
    for (const [k] of [...RATES, ...CAPS]) v[k] = String(r[k]);
    for (const [k, val] of Object.entries(o.rules.ptkpTable)) v[`ptkp_${k}`] = String(val);
    setF(v); setErr({}); setForm(""); setOpen(true);
  }
  async function save(e: React.FormEvent) {
    e.preventDefault(); if (!o) return; setForm(""); const cur = o.rules as unknown as Record<string, number>; const body: Record<string, unknown> = { effectiveFrom: f.effectiveFrom, reason: f.reason };
    for (const [k] of [...RATES, ...CAPS]) { const n = Number(f[k]); if (!Number.isFinite(n)) { setErr({ [k]: t("Enter a number.") }); return; } if (n !== cur[k]) body[k] = n; }
    const ptkp: Record<string, number> = {}; let ptkpChanged = false;
    for (const k of Object.keys(o.rules.ptkpTable)) { const n = Number(f[`ptkp_${k}`]); if (!Number.isFinite(n)) { setErr({ [`ptkp_${k}`]: t("Enter a number.") }); return; } ptkp[k] = n; if (n !== o.rules.ptkpTable[k]) ptkpChanged = true; }
    if (ptkpChanged) body.ptkpTable = ptkp;
    for (const k of ["terA", "terB", "terC"] as const) { const b = parseBrackets(f[k]); if (!b) { setErr({ [k]: t("One bracket per line: upper limit;percent (the last one: max;percent).") }); return; } if (bracketsText(b) !== bracketsText(o.rules[k])) body[k] = b; }
    const r = await run(() => post("/api/hr/payroll/rules", body), t("New rules version saved."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Payroll Rules")} description={t("Contribution rates, tax-free allowances and tax tables. A change applies from the first of a month that no locked pay run covers yet.")} actions={can("payroll.configure") && o && <Button variant="primary" onClick={begin}><Pencil size={16} /> {t("New version")}</Button>} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      {o && <>
        <Panel title={t("In force now")} description={`${o.label}${o.effectiveFrom ? ` · ${t("from")} ${o.effectiveFrom}` : ` · ${t("built-in defaults")}`}`}>
          <table className="tbl" aria-label={t("Rates")}><tbody>
            {RATES.map(([k, label]) => <tr key={k}><th scope="row">{t(label)}</th><td>{pctText((o.rules as unknown as Record<string, number>)[k])}</td></tr>)}
            {CAPS.map(([k, label]) => <tr key={k}><th scope="row">{t(label)}</th><td>{idr((o.rules as unknown as Record<string, number>)[k])}</td></tr>)}
          </tbody></table>
        </Panel>
        <Panel title={t("Tax-free allowance (PTKP)")}>
          <table className="tbl" aria-label={t("PTKP")}><tbody>{Object.entries(o.rules.ptkpTable).map(([k, v]) => <tr key={k}><th scope="row">{k}</th><td>{idr(v)}</td></tr>)}</tbody></table>
        </Panel>
        <Panel title={t("Monthly tax tables (TER)")}>
          <div style={{ display: "grid", gap: 16, gridTemplateColumns: "repeat(auto-fit, minmax(min(100%, 240px), 1fr))" }}>{(["terA", "terB", "terC"] as const).map((k) => <div key={k} style={{ minWidth: 0, overflowX: "auto" }}><b>{t("Category")} {k.slice(-1)}</b>
            <table className="tbl" aria-label={`TER ${k.slice(-1)}`}><thead><tr><th>{t("Up to")}</th><th>%</th></tr></thead><tbody>{o.rules[k].map((b, i) => <tr key={i}><td>{b.upto >= 1e15 ? t("above") : idr(b.upto)}</td><td>{b.pct}</td></tr>)}</tbody></table></div>)}</div>
        </Panel>
        <Panel title={t("Version history")}>
          {o.versions.length === 0 ? <p className="tab-placeholder">{t("No changes yet: the built-in defaults apply.")}</p> : (
            <div style={{ overflowX: "auto" }}><table className="tbl" aria-label={t("Version history")}><thead><tr><th>{t("Version")}</th><th>{t("From")}</th><th>{t("Reason")}</th><th>{t("What changed")}</th></tr></thead>
              <tbody>{o.versions.map((v) => <tr key={v.id}><td><Badge tone="blue" dot={false}>{v.label}</Badge></td><td>{v.effectiveFrom}</td><td>{v.reason}</td><td>{v.changes}</td></tr>)}</tbody></table></div>)}
        </Panel>
      </>}
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("New rules version")} description={t("Only the values you change are recorded. Needs a recent password check.")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rules-form">{t("Save")}</Button></>}>
        <form id="rules-form" className="stack" noValidate onSubmit={save}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <Field label={t("Effective from (first of a month)")} htmlFor="r-from" required error={err.effectiveFrom}><TextInput id="r-from" type="date" data-autofocus value={f.effectiveFrom ?? ""} invalid={!!err.effectiveFrom} onChange={(e) => setF({ ...f, effectiveFrom: e.target.value })} /></Field>
          <Field label={t("Reason (e.g. regulation number)")} htmlFor="r-reason" required error={err.reason}><TextInput id="r-reason" value={f.reason ?? ""} invalid={!!err.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} /></Field>
          {RATES.map(([k, label]) => <Field key={k} label={`${t(label)} (0–1)`} htmlFor={`r-${k}`} error={err[k]}><TextInput id={`r-${k}`} inputMode="decimal" value={f[k] ?? ""} invalid={!!err[k]} onChange={(e) => setF({ ...f, [k]: e.target.value })} /></Field>)}
          {CAPS.map(([k, label]) => <Field key={k} label={t(label)} htmlFor={`r-${k}`} error={err[k]}><TextInput id={`r-${k}`} inputMode="decimal" value={f[k] ?? ""} invalid={!!err[k]} onChange={(e) => setF({ ...f, [k]: e.target.value })} /></Field>)}
          {o && Object.keys(o.rules.ptkpTable).map((k) => <Field key={k} label={`PTKP ${k}`} htmlFor={`r-ptkp-${k}`} error={err[`ptkp_${k}`]}><TextInput id={`r-ptkp-${k}`} inputMode="decimal" value={f[`ptkp_${k}`] ?? ""} invalid={!!err[`ptkp_${k}`]} onChange={(e) => setF({ ...f, [`ptkp_${k}`]: e.target.value })} /></Field>)}
          {(["terA", "terB", "terC"] as const).map((k) => <Field key={k} label={`TER ${k.slice(-1)}`} htmlFor={`r-${k}`} error={err[k]}><textarea id={`r-${k}`} className="input" rows={6} value={f[k] ?? ""} onChange={(e) => setF({ ...f, [k]: e.target.value })} spellCheck={false} /></Field>)}
        </form>
      </Overlay>
    </Require>
  );
}
