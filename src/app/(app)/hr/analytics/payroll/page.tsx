"use client";

import { AnalyticsPage } from "@/components/hr/analytics-page";
import { PayrollAnalyticsView } from "@/components/hr/analytics-views";
import type { PayrollAnalytics } from "@/lib/api/types";

export default function Page() { return <AnalyticsPage<PayrollAnalytics> module="payroll">{(d) => <PayrollAnalyticsView d={d} />}</AnalyticsPage>; }
