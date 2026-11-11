"use client";

import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { MyDayPanels, MyDayStats } from "@/components/hr/my-day";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { MyHome } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** ESS-001 — an employee's start page: today's attendance, leave left, what is still open, what to read. */
export default function MyHrHomePage() {
  const q = useQuery(() => get<MyHome>("/api/hr/me/home"), []); const d = q.data;
  return (
    <>
      <PageHeader title={d?.fullName ? t("Hello, {name}", { name: d.fullName.split(" ")[0] }) : t("My HR")} description={d?.linked ? `${d.employeeNo}${d.jobTitle ? ` · ${d.jobTitle}` : ""}` : undefined} />
      {q.error && <div className="form-alert err" role="alert">{q.error.message}</div>}
      {d && !d.linked && <Panel><EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} /></Panel>}
      {d && d.linked && <div className="stack">
        <MyDayStats d={d} />
        <MyDayPanels d={d} />
      </div>}
    </>
  );
}
