"use client";

import { AnalyticsPage } from "@/components/hr/analytics-page";
import { ClaimsAnalyticsView } from "@/components/hr/analytics-views";
import type { ClaimsAnalytics } from "@/lib/api/types";

export default function Page() { return <AnalyticsPage<ClaimsAnalytics> module="claims">{(d) => <ClaimsAnalyticsView d={d} />}</AnalyticsPage>; }
