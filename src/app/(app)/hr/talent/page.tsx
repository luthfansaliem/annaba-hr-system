"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { TalentPool } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function TalentPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Talent Pool")} description={t("Everyone placed in the 9-box from the latest closed review cycle: performance against potential.")} /><TalentPool /></Require>;
}
