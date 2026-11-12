"use client";

import { useState } from "react";
import { Coffee, KeyRound, MapPin, Plus } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Overlay } from "@/components/ui/overlay";
import { Checkbox, Field, TextInput } from "@/components/ui/field";
import { DataTable, type Column } from "@/components/data/data-table";
import { EmptyState } from "@/components/data/empty-state";
import { get, post, put } from "@/lib/api/client";
import type { FieldVisitItem, HourlyLeaveItem, RosterRow } from "@/lib/api/types";
import { useQuery } from "@/lib/hooks";
import { fmtDate } from "@/lib/format";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const time = (iso: string) => new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false });
export const isoDay = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

/** Start or end a break in the open session. */
export function BreakButton({ onBreakSince, onDone }: { onBreakSince: string | null | undefined; onDone: () => void }) {
  const run = useRun();
  return onBreakSince
    ? <Button onClick={async () => { if ((await run(() => post("/api/hr/attendance/break-end"), t("Welcome back."))).ok) onDone(); }}><Coffee size={16} /> {t("End break (since {time})", { time: time(onBreakSince) })}</Button>
    : <Button onClick={async () => { if ((await run(() => post("/api/hr/attendance/break-start"), t("Enjoy your break."))).ok) onDone(); }}><Coffee size={16} /> {t("Start break")}</Button>;
}

const STATUS: Record<string, ["orange" | "green" | "red" | "gray", string]> = { pending: ["orange", "Pending"], approved: ["green", "Approved"], rejected: ["red", "Rejected"], cancelled: ["gray", "Cancelled"] };

/** Leave by the hour: "mine" asks and cancels, "team" decides. */
export function HourlyLeavePanel({ scope }: { scope: "mine" | "team" | "all" }) {
  const run = useRun(); const q = useQuery(() => get<HourlyLeaveItem[]>("/api/hr/attendance/hourly-leave", { scope }), [scope]);
  const [ask, setAsk] = useState(false); const [f, setF] = useState({ date: isoDay(new Date()), from: "", to: "", paid: true, reason: "" }); const [err, setErr] = useState<Record<string, string>>({}); const [form, setForm] = useState("");
  const [reject, setReject] = useState<HourlyLeaveItem | null>(null); const [note, setNote] = useState("");
  async function send(e: React.FormEvent) { e.preventDefault(); setForm(""); const r = await run(() => post("/api/hr/attendance/hourly-leave", f), t("Request sent."), { silent: true }); if (r.ok) { setAsk(false); q.reload(); } else { setErr(r.fields ?? {}); setForm(r.error); } }
  const decide = async (h: HourlyLeaveItem, approve: boolean, why?: string) => { const r = await run(() => post(`/api/hr/attendance/hourly-leave/${h.id}/decide`, { approve, note: why }), approve ? t("Approved.") : t("Request rejected."), { silent: !approve }); if (r.ok) { setReject(null); setNote(""); q.reload(); } else setErr(r.fields ?? {}); };
  const cols: Column<HourlyLeaveItem>[] = [
    ...(scope !== "mine" ? [{ key: "e", header: t("Employee"), cell: (h: HourlyLeaveItem) => <><b>{h.employeeName}</b><small className="sub">{h.employeeNo}</small></> }] : []),
    { key: "d", header: t("When"), cell: (h) => <><b>{fmtDate(h.date)}</b><small className="sub">{h.from}–{h.to} · {t("{n} h", { n: String(h.hours) })} · {h.paid ? t("paid") : t("unpaid")}</small></> },
    { key: "r", header: t("Reason"), className: "hide-sm", cell: (h) => h.reason },
    { key: "s", header: t("Status"), cell: (h) => <><Badge tone={STATUS[h.status][0]}>{t(STATUS[h.status][1])}</Badge>{h.decisionNote && <small className="sub">{h.decisionNote}</small>}</> },
    { key: "x", header: "", cell: (h) => <div className="row" style={{ gap: 6, justifyContent: "flex-end" }}>
      {h.canDecide && <Button size="sm" variant="primary" onClick={() => decide(h, true)}>{t("Approve")}</Button>}
      {h.canDecide && <Button size="sm" variant="danger-outline" onClick={() => { setNote(""); setErr({}); setReject(h); }}>{t("Reject")}</Button>}
      {h.canCancel && <Button size="sm" onClick={async () => { if ((await run(() => post(`/api/hr/attendance/hourly-leave/${h.id}/cancel`), t("Request cancelled."))).ok) q.reload(); }}>{t("Cancel")}</Button>}
    </div> },
  ];
  const rows = q.data ?? [];
  return (
    <>
      <Panel title={t("Leave by the hour")} description={scope === "mine" ? t("Away for a few hours with permission: an errand, a doctor's visit, leaving early. Unpaid hours are deducted from that month's pay.") : undefined}
        actions={scope === "mine" ? <Button size="sm" onClick={() => { setErr({}); setForm(""); setF({ date: isoDay(new Date()), from: "", to: "", paid: true, reason: "" }); setAsk(true); }}><Plus size={14} /> {t("Ask for hours")}</Button> : undefined}>
        {!q.loading && !q.error && rows.length === 0 ? <EmptyState title={t("No requests")} description={scope === "mine" ? t("Hours you ask for appear here.") : t("Requests from your team appear here.")} />
          : <DataTable label={t("Leave by the hour")} columns={cols} rows={rows} rowKey={(h) => h.id} loading={q.data === undefined && q.loading} error={(q.error as { code?: string } | undefined)?.code === "invariant" ? null : q.error?.message ?? null} onRetry={q.reload} />}
      </Panel>
      <Overlay open={ask} onClose={() => setAsk(false)} side="right" title={t("Ask for hours")} footer={<><Button onClick={() => setAsk(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="hl-form">{t("Send")}</Button></>}>
        <form id="hl-form" className="stack" noValidate onSubmit={send}>
          {form && <div className="form-alert err" role="alert">{form}</div>}
          <div className="fgrid">
            <Field label={t("Day")} htmlFor="hl-d" required error={err.date}><TextInput id="hl-d" type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} /></Field>
            <Field label={t("From")} htmlFor="hl-f" required error={err.from}><TextInput id="hl-f" type="time" data-autofocus value={f.from} onChange={(e) => setF({ ...f, from: e.target.value })} /></Field>
            <Field label={t("Until")} htmlFor="hl-t" required error={err.to}><TextInput id="hl-t" type="time" value={f.to} onChange={(e) => setF({ ...f, to: e.target.value })} /></Field>
          </div>
          <Field label={t("Reason")} htmlFor="hl-r" required error={err.reason}><TextInput id="hl-r" value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} /></Field>
          <label className="row" style={{ gap: 8 }}><Checkbox checked={f.paid} onChange={(e) => setF({ ...f, paid: e.target.checked })} /> {t("Paid (untick if the hours should be deducted from pay)")}</label>
        </form>
      </Overlay>
      <Overlay open={!!reject} onClose={() => setReject(null)} title={t("Reject request")} footer={<><Button onClick={() => setReject(null)}>{t("Cancel")}</Button><Button variant="danger" onClick={() => reject && decide(reject, false, note)}>{t("Reject")}</Button></>}>
        <Field label={t("Reason (the employee will see it)")} htmlFor="hl-note" required error={err.note}><TextInput id="hl-note" data-autofocus value={note} onChange={(e) => setNote(e.target.value)} /></Field>
      </Overlay>
    </>
  );
}

