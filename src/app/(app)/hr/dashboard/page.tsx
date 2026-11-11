import { redirect } from "next/navigation";

/** The HR dashboard became the home page (today's picture) and People analytics (headcount and movement). Old links land on the analytics. */
export default function HrDashboardRedirect() { redirect("/hr/analytics/people"); }
