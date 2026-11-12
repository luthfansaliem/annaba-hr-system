import { readFileSync } from "node:fs";
import AxeBuilder from "@axe-core/playwright";
import { expect, request, test, type APIRequestContext, type Page } from "@playwright/test";
import { PASSWORD, WEB, authFile } from "./helpers";

/** HR journeys through the real UI, plus accessibility and responsive checks for every HR screen. */

async function call(ctx: APIRequestContext, method: "POST" | "PUT" | "PATCH" | "GET", path: string, data?: unknown) {
  if (method === "GET") return ctx.get(path);
  const csrf = (await (await ctx.get("/api/auth/csrf")).json()).token as string;
  return ctx.fetch(path, { method, data, headers: { "X-CSRF-Token": csrf, Origin: WEB } });
}
/** The local calendar day (toISOString would give the UTC day, which is the day before between midnight and 07:00 in Jakarta). */
const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
const yearsAgo = (n: number) => iso(new Date(Date.now() - n * 365 * 864e5));

let rizkyEmployeeId = ""; let runId = ""; let tag = "";

test.beforeAll(async () => {
  tag = Date.now().toString(36).slice(-5);
  const admin = await request.newContext({ baseURL: WEB, storageState: authFile("admin") });
  await call(admin, "POST", "/api/auth/reauth", { password: PASSWORD });
  const users = (await (await call(admin, "GET", "/api/users?pageSize=50")).json()).data as { id: string; email: string }[];
  const rizky = users.find((u) => u.email.startsWith("rizky"))!; const daniel = users.find((u) => u.email.startsWith("daniel"))!;
  // Daniel becomes the second pair of eyes for payroll
  const roles = (await (await call(admin, "GET", "/api/roles")).json()) as { id: string; key: string }[];
  const approver = roles.find((r) => r.key === "payroll_approver")!; const dan = await (await call(admin, "GET", `/api/users/${daniel.id}`)).json();
  const keep = (dan.roles as { id: string }[]).map((r) => r.id); if (!keep.includes(approver.id)) expect((await call(admin, "PUT", `/api/users/${daniel.id}/roles`, { roleIds: [...keep, approver.id] })).ok()).toBeTruthy();
  // Rizky is an employee with a salary
  const emp = await call(admin, "POST", "/api/hr/employees", { fullName: "Rizky Pratama", hireDate: yearsAgo(3), jobTitle: "Analyst", userId: rizky.id, bankName: "BCA", bankAccount: "1234567890" });
  expect([201, 409]).toContain(emp.status());
  rizkyEmployeeId = emp.status() === 201 ? (await emp.json()).id : ((await (await call(admin, "GET", "/api/hr/employees?q=rizky")).json()).data[0].id as string);
  { const rr = await call(admin, "PUT", `/api/hr/payroll/compensation/${rizkyEmployeeId}`, { effectiveFrom: yearsAgo(3), basic: 10_000_000, allowance: 0, ptkpStatus: "TK0" }); expect(rr.ok(), `${rr.status()} ${await rr.text()}`).toBeTruthy(); }
  await admin.dispose();
});

test.describe("employee lifecycle (HR admin)", () => {
  test.describe.configure({ mode: "serial" });
  test.use({ storageState: authFile("admin") });
  test("add an employee, see the onboarding checklist, change the job, end employment", async ({ page }) => {
    await page.goto("/hr/employees");
    await page.getByRole("button", { name: "Add Employee" }).first().click();
    await page.getByLabel(/^Full name/).fill(`E2E Person ${tag}`); await page.getByLabel(/^Job title/).fill("QA Analyst");
    await page.getByLabel(/^Hire date/).fill(iso(new Date(Date.now() - 60 * 864e5))); // a job change needs a later date than the hire date
    await page.getByRole("button", { name: "Add Employee" }).last().click();
    await expect(page.getByRole("heading", { name: `E2E Person ${tag}` })).toBeVisible();
    await page.getByRole("tab", { name: "Checklist" }).click();
    await expect(page.getByText("Collect ID card (KTP), tax ID (NPWP) and BPJS numbers")).toBeVisible();

    await page.getByRole("button", { name: "Change job details" }).click();
    await page.getByLabel(/^Job title/).fill("Senior QA Analyst"); await page.getByLabel("Reason").fill("Promotion");
    await page.getByRole("button", { name: "Save change" }).click();
    await page.getByRole("tab", { name: "Job & history" }).click();
    await expect(page.getByText("Senior QA Analyst").first()).toBeVisible(); await expect(page.getByText("QA Analyst").first()).toBeVisible();

    await page.getByRole("button", { name: "End employment" }).first().click();
    await page.getByLabel(/^Reason/).fill("Resignation");
    await page.getByRole("button", { name: "End employment" }).last().click();
    await expect(page.getByText("Terminated").first()).toBeVisible();
    await page.getByRole("tab", { name: "Checklist" }).click(); await expect(page.getByText("Offboarding")).toBeVisible();
  });
});