/** The employee's own planned days for the next two weeks. */
export function MyRosterPanel() {
  const today = new Date(); const end = new Date(); end.setDate(end.getDate() + 13);
  const q = useQuery(() => get<RosterRow[]>("/api/hr/attendance/roster/mine", { from: isoDay(today), to: isoDay(end) }), []);
  const days = q.data?.[0]?.days ?? [];
  if (!days.some((d) => d.planned)) return null;   // nothing planned: the standing shift applies, nothing to show
  return (
    <Panel title={t("My schedule")} description={t("Your planned shifts and days off for the next two weeks.")}>
      <div className="roster-strip">{days.map((d) => (
        <div key={d.date} className={`roster-day${d.works ? "" : " off"}`}><small>{new Date(d.date).toLocaleDateString(undefined, { weekday: "short", day: "numeric" })}</small><b>{d.works ? d.shiftCode ?? t("Work") : t("Day off")}</b></div>))}</div>
    </Panel>
  );
}

/** Set or change the PIN used at a shared attendance tablet. */
export function KioskPinPanel() {
  const run = useRun(); const q = useQuery(() => get<{ hasPin: boolean }>("/api/hr/attendance/kiosk-pin"), []); const [open, setOpen] = useState(false); const [pin, setPin] = useState(""); const [err, setErr] = useState("");
  if (q.data === undefined) return null;
  return (
    <>
      <Panel title={t("Attendance tablet PIN")} description={q.data.hasPin ? t("You can clock in at the office tablet with your employee number and PIN.") : t("Set a PIN to clock in at the office tablet with your employee number.")}
        actions={<Button size="sm" onClick={() => { setPin(""); setErr(""); setOpen(true); }}><KeyRound size={14} /> {q.data.hasPin ? t("Change PIN") : t("Set PIN")}</Button>}><span /></Panel>
      <Overlay open={open} onClose={() => setOpen(false)} title={t("Tablet PIN")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="pin-form">{t("Save")}</Button></>}>
        <form id="pin-form" className="stack" noValidate onSubmit={async (e) => { e.preventDefault(); const r = await run(() => put("/api/hr/attendance/kiosk-pin", { pin }), t("PIN saved."), { silent: true }); if (r.ok) { setOpen(false); q.reload(); } else setErr(r.fields?.pin ?? r.error); }}>
          <Field label={t("PIN (4–8 digits)")} htmlFor="pin" error={err}><TextInput id="pin" type="password" inputMode="numeric" autoComplete="new-password" data-autofocus value={pin} onChange={(e) => setPin(e.target.value.replace(/\D/g, ""))} /></Field>
        </form>
      </Overlay>
    </>
  );
}

