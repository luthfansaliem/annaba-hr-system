import { Badge, type BadgeTone } from "@/components/ui/badge";
import { t } from "@/i18n";

const tone: Record<string, [BadgeTone, string]> = { pending: ["orange", "Pending"], approved: ["green", "Approved"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"] };
export const LeaveStatusBadge = ({ status }: { status: string }) => <Badge tone={tone[status]?.[0] ?? "gray"}>{t(tone[status]?.[1] ?? status)}</Badge>;

export const days = (n: number) => `${Number.isInteger(n) ? n : n.toFixed(1)}`;
