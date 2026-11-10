import { Badge } from "@/components/ui/badge";
import { t } from "@/i18n";

export const EmployeeStatusBadge = ({ status }: { status: string }) => status === "active" ? <Badge tone="green">{t("Active")}</Badge> : <Badge tone="gray">{t("Terminated")}</Badge>;

export const TYPE_LABEL: Record<string, string> = { permanent: "Permanent", contract: "Contract", probation: "Probation", intern: "Intern" };
export const typeLabel = (v: string | null | undefined) => (v ? t(TYPE_LABEL[v] ?? v) : "—");
export const TYPE_OPTIONS = Object.keys(TYPE_LABEL).map((value) => ({ value, label: t(TYPE_LABEL[value]) }));
