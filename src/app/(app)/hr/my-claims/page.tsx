"use client";

import { PageHeader } from "@/components/page-header";
import { ClaimsPanel } from "@/components/hr/claims";
import { CashAdvancesPanel, ClaimBalances } from "@/components/hr/benefits";
import { t } from "@/i18n";

export default function MyClaimsPage() {
  return (
    <>
      <PageHeader title={t("My Claims")} description={t("Ask back money you spent for work, see your claim limits, and ask for an advance before a work trip.")} />
      <div className="stack">
        <ClaimBalances />
        <ClaimsPanel scope="mine" />
        <CashAdvancesPanel scope="mine" />
      </div>
    </>
  );
}
