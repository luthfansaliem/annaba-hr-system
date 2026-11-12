"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { AssistantAdmin } from "@/components/hr/assistant";
import { t } from "@/i18n";

export default function AssistantAdminPage() {
  return <Require perm="settings.manage"><PageHeader title={t("HR Assistant")} description={t("Turn the AI assistant on or off and give it the HR policy it answers from. Off by default.")} /><AssistantAdmin /></Require>;
}
