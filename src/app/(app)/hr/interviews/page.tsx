"use client";

import { PageHeader } from "@/components/page-header";
import { MyInterviews } from "@/components/hr/recruitment";
import { t } from "@/i18n";

export default function InterviewsPage() {
  return <><PageHeader title={t("My Interviews")} description={t("Candidates you are asked to interview, their CV, and your feedback for HR.")} /><MyInterviews /></>;
}
