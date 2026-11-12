"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { PerformanceView } from "@/components/hr/insight-views";
import { t } from "@/i18n";

export default function Page() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Performance Analytics")} description={t("Ratings from the latest review cycle, the 9-box, and how this year's goals are progressing.")} /><PerformanceView /></Require>;
}
