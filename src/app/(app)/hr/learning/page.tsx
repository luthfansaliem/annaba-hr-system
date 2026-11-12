"use client";

import { PageHeader } from "@/components/page-header";
import { MyLearning } from "@/components/hr/learning";
import { t } from "@/i18n";

export default function MyLearningPage() {
  return <><PageHeader title={t("My Learning")} description={t("Courses assigned to you and open courses you can join, with your progress and certificates.")} /><MyLearning /></>;
}
