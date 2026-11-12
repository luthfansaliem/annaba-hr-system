"use client";

import { useState } from "react";
import { ChevronLeft, ChevronRight, Plus } from "lucide-react";
import { PageHeader } from "@/components/page-header";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Overlay } from "@/components/ui/overlay";
import { Field, TextInput } from "@/components/ui/field";
import { EmptyState } from "@/components/data/empty-state";
import { get, post } from "@/lib/api/client";
import type { CalendarItem } from "@/lib/api/types";
import { useAuth } from "@/lib/auth";
import { useQuery } from "@/lib/hooks";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const KIND: Record<CalendarItem["kind"], string> = { event: "Company event", holiday: "Holiday", birthday: "Birthday", anniversary: "Work anniversary", leave: "On leave (your department)" };
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

/** The company calendar: events, public holidays, birthdays and work anniversaries, and leave in the person's own department. */
export default function CalendarPage() {
  const { can } = useAuth(); const run = useRun(); const now = new Date();
  const [ym, setYm] = useState({ y: now.getFullYear(), m: now.getMonth() + 1 });
  const q = useQuery(() => get<CalendarItem[]>("/api/hr/calendar", { year: ym.y, month: ym.m }), [ym.y, ym.m]);
  const [add, setAdd] = useState(false); const [f, setF] = useState({ title: "", startDate: iso(now), endDate: "", location: "" }); const [err, setErr] = useState<Record<string, string>>({});
  const move = (d: number) => { const x = new Date(ym.y, ym.m - 1 + d, 1); setYm({ y: x.getFullYear(), m: x.getMonth() + 1 }); };
  const first = new Date(ym.y, ym.m - 1, 1); const start = new Date(first); start.setDate(1 - ((first.getDay() + 6) % 7));   // weeks start on Monday
  const days = Array.from({ length: 42 }, (_, i) => { const d = new Date(start); d.setDate(start.getDate() + i); return d; });
  const items = q.data ?? []; const today = iso(now);
  const on = (d: string) => items.filter((i) => i.date <= d && i.endDate >= d);
  const label = (i: CalendarItem) => i.kind === "birthday" ? `🎂 ${i.title}` : i.kind === "anniversary" ? t("{name}: {n} years", { name: i.title, n: i.detail ?? "" }) : i.title;
  async function save(e: React.FormEvent) {
    e.preventDefault();
    const r = await run(() => post("/api/hr/calendar/events", { title: f.title, startDate: f.startDate, endDate: f.endDate || undefined, location: f.location || undefined }), t("Event added."), { silent: true });
    if (r.ok) { setAdd(false); q.reload(); } else setErr(r.fields ?? {});
  }
  const weekdays = Array.from({ length: 7 }, (_, i) => new Date(2024, 0, 1 + i).toLocaleDateString(undefined, { weekday: "short" }));
  const unlinked = (q.error as { code?: string } | undefined)?.code === "invariant";
  return (
    <>
      <PageHeader title={t("Company Calendar")} description={t("Company events, public holidays, birthdays and work anniversaries, and who in your department is on leave.")}
        actions={<div className="row" style={{ gap: 8, alignItems: "center" }}>
          <Button size="sm" aria-label={t("Previous month")} onClick={() => move(-1)}><ChevronLeft size={16} /></Button>
          <b style={{ minWidth: 150, textAlign: "center" }}>{first.toLocaleDateString(undefined, { month: "long", year: "numeric" })}</b>
          <Button size="sm" aria-label={t("Next month")} onClick={() => move(1)}><ChevronRight size={16} /></Button>
          {can("hrconfig.manage") && <Button variant="primary" onClick={() => { setErr({}); setF({ title: "", startDate: iso(now), endDate: "", location: "" }); setAdd(true); }}><Plus size={16} /> {t("Add event")}</Button>}
        </div>} />
      <Panel>
        {unlinked ? <EmptyState title={t("Your account is not linked to an employee record")} description={t("Ask HR to link your account.")} /> : (
          <div className="cal-grid" role="grid" aria-label={t("Company calendar")}>
            {weekdays.map((w) => <div key={w} className="cal-head" role="columnheader">{w}</div>)}
            {days.map((d) => { const k = iso(d); const list = on(k); return (
              <div key={k} role="gridcell" className={`cal-day${d.getMonth() + 1 !== ym.m ? " out" : ""}${k === today ? " today" : ""}`} aria-label={`${d.toLocaleDateString(undefined, { day: "numeric", month: "long" })}${list.length ? `: ${list.map(label).join(", ")}` : ""}`}>
                <span className="num">{d.getDate()}</span>
                {list.slice(0, 4).map((i, n) => <span key={n} className={`cal-chip ${i.kind}`} title={`${t(KIND[i.kind])}: ${label(i)}${i.kind === "event" && i.detail ? ` · ${i.detail}` : ""}`}>{label(i)}</span>)}
                {list.length > 4 && <small className="sub">{t("+{n} more", { n: String(list.length - 4) })}</small>}
              </div>); })}
          </div>)}
        <div className="row" style={{ gap: 12, flexWrap: "wrap", marginTop: 12 }}>{(Object.keys(KIND) as CalendarItem["kind"][]).map((k) => <span key={k} className={`cal-chip ${k}`}>{t(KIND[k])}</span>)}</div>
      </Panel>
      <Overlay open={add} onClose={() => setAdd(false)} side="right" title={t("Add event")} footer={<><Button onClick={() => setAdd(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="ev-form">{t("Save")}</Button></>}>
        <form id="ev-form" className="stack" noValidate onSubmit={save}>
          <Field label={t("Title")} htmlFor="ev-title" required error={err.title}><TextInput id="ev-title" data-autofocus value={f.title} invalid={!!err.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder={t("Town hall")} /></Field>
          <div className="fgrid">
            <Field label={t("From")} htmlFor="ev-from" required error={err.startDate}><TextInput id="ev-from" type="date" value={f.startDate} onChange={(e) => setF({ ...f, startDate: e.target.value })} /></Field>
            <Field label={t("Until")} htmlFor="ev-to" error={err.endDate}><TextInput id="ev-to" type="date" value={f.endDate} onChange={(e) => setF({ ...f, endDate: e.target.value })} /></Field>
          </div>
          <Field label={t("Place")} htmlFor="ev-loc"><TextInput id="ev-loc" value={f.location} onChange={(e) => setF({ ...f, location: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </>
  );
}
