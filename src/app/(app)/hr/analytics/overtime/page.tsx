"use client";

import { PageHeader } from "@/components/page-header";
import { Require } from "@/components/page-guard";
import { OvertimeView } from "@/components/hr/insight-views";
import { t } from "@/i18n";

export default function Page() {
  return <Require perm="attendance.read_all"><PageHeader title={t("Overtime Analytics")} description={t("Approved overtime by month, department and person, to plan staffing and watch for overwork.")} /><OvertimeView /></Require>;
}
