"use client";

import { useState } from "react";
import Link from "next/link";
import { AlertTriangle, Award, BarChart3, Clock, Download, Gauge, Play, Save, Target, Timer, Trash2, UserMinus, UserPlus, Users } from "lucide-react";
import { BarList, ChartGrid, ChartPanel, ColumnChart, Kpi, KpiRow, ShareBar, monthShort, num, type Share, type Tone } from "@/components/charts/analytics";
import { QueryBoundary } from "@/components/query-boundary";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Checkbox, Field, SelectInput, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { BOXES } from "@/components/hr/performance";
import { api, del, get, post } from "@/lib/api/client";
import type { AtRiskRow, OvertimeAnalytics, PerformanceAnalytics, ReportConfig, ReportDataset, ReportFilter, ReportResult, SavedReport, TurnoverAnalytics } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { idr } from "@/lib/format";
import { downloadPost } from "@/lib/download";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const tones: Tone[] = ["blue", "teal", "orange", "purple", "green", "red"];
const pct = (n: number) => `${num(n)}%`;
const TENURE: Record<string, string> = { "< 1 year": "Under 1 year", "1–3 years": "1–3 years", "3–5 years": "3–5 years", "5+ years": "5+ years" };

export function TurnoverView() {
  const q = useQuery(() => get<TurnoverAnalytics>("/api/hr/analytics/turnover"), []);
  return <QueryBoundary q={q}>{(d) => (
    <div className="stack">
      <KpiRow label={t("Turnover figures")}>
        <Kpi icon={<UserMinus size={18} />} tone="red" label={t("Left, last 12 months")} value={d.leavers12} hint={t("{p} of the average headcount", { p: pct(d.annualRatePct) })} />
        <Kpi icon={<UserPlus size={18} />} tone="green" label={t("Joined, last 12 months")} value={d.joiners12} />
        <Kpi icon={<AlertTriangle size={18} />} tone="orange" label={t("Left within their first year")} value={pct(d.earlyLeaverPct)} hint={t("of all leavers")} />
        <Kpi icon={<Clock size={18} />} label={t("Average service of leavers")} value={t("{n} years", { n: num(d.avgTenureYears) })} />
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<BarChart3 size={18} />} title={t("Leavers and joiners by month")} subtitle={t("Last 12 months")}>
          <ColumnChart points={d.months.map((m) => ({ label: monthShort(m.month), value: m.joiners + m.leavers, part: m.leavers, title: `${monthShort(m.month)} · ${t("turnover {p}", { p: pct(m.ratePct) })}` }))} tone="green" partTone="red" label={t("Leavers and joiners by month")} valueName={t("Joiners + leavers")} partName={t("Leavers")} empty={t("Nobody joined or left in the last 12 months.")} />
        </ChartPanel>
        <ChartPanel icon={<Users size={18} />} title={t("Turnover by department")} subtitle={t("Leavers as a share of the department")}>
          <BarList rows={d.byDepartment.map((r) => ({ label: r.key, value: r.pct, note: ` · ${r.count}` }))} format={pct} tone="red" label={t("Turnover by department")} empty={t("Nobody left in the last 12 months.")} />
        </ChartPanel>
        <ChartPanel icon={<Clock size={18} />} title={t("Leavers by length of service")}>
          <ShareBar parts={d.byTenure.map((r, i): Share => ({ label: t(TENURE[r.key] ?? r.key), value: r.count, tone: tones[i % tones.length] }))} label={t("Leavers by length of service")} empty={t("Nobody left in the last 12 months.")} />
        </ChartPanel>
        <ChartPanel icon={<UserMinus size={18} />} title={t("Reasons for leaving")}>
          <BarList rows={d.byReason.map((r) => ({ label: r.key === "Not recorded" ? t("Not recorded") : r.key, value: r.count }))} tone="orange" label={t("Reasons for leaving")} empty={t("Nobody left in the last 12 months.")} />
        </ChartPanel>
      </ChartGrid>
    </div>)}</QueryBoundary>;
}

