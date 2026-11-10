"use client";

import { useEffect, useState } from "react";
import { Save } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/field";
import { get, put } from "@/lib/api/client";
import type { NotificationPreference } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const LABEL: Record<string, [string, string]> = {
  approvals: ["Requests waiting for my decision", "Leave, attendance and pay runs that need your approval."],
  leave: ["My leave", "Decisions on your own leave requests."],
  time: ["My attendance and overtime", "Decisions on your corrections and overtime."],
  payroll: ["My pay", "A new payslip, a decided claim."],
  profile: ["My profile", "Decisions on changes you asked for."],
};

/** COM-002 — which messages reach you behind the bell and by e-mail. Security messages (sign-ins, password changes) are not affected. */
export default function NotificationSettingsPage() {
  const run = useRun(); const q = useQuery(() => get<NotificationPreference[]>("/api/hr/me/notification-preferences"), []);
  const [rows, setRows] = useState<NotificationPreference[]>([]);
  useEffect(() => { if (q.data) setRows(q.data); }, [q.data]);
  const set = (c: string, k: "inApp" | "email", v: boolean) => setRows(rows.map((r) => (r.category === c ? { ...r, [k]: v } : r)));
  const save = async () => { const r = await run(() => put<NotificationPreference[]>("/api/hr/me/notification-preferences", { preferences: rows }), t("Preferences saved.")); if (r.ok) setRows(r.data); };
  return (
    <>
      <PageHeader title={t("Notification Preferences")} description={t("Choose what reaches you in the app and by e-mail. Security messages about your account are always sent.")} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      <Panel>
        <div style={{ overflowX: "auto" }}><table className="tbl" aria-label={t("Notification Preferences")}>
          <thead><tr><th>{t("About")}</th><th style={{ textAlign: "center" }}>{t("In the app")}</th><th style={{ textAlign: "center" }}>{t("E-mail")}</th></tr></thead>
          <tbody>{rows.map((r) => (
            <tr key={r.category}><td><b>{t(LABEL[r.category]?.[0] ?? r.category)}</b><small className="sub">{t(LABEL[r.category]?.[1] ?? "")}</small></td>
              <td style={{ textAlign: "center" }}><Checkbox aria-label={`${t(LABEL[r.category]?.[0] ?? r.category)}: ${t("In the app")}`} checked={r.inApp} onChange={(e) => set(r.category, "inApp", e.target.checked)} /></td>
              <td style={{ textAlign: "center" }}><Checkbox aria-label={`${t(LABEL[r.category]?.[0] ?? r.category)}: ${t("E-mail")}`} checked={r.email} onChange={(e) => set(r.category, "email", e.target.checked)} /></td></tr>))}</tbody>
        </table></div>
        <div style={{ marginTop: 12 }}><Button variant="primary" onClick={save} disabled={rows.length === 0}><Save size={15} /> {t("Save changes")}</Button></div>
      </Panel>
    </>
  );
}
