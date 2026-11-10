"use client";

import { PageHeader } from "@/components/page-header";
import { ClaimsPanel } from "@/components/hr/claims";
import { t } from "@/i18n";

export default function MyClaimsPage() {
  return (<><PageHeader title={t("My Claims")} description={t("Ask back money you spent for work. Your manager or payroll decides; approved claims are paid with your next payslip.")} /><ClaimsPanel scope="mine" /></>);
}
