"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Tabs } from "@/components/ui/tabs";
import { Requisitions } from "@/components/hr/recruitment";
import { useAuth } from "@/lib/auth";
import { t } from "@/i18n";

/** Managers ask to hire; HR (Recruitment) sees and decides every request. */
export default function RequisitionsPage() {
  const { can } = useAuth(); const hr = can("hrconfig.manage"); const [tab, setTab] = useState<"mine" | "all">(hr ? "all" : "mine");
  return (
    <>
      <PageHeader title={t("Hiring Requests")} description={t("Ask for a new or replacement position; HR checks it against the manpower plan and approves it before a job is posted.")} />
      {hr && <div style={{ marginBottom: 12 }}><Tabs variant="segmented" items={[{ key: "all" as const, label: t("All requests") }, { key: "mine" as const, label: t("My requests") }]} value={tab} onChange={setTab} label={t("Whose requests")} /></div>}
      <Requisitions all={hr && tab === "all"} />
    </>
  );
}
