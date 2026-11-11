"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Bell } from "lucide-react";
import { get, post } from "@/lib/api/client";
import type { NotificationList } from "@/lib/api/types";
import { rel } from "@/lib/format";
import { t } from "@/i18n";

/** The bell in the top bar: messages for you (leave decided, payslip ready, something to approve). Checks once a minute while the tab is visible and again when opened. */
export function NotificationBell() {
  const router = useRouter(); const [data, setData] = useState<NotificationList | null>(null); const [open, setOpen] = useState(false); const ref = useRef<HTMLDivElement>(null);
  const load = useCallback(async () => { try { setData(await get<NotificationList>("/api/hr/me/notifications")); } catch { /* the bell is a convenience: stay quiet when the API is unreachable */ } }, []);
  useEffect(() => {
    void load(); const timer = setInterval(() => { if (document.visibilityState === "visible") void load(); }, 60_000);
    return () => clearInterval(timer);
  }, [load]);
  useEffect(() => {
    if (!open) return; void load();
    const click = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    const key = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", click); document.addEventListener("keydown", key);
    return () => { document.removeEventListener("mousedown", click); document.removeEventListener("keydown", key); };
  }, [open, load]);
  const unread = data?.unread ?? 0;
  async function openItem(id: string, link: string | null, wasRead: boolean) {
    setOpen(false); if (!wasRead) { try { await post(`/api/hr/me/notifications/${id}/read`); } catch { /* the message still opens */ } void load(); }
    if (link) router.push(link);
  }
  async function readAll() { try { await post("/api/hr/me/notifications/read-all"); } finally { void load(); } }
  return (
    <div className="user-menu" ref={ref}>
      <button type="button" className="bell icon-btn" aria-haspopup="true" aria-expanded={open} aria-label={unread > 0 ? t("Notifications ({n} unread)", { n: unread }) : t("Notifications")} onClick={() => setOpen(!open)}>
        <Bell size={20} />{unread > 0 && <span className="bell-count" data-count={unread > 99 ? "99+" : unread} aria-hidden="true" />}
      </button>
      {open && (
        <div className="menu notif-menu" role="region" aria-label={t("Notifications")}>
          <div className="notif-head"><strong>{t("Notifications")}</strong>{unread > 0 && <button type="button" onClick={readAll}>{t("Mark all read")}</button>}</div>
          {!data || data.items.length === 0 ? <p className="notif-empty">{t("You are all caught up.")}</p> : (
            <ul>{data.items.map((n) => (
              <li key={n.id}><button type="button" className={n.read ? "" : "unread"} onClick={() => openItem(n.id, n.link, n.read)}>
                <span className="notif-title">{!n.read && <i aria-hidden="true" />}{n.title}</span>{n.body && <small>{n.body}</small>}<small>{rel(n.createdAt)}</small>
              </button></li>))}</ul>)}
          <div className="notif-foot"><Link href="/hr/notification-settings" onClick={() => setOpen(false)}>{t("Preferences")}</Link></div>
        </div>)}
    </div>
  );
}
