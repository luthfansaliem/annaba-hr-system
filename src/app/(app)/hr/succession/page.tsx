"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { SuccessionBoard } from "@/components/hr/performance";
import { t } from "@/i18n";

export default function SuccessionPage() {
  return <Require perm="hrconfig.manage"><PageHeader title={t("Succession Planning")} description={t("Key positions and who could take them over, with how ready each successor is.")} /><SuccessionBoard /></Require>;
}