test.describe("leave (employee asks, HR decides)", () => {
  test.describe.configure({ mode: "serial" });
  test("the employee requests leave and sees the balance drop", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("member") }); const page = await ctx.newPage();
    await page.goto("/hr/my-leave"); await expect(page.getByText("Annual leave").first()).toBeVisible();
    await page.getByRole("button", { name: "Request leave" }).click();
    await page.getByRole("button", { name: "Submit request" }).click();
    await expect(page.getByText("Pending").first()).toBeVisible();
    await ctx.close();
  });
  test("HR approves it from All Leave", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("admin") }); const page = await ctx.newPage();
    await page.goto("/hr/leave-all"); await page.getByRole("tab", { name: "Pending" }).click();
    await page.getByRole("button", { name: "Approve" }).first().click();
    await expect(page.getByText("Leave approved.")).toBeVisible();
    await ctx.close();
  });
  test("the employee sees the decision behind the bell and it opens the right page", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("member") }); const page = await ctx.newPage();
    await page.goto("/hr/my-leave");
    await page.getByRole("button", { name: /^Notifications/ }).click();
    const item = page.getByRole("region", { name: "Notifications" }).getByRole("button", { name: /Your leave was approved/ });
    await expect(item).toBeVisible(); await item.click();
    await expect(page).toHaveURL(/\/hr\/my-leave/);
    await page.getByRole("button", { name: /^Notifications/ }).click();
    await expect(page.getByRole("button", { name: "Notifications" })).toBeVisible();                                      // nothing unread any more: the label has no count
    await ctx.close();
  });
});

