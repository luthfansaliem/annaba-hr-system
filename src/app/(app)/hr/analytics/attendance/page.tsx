"use client";

import { AnalyticsPage } from "@/components/hr/analytics-page";
import { AttendanceAnalyticsView } from "@/components/hr/analytics-views";
import type { AttendanceAnalytics } from "@/lib/api/types";

export default function Page() { return <AnalyticsPage<AttendanceAnalytics> module="attendance">{(d) => <AttendanceAnalyticsView d={d} />}</AnalyticsPage>; }
