"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { AtRiskList } from "@/components/hr/insight-views";
import { t } from "@/i18n";

export default function Page() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Employees at Risk")} description={t("People showing several warning signs that they may leave, with the reasons, so their manager can talk with them in time.")} /><AtRiskList /></Require>;
}