export function OvertimeView() {
  const q = useQuery(() => get<OvertimeAnalytics>("/api/hr/analytics/overtime"), []);
  return <QueryBoundary q={q}>{(d) => (
    <div className="stack">
      <KpiRow label={t("Overtime figures")}>
        <Kpi icon={<Timer size={18} />} label={t("Approved hours, last 12 months")} value={num(d.hours12)} />
        <Kpi icon={<Clock size={18} />} tone="orange" label={t("Hours this month")} value={num(d.hoursThisMonth)} hint={t("{n} people · {a} hours each", { n: d.peopleThisMonth, a: num(d.avgPerPerson) })} />
        <Kpi icon={<AlertTriangle size={18} />} tone="red" label={t("Waiting for approval")} value={d.pending} href="/hr/time-approvals" />
        {d.payThisYear != null && <Kpi icon={<Gauge size={18} />} tone="purple" label={t("Overtime pay this year")} value={idr(d.payThisYear)} />}
      </KpiRow>
      <ChartGrid>
        <ChartPanel wide icon={<BarChart3 size={18} />} title={t("Overtime hours by month")} subtitle={t("Approved, last 12 months")}>
          <ColumnChart points={d.hoursByMonth.map((p) => ({ label: monthShort(p.label), value: p.value }))} tone="orange" label={t("Overtime hours by month")} valueName={t("Hours")} empty={t("No approved overtime yet.")} />
        </ChartPanel>
        <ChartPanel icon={<Users size={18} />} title={t("Hours by department")}>
          <BarList rows={d.byDepartment.map((r) => ({ label: r.key, value: r.amount, note: ` · ${t("{n} people", { n: r.count })}` }))} tone="orange" label={t("Hours by department")} empty={t("No approved overtime yet.")} />
        </ChartPanel>
        <ChartPanel icon={<Timer size={18} />} title={t("Most overtime")} subtitle={t("Watch for overwork")}>
          <BarList rows={d.top.map((p) => ({ label: p.name, value: p.hours, note: p.department ? ` · ${p.department}` : "" }))} tone="red" label={t("Most overtime")} empty={t("No approved overtime yet.")} />
        </ChartPanel>
      </ChartGrid>
    </div>)}</QueryBoundary>;
}

export function PerformanceView() {
  const q = useQuery(() => get<PerformanceAnalytics>("/api/hr/analytics/performance"), []);
  return <QueryBoundary q={q}>{(d) => (
    <div className="stack">
      <KpiRow label={t("Performance figures")}>
        <Kpi icon={<Users size={18} />} label={t("People reviewed")} value={d.reviewed} hint={d.cycle ?? t("No review cycle yet")} />
        <Kpi icon={<Award size={18} />} tone="green" label={t("Average score")} value={d.avgScore != null ? num(d.avgScore) : "—"} hint={t("of 5")} />
        <Kpi icon={<Target size={18} />} tone="blue" label={t("Average goal progress")} value={pct(d.avgGoalProgress)} hint={t("{n} goals this year", { n: d.goals })} />
        <Kpi icon={<AlertTriangle size={18} />} tone="red" label={t("Goals at risk or off track")} value={d.goalsAtRisk} />
      </KpiRow>
      <ChartGrid>
        <ChartPanel icon={<BarChart3 size={18} />} title={t("Rating distribution")} subtitle={d.cycle ?? undefined}>
          <ColumnChart points={d.ratings.map((r) => ({ label: r.key, value: r.count }))} tone="purple" label={t("Rating distribution")} valueName={t("People")} empty={t("Ratings appear after a review cycle.")} />
        </ChartPanel>
        <ChartPanel icon={<Users size={18} />} title={t("Average score by department")}>
          <BarList rows={d.avgByDepartment.map((r) => ({ label: r.key, value: r.amount, note: ` · ${r.count}` }))} tone="purple" label={t("Average score by department")} empty={t("Ratings appear after a review cycle.")} />
        </ChartPanel>
        <ChartPanel icon={<Award size={18} />} title={t("9-box")}>
          <ShareBar parts={d.boxes.map((b): Share => ({ label: t(BOXES.find((x) => x.key === b.key)?.label ?? b.key), value: b.count, tone: ({ green: "green", blue: "blue", orange: "orange", red: "red", gray: "teal" } as Record<string, Tone>)[BOXES.find((x) => x.key === b.key)?.tone ?? "gray"] }))} label={t("9-box")} empty={t("Set potential in calibration to fill the 9-box.")} />
        </ChartPanel>
        <ChartPanel icon={<Target size={18} />} title={t("Goal progress by department")} subtitle={t("This year")}>
          <BarList rows={d.goalProgressByDepartment.map((r) => ({ label: r.key, value: r.amount, note: ` · ${t("{n} goals", { n: r.count })}` }))} format={pct} tone="blue" label={t("Goal progress by department")} empty={t("No goals this year yet.")} />
        </ChartPanel>
      </ChartGrid>
    </div>)}</QueryBoundary>;
}

