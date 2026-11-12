"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { WageAdvancePanel } from "@/components/hr/benefits";
import { t } from "@/i18n";

/** Early wage withdrawals asked by employees: payroll approves, transfers, and the month's pay run deducts them. */
export default function WageAdvancesPage() {
  return (
    <Require perm="payroll.read">
      <PageHeader title={t("Early Wage Withdrawals")} description={t("Part of the wage already earned, paid before payday and deducted in that month's pay run. The share employees may take is set in the payroll rules.")} />
      <WageAdvancePanel scope="all" />
    </Require>
  );
}
