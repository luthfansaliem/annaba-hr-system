"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Tabs } from "@/components/ui/tabs";
import { EmptyState } from "@/components/data/empty-state";
import { ChangeRequestsTable } from "@/components/hr/change-requests-table";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { ChangeRequestItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

const FILTERS = [["pending", "Pending"], ["approved", "Approved"], ["rejected", "Rejected"], ["all", "All"]] as const;

export default function ChangeRequestsPage() {
  const [status, setStatus] = useState<(typeof FILTERS)[number][0]>("pending");
  const q = useQuery(() => get<ChangeRequestItem[]>("/api/hr/change-requests", { status }), [status]); const rows = q.data ?? [];
  return (
    <Require perm="employees.update">
      <PageHeader title={t("Change Requests")} description={t("Employees ask to change their name, marital status, bank or tax details here. Check the change before approving: a new bank account is the usual payroll fraud.")} />
      <Panel>
        <Tabs items={FILTERS.map(([key, label]) => ({ key, label: t(label) }))} value={status} onChange={setStatus} label={t("Status")} />
        <div style={{ paddingTop: 12 }}>
          {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("Nothing here")} description={t("No requests match this filter.")} />
            : <ChangeRequestsTable rows={rows} base="/api/hr/change-requests" showEmployee loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} onChanged={q.reload} />}
        </div>
      </Panel>
    </Require>
  );
}
