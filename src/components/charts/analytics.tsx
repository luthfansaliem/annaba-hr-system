import Link from "next/link";
import { cn } from "@/lib/cn";
import { Card } from "@/components/ui/card";
import { formatLocale, t } from "@/i18n";

/** Building blocks of the analytics screens: KPI tiles, ranked bars and month/day columns. Every chart carries its numbers as text or as a table for screen readers; colour is never the only signal. */

export type Tone = "blue" | "green" | "orange" | "red" | "teal" | "purple";
const ink: Record<Tone, string> = { blue: "var(--blue)", green: "var(--green)", orange: "var(--orange)", red: "var(--red)", teal: "var(--teal)", purple: "var(--purple)" };

export const compact = (n: number) => new Intl.NumberFormat(formatLocale(), { notation: "compact", maximumFractionDigits: 1 }).format(n);
export const num = (n: number) => new Intl.NumberFormat(formatLocale(), { maximumFractionDigits: 1 }).format(n);
/** "2026-11" → "Nov" (with the year when it is January, so a 12-month axis shows where the year turns). */
export const monthShort = (ym: string) => {
  const [y, m] = ym.split("-").map(Number); const d = new Date(y, m - 1, 1);
  return d.toLocaleDateString(formatLocale(), m === 1 ? { month: "short", year: "2-digit" } : { month: "short" });
};

export function Kpi({ icon, label, value, hint, tone = "blue", href }: { icon: React.ReactNode; label: string; value: React.ReactNode; hint?: React.ReactNode; tone?: Tone; href?: string }) {
  const body = (
    <>
      <span className={cn("kpi-ic", tone)} aria-hidden>{icon}</span>
      <div className="kpi-body"><span className="kpi-label">{label}</span><span className="kpi-value">{value}</span>{hint && <span className="kpi-hint">{hint}</span>}</div>
    </>
  );
  return href ? <Link href={href} className="card kpi link">{body}</Link> : <Card className="kpi">{body}</Card>;
}
export const KpiRow = ({ children, label }: { children: React.ReactNode; label?: string }) => <section className="kpis" aria-label={label ?? t("Key figures")}>{children}</section>;

export function ChartPanel({ title, subtitle, icon, trailing, wide, children }: { title: string; subtitle?: string; icon?: React.ReactNode; trailing?: React.ReactNode; wide?: boolean; children: React.ReactNode }) {
  return (
    <Card className={cn("panel chart-panel", wide && "wide")}>
      <div className="ph">{icon && <span className="pic">{icon}</span>}<div><h2>{title}</h2>{subtitle && <p>{subtitle}</p>}</div>{trailing}</div>
      {children}
    </Card>
  );
}
export const ChartGrid = ({ children }: { children: React.ReactNode }) => <div className="chart-grid">{children}</div>;
export const NoData = ({ children }: { children?: React.ReactNode }) => <p className="chart-empty">{children ?? t("No data yet.")}</p>;

export type BarRow = { label: string; value: number; note?: string };
/** Ranked horizontal bars with the value printed next to each bar. */
export function BarList({ rows, format = num, tone = "blue", label }: { rows: BarRow[]; format?: (n: number) => string; tone?: Tone; label: string }) {
  if (rows.length === 0 || rows.every((r) => r.value === 0)) return <NoData />;
  const max = Math.max(...rows.map((r) => r.value), 1);
  return (
    <ul className="barlist" aria-label={label}>
      {rows.map((r) => (
        <li key={r.label}>
          <span className="bl-label" title={r.label}>{r.label}</span>
          <span className="bl-track" aria-hidden><i style={{ width: `${Math.max(2, (r.value / max) * 100)}%`, background: ink[tone] }} /></span>
          <b className="bl-value">{format(r.value)}{r.note && <small>{r.note}</small>}</b>
        </li>
      ))}
    </ul>
  );
}

export type ColumnPoint = { label: string; value: number; part?: number; title?: string };
/**
 * Columns over time. `part` (optional) is drawn as a second colour inside the column (late arrivals inside everyone present, for example).
 * Labels are thinned automatically; exact figures are in the table that follows for screen readers.
 */
