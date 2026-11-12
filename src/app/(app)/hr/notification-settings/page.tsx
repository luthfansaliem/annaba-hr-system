"use client";

import { useEffect, useState } from "react";
import { Save } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { PushToggle } from "@/components/hr/push-toggle";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/field";
import { get, put } from "@/lib/api/client";
import type { NotificationPreference, ReportSchedulesOverview } from "@/lib/api/types";
import { del, post } from "@/lib/api/client";
import { SelectInput } from "@/components/ui/field";
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
  const sched = useQuery(() => get<ReportSchedulesOverview>("/api/hr/me/report-schedules"), []); const allowed = (sched.data?.reports ?? []).filter((r) => r.allowed);
  const [n, setN] = useState({ report: "", frequency: "weekly", day: "1" }); const DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
  const add = async () => { if ((await run(() => post("/api/hr/me/report-schedules", { report: n.report || allowed[0]?.key, frequency: n.frequency, day: Number(n.day) }), t("Reminder added."))).ok) sched.reload(); };
  useEffect(() => { if (q.data) setRows(q.data); }, [q.data]);
  const set = (c: string, k: "inApp" | "email", v: boolean) => setRows(rows.map((r) => (r.category === c ? { ...r, [k]: v } : r)));
  const save = async () => { const r = await run(() => put<NotificationPreference[]>("/api/hr/me/notification-preferences", { preferences: rows }), t("Preferences saved.")); if (r.ok) setRows(r.data); };
  return (
    <>
      <PageHeader title={t("Notification Preferences")} description={t("Choose what reaches you in the app and by e-mail. Security messages about your account are always sent.")} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      <div style={{ marginBottom: 16 }}><PushToggle /></div>
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
      {allowed.length > 0 && <Panel title={t("Scheduled reports")} description={t("An e-mail with a link to a report, every week or month. The e-mail never contains figures.")}>
        <div className="row" style={{ gap: 8, flexWrap: "wrap", alignItems: "end" }}>
          <SelectInput aria-label={t("Report")} value={n.report || allowed[0].key} onChange={(e) => setN({ ...n, report: e.target.value })} options={allowed.map((r) => ({ value: r.key, label: t(r.name) }))} />
          <SelectInput aria-label={t("How often")} value={n.frequency} onChange={(e) => setN({ ...n, frequency: e.target.value, day: "1" })} options={[{ value: "weekly", label: t("Every week") }, { value: "monthly", label: t("Every month") }]} />
          <SelectInput aria-label={t("Day")} value={n.day} onChange={(e) => setN({ ...n, day: e.target.value })} options={n.frequency === "weekly" ? DAYS.map((d, i) => ({ value: String(i + 1), label: t(d) })) : Array.from({ length: 28 }, (_, i) => ({ value: String(i + 1), label: String(i + 1) }))} />
          <Button onClick={add}>{t("Add reminder")}</Button>
        </div>
        {(sched.data?.schedules.length ?? 0) > 0 && <ul className="mini-list" style={{ marginTop: 12 }}>{sched.data!.schedules.map((x) => <li key={x.id}><div><strong>{t(x.name)}</strong><small className="sub">{x.frequency === "weekly" ? `${t("Every week")} · ${t(DAYS[x.day - 1])}` : `${t("Every month")} · ${x.day}`}</small></div><Button size="sm" onClick={async () => { if ((await run(() => del(`/api/hr/me/report-schedules/${x.id}`), t("Reminder removed."))).ok) sched.reload(); }}>{t("Remove")}</Button></li>)}</ul>}
      </Panel>}
    </>
  );
}
