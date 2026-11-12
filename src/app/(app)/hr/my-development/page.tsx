"use client";

import { PageHeader } from "@/components/page-header";
import { DevelopmentPlans } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function MyDevelopmentPage() {
  return <><PageHeader title={t("My Development")} description={t("Your individual development plan: what you want to grow in and the steps agreed with your manager.")} /><DevelopmentPlans /></>;
}
