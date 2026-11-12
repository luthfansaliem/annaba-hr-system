"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { CourseCatalogue } from "@/components/hr/learning";
import { t } from "@/i18n";

export default function CoursesPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Courses")} description={t("Build courses with lessons and a quiz, assign them with a due date and follow who has finished.")} /><CourseCatalogue /></Require>;
}
