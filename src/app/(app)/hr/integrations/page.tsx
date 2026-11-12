"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Tabs } from "@/components/ui/tabs";
import { EmptyState } from "@/components/data/empty-state";
import { Panel } from "@/components/ui/card";
import { ApiKeys, BankLayouts, JournalAccounts } from "@/components/hr/integrations";
import { useAuth } from "@/lib/auth";
import { t } from "@/i18n";

/** Connections to other systems: API keys (system administrators), bank file layouts (who locks pay runs) and journal accounts (payroll). */
export default function IntegrationsPage() {
  const { can } = useAuth();
  const tabs = [...(can("settings.manage") ? [{ key: "api" as const, label: t("API keys") }] : []), ...(can("payroll.lock") ? [{ key: "bank" as const, label: t("Bank file layouts") }] : []), ...(can("payroll.read") ? [{ key: "journal" as const, label: t("Journal accounts") }] : [])];
  const [tab, setTab] = useState<"api" | "bank" | "journal" | null>(null); const current = tab ?? tabs[0]?.key ?? null;
  return (
    <>
      <PageHeader title={t("Integrations")} description={t("Connect Annaba HRIS to other systems: read-only API keys, your bank's transfer file layout, and the account codes of the payroll journal.")} />
      {tabs.length === 0 ? <Panel><EmptyState title={t("Nothing to set up here")} description={t("Integrations are set up by system administrators and payroll.")} /></Panel> : <>
        {tabs.length > 1 && <div style={{ marginBottom: 12 }}><Tabs variant="segmented" items={tabs} value={current!} onChange={setTab} label={t("Integration")} /></div>}
        {current === "api" && <ApiKeys />}{current === "bank" && <BankLayouts />}{current === "journal" && <JournalAccounts />}
      </>}
    </>
  );
}
