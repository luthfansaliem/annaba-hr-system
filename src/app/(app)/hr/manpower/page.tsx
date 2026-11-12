"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { ManpowerPlans } from "@/components/hr/recruitment";
import { t } from "@/i18n";

export default function ManpowerPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Manpower Planning")} description={t("Positions each department needs this year, with headcount and budget, against who is employed and who is being hired.")} /><ManpowerPlans /></Require>;
}
