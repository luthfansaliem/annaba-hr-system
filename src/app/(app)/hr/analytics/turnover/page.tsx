"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { TurnoverView } from "@/components/hr/insight-views";
import { t } from "@/i18n";

export default function Page() {
  return <Require perm="employees.read"><PageHeader title={t("Turnover Analytics")} description={t("Who left and joined over the last 12 months, by department, length of service and reason.")} /><TurnoverView /></Require>;
}
