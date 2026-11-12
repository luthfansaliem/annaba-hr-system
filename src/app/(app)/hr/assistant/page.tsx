"use client";

import { PageHeader } from "@/components/page-header";
import { AssistantChat } from "@/components/hr/assistant";
import { t } from "@/i18n";

export default function AssistantPage() {
  return <><PageHeader title={t("HR Assistant")} description={t("Ask about the company's HR policy and your own leave, attendance and claims.")} /><AssistantChat /></>;
}
