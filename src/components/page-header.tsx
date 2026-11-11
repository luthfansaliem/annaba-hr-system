"use client";

import { usePathname } from "next/navigation";
import { activeTrail, nav } from "@/config/nav";
import { activeHref } from "@/components/app-shell";
import { t } from "@/i18n";

/** Page title row. The small line above the title names the module the page belongs to (taken from the menu), so every screen says where it is. */
export function PageHeader({ title, description, actions }: { title: string; description?: string; actions?: React.ReactNode }) {
  const trail = activeTrail(nav, activeHref(usePathname() ?? "")) ?? [];
  return (
    <div className="crud-top">
      <div>{trail.length > 0 && <p className="eyebrow">{trail.map((x) => t(x)).join(" · ")}</p>}<h1>{title}</h1>{description && <p>{description}</p>}</div>
      {actions}
    </div>
  );
}
