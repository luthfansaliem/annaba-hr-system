import type { BadgeTone } from "@/components/ui/badge";
import type { TicketItem } from "@/lib/api/types";

/** English labels (translated where shown) for helpdesk topics and statuses. */
export const TICKET_TOPICS: Record<string, string> = { payroll: "Pay and payslips", leave: "Leave", attendance: "Attendance", documents: "Letters and documents", benefits: "Claims and benefits", data: "My personal data", other: "Something else" };
export const TICKET_STATUS: Record<TicketItem["status"], [BadgeTone, string]> = { open: ["orange", "Open"], in_progress: ["blue", "Being handled"], resolved: ["green", "Answered"], closed: ["gray", "Closed"] };
