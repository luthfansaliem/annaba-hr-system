"use client";

import { PageHeader } from "@/components/page-header";
import { MyReviews } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function MyReviewsPage() {
  return <><PageHeader title={t("Performance Reviews")} description={t("Reviews you are asked to write — your own, your team's, a colleague's — and your results once HR releases them.")} /><MyReviews /></>;
}
