"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { ClaimsPanel } from "@/components/hr/claims";
import { t } from "@/i18n";

export default function ClaimsPage() {
  return (<Require perm="claims.manage"><PageHeader title={t("Expense Claims")} description={t("Every claim and its status. Approved claims join the employee's next pay run as a tax-free line and become paid when that run is locked.")} /><ClaimsPanel scope="all" /></Require>);
}
