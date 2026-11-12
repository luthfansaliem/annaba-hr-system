"use client";

import { use, useState } from "react";
import Link from "next/link";
import { ArrowLeft, Plus, TrendingUp, Users, Wallet } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Field, SelectInput, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { Require } from "@/components/page-guard";
import { QueryBoundary } from "@/components/query-boundary";
import { Kpi, KpiRow } from "@/components/charts/analytics";
import { del, get, post, put } from "@/lib/api/client";
import type { CompensationRow, OrgUnitItem, SalaryGrade, SalaryReviewDetail, SalaryReviewLine } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate, idr } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { REVIEW_STATUS } from "@/components/hr/review-status";
import { t } from "@/i18n";

const POSITION: Record<string, [("orange" | "green" | "blue" | "red"), string]> = { below: ["red", "below range"], low: ["blue", "lower half"], high: ["green", "upper half"], above: ["orange", "above range"] };

function Detail({ id }: { id: string }) {
  const run = useRun(); const q = useQuery(() => get<SalaryReviewDetail>(`/api/hr/payroll/reviews/${id}`), [id]);
  const grades = useQuery(() => get<SalaryGrade[]>("/api/hr/payroll/grades"), []);
  const [adding, setAdding] = useState(false); const [who, setWho] = useState({ mode: "all", unit: "", employeeId: "", pct: "5" });
  const people = useQuery(() => (adding ? get<CompensationRow[]>("/api/hr/payroll/compensation") : Promise.resolve([] as CompensationRow[])), [adding]);
  const units = useQuery(() => (adding ? get<OrgUnitItem[]>("/api/hr/org-units") : Promise.resolve([] as OrgUnitItem[])), [adding]);
  const [line, setLine] = useState<SalaryReviewLine | null>(null); const [lf, setLf] = useState({ basic: "", allowance: "", gradeId: "", note: "" });
  const [back, setBack] = useState(false); const [note, setNote] = useState("");
  const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");

  async function add(e: React.FormEvent) {
    e.preventDefault(); setForm("");
    const body = { allEmployees: who.mode === "all" || undefined, orgUnitId: who.mode === "unit" ? who.unit || units.data?.[0]?.id : undefined, employeeIds: who.mode === "one" ? [who.employeeId || people.data?.[0]?.employeeId] : undefined, increasePct: Number(who.pct || 0) };
    const r = await run(() => post<{ added: number }>(`/api/hr/payroll/reviews/${id}/employees`, body), undefined, { silent: true });
    if (r.ok) { setAdding(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  async function saveLine(e: React.FormEvent) {
    e.preventDefault(); if (!line) return; setForm("");
    const r = await run(() => put(`/api/hr/payroll/reviews/${id}/lines/${line.id}`, { proposedBasic: Number(lf.basic), proposedAllowance: Number(lf.allowance || 0), gradeId: lf.gradeId || undefined, note: lf.note }), t("Proposal saved."), { silent: true });
    if (r.ok) { setLine(null); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); }
  }
  const act = async (action: string, msg: string, body?: object) => { const r = await run(() => post(`/api/hr/payroll/reviews/${id}/${action}`, body ?? {}), msg, { silent: action === "return" }); if (r.ok) { setBack(false); q.reload(); } else if (action === "return") setErr(r.fields ?? {}); };

  return (
    <QueryBoundary q={q} rows={6}>{({ review: r, lines, currentTotal, proposedTotal, increasePct }) => {
      const cols: Column<SalaryReviewLine>[] = [
        { key: "e", header: t("Employee"), cell: (l) => <><b>{l.employeeName}</b><small className="sub">{l.employeeNo}{l.department ? ` · ${l.department}` : ""}</small></> },
        { key: "c", header: t("Now"), className: "hide-sm", cell: (l) => <>{idr(l.currentBasic)}{l.currentAllowance > 0 && <small className="sub">+ {idr(l.currentAllowance)}</small>}</> },
        { key: "p", header: t("Proposed"), cell: (l) => <><b>{idr(l.proposedBasic)}</b>{l.proposedAllowance > 0 && <small className="sub">+ {idr(l.proposedAllowance)}</small>}</> },
        { key: "i", header: t("Difference"), cell: (l) => <Badge tone={l.increasePct > 0 ? "green" : l.increasePct < 0 ? "red" : "gray"} dot={false}>{l.increasePct > 0 ? "+" : ""}{l.increasePct}%</Badge> },
        { key: "g", header: t("Grade"), className: "hide-md", cell: (l) => l.grade ? <>{l.grade}{l.gradePosition && <small className="sub">{t(POSITION[l.gradePosition][1])}</small>}</> : "—" },
        { key: "x", header: "", cell: (l) => <>{l.applyError && <small className="sub" style={{ color: "var(--red-ink)" }}>{l.applyError}</small>}{r.canEdit && <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
          <Button size="sm" onClick={() => { setErr({}); setForm(""); setLf({ basic: String(l.proposedBasic), allowance: String(l.proposedAllowance), gradeId: grades.data?.find((g) => g.code === l.grade)?.id ?? "", note: l.note ?? "" }); setLine(l); }}>{t("Edit")}</Button>
          <Button size="sm" variant="danger-outline" aria-label={t("Remove {name}", { name: l.employeeName })} onClick={async () => { if ((await run(() => del(`/api/hr/payroll/reviews/${id}/lines/${l.id}`), t("Removed."))).ok) q.reload(); }}>{t("Remove")}</Button>
        </div>}</> },
      ];
      return (
        <>
          <PageHeader title={r.name} description={t("New salaries from {date}. Prepared by payroll, approved by someone else, then applied as new salary periods.", { date: fmtDate(r.effectiveFrom) })}
            actions={<div className="row" style={{ gap: 8, flexWrap: "wrap" }}>
              <Badge tone={REVIEW_STATUS[r.status][0]}>{t(REVIEW_STATUS[r.status][1])}</Badge>
              {r.canEdit && <Button onClick={() => { setErr({}); setForm(""); setAdding(true); }}><Plus size={16} /> {t("Add employees")}</Button>}
              {r.canSubmit && <Button variant="primary" onClick={() => act("submit", t("Sent for approval."))}>{t("Submit for approval")}</Button>}
              {r.canApprove && <Button onClick={() => { setNote(""); setErr({}); setBack(true); }}>{t("Send back")}</Button>}
              {r.canApprove && <Button variant="primary" onClick={() => act("approve", t("Review approved."))}>{t("Approve")}</Button>}
              {r.canApply && <Button variant="primary" onClick={() => act("apply", t("New salaries applied."))}>{t("Apply new salaries")}</Button>}
              {(r.status === "draft" || r.status === "submitted" || r.status === "approved") && r.canEdit && <Button variant="danger-outline" onClick={() => act("cancel", t("Review cancelled."))}>{t("Cancel review")}</Button>}
            </div>} />
          {r.note && <div className="form-alert" role="status" style={{ marginBottom: 12 }}>{r.note}</div>}
          <KpiRow>
            <Kpi icon={<Users size={16} />} label={t("Employees")} value={lines.length} />
            <Kpi icon={<Wallet size={16} />} label={t("Monthly pay now")} value={idr(currentTotal)} tone="blue" />
            <Kpi icon={<Wallet size={16} />} label={t("Monthly pay proposed")} value={idr(proposedTotal)} tone="green" />
            <Kpi icon={<TrendingUp size={16} />} label={t("Difference")} value={`${increasePct > 0 ? "+" : ""}${increasePct}%`} hint={idr(proposedTotal - currentTotal)} tone="orange" />
          </KpiRow>
          <Panel>
            {lines.length === 0 ? <EmptyState title={t("No one in this review yet")} description={t("Add everyone, a department or one person with a percentage, then adjust each proposal.")} />
              : <DataTable label={t("Proposals")} columns={cols} rows={lines} rowKey={(l) => l.id} />}
          </Panel>
          <Overlay open={adding} onClose={() => setAdding(false)} side="right" title={t("Add employees")} footer={<><Button onClick={() => setAdding(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rva-form">{t("Add")}</Button></>}>
            <form id="rva-form" className="stack" noValidate onSubmit={add}>
              {form && <div className="form-alert err" role="alert">{form}</div>}
              <Field label={t("Who")} htmlFor="rva-mode"><SelectInput id="rva-mode" value={who.mode} onChange={(e) => setWho({ ...who, mode: e.target.value })} options={[{ value: "all", label: t("Everyone with a salary") }, { value: "unit", label: t("One department") }, { value: "one", label: t("One person") }]} /></Field>
              {who.mode === "unit" && <Field label={t("Department")} htmlFor="rva-unit"><SelectInput id="rva-unit" value={who.unit || units.data?.[0]?.id || ""} onChange={(e) => setWho({ ...who, unit: e.target.value })} options={(units.data ?? []).map((u) => ({ value: u.id, label: u.name }))} /></Field>}
              {who.mode === "one" && <Field label={t("Employee")} htmlFor="rva-one"><SelectInput id="rva-one" value={who.employeeId || people.data?.[0]?.employeeId || ""} onChange={(e) => setWho({ ...who, employeeId: e.target.value })} options={(people.data ?? []).filter((p) => p.hasCompensation).map((p) => ({ value: p.employeeId, label: `${p.employeeName} (${p.employeeNo})` }))} /></Field>}
              <Field label={t("Increase on the basic salary (%)")} htmlFor="rva-pct" error={err.increasePct} hint={t("Each proposal can be changed afterwards. People already in the review are skipped.")}><TextInput id="rva-pct" inputMode="decimal" value={who.pct} invalid={!!err.increasePct} onChange={(e) => setWho({ ...who, pct: e.target.value })} /></Field>
            </form>
          </Overlay>
          <Overlay open={!!line} onClose={() => setLine(null)} side="right" title={line ? line.employeeName : ""} description={line ? t("Now {basic} basic + {allow} allowance", { basic: idr(line.currentBasic), allow: idr(line.currentAllowance) }) : undefined}
            footer={<><Button onClick={() => setLine(null)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="rvl-form">{t("Save")}</Button></>}>
            <form id="rvl-form" className="stack" noValidate onSubmit={saveLine}>
              {form && <div className="form-alert err" role="alert">{form}</div>}
              <div className="fgrid">
                <Field label={t("New basic salary (IDR / month)")} htmlFor="rvl-b" required error={err.proposedBasic}><TextInput id="rvl-b" inputMode="decimal" data-autofocus value={lf.basic} invalid={!!err.proposedBasic} onChange={(e) => setLf({ ...lf, basic: e.target.value })} /></Field>
                <Field label={t("New fixed allowance (IDR / month)")} htmlFor="rvl-a" error={err.proposedAllowance}><TextInput id="rvl-a" inputMode="decimal" value={lf.allowance} invalid={!!err.proposedAllowance} onChange={(e) => setLf({ ...lf, allowance: e.target.value })} /></Field>
                <Field label={t("Grade")} htmlFor="rvl-g" error={err.gradeId}><SelectInput id="rvl-g" value={lf.gradeId} onChange={(e) => setLf({ ...lf, gradeId: e.target.value })} options={[{ value: "", label: t("Keep as is") }, ...(grades.data ?? []).filter((g) => !g.archived).map((g) => ({ value: g.id, label: `${g.code} · ${idr(g.minPay)}–${idr(g.maxPay)}` }))]} /></Field>
                <Field label={t("Note")} htmlFor="rvl-n"><TextInput id="rvl-n" value={lf.note} onChange={(e) => setLf({ ...lf, note: e.target.value })} placeholder={t("Promotion, market adjustment…")} /></Field>
              </div>
            </form>
          </Overlay>
          <Overlay open={back} onClose={() => setBack(false)} title={t("Send back to the preparer")} footer={<><Button onClick={() => setBack(false)}>{t("Cancel")}</Button><Button variant="primary" onClick={() => act("return", t("Sent back."), { note })}>{t("Send back")}</Button></>}>
            <Field label={t("What should change?")} htmlFor="rvb-note" required error={err.note}><TextInput id="rvb-note" data-autofocus value={note} invalid={!!err.note} onChange={(e) => setNote(e.target.value)} /></Field>
          </Overlay>
        </>
      );
    }}</QueryBoundary>
  );
}

export default function SalaryReviewPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <Require perm="payroll.read"><p style={{ marginBottom: 12 }}><Link href="/hr/salary-reviews" className="link-btn"><ArrowLeft size={14} /> {t("All salary reviews")}</Link></p><Detail id={id} /></Require>;
}
