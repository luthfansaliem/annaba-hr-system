"use client";

import { PageHeader } from "@/components/page-header";
import { ReportBuilder } from "@/components/hr/insight-views";
import { t } from "@/i18n";

/** Open to everyone signed in; each data set shows only for people allowed to see its list. */
export default function ReportsPage() {
  return <><PageHeader title={t("Report Builder")} description={t("Build your own report: choose the data, columns and filters, group and pivot, export to CSV and save it for later.")} /><ReportBuilder /></>;
}
