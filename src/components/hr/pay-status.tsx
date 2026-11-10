import { Badge, type BadgeTone } from "@/components/ui/badge";
import { t } from "@/i18n";

const tone: Record<string, [BadgeTone, string]> = { draft: ["gray", "Draft"], calculated: ["blue", "Calculated"], approved: ["orange", "Approved"], locked: ["green", "Locked"], paid: ["green", "Paid"] };
export const PayStatusBadge = ({ status }: { status: string }) => <Badge tone={tone[status]?.[0] ?? "gray"}>{t(tone[status]?.[1] ?? status)}</Badge>;
