"use client";

import { useState } from "react";
import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { SelectInput } from "@/components/ui/field";
import { CashAdvancesPanel } from "@/components/hr/benefits";
import { useAuth } from "@/lib/auth";
import { t } from "@/i18n";

/** Cash advances of the team (managers) or of everyone (claims managers, who also mark advances as paid out). */
export default function CashAdvancesPage() {
  const { can } = useAuth(); const [scope, setScope] = useState<"team" | "all">(can("claims.manage") ? "all" : "team");
  return (
    <Require perm="claims.approve">
      <PageHeader title={t("Cash Advances")} description={t("Money paid before a work trip and settled afterwards against what was actually spent. The difference goes through the next payslip.")}
        actions={can("claims.manage") && <SelectInput aria-label={t("Show")} value={scope} onChange={(e) => setScope(e.target.value as "team" | "all")} options={[{ value: "all", label: t("Everyone") }, { value: "team", label: t("My team") }]} />} />
      <CashAdvancesPanel scope={scope} />
    </Require>
  );
}