const SIGNAL: Record<string, string> = { contract_ending: "Contract ending", low_rating: "Low rating", rating_drop: "Rating fell", no_raise: "No pay change", overtime: "Heavy overtime", short_absences: "Short absences", new_joiner: "New joiner", team_leavers: "Team leavers" };
export function AtRiskList() {
  const q = useQuery(() => get<AtRiskRow[]>("/api/hr/analytics/at-risk"), []); const [level, setLevel] = useState("all");
  return <QueryBoundary q={q}>{(rows) => { const shown = rows.filter((r) => level === "all" || r.level === level); return (
    <div className="stack">
      <KpiRow label={t("At-risk figures")}>
        <Kpi icon={<AlertTriangle size={18} />} tone="red" label={t("High")} value={rows.filter((r) => r.level === "high").length} hint={t("50 points or more")} />
        <Kpi icon={<AlertTriangle size={18} />} tone="orange" label={t("Medium")} value={rows.filter((r) => r.level === "medium").length} hint={t("25–49 points")} />
        <Kpi icon={<Users size={18} />} label={t("Most common reason")} value={(() => { const c: Record<string, number> = {}; rows.forEach((r) => r.signals.forEach((s) => (c[s.code] = (c[s.code] ?? 0) + 1))); const top = Object.entries(c).sort((a, b) => b[1] - a[1])[0]; return top ? t(SIGNAL[top[0]] ?? top[0]) : "—"; })()} />
      </KpiRow>
      <Panel title={t("People to talk to")} description={t("An early warning from simple rules, not a prediction. Use it to start a conversation, never to decide about someone. Viewing this list is recorded.")}
        actions={<SelectInput aria-label={t("Level")} value={level} onChange={(e) => setLevel(e.target.value)} options={[{ value: "all", label: t("All") }, { value: "high", label: t("High") }, { value: "medium", label: t("Medium") }]} />}>
        {shown.length === 0 ? <EmptyState title={t("Nobody flagged")} description={t("No active employee collects enough warning signs right now.")} /> : (
          <ul className="risk-list">{shown.map((r) => (
            <li key={r.employeeId}>
              <div className="risk-score"><b>{r.score}</b><Badge tone={r.level === "high" ? "red" : "orange"}>{t(r.level === "high" ? "High" : "Medium")}</Badge></div>
              <div><Link href={`/hr/employees/${r.employeeId}`}><b>{r.name}</b></Link><small className="sub">{[r.employeeNo, r.jobTitle, r.department].filter(Boolean).join(" · ")}</small>
                <ul className="chips" style={{ marginTop: 6 }}>{r.signals.map((s) => <li key={s.code}><span className="tag" title={s.text}>{t(SIGNAL[s.code] ?? s.code)} +{s.points}</span></li>)}</ul>
                <small className="sub">{r.signals.map((s) => s.text).join(" · ")}</small></div>
            </li>))}</ul>)}
      </Panel>
    </div>); }}</QueryBoundary>;
}

// ============================================================ report builder
const OPS: { value: string; label: string; types: string[] }[] = [
  { value: "eq", label: "is", types: ["text", "number", "date"] }, { value: "neq", label: "is not", types: ["text", "number", "date"] }, { value: "contains", label: "contains", types: ["text"] },
  { value: "gt", label: "greater than", types: ["number", "date"] }, { value: "gte", label: "at least", types: ["number", "date"] }, { value: "lt", label: "less than", types: ["number", "date"] }, { value: "lte", label: "at most", types: ["number", "date"] },
  { value: "empty", label: "is empty", types: ["text", "number", "date"] }, { value: "notempty", label: "is not empty", types: ["text", "number", "date"] },
];

