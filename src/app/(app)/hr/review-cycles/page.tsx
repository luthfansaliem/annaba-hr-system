"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { ReviewCycles } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function ReviewCyclesPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Review Cycles")} description={t("Run performance reviews: launch a cycle, follow the reviews coming in, calibrate the ratings and release the results.")} /><ReviewCycles /></Require>;
}
