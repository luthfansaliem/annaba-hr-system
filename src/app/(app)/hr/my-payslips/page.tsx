"use client";

import { useState } from "react";
import { Download, Printer } from "lucide-react";
import { download } from "@/lib/download";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get } from "@/lib/api/client";
import type { Payslip, PayslipRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { idr, monthName } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const Line = ({ k, v, strong }: { k: string; v: number; strong?: boolean }) => <tr><td>{strong ? <b>{k}</b> : k}</td><td style={{ textAlign: "right" }}>{strong ? <b>{idr(v)}</b> : idr(v)}</td></tr>;

export default function MyPayslipsPage() {
  const run = useRun(); const q = useQuery(() => get<PayslipRow[]>("/api/hr/payroll/payslips"), []); const [slip, setSlip] = useState<Payslip | null>(null);
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  const open = async (r: PayslipRow) => { const res = await run(() => get<Payslip>(`/api/hr/payroll/payslips/${r.itemId}`)); if (res.ok) setSlip(res.data); };
  const cols: Column<PayslipRow>[] = [
    { key: "p", header: t("Period"), cell: (r) => <b>{monthName(r.year, r.month)}</b> }, { key: "g", header: t("Gross"), className: "hide-sm", cell: (r) => idr(r.gross) }, { key: "n", header: t("Take-home pay"), cell: (r) => idr(r.net) },
    { key: "a", header: "", cell: (r) => <Button size="sm" onClick={() => open(r)}>{t("View")}</Button> },
  ];
  const i = slip?.item;
  return (
    <>
      <PageHeader title={t("My Payslips")} description={t("Your payslip appears here once the month's payroll is finalised.")} />
      <Panel>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} />
          : !q.loading && (q.data?.length ?? 0) === 0 ? <EmptyState title={t("No payslips yet")} description={t("Finalised payslips will appear here.")} />
          : <DataTable label={t("Payslips")} columns={cols} rows={q.data ?? []} rowKey={(r) => r.itemId} loading={q.data === undefined && q.loading} error={q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={!!slip} onClose={() => setSlip(null)} side="right" title={slip ? `${t("Payslip")} ${monthName(slip.year, slip.month)}` : ""} description={i ? `${i.employeeName} · ${i.employeeNo}` : undefined}
        footer={<><Button onClick={() => setSlip(null)}>{t("Close")}</Button><Button onClick={() => run(() => download(`/api/hr/payroll/payslips/${i!.id}/pdf`))}><Download size={15} /> {t("PDF")}</Button><Button variant="primary" onClick={() => window.print()}><Printer size={15} /> {t("Print")}</Button></>}>
        {i && slip && (
          <div className="stack">
            <table style={{ width: "100%" }}><tbody>
              <tr><th colSpan={2} style={{ textAlign: "left" }}>{t("Earnings")}</th></tr>
              {i.dailyRate != null ? <><Line k={t("Daily wage ({days} days × {rate})", { days: String(i.daysPaid ?? 0), rate: idr(i.dailyRate) })} v={i.basic} />{i.allowance > 0 && <Line k={t("Daily allowance")} v={i.allowance} />}</> : <><Line k={t("Basic salary")} v={i.basic * i.prorateFactor} /><Line k={t("Fixed allowance")} v={i.allowance * i.prorateFactor} /></>}
              {i.overtimePay > 0 && <Line k={t("Overtime")} v={i.overtimePay} />}
              {i.unpaidDeduction > 0 && <Line k={t("Unpaid leave")} v={-i.unpaidDeduction} />}
              {slip.lines.filter((l) => l.kind === "earning").map((l) => <Line key={l.id} k={l.label} v={l.amount} />)}
              <Line k={t("Gross")} v={i.gross} strong />
              <tr><th colSpan={2} style={{ textAlign: "left", paddingTop: 12 }}>{t("Deductions")}</th></tr>
              <Line k="BPJS Kesehatan" v={-i.kesEmployee} /><Line k="BPJS JHT" v={-i.jhtEmployee} /><Line k="BPJS JP" v={-i.jpEmployee} /><Line k={`PPh 21 (${i.taxMethod})`} v={-i.tax} />
              {slip.lines.filter((l) => l.kind === "deduction").map((l) => <Line key={l.id} k={l.label} v={-l.amount} />)}
              <Line k={t("Take-home pay")} v={i.net} strong />
            </tbody></table>
            <small className="sub">{t("Employer contributions (not deducted from you): BPJS Kesehatan {a}, JHT {b}, JP {c}, JKK {d}, JKM {e}.", { a: idr(i.kesEmployer), b: idr(i.jhtEmployer), c: idr(i.jpEmployer), d: idr(i.jkkEmployer), e: idr(i.jkmEmployer) })}</small>
          </div>)}
      </Overlay>
    </>
  );
}
