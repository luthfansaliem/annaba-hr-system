"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Tabs } from "@/components/ui/tabs";
import { EmptyState } from "@/components/data/empty-state";
import { LeaveTable } from "@/components/hr/leave-table";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { LeaveRequestItem, Paged } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const FILTERS = [["pending", "Pending"], ["approved", "Approved"], ["rejected", "Rejected"], ["all", "All"]] as const;

export default function Page() {
  const [status, setStatus] = useState<(typeof FILTERS)[number][0]>("pending");
  const q = useQuery(() => get<Paged<LeaveRequestItem>>("/api/hr/leave/requests", { scope: "team", status, pageSize: 100 }), [status]);
  const rows = q.data?.data ?? [];
  return (
    <Require perm="leave.approve">
      <PageHeader title={t("Leave Approvals")} description={t("Requests from the people who report to you.")} />
      <Panel>
        <Tabs items={FILTERS.map(([key, label]) => ({ key, label: t(label) }))} value={status} onChange={setStatus} label={t("Status")} />
        <div style={{ paddingTop: 12 }}>
          {!q.loading && !q.error && rows.length === 0
            ? <EmptyState title={t("Nothing here")} description={t("No leave requests match this filter.")} />
            : <LeaveTable rows={rows} showEmployee loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} onChanged={q.reload} />}
        </div>
      </Panel>
    </Require>
  );
}
