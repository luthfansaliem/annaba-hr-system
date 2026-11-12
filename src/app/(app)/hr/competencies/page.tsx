"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { CompetencyLibrary } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function CompetenciesPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Competencies")} description={t("The skills and behaviours rated in reviews and grown in development plans.")} /><CompetencyLibrary /></Require>;
}
