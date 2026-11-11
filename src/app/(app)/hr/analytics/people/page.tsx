"use client";

import { AnalyticsPage } from "@/components/hr/analytics-page";
import { PeopleAnalyticsView } from "@/components/hr/analytics-views";
import type { HrDashboard } from "@/lib/api/types";

export default function Page() { return <AnalyticsPage<HrDashboard> module="people">{(d) => <PeopleAnalyticsView d={d} />}</AnalyticsPage>; }
