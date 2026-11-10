"use client";

import { useMemo } from "react";
import Link from "next/link";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Avatar } from "@/components/ui/avatar";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { EmployeeListItem } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

type Node = EmployeeListItem & { reports: Node[] };

/** EMP-008 — who reports to whom. Built in the browser from one flat list; people whose manager is missing from the list (or who have none) become roots. */
function Branch({ node, depth }: { node: Node; depth: number }) {
  return (
    <li style={{ listStyle: "none", marginLeft: depth === 0 ? 0 : 20, borderLeft: depth === 0 ? undefined : "1px solid var(--border, #2b3a63)", paddingLeft: depth === 0 ? 0 : 12 }}>
      <Link href={`/hr/employees/${node.id}`} className="who" style={{ padding: "6px 0" }}>
        <Avatar name={node.fullName} size={28} /><span><b>{node.fullName}</b><small className="sub">{node.jobTitle ?? "—"}{node.orgUnit ? ` · ${node.orgUnit}` : ""}{node.reports.length > 0 ? ` · ${t("{n} direct reports", { n: node.reports.length })}` : ""}</small></span>
      </Link>
      {node.reports.length > 0 && <ul style={{ margin: 0, padding: 0 }}>{node.reports.map((r) => <Branch key={r.id} node={r} depth={depth + 1} />)}</ul>}
    </li>
  );
}

export default function OrgChartPage() {
  const q = useQuery(() => get<EmployeeListItem[]>("/api/hr/employees/org-chart"), []);
  const roots = useMemo(() => {
    const nodes = new Map((q.data ?? []).map((e) => [e.id, { ...e, reports: [] as Node[] }]));
    const top: Node[] = [];
    for (const n of nodes.values()) { const p = n.managerId ? nodes.get(n.managerId) : undefined; if (p && p !== n) p.reports.push(n); else top.push(n); }
    return top.sort((a, b) => b.reports.length - a.reports.length || a.fullName.localeCompare(b.fullName));
  }, [q.data]);
  return (
    <Require perm="employees.read">
      <PageHeader title={t("Org Chart")} description={t("Who reports to whom. Click a person to open their record.")} />
      <Panel>
        {q.error ? <div className="form-alert err" role="alert">{q.error.message}</div>
          : !q.loading && roots.length === 0 ? <EmptyState title={t("No employees yet")} description={t("The chart appears once employees are added.")} />
          : <ul style={{ margin: 0, padding: 0 }}>{roots.map((r) => <Branch key={r.id} node={r} depth={0} />)}</ul>}
      </Panel>
    </Require>
  );
}