export function ReportBuilder() {
  const run = useRun(); const sets = useQuery(() => get<ReportDataset[]>("/api/hr/reports/datasets"), []); const saved = useQuery(() => get<SavedReport[]>("/api/hr/reports/saved"), []);
  const [cfg, setCfg] = useState<ReportConfig>({ dataset: "", columns: [], filters: [], groupBy: null, pivotBy: null, measure: "count", measureField: null });
  const [res, setRes] = useState<ReportResult | null>(null); const [form, setForm] = useState(""); const [name, setName] = useState(""); const [shared, setShared] = useState(false); const [savedId, setSavedId] = useState<string | null>(null);
  const ds = sets.data?.find((d) => d.key === (cfg.dataset || sets.data?.[0]?.key)); const dataset = ds?.key ?? "";
  const body = (): ReportConfig => ({ ...cfg, dataset, columns: cfg.columns?.length ? cfg.columns : undefined, groupBy: cfg.groupBy || null, pivotBy: cfg.groupBy ? cfg.pivotBy || null : null, measureField: cfg.measure === "count" ? null : cfg.measureField });
  const go = async () => { setForm(""); const r = await run(() => post<ReportResult>("/api/hr/reports/run", body()), undefined, { silent: true }); if (r.ok) setRes(r.data); else setForm(r.error); };
  const exportCsv = async () => { setForm(""); try { await downloadPost("/api/hr/reports/export", body(), `report-${dataset}.csv`); } catch (e) { setForm((e as Error).message); } };
  const save = async () => { const r = await run(() => (savedId ? api<SavedReport>(`/api/hr/reports/saved/${savedId}`, { method: "PUT", body: { name, shared, config: body() } }) : post<SavedReport>("/api/hr/reports/saved", { name, shared, config: body() })), t("Report saved."), { silent: true }); if (r.ok) { setSavedId(r.data.id); saved.reload(); } else setForm(r.error); };
  const open = (s: SavedReport) => { setCfg({ ...s.config, filters: s.config.filters ?? [], columns: s.config.columns ?? [] }); setName(s.name); setShared(s.shared); setSavedId(s.mine ? s.id : null); setRes(null); };
  const colOpts = (ds?.columns ?? []).map((c) => ({ value: c.key, label: t(c.label) }));
  const setFilter = (i: number, f: ReportFilter) => setCfg({ ...cfg, filters: (cfg.filters ?? []).map((x, j) => (j === i ? f : x)) });
  return (
    <QueryBoundary q={sets}>{(list) => list.length === 0 ? <Panel><EmptyState title={t("No data you can report on")} description={t("Reports use the same permissions as the lists they come from.")} /></Panel> : (
      <div className="report-builder">
        <aside className="stack">
          <Panel title={t("Saved reports")}>
            {(saved.data ?? []).length === 0 ? <small className="sub">{t("Build a report and save it to use it again.")}</small> : (
              <ul className="mini-list">{saved.data!.map((s) => (
                <li key={s.id}><div><button type="button" className="link-btn" onClick={() => open(s)}><strong>{s.name}</strong></button><small>{t(list.find((d) => d.key === s.dataset)?.label ?? s.dataset)}{s.shared ? ` · ${t("shared")}` : ""}{!s.mine ? ` · ${s.owner}` : ""}</small></div>
                  {s.mine && <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={async () => { if ((await run(() => del(`/api/hr/reports/saved/${s.id}`), t("Removed."))).ok) { saved.reload(); if (savedId === s.id) setSavedId(null); } }}><Trash2 size={14} /></Button>}</li>))}</ul>)}
          </Panel>
          <Panel title={t("Report")}>
            <div className="stack">
              <Field label={t("Data")} htmlFor="rb-ds"><SelectInput id="rb-ds" value={dataset} onChange={(e) => { setCfg({ dataset: e.target.value, columns: [], filters: [], groupBy: null, pivotBy: null, measure: "count", measureField: null }); setRes(null); setSavedId(null); }} options={list.map((d) => ({ value: d.key, label: t(d.label) }))} /></Field>
              <Field label={t("Group rows by")} htmlFor="rb-g"><SelectInput id="rb-g" value={cfg.groupBy ?? ""} onChange={(e) => setCfg({ ...cfg, groupBy: e.target.value || null, pivotBy: e.target.value ? cfg.pivotBy : null })} options={[{ value: "", label: t("No grouping (list rows)") }, ...colOpts]} /></Field>
              {cfg.groupBy && <Field label={t("Columns by (pivot)")} htmlFor="rb-p"><SelectInput id="rb-p" value={cfg.pivotBy ?? ""} onChange={(e) => setCfg({ ...cfg, pivotBy: e.target.value || null })} options={[{ value: "", label: t("No pivot") }, ...colOpts.filter((c) => c.value !== cfg.groupBy)]} /></Field>}
              {cfg.groupBy && <div className="fgrid">
                <Field label={t("Measure")} htmlFor="rb-m"><SelectInput id="rb-m" value={cfg.measure ?? "count"} onChange={(e) => setCfg({ ...cfg, measure: e.target.value })} options={[{ value: "count", label: t("Count") }, { value: "sum", label: t("Total") }, { value: "avg", label: t("Average") }, { value: "min", label: t("Minimum") }, { value: "max", label: t("Maximum") }]} /></Field>
                {cfg.measure !== "count" && <Field label={t("Of")} htmlFor="rb-mf"><SelectInput id="rb-mf" value={cfg.measureField ?? ""} onChange={(e) => setCfg({ ...cfg, measureField: e.target.value })} options={[{ value: "", label: t("Choose…") }, ...(ds?.columns ?? []).filter((c) => c.type === "number").map((c) => ({ value: c.key, label: t(c.label) }))]} /></Field>}
              </div>}
              {!cfg.groupBy && <fieldset className="stack"><legend>{t("Columns")}</legend><div className="col-picks">{(ds?.columns ?? []).map((c) => (
                <label key={c.key} className="check"><Checkbox checked={(cfg.columns ?? []).includes(c.key)} onChange={(e) => setCfg({ ...cfg, columns: e.target.checked ? [...(cfg.columns ?? []), c.key] : (cfg.columns ?? []).filter((x) => x !== c.key) })} /> {t(c.label)}</label>))}</div>
                <small className="sub">{t("None ticked = every column.")}</small></fieldset>}
              <fieldset className="stack"><legend>{t("Filters")}</legend>
                {(cfg.filters ?? []).map((f, i) => { const col = ds?.columns.find((c) => c.key === f.field); return (
                  <div key={i} className="filter-row">
                    <SelectInput aria-label={t("Field")} value={f.field} onChange={(e) => setFilter(i, { ...f, field: e.target.value })} options={colOpts} />
                    <SelectInput aria-label={t("Condition")} value={f.op} onChange={(e) => setFilter(i, { ...f, op: e.target.value })} options={OPS.filter((o) => o.types.includes(col?.type ?? "text")).map((o) => ({ value: o.value, label: t(o.label) }))} />
                    {f.op !== "empty" && f.op !== "notempty" && <TextInput aria-label={t("Value")} type={col?.type === "date" ? "date" : "text"} inputMode={col?.type === "number" ? "decimal" : undefined} value={f.value} onChange={(e) => setFilter(i, { ...f, value: e.target.value })} />}
                    <Button size="sm" variant="danger-outline" aria-label={t("Remove")} onClick={() => setCfg({ ...cfg, filters: (cfg.filters ?? []).filter((_, j) => j !== i) })}><Trash2 size={14} /></Button>
                  </div>); })}
                <Button size="sm" onClick={() => setCfg({ ...cfg, filters: [...(cfg.filters ?? []), { field: ds?.columns[0]?.key ?? "", op: "eq", value: "" }] })}>{t("Add filter")}</Button>
              </fieldset>
              <div className="row" style={{ gap: 8 }}><Button variant="primary" onClick={go}><Play size={14} /> {t("Run")}</Button><Button onClick={exportCsv}><Download size={14} /> {t("CSV")}</Button></div>
              <div className="row" style={{ gap: 6, alignItems: "flex-end" }}><Field label={t("Save as")} htmlFor="rb-name"><TextInput id="rb-name" value={name} onChange={(e) => setName(e.target.value)} /></Field><Button onClick={save} disabled={name.trim().length < 2}><Save size={14} /> {t("Save")}</Button></div>
              <label className="check"><Checkbox checked={shared} onChange={(e) => setShared(e.target.checked)} /> {t("Share with colleagues who can see this data")}</label>
            </div>
          </Panel>
        </aside>
        <section>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          {!res ? <Panel><EmptyState title={t("Choose the data and press Run")} description={t("List rows with the columns you need, or group them (for example headcount per department) and pivot by a second column (for example per employment type).")} /></Panel> : (
            <Panel title={t("{n} rows", { n: res.totalRows })} description={res.truncated ? t("Showing the first part; export to CSV for up to 5,000 rows.") : undefined}>
              <div className="table-wrap"><table className="table report-table">
                <thead><tr>{res.headers.map((h, i) => <th key={i} className={res.types[i] === "number" ? "num" : undefined}>{t(h)}</th>)}</tr></thead>
                <tbody>{res.rows.map((r, i) => <tr key={i} className={cfg.pivotBy && i === res.rows.length - 1 ? "total" : undefined}>{r.map((v, k) => <td key={k} className={res.types[k] === "number" ? "num" : undefined}>{v == null ? "—" : typeof v === "number" ? num(v) : String(v)}</td>)}</tr>)}</tbody>
              </table></div>
            </Panel>)}
        </section>
      </div>)}</QueryBoundary>
  );
}
