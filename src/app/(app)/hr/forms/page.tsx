"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get, post } from "@/lib/api/client";
import type { HrFormItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

/** Forms and surveys sent to employees. */
export default function FormsPage() {
  const run = useRun(); const router = useRouter(); const q = useQuery(() => get<HrFormItem[]>("/api/hr/forms"), []);
  async function create() {
    const r = await run(() => post<HrFormItem>("/api/hr/forms", { title: t("New survey"), questions: [{ id: "q1", type: "rating", label: t("How satisfied are you at work?"), required: true }] }), undefined, { silent: true });
    if (r.ok) router.push(`/hr/forms/${r.data.id}`);
  }
  const cols: Column<HrFormItem>[] = [
    { key: "t", header: t("Form"), cell: (f) => <><Link href={`/hr/forms/${f.id}`}><b>{f.title}</b></Link><small className="sub">{t("{n} questions", { n: f.questions.length })} · {f.audience ?? t("Everyone")}{f.anonymous ? ` · ${t("anonymous")}` : ""}</small></> },
    { key: "s", header: t("Status"), cell: (f) => <Badge tone={f.status === "open" ? "green" : f.status === "draft" ? "gray" : "blue"}>{f.status === "open" ? t("Open") : f.status === "draft" ? t("Draft") : t("Closed")}</Badge> },
    { key: "r", header: t("Answers"), cell: (f) => `${f.responses} / ${f.audienceSize}` },
    { key: "x", header: "", cell: (f) => <Button size="sm" href={`/hr/forms/${f.id}`}>{f.status === "draft" ? t("Edit") : t("Results")}</Button> },
  ];
  const rows = q.data ?? [];
  return (
    <Require perm="hrconfig.manage">
      <PageHeader title={t("Forms & Surveys")} description={t("Ask everyone, or one department, to fill in a form: engagement surveys, event sign-ups, data checks. Anonymous forms never show who answered what.")}
        actions={<Button variant="primary" onClick={create}><Plus size={16} /> {t("New form")}</Button>} />
      <Panel>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No forms yet")} description={t("Create a survey, add questions, then open it: employees are notified and answer under My Forms.")} />
          : <DataTable label={t("Forms")} columns={cols} rows={rows} rowKey={(f) => f.id} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
    </Require>
  );
}
