"use client";

import { useEffect, useState } from "react";
import { LogIn, LogOut, Tablet } from "lucide-react";
import { AuthFrame, AuthHeading } from "@/components/auth/auth-frame";
import { t } from "@/i18n";

const KEY = "annaba-kiosk-token";
type Result = { employeeName: string; action: "in" | "out"; at: string };

/**
 * The shared attendance tablet. It is registered once from Attendance Tablets: the link carries the device token after "#" (never sent to a server in the address),
 * the page keeps it on this device, and every clock-in sends it in a header. People clock in with their employee number and PIN.
 */
export default function KioskPage() {
  const [token, setToken] = useState<string | null>(null); const [device, setDevice] = useState(""); const [error, setError] = useState("");
  const [no, setNo] = useState(""); const [pin, setPin] = useState(""); const [busy, setBusy] = useState(false); const [done, setDone] = useState<Result | null>(null);
  useEffect(() => {
    const fromLink = window.location.hash.slice(1);
    if (fromLink) { localStorage.setItem(KEY, fromLink); history.replaceState(null, "", window.location.pathname); }
    const tok = fromLink || localStorage.getItem(KEY); setToken(tok);
    if (tok) fetch("/api/kiosk/device", { headers: { "X-Kiosk-Token": tok }, cache: "no-store" }).then(async (r) => { if (r.ok) setDevice((await r.json()).name); else setError(t("This tablet is not registered. Ask HR for a new link.")); }).catch(() => setError(t("Cannot reach the server.")));
  }, []);
  useEffect(() => { if (!done) return; const i = setTimeout(() => setDone(null), 5000); return () => clearTimeout(i); }, [done]);
  async function clock(action: "in" | "out") {
    if (!token) return; setBusy(true); setError("");
    try {
      const r = await fetch("/api/kiosk/clock", { method: "POST", headers: { "Content-Type": "application/json", "X-Kiosk-Token": token }, body: JSON.stringify({ employeeNo: no.trim(), pin, action }), cache: "no-store" });
      const body = await r.json().catch(() => null);
      if (r.ok) { setDone(body as Result); setNo(""); setPin(""); }
      else setError(r.status === 429 ? t("Too many wrong tries. Wait a few minutes and try again.") : body?.error?.fields?.pin ?? body?.error?.message ?? t("Something went wrong."));
    } catch { setError(t("Cannot reach the server.")); } finally { setBusy(false); }
  }
  const key = (d: string) => setPin((p) => (d === "⌫" ? p.slice(0, -1) : p.length < 8 ? p + d : p));
  return (
    <AuthFrame>
      <AuthHeading icon={<Tablet size={26} />} title={device || t("Attendance tablet")} sub={t("Enter your employee number and PIN, then clock in or out.")} />
      {!token ? <div className="form-alert err" role="alert">{t("This tablet is not registered. Ask HR for a new link.")}</div> : (
        <div className="stack">
          {done && <div className="kiosk-result" role="status">{done.action === "in" ? t("Welcome, {name}. Clocked in at {time}.", { name: done.employeeName, time: new Date(done.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false }) }) : t("Goodbye, {name}. Clocked out at {time}.", { name: done.employeeName, time: new Date(done.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false }) })}</div>}
          {error && <div className="form-alert err" role="alert">{error}</div>}
          <label className="auth-label" htmlFor="kno">{t("Employee number")}</label>
          <input id="kno" className="input" autoComplete="off" value={no} onChange={(e) => setNo(e.target.value.toUpperCase())} />
          <label className="auth-label" htmlFor="kpin">{t("PIN")}</label>
          <input id="kpin" className="input" type="password" inputMode="numeric" autoComplete="off" value={pin} onChange={(e) => setPin(e.target.value.replace(/\D/g, "").slice(0, 8))} />
          <div className="kiosk-pad" aria-label={t("Number pad")}>{["1", "2", "3", "4", "5", "6", "7", "8", "9", "", "0", "⌫"].map((d, i) => d ? <button key={i} type="button" onClick={() => key(d)} aria-label={d === "⌫" ? t("Delete") : d}>{d}</button> : <span key={i} />)}</div>
          <div className="row" style={{ gap: 10 }}>
            <button className="signin" type="button" disabled={busy || !no || pin.length < 4} onClick={() => clock("in")}><LogIn size={18} /> {t("Clock in")}</button>
            <button className="signin" type="button" style={{ background: "var(--navy)" }} disabled={busy || !no || pin.length < 4} onClick={() => clock("out")}><LogOut size={18} /> {t("Clock out")}</button>
          </div>
        </div>)}
    </AuthFrame>
  );
}
