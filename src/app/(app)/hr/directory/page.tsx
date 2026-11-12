"use client";

import { useState } from "react";
import { Search } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { BranchItem, DirectoryEntry, OrgUnitItem, Paged } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { t } from "@/i18n";

/** Find a colleague: name, job, department, branch, manager and work e-mail. Personal contact details are never shown here. */
export default function DirectoryPage() {
  const [q, setQ] = useState(""); const [unit, setUnit] = useState(""); const [branch, setBranch] = useState("");
  const list = useQuery(() => get<Paged<DirectoryEntry>>("/api/hr/directory", { q: q || undefined, orgUnitId: unit || undefined, branchId: branch || undefined, pageSize: 100 }), [q, unit, branch]);
  const units = useQuery(() => get<OrgUnitItem[]>("/api/hr/org-units").catch(() => [] as OrgUnitItem[]), []);
  const branches = useQuery(() => get<BranchItem[]>("/api/hr/branches").catch(() => [] as BranchItem[]), []);
  const rows = list.data?.data ?? []; const unlinked = (list.error as { code?: string } | undefined)?.code === "invariant";
  return (
    <>
      <PageHeader title={t("Employee Directory")} description={t("Find colleagues by name, job or department.")} />
      <Panel actions={<div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
        <TextInput aria-label={t("Search")} icon={<Search size={15} />} value={q} onChange={(e) => setQ(e.target.value)} placeholder={t("Name, number or job")} />
        {(units.data?.length ?? 0) > 0 && <SelectInput aria-label={t("Department")} value={unit} onChange={(e) => setUnit(e.target.value)} options={[{ value: "", label: t("All departments") }, ...units.data!.map((u) => ({ value: u.id, label: u.name }))]} />}
        {(branches.data?.length ?? 0) > 0 && <SelectInput aria-label={t("Branch")} value={branch} onChange={(e) => setBranch(e.target.value)} options={[{ value: "", label: t("All branches") }, ...branches.data!.map((b) => ({ value: b.id, label: b.name }))]} />}
      </div>}>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !list.loading && rows.length === 0 ? <EmptyState title={t("No one found")} description={t("Try another name or remove a filter.")} />
          : <div className="dir-grid">{rows.map((p) => (
            <div key={p.employeeId} className="dir-card">
              <b>{p.fullName}</b>
              <small className="sub">{p.jobTitle ?? "—"}{p.department ? ` · ${p.department}` : ""}</small>
              {p.branch && <small className="sub">{p.branch}</small>}
              {p.manager && <small className="sub">{t("Reports to {name}", { name: p.manager })}</small>}
              {p.workEmail && <a href={`mailto:${p.workEmail}`} className="sub">{p.workEmail}</a>}
            </div>))}</div>}
      </Panel>
    </>
  );
}