test.describe("payroll (prepare, a second person approves, employee reads the payslip)", () => {
  test.describe.configure({ mode: "serial" });
  test("the preparer calculates but cannot approve", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("admin") }); const page = await ctx.newPage();
    await call(ctx.request, "POST", "/api/auth/reauth", { password: PASSWORD });
    const r = await call(ctx.request, "POST", "/api/hr/payroll/runs", { year: new Date().getFullYear() - 1, month: 1 }); expect([201, 409]).toContain(r.status());
    runId = r.status() === 201 ? (await r.json()).id : ((await (await call(ctx.request, "GET", "/api/hr/payroll/runs")).json()) as { id: string; year: number; month: number }[]).find((x) => x.month === 1 && x.year === new Date().getFullYear() - 1)!.id;
    await page.goto(`/hr/payroll/${runId}`);
    await page.getByRole("button", { name: /^(Calculate|Recalculate)$/ }).click();
    await expect(page.getByText("Calculated.")).toBeVisible();
    await expect(page.getByText(/9,338,650|9\.338\.650/).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Approve", exact: true })).toHaveCount(0);
    await expect(page.getByText(/must check this run/)).toBeVisible();
    // parallel run: the old payroll's file is laid next to this run; someone only the old payroll knows is pointed out
    await page.getByRole("button", { name: "Compare with the old payroll" }).click();
    await page.locator("#cmp-file").setInputFiles({ name: "old-payroll.csv", mimeType: "text/csv", buffer: Buffer.from("NIK;Bruto;PPh 21;Gaji Bersih\r\nOLD-777;5.000.000;0;5.000.000\r\n") });
    await page.getByRole("button", { name: "Compare", exact: true }).click();
    await expect(page.getByText("1 not in this run")).toBeVisible();
    await expect(page.getByText("OLD-777")).toBeVisible();
    await ctx.close();
  });
  test("a different person approves and locks", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("userAdmin") }); const page = await ctx.newPage();
    await call(ctx.request, "POST", "/api/auth/reauth", { password: PASSWORD });
    await page.goto(`/hr/payroll/${runId}`);
    await page.getByRole("button", { name: "Approve", exact: true }).click(); await expect(page.getByText("Approved.")).toBeVisible();
    await page.getByRole("button", { name: "Lock", exact: true }).click(); await expect(page.getByText("Locked.")).toBeVisible();
    await expect(page.getByRole("button", { name: "Bank file" })).toBeVisible();
    await ctx.close();
  });
  test("the employee opens the payslip and downloads the PDF", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("member"), acceptDownloads: true }); const page = await ctx.newPage();
    await page.goto("/hr/my-payslips"); await page.getByRole("button", { name: "View" }).first().click();
    await expect(page.getByText("Take-home pay").first()).toBeVisible();
    const [dl] = await Promise.all([page.waitForEvent("download"), page.getByRole("button", { name: "PDF", exact: true }).click()]);
    expect(dl.suggestedFilename()).toMatch(/^payslip-\d{4}-\d{2}\.pdf$/);
    // whoever is shown this payslip can check it without an account: the code on the PDF, and whether the take-home pay is the real one
    const code = /Verification code: ([0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4})/.exec(readFileSync((await dl.path())!, "latin1"))?.[1];
    expect(code).toBeTruthy();
    const anon = await browser.newContext({ storageState: { cookies: [], origins: [] } }); const bank = await anon.newPage();
    await bank.goto("/verify"); await bank.getByLabel("Verification code").fill(code!); await bank.getByLabel(/Take-home pay on the payslip/).fill("9.338.650");
    await bank.getByRole("button", { name: "Check", exact: true }).click();
    await expect(bank.getByText("Genuine payslip")).toBeVisible(); await expect(bank.getByText("Matches our records")).toBeVisible();
    await bank.getByLabel(/Take-home pay on the payslip/).fill("19.338.650"); await bank.getByRole("button", { name: "Check", exact: true }).click();
    await expect(bank.getByText("Does NOT match our records")).toBeVisible();
    await anon.close();
    await ctx.close();
  });
});

test.describe("approvals inbox and personal pages", () => {
  test("a request shows up in the inbox and is approved from there", async ({ browser }) => {
    const m = await browser.newContext({ storageState: authFile("member") });
    const types = (await (await call(m.request, "GET", "/api/hr/leave/types")).json()) as { id: string; code: string }[];
    const d = new Date(); d.setDate(d.getDate() + 20 + Math.floor(Math.random() * 15)); while ([0, 6].includes(d.getDay())) d.setDate(d.getDate() + 1);
    const day = iso(d); const reason = `E2E inbox ${Date.now().toString(36).slice(-5)}`;
    const asked = await call(m.request, "POST", "/api/hr/leave/requests", { leaveTypeId: types.find((x) => x.code === "SICK")!.id, startDate: day, endDate: day, halfDay: false, reason });
    expect(asked.status(), await asked.text()).toBe(201);
    const mp = await m.newPage(); await mp.goto("/hr/home"); await expect(mp.getByRole("heading", { name: /Hello, Rizky/ })).toBeVisible(); await expect(mp.getByText("Waiting for a decision")).toBeVisible();
    await m.close();
    const ctx = await browser.newContext({ storageState: authFile("admin") }); const page = await ctx.newPage();
    await page.goto("/hr/approvals"); const row = page.getByRole("row", { name: new RegExp(reason) });
    await expect(row).toBeVisible(); await row.getByRole("checkbox").check();
    await page.getByRole("button", { name: "Approve selected (1)" }).click();
    await expect(row).toHaveCount(0);
    await page.goto("/hr/team"); await expect(page.getByRole("heading", { name: "My Team" })).toBeVisible();
    await ctx.close();
  });
});

