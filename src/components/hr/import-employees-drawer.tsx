"use client";

import { useEffect, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { post } from "@/lib/api/client";
import type { ImportEmployeesResult } from "@/lib/api/types";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TEMPLATE = "employee_no,full_name,hire_date,job_title,type,contract_end,department,manager_no,work_email,phone,birth_date,gender,marital_status,grade,location\n"
  + "EMP-0001,Jane Doe,2023-01-02,Finance Analyst,permanent,,FIN,,jane.doe@example.com,+62 811 0000 0000,1992-05-20,female,single,G4,Jakarta\n";

/** EMP-006 — load employees from a CSV. All-or-nothing: the file is checked first and only imported when every row is fine. */
export function ImportEmployeesDrawer({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun();
  const [csv, setCsv] = useState(""); const [fileName, setFileName] = useState(""); const [check, setCheck] = useState<ImportEmployeesResult | null>(null); const [done, setDone] = useState<ImportEmployeesResult | null>(null);
  const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { if (open) { setCsv(""); setFileName(""); setCheck(null); setDone(null); setErr(""); } }, [open]);

  async function pick(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; setCheck(null); setDone(null); setErr("");
    if (!f) return;
    if (f.size > 1_000_000) { setErr(t("The file is larger than 1 MB.")); return; }
    setCsv(await f.text()); setFileName(f.name);
  }
  async function send(dryRun: boolean) {
    setErr(""); if (!csv) { setErr(t("Choose a CSV file first.")); return; }
    setBusy(true);
    const r = await run(() => post<ImportEmployeesResult>("/api/hr/employees/import", { csv, dryRun }), undefined, { silent: true });
    setBusy(false);
    if (!r.ok) { setErr(r.error); return; }
    if (dryRun) setCheck(r.data); else if (r.data.failed > 0) setCheck(r.data); else { setDone(r.data); onDone(); }
  }
  const shown = done ?? check; const bad = shown?.rows.filter((r) => r.status === "error") ?? [];

  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Import employees")} description={t("Load many employees from a CSV file. Either every row is imported or none is.")}
      footer={done ? <Button variant="primary" onClick={onClose}>{t("Close")}</Button> : <><Button onClick={onClose}>{t("Cancel")}</Button>
        {!check || check.failed > 0 ? <Button variant="primary" disabled={busy || !csv} onClick={() => send(true)}>{busy ? t("Checking…") : t("Check file")}</Button>
          : <Button variant="primary" disabled={busy} onClick={() => send(false)}>{busy ? t("Importing…") : t("Import {n} employees", { n: check.total })}</Button>}</>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {!done && (
          <div>
            <label htmlFor="imp-emp-file" className="fl-label">{t("CSV file")}</label>
            <input id="imp-emp-file" type="file" accept=".csv,text/csv" onChange={pick} />
            <p className="field-help">{t("Required columns:")} <code>full_name</code>, <code>hire_date</code>, <code>job_title</code>. {t("Optional: employee_no, type, contract_end, department (code or name), manager_no, work_email, phone, birth_date, gender, marital_status, grade, location, and the protected columns national_id, tax_id, bank_name, bank_account. Up to 1,000 rows.")}{" "}{fileName && <b>{fileName}</b>}{" "}
              <a href={`data:text/csv;charset=utf-8,${encodeURIComponent(TEMPLATE)}`} download="employees-template.csv">{t("Download a template")}</a></p>
          </div>)}
        {shown && (
          <div className={`form-alert ${shown.failed ? "err" : "ok"}`} role="status">
            {done ? t("{n} employees imported.", { n: done.created }) : shown.failed ? t("{failed} of {total} rows have problems. Nothing was imported; fix the file and check again.", { failed: shown.failed, total: shown.total }) : t("All {n} rows are ready to import.", { n: shown.total })}
          </div>)}
        {bad.length > 0 && (
          <table className="tbl" aria-label={t("Rows with problems")}><thead><tr><th>{t("Line")}</th><th>{t("Employee")}</th><th>{t("Problem")}</th></tr></thead>
            <tbody>{bad.map((r) => <tr key={r.line}><td>{r.line}</td><td>{r.name || r.employeeNo || "—"}</td><td>{r.error}</td></tr>)}</tbody></table>)}
      </div>
    </Overlay>
  );
}
