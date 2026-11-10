"use client";

import { Suspense, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Download, Plus, Upload } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Avatar } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { DataTable, type Column, type Sort } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { FilterBar, FilterSelect, SearchBox } from "@/components/data/filter-bar";
import { EmptyState } from "@/components/data/empty-state";
import { AddEmployeeDrawer } from "@/components/hr/add-employee-drawer";
import { ImportEmployeesDrawer } from "@/components/hr/import-employees-drawer";
import { EmployeeStatusBadge, typeLabel } from "@/components/hr/employee-status";
import { Require } from "@/components/page-guard";
import { get } from "@/lib/api/client";
import type { EmployeeListItem, ExpiryItem, OrgUnitItem, Paged } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useDebounced, useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const ALL = { status: "All statuses", unit: "All departments", type: "All types" };
const TYPES = ["Permanent", "Contract", "Probation", "Intern"];

function Employees() {
  const { can } = useAuth(); const router = useRouter(); const run = useRun();
  const [q, setQ] = useState(""); const dq = useDebounced(q, 250);
  const [status, setStatus] = useState("Active"); const [unit, setUnit] = useState(ALL.unit); const [type, setType] = useState(ALL.type);
  const [sort, setSort] = useState<Sort>({ key: "name", dir: "asc" });
  const [page, setPage] = useState(1); const [size, setSize] = useState(10); const [add, setAdd] = useState(false); const [imp, setImp] = useState(false);
  useEffect(() => setPage(1), [dq, status, unit, type, size]);

  const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units"), []);
  const unitId = unit === ALL.unit ? undefined : units.data?.find((u) => u.name === unit)?.id;
  const filters = { q: dq.trim() || undefined, status: status === ALL.status ? "all" : status.toLowerCase(), orgUnitId: unitId, type: type === ALL.type ? undefined : type.toLowerCase() };
  const list = useQuery(() => get<Paged<EmployeeListItem>>("/api/hr/employees", { ...filters, sort: sort.key, dir: sort.dir, page, pageSize: size }), [dq, status, unitId, type, sort.key, sort.dir, page, size]);
  const expiring = useQuery(() => get<ExpiryItem[]>("/api/hr/employees/expiring", { days: 60 }), []);
  const rows = list.data?.data ?? []; const total = list.data?.total ?? 0; const pageCount = Math.max(1, Math.ceil(total / size));

  // Export goes through fetch so the session cookie is used and a refusal shows as a message instead of a blank download.
  const exportCsv = (format?: string) => run(async () => {
    const qs = new URLSearchParams([...(Object.entries(filters).filter(([, v]) => v) as [string, string][]), ...(format ? [["format", format]] : [])] as [string, string][]).toString();
    const res = await fetch(`/api/hr/employees/export${qs ? `?${qs}` : ""}`, { credentials: "same-origin", cache: "no-store" });
    if (!res.ok) throw new Error(t("Export failed."));
    const a = document.createElement("a"); a.href = URL.createObjectURL(await res.blob());
    a.download = res.headers.get("content-disposition")?.match(/filename="?([^";]+)/)?.[1] ?? "employees.csv"; a.click(); URL.revokeObjectURL(a.href);
  }, t("Export downloaded."));

  const columns: Column<EmployeeListItem>[] = [
    { key: "name", header: t("Employee"), sortable: true, cell: (e) => <Link href={`/hr/employees/${e.id}`} className="who"><Avatar name={e.fullName} size={30} /><span><b>{e.fullName}</b><small className="sub">{e.employeeNo}{e.workEmail ? ` · ${e.workEmail}` : ""}</small></span></Link> },
    { key: "job", header: t("Job"), cell: (e) => <><b>{e.jobTitle ?? "—"}</b><small className="sub">{e.orgUnit ?? "—"} · {typeLabel(e.type)}</small></> },
    { key: "manager", header: t("Manager"), className: "hide-md", cell: (e) => e.managerName ?? "—" },
    { key: "hireDate", header: t("Hired"), sortable: true, className: "hide-sm", cell: (e) => fmtDate(e.hireDate) },
    { key: "status", header: t("Status"), sortable: true, cell: (e) => <EmployeeStatusBadge status={e.status} /> },
  ];
  const empty = !list.loading && !list.error && rows.length === 0;
  return (
    <Require perm="employees.read">
      <PageHeader title={t("Employees")} description={t("Everyone in the organisation, with their current job.")}
        actions={<div className="row" style={{ gap: 8 }}>
          {can("employees.export") && <Button onClick={() => exportCsv()}><Download size={16} /> {t("Export CSV")}</Button>}
          {can("employees.export") && <Button onClick={() => exportCsv("xlsx")}><Download size={16} /> {t("Export Excel")}</Button>}
          {can("employees.create") && <Button onClick={() => setImp(true)}><Upload size={16} /> {t("Import CSV")}</Button>}
          {can("employees.create") && <Button variant="primary" onClick={() => setAdd(true)}><Plus size={16} /> {t("Add Employee")}</Button>}</div>} />
      {(expiring.data?.length ?? 0) > 0 && (
        <Panel title={t("Contracts ending soon")} description={t("Fixed-term contracts, probation and internships that end within 60 days.")} style={{ marginBottom: 16 }}>
          <ul className="mini-list">{expiring.data!.slice(0, 8).map((x) => (
            <li key={`${x.kind}-${x.employeeId}-${x.date}`}><div><strong><Link href={`/hr/employees/${x.employeeId}`}>{x.employeeName}</Link></strong><small>{x.employeeNo} · {typeLabel(x.label)} · {fmtDate(x.date)}</small></div>
              <Badge tone={x.daysLeft < 0 ? "red" : x.daysLeft <= 14 ? "orange" : "gray"}>{x.daysLeft < 0 ? t("Ended {n} days ago", { n: -x.daysLeft }) : t("{n} days left", { n: x.daysLeft })}</Badge></li>))}</ul>
        </Panel>)}
      <Panel>
        <FilterBar>
          <SearchBox label={t("Search employees")} placeholder={t("Search by name, number or email…")} value={q} onChange={setQ} />
          <FilterSelect label={t("Status")} value={status} onChange={setStatus} options={["Active", "Terminated", ALL.status]} />
          <FilterSelect label={t("Department")} value={unit} onChange={setUnit} options={[ALL.unit, ...(units.data ?? []).map((u) => u.name)]} />
          <FilterSelect label={t("Type")} value={type} onChange={setType} options={[ALL.type, ...TYPES]} />
        </FilterBar>
        {empty
          ? <EmptyState title={t("No employees found")} description={t("Try adjusting your search or filters.")} action={can("employees.create") ? <Button variant="primary" onClick={() => setAdd(true)}><Plus size={15} /> {t("Add Employee")}</Button> : undefined} />
          : <DataTable label={t("Employees")} columns={columns} rows={rows} rowKey={(e) => e.id} sort={sort} onSortChange={setSort} loading={list.data === undefined && list.loading} error={list.error?.message ?? null} onRetry={list.reload} />}
        {rows.length > 0 && <Pagination page={Math.min(page, pageCount)} pageCount={pageCount} onPageChange={setPage} pageSize={size} onPageSizeChange={setSize} summary={`Showing ${(page - 1) * size + 1}–${Math.min(page * size, total)} of ${total} employees`} />}
      </Panel>
      <ImportEmployeesDrawer open={imp} onClose={() => setImp(false)} onDone={() => list.reload()} />
      <AddEmployeeDrawer open={add} onClose={() => setAdd(false)} onDone={(id) => router.push(`/hr/employees/${id}`)} />
    </Require>
  );
}
export default function EmployeesPage() { return <Suspense><Employees /></Suspense>; }
