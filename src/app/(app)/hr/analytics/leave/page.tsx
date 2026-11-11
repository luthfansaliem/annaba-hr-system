"use client";

import { AnalyticsPage } from "@/components/hr/analytics-page";
import { LeaveAnalyticsView } from "@/components/hr/analytics-views";
import type { LeaveAnalytics } from "@/lib/api/types";

export default function Page() { return <AnalyticsPage<LeaveAnalytics> module="leave">{(d) => <LeaveAnalyticsView d={d} />}</AnalyticsPage>; }