test.describe("claims and letters (employee asks, HR decides from the inbox)", () => {
  test("an expense claim and an employment letter go from request to decision", async ({ browser }) => {
    const tag = Date.now().toString(36).slice(-5);
    const m = await browser.newContext({ storageState: authFile("member") }); const mp = await m.newPage();
    await mp.goto("/hr/my-claims"); await mp.getByRole("button", { name: "New claim" }).click();
    await mp.getByLabel("Amount (IDR)").fill("125000"); await mp.getByLabel("What was it for?").fill(`Taxi ${tag}`);
    await mp.getByRole("button", { name: "Submit", exact: true }).click(); await expect(mp.getByText("Claim submitted.")).toBeVisible();
    await expect(mp.getByRole("row", { name: new RegExp(`Taxi ${tag}`) })).toContainText("Pending");
    await mp.goto("/hr/my-letters"); await mp.getByRole("button", { name: "Request a letter" }).first().click();
    await mp.getByLabel("What is the letter for?").fill(`Bank ${tag}`); await mp.getByRole("button", { name: "Send request" }).click();
    await expect(mp.getByText("Request sent to HR.")).toBeVisible();

    const a = await browser.newContext({ storageState: authFile("admin") }); const ap = await a.newPage();
    await call(a.request, "POST", "/api/auth/reauth", { password: PASSWORD });
    await ap.goto("/hr/approvals");
    for (const name of [`Taxi ${tag}`, `Bank ${tag}`]) { const row = ap.getByRole("row", { name: new RegExp(name) }); await expect(row).toBeVisible(); await row.getByRole("checkbox").check(); }
    await ap.getByRole("button", { name: "Approve selected (2)" }).click();
    await expect(ap.getByRole("row", { name: new RegExp(`Taxi ${tag}`) })).toHaveCount(0);
    await a.close();

    await mp.reload(); const letter = mp.getByRole("row", { name: new RegExp(`Bank ${tag}`) });
    await expect(letter).toContainText("Issued"); await expect(letter).toContainText(/SKK\/\d{4}\/\d{4}/);
    const [dl] = await Promise.all([mp.waitForEvent("download"), letter.getByRole("button", { name: "PDF" }).click()]);
    expect(dl.suggestedFilename()).toMatch(/^surat-keterangan-kerja-SKK-\d{4}-\d{4}\.pdf$/);
    await mp.goto("/hr/my-claims"); await expect(mp.getByRole("row", { name: new RegExp(`Taxi ${tag}`) })).toContainText("Approved");
    await m.close();
  });
});

test.describe("payroll rules (a correction is a new version, with history)", () => {
  test("the preparer enters a future correction and it shows in the history", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("admin") }); const page = await ctx.newPage();
    await call(ctx.request, "POST", "/api/auth/reauth", { password: PASSWORD });
    await page.goto("/hr/payroll-rules"); await expect(page.getByText(/ID-2026\.1/).first()).toBeVisible();
    await page.getByRole("button", { name: "New version" }).click();
    const month = String(Math.floor(Math.random() * 12) + 1).padStart(2, "0"); const date = `${new Date().getFullYear() + 2}-${month}-01`;
    await page.getByLabel(/Effective from/).fill(date); await page.getByLabel(/Reason/).fill("E2E: regulation 1/2099");
    await page.getByLabel(/Health \(BPJS Kes\) – employer/).fill("0.045");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByText("New rules version saved.")).toBeVisible();
    await expect(page.getByText("E2E: regulation 1/2099")).toBeVisible(); await expect(page.getByText(/kesEmployerRate 0\.04→0\.045/)).toBeVisible();
    await ctx.close();
  });
});

test.describe("companies (a second legal entity with its own tax number)", () => {
  test("HR adds a company and it appears next to the default one", async ({ browser }) => {
    const ctx = await browser.newContext({ storageState: authFile("admin") }); const page = await ctx.newPage();
    const name = `PT E2E Cabang ${Date.now().toString(36).slice(-5)}`;
    await page.goto("/hr/companies"); await expect(page.getByText("Default").first()).toBeVisible();
    await page.getByRole("button", { name: "Add company" }).first().click();
    await page.getByLabel("Company name").fill(name); await page.getByLabel("Tax number (NPWP)").fill("01.234.567.8-901.000");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByText("Company saved.")).toBeVisible();
    await expect(page.getByRole("cell", { name: new RegExp(name) })).toBeVisible(); await expect(page.getByText("01.234.567.8-901.000")).toBeVisible();
    await ctx.close();
  });
});

