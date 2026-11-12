"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { TeamLearning } from "@/components/hr/learning";
import { t } from "@/i18n";

export default function TeamLearningPage() {
  return <Require perm="leave.approve"><PageHeader title={t("Team Learning")} description={t("Courses your direct reports are taking, with progress and due dates.")} /><TeamLearning /></Require>;
}