export function ColumnChart({ points, format = num, tone = "blue", partTone = "orange", label, valueName, partName, height = 190 }: {
  points: ColumnPoint[]; format?: (n: number) => string; tone?: Tone; partTone?: Tone; label: string; valueName: string; partName?: string; height?: number;
}) {
  if (points.length === 0 || points.every((p) => p.value === 0)) return <NoData />;
  const W = 640, H = height, L = 44, B = 22, T = 14;
  const max = niceMax(Math.max(...points.map((p) => p.value), 1));
  const slot = (W - L - 6) / points.length; const bw = Math.min(34, slot * 0.64);
  const y = (v: number) => T + (H - T - B) * (1 - v / max);
  const every = Math.ceil(points.length / 12); const showValues = points.length <= 12;
  const ticks = [0, max / 2, max];
  return (
    <figure className="colchart">
      <svg viewBox={`0 0 ${W} ${H}`} width="100%" role="img" aria-label={label}>
        {ticks.map((v) => <g key={v}><line x1={L} x2={W - 4} y1={y(v)} y2={y(v)} className="grid" /><text x={L - 6} y={y(v) + 3.5} textAnchor="end" className="axis">{format(v)}</text></g>)}
        {points.map((p, i) => {
          const x = L + i * slot + (slot - bw) / 2; const h = Math.max(p.value > 0 ? 2 : 0, y(0) - y(p.value)); const ph = p.part ? Math.max(2, y(0) - y(p.part)) : 0;
          return (
            <g key={p.label + i}>
              <title>{`${p.title ?? p.label}: ${format(p.value)}${partName && p.part !== undefined ? ` · ${partName} ${format(p.part)}` : ""}`}</title>
              <rect x={x} y={y(0) - h} width={bw} height={h} rx={Math.min(4, bw / 3)} style={{ fill: ink[tone] }} />
              {ph > 0 && <rect x={x} y={y(0) - ph} width={bw} height={ph} rx={Math.min(4, bw / 3)} style={{ fill: ink[partTone] }} />}
              {showValues && p.value > 0 && <text x={x + bw / 2} y={y(0) - h - 4} textAnchor="middle" className="val">{format(p.value)}</text>}
              {i % every === 0 && <text x={x + bw / 2} y={H - 6} textAnchor="middle" className="axis">{p.label}</text>}
            </g>
          );
        })}
      </svg>
      {partName && <figcaption className="chart-legend"><span><i style={{ background: ink[tone] }} />{valueName}</span><span><i style={{ background: ink[partTone] }} />{partName}</span></figcaption>}
      <table className="sr-only"><caption>{label}</caption><thead><tr><th>{t("Period")}</th><th>{valueName}</th>{partName && <th>{partName}</th>}</tr></thead>
        <tbody>{points.map((p, i) => <tr key={p.label + i}><td>{p.title ?? p.label}</td><td>{format(p.value)}</td>{partName && <td>{format(p.part ?? 0)}</td>}</tr>)}</tbody></table>
    </figure>
  );
}
/** The axis ends on a round number whose half is round too: 2, 4, 10, 20, 40, 100… */
function niceMax(raw: number) { if (raw <= 2) return 2; const pow = Math.pow(10, Math.floor(Math.log10(raw))); return [1, 2, 4, 10].map((f) => f * pow).find((v) => v >= raw) ?? 10 * pow; }

export type Share = { label: string; value: number; tone: Tone };
/** Parts of a whole as one stacked bar with a legend that prints value and percentage (easier to read than a ring when shares are close). */
export function ShareBar({ parts, format = num, label }: { parts: Share[]; format?: (n: number) => string; label: string }) {
  const total = parts.reduce((a, p) => a + p.value, 0);
  if (total === 0) return <NoData />;
  return (
    <div className="sharebar">
      <div className="sb-track" role="img" aria-label={label}>{parts.filter((p) => p.value > 0).map((p) => <i key={p.label} style={{ width: `${(p.value / total) * 100}%`, background: ink[p.tone] }} title={`${p.label}: ${format(p.value)}`} />)}</div>
      <ul className="sb-legend">{parts.map((p) => <li key={p.label}><i style={{ background: ink[p.tone] }} aria-hidden /><span>{p.label}</span><b>{format(p.value)}</b><small>{Math.round((p.value / total) * 100)}%</small></li>)}</ul>
    </div>
  );
}
