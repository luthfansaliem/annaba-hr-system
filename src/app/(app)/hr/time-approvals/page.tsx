"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Tabs } from "@/components/ui/tabs";
import { EmptyState } from "@/components/data/empty-state";
import { TimeRequestTable } from "@/components/hr/time-requests";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { Paged, TimeRequest } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const FILTERS = [["pending", "Pending"], ["approved", "Approved"], ["rejected", "Rejected"], ["all", "All"]] as const;

export default function TimeApprovalsPage() {
  const { can } = useAuth(); const [kind, setKind] = useState<"corrections" | "overtime">("corrections"); const [status, setStatus] = useState<(typeof FILTERS)[number][0]>("pending");
  const scope = can("attendance.read_all") ? "all" : "team";
  const q = useQuery(() => get<Paged<TimeRequest>>(`/api/hr/attendance/${kind}`, { scope, status, pageSize: 100 }), [kind, status, scope]); const rows = q.data?.data ?? [];
  return (
    <Require perm="attendance.approve">
      <PageHeader title={t("Time Approvals")} description={scope === "all" ? t("Attendance corrections and overtime across the organisation.") : t("Attendance corrections and overtime of the people who report to you.")} />
      <Panel>
        <Tabs items={[{ key: "corrections", label: t("Corrections") }, { key: "overtime", label: t("Overtime") }]} value={kind} onChange={setKind} label={t("Request type")} />
        <div style={{ paddingTop: 12 }}><Tabs variant="pill" items={FILTERS.map(([key, label]) => ({ key, label: t(label) }))} value={status} onChange={setStatus} label={t("Status")} /></div>
        <div style={{ paddingTop: 12 }}>
          {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("Nothing here")} description={t("No requests match this filter.")} />
            : <TimeRequestTable rows={rows} showEmployee loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} onChanged={q.reload} />}
        </div>
      </Panel>
    </Require>
  );
}
