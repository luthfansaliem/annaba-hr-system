"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { PostingsBoard } from "@/components/hr/recruitment";
import { t } from "@/i18n";

export default function RecruitmentPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Recruitment")} description={t("Job postings on your career page and the applicants in each, from application to hire.")} /><PostingsBoard /></Require>;
}
