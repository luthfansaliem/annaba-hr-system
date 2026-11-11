"use client";

import { useEffect, useState } from "react";
import { Overlay } from "@/components/ui/overlay";
import { Button } from "@/components/ui/button";
import { post } from "@/lib/api/client";
import type { ImportAttendanceResult } from "@/lib/api/types";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const TEMPLATE = "employee_no,datetime\nEMP-0001,2026-11-02 08:57\nEMP-0001,2026-11-02 17:06\n";

/** ATT-003 — load a fingerprint/access-machine export. Check first (nothing is written), then import; days that already have attendance are skipped. */
export function ImportAttendanceDrawer({ open, onClose, onDone }: { open: boolean; onClose: () => void; onDone: () => void }) {
  const run = useRun(); const [csv, setCsv] = useState(""); const [fileName, setFileName] = useState(""); const [check, setCheck] = useState<ImportAttendanceResult | null>(null); const [done, setDone] = useState<ImportAttendanceResult | null>(null);
  const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { if (open) { setCsv(""); setFileName(""); setCheck(null); setDone(null); setErr(""); } }, [open]);
  async function pick(e: React.ChangeEvent<HTMLInputElement>) {
    const f = e.target.files?.[0]; setCheck(null); setDone(null); setErr(""); if (!f) return;
    if (f.size > 2_000_000) { setErr(t("The file is larger than 2 MB.")); return; } setCsv(await f.text()); setFileName(f.name);
  }
  async function send(dryRun: boolean) {
    setErr(""); if (!csv) { setErr(t("Choose a CSV file first.")); return; } setBusy(true);
    const r = await run(() => post<ImportAttendanceResult>("/api/hr/attendance/import", { csv, dryRun }), undefined, { silent: true }); setBusy(false);
    if (!r.ok) { setErr(r.error); return; } if (dryRun) setCheck(r.data); else { setDone(r.data); onDone(); }
  }
  const shown = done ?? check; const issues = shown?.rows.filter((r) => r.status !== "ok") ?? [];
  const ready = check ? check.days - check.skipped - check.failed : 0;
  return (
    <Overlay open={open} onClose={onClose} side="right" title={t("Import attendance")} description={t("Machine punches: the first punch of a day is the clock-in, the last is the clock-out.")}
      footer={done ? <Button variant="primary" onClick={onClose}>{t("Close")}</Button> : <><Button onClick={onClose}>{t("Cancel")}</Button>
        {!check ? <Button variant="primary" disabled={busy || !csv} onClick={() => send(true)}>{busy ? t("Checking…") : t("Check file")}</Button>
          : <Button variant="primary" disabled={busy || ready <= 0} onClick={() => send(false)}>{busy ? t("Importing…") : t("Import {n} days", { n: ready })}</Button>}</>}>
      <div className="stack">
        {err && <div className="form-alert err" role="alert">{err}</div>}
        {!done && <div><label htmlFor="imp-att-file" className="fl-label">{t("CSV file")}</label><input id="imp-att-file" type="file" accept=".csv,text/csv" onChange={pick} />
          <p className="field-help">{t("Columns: employee_no and datetime (local time, yyyy-mm-dd hh:mm). Up to 20,000 rows.")}{" "}{fileName && <b>{fileName}</b>}{" "}<a href={`data:text/csv;charset=utf-8,${encodeURIComponent(TEMPLATE)}`} download="attendance-template.csv">{t("Download a template")}</a></p></div>}
        {shown && <div className={`form-alert ${shown.failed ? "warn" : "ok"}`} role="status">{done ? t("{n} days imported, {skipped} skipped, {failed} with problems.", { n: done.imported, skipped: done.skipped, failed: done.failed }) : t("{ready} of {days} days are ready; {skipped} already exist; {failed} have problems.", { ready, days: shown.days, skipped: shown.skipped, failed: shown.failed })}</div>}
        {issues.length > 0 && <table className="tbl" aria-label={t("Rows with problems")}><thead><tr><th>{t("Employee")}</th><th>{t("Day")}</th><th>{t("Problem")}</th></tr></thead>
          <tbody>{issues.map((r, i) => <tr key={i}><td>{r.employeeNo || "—"}</td><td>{r.date ? fmtDate(r.date) : r.line ? `#${r.line}` : "—"}</td><td>{r.message}</td></tr>)}</tbody></table>}
      </div>
    </Overlay>
  );
}
