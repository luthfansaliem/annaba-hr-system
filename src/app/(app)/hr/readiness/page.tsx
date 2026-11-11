"use client";

import { CheckCircle2, Hand, ShieldAlert, Wrench, RefreshCw } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { Panel } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { ReadinessReport } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDateTime } from "@/lib/format";
import { t } from "@/i18n";

const TONE: Record<string, ["green" | "orange" | "red" | "gray", string]> = { ok: ["green", "OK"], warn: ["orange", "Fix soon"], fail: ["red", "Blocks go-live"], manual: ["gray", "Confirm by hand"] };

/** The go-live checklist, answered by the system where it can and listed for a person to confirm where it cannot. */
export default function ReadinessPage() {
  const q = useQuery(() => get<ReadinessReport>("/api/hr/readiness"), []); const d = q.data;
  const areas = d ? [...new Set(d.checks.map((c) => c.area))] : [];
  return (
    <Require perm="settings.manage">
      <PageHeader title={t("Go-Live Check")} description={t("What is ready, what still blocks going live, and what only a person can confirm.")} actions={<Button onClick={q.reload}><RefreshCw size={15} /> {t("Check again")}</Button>} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      {d && <div className="stack">
        <KpiRow><Kpi icon={<ShieldAlert size={20} />} tone="red" label={t("Blocks go-live")} value={d.fail} /><Kpi icon={<Wrench size={20} />} tone="orange" label={t("Fix soon")} value={d.warn} /><Kpi icon={<CheckCircle2 size={20} />} tone="green" label={t("OK")} value={d.ok} /><Kpi icon={<Hand size={20} />} tone="blue" label={t("Confirm by hand")} value={d.manual} /></KpiRow>
        {areas.map((a) => (
          <Panel key={a} title={t(a)}>
            <ul className="mini-list">{d.checks.filter((c) => c.area === a).map((c) => (
              <li key={c.key}><div><strong>{t(c.title)}</strong><small className="sub">{c.detail}</small></div><Badge tone={TONE[c.status]?.[0] ?? "gray"}>{t(TONE[c.status]?.[1] ?? c.status)}</Badge></li>))}</ul>
          </Panel>))}
        <small className="sub">{t("Checked")} {fmtDateTime(d.checkedAt)}</small>
      </div>}
    </Require>
  );
}
