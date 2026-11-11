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

/** EMP-008 — who reports to whom. Built in the browser from one flat list; people whose manager is missing from the list (or who have none) become roots, shown under their department. */
function Branch({ node, top = false }: { node: Node; top?: boolean }) {
  return (
    <li className={top && node.reports.length > 0 ? "has-reports" : undefined}>
      <Link href={`/hr/employees/${node.id}`} className="who">
        <Avatar name={node.fullName} size={30} /><span><b>{node.fullName}</b><small className="sub">{node.jobTitle ?? "—"}{node.reports.length > 0 ? ` · ${t("{n} direct reports", { n: node.reports.length })}` : ""}</small></span>
      </Link>
      {node.reports.length > 0 && <ul>{node.reports.map((r) => <Branch key={r.id} node={r} />)}</ul>}
    </li>
  );
}
const size = (n: Node): number => 1 + n.reports.reduce((a, r) => a + size(r), 0);

export default function OrgChartPage() {
  const q = useQuery(() => get<EmployeeListItem[]>("/api/hr/employees/org-chart"), []);
  const roots = useMemo(() => {
    const nodes = new Map((q.data ?? []).map((e) => [e.id, { ...e, reports: [] as Node[] }]));
    const top: Node[] = [];
    for (const n of nodes.values()) { const p = n.managerId ? nodes.get(n.managerId) : undefined; if (p && p !== n) p.reports.push(n); else top.push(n); }
    return top.sort((a, b) => b.reports.length - a.reports.length || a.fullName.localeCompare(b.fullName));
  }, [q.data]);
  const depts = useMemo(() => {
    const by = new Map<string, Node[]>();
    for (const r of roots) { const k = r.orgUnit ?? ""; by.set(k, [...(by.get(k) ?? []), r]); }
    return [...by.entries()].sort(([a], [b]) => (a === "" ? 1 : b === "" ? -1 : a.localeCompare(b)));
  }, [roots]);
  const flat = roots.length > 1 && roots.every((r) => r.reports.length === 0);
  return (
    <Require perm="employees.read">
      <PageHeader title={t("Org Chart")} description={t("Who reports to whom. Click a person to open their record.")} />
      <Panel>
        {q.error ? <div className="form-alert err" role="alert">{q.error.message}</div>
          : !q.loading && roots.length === 0 ? <EmptyState title={t("No employees yet")} description={t("The chart appears once employees are added.")} />
          : <>
            {flat && <div className="form-alert" role="note" style={{ marginBottom: 14 }}>{t("Nobody has a manager yet, so everyone is listed by department. Set a person's manager under Change job details on their record and the reporting lines appear here.")}</div>}
            {depts.map(([name, people]) => (
              <section key={name || "none"} aria-label={name || t("No department")}>
                <h2 className="org-dept">{name || t("No department")}<span>{t("{n} people", { n: people.reduce((a, p) => a + size(p), 0) })}</span></h2>
                <ul className="org-grid">{people.map((r) => <Branch key={r.id} node={r} top />)}</ul>
              </section>))}
          </>}
      </Panel>
    </Require>
  );
}
