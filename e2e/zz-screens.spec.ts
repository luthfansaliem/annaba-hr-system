import { execSync } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { test, type Page } from "@playwright/test";
import { PASSWORD, WEB, authFile } from "./helpers";

/**
 * Not a test: a screen review aid. Photographs every screen as the administrator and as an ordinary employee and lays the
 * pictures out on contact sheets. Only runs when SCREENS_DIR is set:
 *   SCREENS_DIR=/tmp/screens npx playwright test e2e/zz-screens.spec.ts
 * It first fills the throw-away e2e database with a sample organisation (e2e/scripts/seed-screens.sql) so that lists and charts have something to show.
 */
const OUT = process.env.SCREENS_DIR ?? "";
const ADMIN = [
  "/dashboard", "/hr/analytics/people", "/hr/analytics/attendance", "/hr/analytics/leave", "/hr/analytics/payroll", "/hr/analytics/claims", "/admin/overview", "/hr/home", "/hr/approvals", "/hr/team", "/hr/employees", "/hr/departments", "/hr/org-chart", "/hr/change-requests",
  "/hr/approval-flows", "/hr/companies", "/hr/webhooks", "/hr/readiness", "/hr/leave-approvals", "/hr/leave-all", "/hr/leave-calendar", "/hr/leave-setup",
  "/hr/time-approvals", "/hr/delegations", "/hr/shifts", "/hr/work-locations", "/hr/attendance-report", "/hr/payroll", "/hr/weekly-payouts", "/hr/compensation",
  "/hr/recurring-pay", "/hr/loans", "/hr/claims", "/hr/payroll-rules", "/hr/announcements", "/hr/my-profile", "/hr/my-leave", "/hr/my-attendance", "/hr/my-payslips",
  "/hr/my-claims", "/hr/my-letters", "/hr/notification-settings", "/users", "/users/all", "/users/invitations", "/roles", "/permissions", "/access-matrix",
  "/access-requests", "/access-reviews", "/account-closures", "/sessions", "/provisioning", "/audit", "/security-events", "/profile", "/settings",
  "/admin/settings", "/admin/security-policy", "/admin/email", "/admin/tenants",
];
const MEMBER = ["/dashboard", "/hr/home", "/hr/my-profile", "/hr/my-leave", "/hr/my-attendance", "/hr/my-payslips", "/hr/my-claims", "/hr/my-letters", "/hr/announcements"];
const name = (prefix: string, i: number, path: string) => `${prefix}-${String(i).padStart(2, "0")}${path.replace(/\//g, "_")}.png`;

async function shoot(page: Page, path: string, file: string) {
  await page.goto(path); await page.waitForLoadState("networkidle").catch(() => {}); await page.waitForTimeout(350);
  await page.screenshot({ path: resolve(OUT, file) });
}
async function sheets(page: Page, files: string[], prefix: string) {
  for (let i = 0; i < files.length; i += 6) {
    const cells = files.slice(i, i + 6).map((f) => `<figure><img src="file://${resolve(OUT, f)}"><figcaption>${f}</figcaption></figure>`).join("");
    const html = resolve(OUT, `_${prefix}-sheet-${String(i / 6 + 1).padStart(2, "0")}.html`);
    writeFileSync(html, `<style>body{margin:0;background:#888;display:grid;grid-template-columns:1fr 1fr;gap:6px;padding:6px;font:12px sans-serif}figure{margin:0;background:#fff}img{width:100%;display:block}figcaption{padding:2px 6px}</style>${cells}`);
    await page.setViewportSize({ width: 1500, height: 1500 }); await page.goto(`file://${html}`); await page.waitForLoadState("load");
    await page.screenshot({ path: html.replace(/\.html$/, ".png"), fullPage: true });
  }
}

test.describe("screen review", () => {
  test.skip(!OUT, "set SCREENS_DIR to photograph every screen");
  test.setTimeout(600_000);

  test("administrator screens", async ({ browser }) => {
    mkdirSync(OUT, { recursive: true });
    const ctx = await browser.newContext({ storageState: authFile("admin"), viewport: { width: 1366, height: 820 }, locale: process.env.SCREENS_LOCALE ?? "en-GB" }); const page = await ctx.newPage();
    await ctx.request.get("/api/hr/leave/types"); // creates the default leave types the sample data refers to
    const csrf = (await (await ctx.request.get("/api/auth/csrf")).json()).token as string;
    await ctx.request.post("/api/auth/reauth", { data: { password: PASSWORD }, headers: { "X-CSRF-Token": csrf, Origin: WEB } }); // payroll figures need a recent password check
    console.log(execSync(`docker exec -i ${process.env.E2E_DB_CONTAINER ?? "annaba-digital-dev-db"} psql -U annaba_digital -d annaba_digital_e2e -v ON_ERROR_STOP=1 -q -At < e2e/scripts/seed-screens.sql`, { encoding: "utf8" }));
    const files: string[] = [];
    for (const [i, p] of ADMIN.entries()) { const f = name("a", i + 1, p); await shoot(page, p, f); files.push(f); }
    const emp = (await (await ctx.request.get("/api/hr/employees?pageSize=1")).json()).data?.[0]?.id as string | undefined;
    if (emp) { const f = name("a", 90, "/hr/employees/detail"); await shoot(page, `/hr/employees/${emp}`, f); files.push(f); }
    const runs = await (await ctx.request.get("/api/hr/payroll/runs")).json(); const run = (Array.isArray(runs) ? runs : runs.data)?.[0]?.id as string | undefined;
    if (run) { const f = name("a", 91, "/hr/payroll/detail"); await shoot(page, `/hr/payroll/${run}`, f); files.push(f); }
    await ctx.close();
    const p2 = await (await browser.newContext()).newPage(); await sheets(p2, files, "a"); await p2.context().close();
  });

  test("employee screens, phone and sign-in", async ({ browser }) => {
    mkdirSync(OUT, { recursive: true });
    const files: string[] = [];
    const ctx = await browser.newContext({ storageState: authFile("member"), viewport: { width: 1366, height: 820 } }); const page = await ctx.newPage();
    for (const [i, p] of MEMBER.entries()) { const f = name("m", i + 1, p); await shoot(page, p, f); files.push(f); }
    await ctx.close();
    const phone = await browser.newContext({ storageState: authFile("admin"), viewport: { width: 390, height: 800 } }); const pp = await phone.newPage();
    for (const [i, p] of ["/dashboard", "/hr/analytics/attendance", "/hr/employees", "/hr/payroll", "/hr/analytics/payroll", "/hr/my-attendance"].entries()) { const f = name("p", i + 1, p); await shoot(pp, p, f); files.push(f); }
    await phone.close();
    const anon = await browser.newContext({ viewport: { width: 1366, height: 820 } }); const ap = await anon.newPage();
    for (const [i, p] of ["/login", "/verify", "/forgot-password"].entries()) { const f = name("x", i + 1, p); await shoot(ap, p, f); files.push(f); }
    await anon.close();
    const p2 = await (await browser.newContext()).newPage(); await sheets(p2, files, "m"); await p2.context().close();
  });
});