/** Field visits: check in on arrival and on leaving, with the phone's position. */
export function FieldVisitPanel() {
  const run = useRun(); const today = isoDay(new Date()); const q = useQuery(() => get<FieldVisitItem[]>("/api/hr/attendance/visits", { date: today }), []);
  const [open, setOpen] = useState(false); const [f, setF] = useState({ kind: "arrive", place: "", note: "" }); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  async function send(e: React.FormEvent) {
    e.preventDefault(); setErr(""); setBusy(true);
    const pos = await new Promise<GeolocationPosition | null>((res) => ("geolocation" in navigator ? navigator.geolocation.getCurrentPosition(res, () => res(null), { enableHighAccuracy: true, timeout: 10000 }) : res(null)));
    setBusy(false);
    if (!pos) { setErr(t("Allow location access to check in at a visit.")); return; }
    const r = await run(() => post("/api/hr/attendance/visits", { ...f, latitude: pos.coords.latitude, longitude: pos.coords.longitude, accuracy: pos.coords.accuracy }), t("Visit recorded."), { silent: true });
    if (r.ok) { setOpen(false); q.reload(); } else setErr(r.fields?.place ?? r.error);
  }
  const rows = q.data ?? [];
  return (
    <>
      <Panel title={t("Field visits today")} description={t("For work outside the office: check in when you arrive at a client and when you leave. Your position is recorded only at these moments.")}
        actions={<Button size="sm" onClick={() => { setErr(""); setF({ kind: rows.length && rows[rows.length - 1].kind === "arrive" ? "leave" : "arrive", place: rows.length && rows[rows.length - 1].kind === "arrive" ? rows[rows.length - 1].place : "", note: "" }); setOpen(true); }}><MapPin size={14} /> {t("Check in at a visit")}</Button>}>
        {rows.length === 0 ? <p className="sub">{t("No visits recorded today.")}</p>
          : <ul className="mini-list">{rows.map((v) => <li key={v.id}><div><strong>{time(v.at)} · {v.kind === "arrive" ? t("Arrived at {place}", { place: v.place }) : t("Left {place}", { place: v.place })}</strong>{v.note && <small>{v.note}</small>}</div></li>)}</ul>}
      </Panel>
      <Overlay open={open} onClose={() => setOpen(false)} side="right" title={t("Check in at a visit")} footer={<><Button onClick={() => setOpen(false)}>{t("Cancel")}</Button><Button variant="primary" type="submit" form="fv-form" disabled={busy}>{busy ? t("Finding your location…") : t("Check in")}</Button></>}>
        <form id="fv-form" className="stack" noValidate onSubmit={send}>
          {err && <div className="form-alert err" role="alert">{err}</div>}
          <div className="row" style={{ gap: 12 }}>
            <label className="row" style={{ gap: 6 }}><input type="radio" name="fv-kind" checked={f.kind === "arrive"} onChange={() => setF({ ...f, kind: "arrive" })} /> {t("Arriving")}</label>
            <label className="row" style={{ gap: 6 }}><input type="radio" name="fv-kind" checked={f.kind === "leave"} onChange={() => setF({ ...f, kind: "leave" })} /> {t("Leaving")}</label>
          </div>
          <Field label={t("Place or client")} htmlFor="fv-place" required><TextInput id="fv-place" data-autofocus value={f.place} onChange={(e) => setF({ ...f, place: e.target.value })} placeholder={t("PT Maju Jaya, Surabaya")} /></Field>
          <Field label={t("Note")} htmlFor="fv-note"><TextInput id="fv-note" value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder={t("Optional")} /></Field>
        </form>
      </Overlay>
    </>
  );
}
