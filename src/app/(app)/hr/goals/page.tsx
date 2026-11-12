"use client";

import { PageHeader } from "@/components/page-header";
import { GoalsBoard } from "@/components/hr/performance";
import { t } from "@/i18n";

/** KPI and OKR for everyone: own goals, the team's (managers), the company's. */
export default function GoalsPage() {
  return <><PageHeader title={t("Goals & OKR")} description={t("Measurable goals (KPI) and objectives with key results (OKR), aligned from the company down to each person, with progress updates.")} /><GoalsBoard /></>;
}
