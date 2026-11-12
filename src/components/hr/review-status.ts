import type { BadgeTone } from "@/components/ui/badge";
import type { SalaryReviewSummary } from "@/lib/api/types";

/** Badge tone and English label (translated where shown) for each salary review status. */
export const REVIEW_STATUS: Record<SalaryReviewSummary["status"], [BadgeTone, string]> = {
  draft: ["gray", "Draft"], submitted: ["orange", "Waiting for approval"], approved: ["blue", "Approved · ready to apply"], applied: ["green", "Applied"], cancelled: ["gray", "Cancelled"],
};
