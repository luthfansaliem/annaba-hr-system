"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { Assessments } from "@/components/hr/recruitment";
import { t } from "@/i18n";

export default function AssessmentsPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Assessments")} description={t("Tests and questionnaires sent to applicants through a personal link: knowledge tests with a pass mark, and a personality profile.")} /><Assessments /></Require>;
}
