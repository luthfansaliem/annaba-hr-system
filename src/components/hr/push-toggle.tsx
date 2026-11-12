"use client";

import { useEffect, useState } from "react";
import { BellRing } from "lucide-react";
import { Panel } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { get, post, put } from "@/lib/api/client";
import { useRun } from "@/lib/use-run";
import { t } from "@/i18n";

const toKey = (b64: string) => { const s = b64.replace(/-/g, "+").replace(/_/g, "/"); const raw = atob(s + "=".repeat((4 - (s.length % 4)) % 4)); return Uint8Array.from(raw, (c) => c.charCodeAt(0)); };
const b64url = (buf: ArrayBuffer | null) => (buf ? btoa(String.fromCharCode(...new Uint8Array(buf))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "") : "");

/** Turns push notifications on or off for this device (installed app or browser). Needs the service worker, so it only works in the deployed app, not in development. */
export function PushToggle() {
  const run = useRun(); const [state, setState] = useState<"unsupported" | "off" | "on" | "blocked" | "loading">("loading");
  useEffect(() => {
    (async () => {
      if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window)) { setState("unsupported"); return; }
      if (Notification.permission === "denied") { setState("blocked"); return; }
      const reg = await navigator.serviceWorker.getRegistration(); const sub = await reg?.pushManager.getSubscription();
      setState(sub ? "on" : reg ? "off" : "unsupported");
    })().catch(() => setState("unsupported"));
  }, []);
  async function turnOn() {
    const r = await run(async () => {
      const perm = await Notification.requestPermission(); if (perm !== "granted") throw new Error(t("Notifications are blocked for this site. Allow them in the browser settings."));
      const reg = await navigator.serviceWorker.ready; const { publicKey } = await get<{ publicKey: string }>("/api/me/push/key");
      const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: toKey(publicKey) });
      await post("/api/me/push/subscribe", { endpoint: sub.endpoint, p256dh: b64url(sub.getKey("p256dh")), auth: b64url(sub.getKey("auth")), device: navigator.userAgent.slice(0, 200) });
    }, t("Push notifications are on for this device."));
    setState(r.ok ? "on" : Notification.permission === "denied" ? "blocked" : "off");
  }
  async function turnOff() {
    const r = await run(async () => { const reg = await navigator.serviceWorker.getRegistration(); const sub = await reg?.pushManager.getSubscription(); if (sub) { await post("/api/me/push/unsubscribe", { endpoint: sub.endpoint }); await sub.unsubscribe(); } }, t("Push notifications are off for this device."));
    if (r.ok) setState("off");
  }
  if (state === "loading") return null;
  return (
    <Panel title={t("Push notifications on this device")} description={state === "unsupported" ? t("This browser cannot receive push notifications here. Install the app on your phone (Add to Home Screen) and open it from there.") : state === "blocked" ? t("Notifications are blocked for this site. Allow them in the browser settings, then come back.") : t("Get approvals, decisions and payslip news on this phone or computer even when the app is closed.")}
      actions={state === "on" ? <Button onClick={turnOff}>{t("Turn off")}</Button> : state === "off" ? <Button variant="primary" onClick={turnOn}><BellRing size={16} /> {t("Turn on")}</Button> : undefined}>
      <span />
    </Panel>
  );
}

/** The employee's own lock: ask for the password before payslips are shown. */
export function PayslipLockToggle() {
  const run = useRun(); const [locked, setLocked] = useState<boolean | null>(null);
  useEffect(() => { get<{ locked: boolean }>("/api/hr/payroll/payslips/lock").then((r) => setLocked(r.locked)).catch(() => setLocked(null)); }, []);
  if (locked === null) return null;
  return (
    <Panel title={t("Payslip lock")} description={locked ? t("Your password is asked before payslips are shown.") : t("Ask for my password before showing payslips, for example on a shared or family device.")}
      actions={<Button onClick={async () => { const r = await run(() => put<{ locked: boolean }>("/api/hr/payroll/payslips/lock", { locked: !locked }), !locked ? t("Payslips are locked.") : t("Payslip lock is off.")); if (r.ok) setLocked(r.data.locked); }}>{locked ? t("Turn off") : t("Turn on")}</Button>}>
      <span />
    </Panel>
  );
}
