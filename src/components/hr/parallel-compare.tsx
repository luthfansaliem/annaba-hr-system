"use client";

import { useState } from "react";
import { Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { post } from "@/lib/api/client";
import type { CompareAmount, CompareResult } from "@/lib/api/types";
import { idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const FIELDS = ["gross", "bpjs", "tax", "net"] as const;
const LABEL: Record<(typeof FIELDS)[number], string> = { gross: "Gross", bpjs: "BPJS (employee)", tax: "PPh 21", net: "Take-home pay" };

/** Parallel run: lay the old payroll's figures for the same month next to this run. The file is read in the browser and compared by the server; it is not stored. */
export function ParallelCompare({ id, open, onClose }: { id: string; open: boolean; onClose: () => void }) {
  const run = useRun(); const [csv, setCsv] = useState(""); const [tolerance, setTolerance] = useState("1"); const [err, setErr] = useState<Record<string, string>>({}); const [res, setRes] = useState<CompareResult | null>(null);
  async function pick(e: React.ChangeEvent<HTMLInputElement>) { const f = e.target.files?.[0]; setRes(null); setErr({}); setCsv(f ? await f.text() : ""); }
  async function submit(e: React.FormEvent) {
    e.preventDefault(); setErr({});
    const r = await run(() => post<CompareResult>(`/api/hr/payroll/runs/${id}/compare`, { csv, tolerance: Number(tolerance) || 0 }), undefined, { silent: true });
    if (r.ok) setRes(r.data); else setErr(r.fields ?? { csv: r.error });
  }
  function save() {
    if (!res) return;
    const cell = (a: CompareAmount) => [a.system ?? "", a.legacy ?? "", a.difference ?? ""].join(",");
    const lines = ["employee_no,name,status," + FIELDS.map((f) => `${f}_system,${f}_old,${f}_difference`).join(","), ...res.rows.map((r) => [r.employeeNo, `"${(r.name ?? "").replace(/"/g, '""')}"`, r.status, ...FIELDS.map((f) => cell(r[f]))].join(","))];
    const url = URL.createObjectURL(new Blob([lines.join("\r\n")], { type: "text/csv" })); const a = document.createElement("a"); a.href = url; a.download = "parallel-run-differences.csv"; a.click(); URL.revokeObjectURL(url);
  }
  const open_ = res?.rows.filter((r) => r.status !== "match") ?? [];
  const tone = (s: string) => (s === "different" ? "red" : "orange") as "red" | "orange";
  const word = (s: string) => (s === "different" ? t("Different") : s === "only_system" ? t("Not in the old payroll") : t("Not in this run"));
  return (
    <Overlay open={open} onClose={onClose} width={860} title={t("Compare with the old payroll")} description={t("Parallel run: upload the old payroll's result for the same month as a CSV file with a column for the employee number and any of gross, BPJS, PPh 21 and take-home pay. The file is compared and not stored.")}
      footer={<><Button onClick={onClose}>{t("Close")}</Button>{res && <Button onClick={save}><Download size={15} /> {t("Download differences")}</Button>}<Button variant="primary" type="submit" form="cmp-form" disabled={!csv}>{t("Compare")}</Button></>}>
      <form id="cmp-form" className="stack" noValidate onSubmit={submit}>
        <Field label={t("File from the old payroll (CSV)")} htmlFor="cmp-file" required error={err.csv}><input id="cmp-file" type="file" accept=".csv,text/csv" onChange={pick} /></Field>
        <Field label={t("Ignore differences up to (Rp)")} htmlFor="cmp-tol" error={err.tolerance}><TextInput id="cmp-tol" inputMode="numeric" value={tolerance} onChange={(e) => setTolerance(e.target.value)} style={{ maxWidth: 140 }} aria-describedby="cmp-tol-hint" /><small id="cmp-tol-hint" className="sub">{t("Rounding differences of a rupiah or two are normal.")}</small></Field>
      </form>
      {res && (
        <div className="stack" style={{ marginTop: 16 }} role="status">
          <p style={{ margin: 0 }}><Badge tone="green">{res.matched} {t("match")}</Badge> <Badge tone={res.different ? "red" : "gray"}>{res.different} {t("different")}</Badge> <Badge tone={res.onlyInSystem ? "orange" : "gray"}>{res.onlyInSystem} {t("not in the old payroll")}</Badge> <Badge tone={res.onlyInLegacy ? "orange" : "gray"}>{res.onlyInLegacy} {t("not in this run")}</Badge></p>
          <div className="table-wrap"><table className="table" aria-label={t("Totals of the employees found in both")}>
            <thead><tr><th>{t("Totals (employees in both)")}</th><th className="num">{t("This run")}</th><th className="num">{t("Old payroll")}</th><th className="num">{t("Difference")}</th></tr></thead>
            <tbody>{FIELDS.filter((f) => res.comparedFields.includes(f)).map((f) => <tr key={f}><td>{t(LABEL[f])}</td><td className="num">{idr(res[f].system)}</td><td className="num">{idr(res[f].legacy)}</td><td className="num">{idr(res[f].difference)}</td></tr>)}</tbody>
          </table></div>
          {open_.length === 0 ? <p className="form-alert ok">{t("No differences: every employee matches within the tolerance.")}</p> : (
            <div className="table-wrap"><table className="table" aria-label={t("Employees to look into")}>
              <thead><tr><th>{t("Employee")}</th><th>{t("Finding")}</th>{FIELDS.filter((f) => res.comparedFields.includes(f)).map((f) => <th key={f} className="num">{t(LABEL[f])}</th>)}</tr></thead>
              <tbody>{open_.slice(0, 200).map((r) => (
                <tr key={r.employeeNo}><td><b>{r.name ?? "—"}</b><small className="sub">{r.employeeNo}</small></td><td><Badge tone={tone(r.status)}>{word(r.status)}</Badge></td>
                  {FIELDS.filter((f) => res.comparedFields.includes(f)).map((f) => <td key={f} className="num">{r[f].difference !== null && Math.abs(r[f].difference!) > res.tolerance ? <><b>{idr(r[f].difference)}</b><small className="sub">{idr(r[f].system)} / {idr(r[f].legacy)}</small></> : r.status === "different" ? "✓" : idr(r[f].system ?? r[f].legacy)}</td>)}
                </tr>))}</tbody>
            </table></div>)}
          {open_.length > 200 && <p className="sub">{t("Showing the first 200; download the file for all of them.")}</p>}
        </div>
      )}
    </Overlay>
  );
}