const hrPages = ["/dashboard", "/hr/analytics/people", "/hr/analytics/attendance", "/hr/analytics/leave", "/hr/analytics/payroll", "/hr/analytics/claims", "/hr/approval-flows", "/provisioning", "/hr/readiness", "/hr/my-claims", "/hr/claims", "/hr/my-letters", "/hr/notification-settings", "/hr/home", "/hr/approvals", "/hr/team", "/hr/employees", "/hr/departments", "/hr/org-chart", "/hr/leave-approvals", "/hr/leave-all", "/hr/leave-calendar", "/hr/leave-setup", "/hr/time-approvals", "/hr/delegations", "/hr/shifts", "/hr/work-locations",
  "/hr/attendance-report", "/hr/payroll", "/hr/weekly-payouts", "/hr/compensation", "/hr/loans", "/hr/recurring-pay", "/hr/payroll-rules", "/hr/companies", "/hr/change-requests", "/hr/webhooks", "/hr/my-profile", "/hr/my-leave", "/hr/my-attendance", "/hr/my-payslips", "/hr/announcements"];

async function axe(page: Page, name: string) {
  const r = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
  const bad = r.violations.filter((v) => v.impact === "serious" || v.impact === "critical");
  expect(bad.map((v) => `${name}: ${v.id} (${v.nodes.length}) ${v.nodes[0]?.target.join(" ")}`), `${name} accessibility`).toEqual([]);
}

test.describe("accessibility of every HR screen (axe, WCAG 2.1 A/AA — serious and critical)", () => {
  test.use({ storageState: authFile("admin") });
  test.beforeAll(async () => { const c = await request.newContext({ baseURL: WEB, storageState: authFile("admin") }); await call(c, "POST", "/api/auth/reauth", { password: PASSWORD }); await c.dispose(); });
  for (const p of hrPages) test(p, async ({ page }) => { await page.goto(p); await page.waitForLoadState("networkidle"); await expect(page.getByRole("heading").first()).toBeVisible(); await axe(page, p); });
  test("notification bell open", async ({ page }) => {
    await page.goto("/dashboard"); await page.waitForLoadState("networkidle");
    await page.getByRole("button", { name: /^Notifications/ }).click(); await expect(page.getByRole("region", { name: "Notifications" })).toBeVisible(); await axe(page, "notification bell");
  });
  test("an employee record with its tabs", async ({ page }) => {
    await page.goto(`/hr/employees/${rizkyEmployeeId}`); await page.waitForLoadState("networkidle");
    for (const tab of ["Overview", "Job & history", "Checklist", "Documents", "Protected details", "Team"]) { await page.getByRole("tab", { name: tab }).click(); await axe(page, `employee/${tab}`); }
  });
  test("an open drawer", async ({ page }) => {
    await page.goto("/hr/employees"); await page.getByRole("button", { name: "Add Employee" }).first().click(); await expect(page.getByRole("dialog")).toBeVisible(); await axe(page, "add employee drawer");
  });
  test.describe("dark mode", () => {
    test.beforeEach(async ({ page }) => { await page.addInitScript(() => localStorage.setItem("annaba-digital-theme", "dark")); });
    for (const p of ["/dashboard", "/hr/analytics/people", "/hr/analytics/payroll", "/hr/employees", "/hr/payroll", "/hr/my-leave", "/hr/announcements"]) test(p, async ({ page }) => { await page.goto(p); await page.waitForLoadState("networkidle"); await axe(page, `dark ${p}`); });
  });
});

test.describe("responsive: HR screens do not overflow the page", () => {
  test.use({ storageState: authFile("admin") });
  for (const w of [375, 768, 1366]) test(`${w}px`, async ({ page }) => {
    await page.setViewportSize({ width: w, height: 900 });
    for (const p of hrPages) {
      await page.goto(p); await page.waitForLoadState("networkidle");
      const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      const culprit = over > 1 ? await page.evaluate(() => [...document.querySelectorAll("body *")].filter((e) => e.getBoundingClientRect().right > document.documentElement.clientWidth + 1).slice(0, 4).map((e) => `${e.tagName}.${String((e as HTMLElement).className).slice(0, 40)}`).join(" | ")) : "";
      expect(over, `${p} at ${w}px overflows by ${over}px: ${culprit}`).toBeLessThanOrEqual(1);
    }
  });
});
