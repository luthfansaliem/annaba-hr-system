"use client";

import Link from "next/link";
import { Banknote, CalendarDays, Clock, Receipt, Users } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { QueryBoundary } from "@/components/query-boundary";
import { Require } from "@/components/page-guard";
import { cn } from "@/lib/cn";
import { get } from "@/lib/api/client";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

export type AnalyticsModule = "people" | "attendance" | "leave" | "payroll" | "claims";
export const ANALYTICS: Record<AnalyticsModule, { label: string; perm: string; api: string; title: string; description: string; icon: React.ReactNode }> = {
  people: { label: "People", perm: "employees.read", api: "/api/hr/dashboard", title: "People analytics", description: "Headcount, who joined and left, and how the workforce is made up.", icon: <Users size={15} /> },
  attendance: { label: "Attendance", perm: "attendance.read_all", api: "/api/hr/analytics/attendance", title: "Attendance analytics", description: "Who is in today, punctuality, where people work and overtime.", icon: <Clock size={15} /> },
  leave: { label: "Leave", perm: "leave.read_all", api: "/api/hr/analytics/leave", title: "Leave analytics", description: "Who is away, how much leave is taken, and how quickly requests are decided.", icon: <CalendarDays size={15} /> },
  payroll: { label: "Payroll", perm: "payroll.read", api: "/api/hr/analytics/payroll", title: "Payroll analytics", description: "What each month's pay costs, where it goes, and what is still in progress. Totals only.", icon: <Banknote size={15} /> },
  claims: { label: "Claims", perm: "claims.manage", api: "/api/hr/analytics/claims", title: "Claims analytics", description: "What employees claim back, by category and department, and how quickly claims are decided.", icon: <Receipt size={15} /> },
};

/** One analytics screen per module, with a switch between the modules the signed-in person may see. */
export function AnalyticsPage<T>({ module, children }: { module: AnalyticsModule; children: (data: T) => React.ReactNode }) {
  const { can } = useAuth(); const m = ANALYTICS[module];
  const q = useQuery(() => get<T>(m.api), [module]);
  const visible = (Object.keys(ANALYTICS) as AnalyticsModule[]).filter((k) => can(ANALYTICS[k].perm));
  return (
    <Require perm={m.perm}>
      <PageHeader title={t(m.title)} description={t(m.description)} />
      {visible.length > 1 && (
        <nav className="seg module-switch" aria-label={t("Analytics by module")}>
          {visible.map((k) => <Link key={k} href={`/hr/analytics/${k}`} className={cn(k === module && "on")} aria-current={k === module ? "page" : undefined}>{ANALYTICS[k].icon}{t(ANALYTICS[k].label)}</Link>)}
        </nav>
      )}
      <QueryBoundary q={q} rows={8}>{children}</QueryBoundary>
    </Require>
  );
}
