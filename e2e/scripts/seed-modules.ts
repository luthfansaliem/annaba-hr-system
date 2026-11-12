import type { APIRequestContext } from "@playwright/test";
import { WEB } from "../helpers";

/**
 * Sample data for the stage 15–21 screens (performance, talent, recruitment, learning, reports, integrations), created through the API as the
 * administrator so that it goes through the same rules as real use. Called by the screen review after seed-screens.sql. NOT for production.
 */
export async function seedModules(req: APIRequestContext): Promise<string[]> {
  const log: string[] = [];
  const csrf = (await (await req.get("/api/auth/csrf")).json()).token as string;
  const headers = { "X-CSRF-Token": csrf, Origin: WEB };
  const call = async <T = unknown>(method: "post" | "put" | "patch", path: string, data: unknown = {}): Promise<T | undefined> => {
    const r = await req[method](path, { data, headers });
    if (!r.ok()) { log.push(`${method.toUpperCase()} ${path} -> ${r.status()} ${(await r.text()).slice(0, 160)}`); return undefined; }
    return (r.status() === 204 ? undefined : await r.json()) as T;
  };
  const get = async <T,>(path: string) => (await (await req.get(path)).json()) as T;
  const year = new Date().getFullYear(); const day = (n: number) => { const d = new Date(Date.now() + n * 86400000); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`; };

  const units = await get<{ id: string; code: string; name: string }[]>("/api/hr/org-units");
  const unit = (code: string) => units.find((u) => u.code === code)?.id;
  const people = (await get<{ data: { employeeId: string; fullName: string }[] }>("/api/hr/directory?pageSize=100")).data;
  const pid = (name: string) => people.find((p) => p.fullName === name)?.employeeId;

  // competencies, goals
  const comps: string[] = [];
  for (const [code, name, category] of [["COMM", "Communication", "core"], ["OWN", "Ownership", "core"], ["CUST", "Customer focus", "core"], ["LEAD", "Leading others", "leadership"], ["DATA", "Data literacy", "technical"]]) {
    const c = await call<{ id: string }>("post", "/api/hr/competencies", { code, name, category, description: `What good ${name.toLowerCase()} looks like at Annaba.` }); if (c) comps.push(c.id);
  }
  const growth = await call<{ id: string }>("post", "/api/hr/goals", { company: true, kind: "objective", title: "Grow recurring revenue 25%", year });
  const quality = await call<{ id: string }>("post", "/api/hr/goals", { company: true, kind: "objective", title: "Customers love our support", year, orgUnitId: unit("OPS") });
  const kr1 = await call<{ id: string }>("post", "/api/hr/goals", { kind: "key_result", title: "Sign 12 new enterprise clients", measure: "clients", startValue: 0, targetValue: 12, weight: 50, parentId: growth?.id, dueDate: `${year}-12-31` });
  const kr2 = await call<{ id: string }>("post", "/api/hr/goals", { kind: "kpi", title: "First reply within 2 hours", measure: "%", startValue: 60, targetValue: 95, weight: 30, parentId: quality?.id });
  await call("post", "/api/hr/goals", { kind: "kpi", title: "Finish the data literacy course", startValue: 0, targetValue: 1, weight: 20 });
  if (kr1) await call("post", `/api/hr/goals/${kr1.id}/check-in`, { value: 7, status: "on_track", note: "Two pilots converted this month" });
  if (kr2) await call("post", `/api/hr/goals/${kr2.id}/check-in`, { value: 78, status: "at_risk", note: "Weekend backlog" });

  // a closed review cycle with calibrated results (talent pool, 9-box) and an open one (reviews to write)
  const annual = await call<{ id: string }>("post", "/api/hr/review-cycles", { name: `Annual review ${year - 1}`, periodStart: `${year - 1}-01-01`, periodEnd: `${year - 1}-12-31`, goalsWeight: 60, competencyIds: comps.slice(0, 4) });
  if (annual) {
    await call("post", `/api/hr/review-cycles/${annual.id}/launch`);
    await call("post", `/api/hr/review-cycles/${annual.id}/calibrate`);
    const results = await get<{ employeeId: string }[]>(`/api/hr/review-cycles/${annual.id}/results`);
    for (const [i, r] of results.entries()) await call("post", `/api/hr/review-cycles/${annual.id}/results`, { employeeId: r.employeeId, rating: 1 + ((i * 7) % 5), potential: 1 + ((i * 5) % 3) });
    await call("post", `/api/hr/review-cycles/${annual.id}/close`);
  }
  const mid = await call<{ id: string }>("post", "/api/hr/review-cycles", { name: `Mid-year review ${year}`, periodStart: `${year}-01-01`, periodEnd: `${year}-12-31`, dueDate: day(14), goalsWeight: 50, peerReview: true, competencyIds: comps.slice(0, 3) });
  if (mid) await call("post", `/api/hr/review-cycles/${mid.id}/launch`);

  // development plan, succession
  await call("post", "/api/hr/talent/plans", { title: "Grow into a team lead", aim: "Lead the operations team within two years", competencyId: comps[3], actions: [
    { id: "", text: "Finish the leading-others course", due: day(60), done: true }, { id: "", text: "Run the weekly operations stand-up", due: day(30), done: false }, { id: "", text: "Shadow the head of operations for a month", due: day(120), done: false }] });
  const head = await call<{ id: string }>("post", "/api/hr/talent/succession", { position: "Head of Operations", orgUnitId: unit("OPS"), incumbentEmployeeId: pid("Citra Dewi"), critical: true });
  if (head) { await call("post", `/api/hr/talent/succession/${head.id}/candidates`, { employeeId: pid("Eka Putri"), readiness: "ready_now" }); await call("post", `/api/hr/talent/succession/${head.id}/candidates`, { employeeId: pid("Lukman Hakim"), readiness: "one_to_two_years", note: "Needs budgeting experience" }); }
  const fin = await call<{ id: string }>("post", "/api/hr/talent/succession", { position: "Finance Manager", orgUnitId: unit("FIN"), incumbentEmployeeId: pid("Hendra Wijaya") });
  if (fin) await call("post", `/api/hr/talent/succession/${fin.id}/candidates`, { employeeId: pid("Maya Anggraini"), readiness: "three_plus_years" });

  // recruitment
  for (const [code, title, hc, budget] of [["ENG", "Engineer", 6, 90_000_000], ["SAL", "Specialist", 4, 40_000_000], ["OPS", "Officer", 5, 35_000_000], ["FIN", "Analyst", 2, 22_000_000]] as const)
    await call("post", "/api/hr/recruitment/manpower", { year, orgUnitId: unit(code), jobTitle: title, headcount: hc, monthlyBudget: budget });
  await call("post", "/api/hr/recruitment/requisitions", { jobTitle: "Engineer", orgUnitId: unit("ENG"), openings: 2, salaryMin: 12_000_000, salaryMax: 18_000_000, justification: "New payments squad starting next quarter." });
  await call("post", "/api/hr/recruitment/requisitions", { jobTitle: "Officer", orgUnitId: unit("OPS"), openings: 1, replacement: true, justification: "Replaces Taufik, who moved to Sales." });
  const posting = await call<{ id: string }>("post", "/api/hr/recruitment/postings", { title: "Customer Support Officer", orgUnitId: unit("OPS"), location: "Jakarta (hybrid)", description: "Help our customers by chat, email and phone, and turn what you hear into improvements to the product.", requirements: "Clear written Indonesian and English.\nPatience and curiosity.", salaryMin: 6_000_000, salaryMax: 8_500_000, showSalary: true, closesOn: day(30) });
  if (posting) {
    await call("post", `/api/hr/recruitment/postings/${posting.id}/open`);
    const stages = ["applied", "screening", "screening", "assessment", "interview", "offer", "applied"];
    for (const [i, n] of ["Rina Putri", "Bagus Santoso", "Laila Hasanah", "Teguh Wibowo", "Nadia Kusuma", "Fikri Ramadhan", "Sari Melati"].entries()) {
      const a = await call<{ id: string }>("post", `/api/hr/recruitment/postings/${posting.id}/applications`, { fullName: n, email: `${n.split(" ")[0].toLowerCase()}.${i}@example.com`, phone: `0812000${i}111`, source: ["referral", "job_portal", "linkedin", "campus"][i % 4] });
      if (a && stages[i] !== "applied") await call("patch", `/api/hr/recruitment/applications/${a.id}`, { stage: stages[i], rating: 2 + (i % 4) });
    }
  }
  const eng = await call<{ id: string }>("post", "/api/hr/recruitment/postings", { title: "Backend Engineer (.NET)", orgUnitId: unit("ENG"), location: "Remote, Indonesia", description: "Build the services behind our HR and payroll product with C# and PostgreSQL.", salaryMin: 14_000_000, salaryMax: 20_000_000 });
  if (eng) await call("post", `/api/hr/recruitment/postings/${eng.id}/open`);
  await call("post", "/api/hr/recruitment/assessments/sample-profile");
  await call("post", "/api/hr/recruitment/assessments", { name: "Support basics", kind: "test", passScore: 60, timeLimitMinutes: 15, questions: [
    { id: "q1", text: "A customer is upset. What do you do first?", options: [{ text: "Listen and acknowledge", correct: true }, { text: "Explain the policy" }, { text: "Escalate at once" }] },
    { id: "q2", text: "Target time for a first chat reply?", options: [{ text: "1 day" }, { text: "2 minutes", correct: true }] }] });

  // learning
  const k3 = await call<{ course: { id: string } }>("post", "/api/hr/learning/courses", { title: "Workplace safety (K3) basics", category: "safety", mandatory: true, selfEnroll: false, passScore: 70, validMonths: 12, description: "What every employee must know about safety at work, evacuation and reporting incidents.",
    quiz: [{ id: "q1", text: "Where is the assembly point?", options: [{ text: "Car park", correct: true }, { text: "Roof" }] }, { id: "q2", text: "Who do you report a near miss to?", options: [{ text: "Nobody" }, { text: "The K3 officer", correct: true }] }] });
  if (k3) {
    const id = k3.course.id;
    await call("post", `/api/hr/learning/courses/${id}/lessons`, { title: "Why safety matters", kind: "text", body: "Every accident is preventable. This lesson explains the company's K3 policy and your part in it.", minutes: 5 });
    await call("post", `/api/hr/learning/courses/${id}/lessons`, { title: "Evacuation drill", kind: "video", url: "https://www.youtube.com/watch?v=example", minutes: 8 });
    await call("post", `/api/hr/learning/courses/${id}/lessons`, { title: "Reporting incidents", kind: "text", body: "Report every incident and near miss on the same day to the K3 officer.", minutes: 4 });
    await call("post", `/api/hr/learning/courses/${id}/publish`);
    await call("post", `/api/hr/learning/courses/${id}/assign`, { everyone: true, dueDate: day(21) });
  }
  const mail = await call<{ course: { id: string } }>("post", "/api/hr/learning/courses", { title: "Writing clear emails", category: "general", selfEnroll: true, description: "Short, kind, and easy to act on." });
  if (mail) {
    const id = mail.course.id;
    const e = await call<{ lessons: { id: string }[] }>("post", `/api/hr/learning/courses/${id}/lessons`, { title: "Short and kind", kind: "link", url: "https://example.com/clear-emails", minutes: 10 });
    await call("post", `/api/hr/learning/courses/${id}/publish`);
    await call("post", `/api/hr/learning/take/${id}/enroll`);
    if (e) await call("post", `/api/hr/learning/take/${id}/lessons/${e.lessons[0].id}/done`);
  }

  // reports and integrations
  await call("post", "/api/hr/reports/saved", { name: "Headcount by department and type", shared: true, config: { dataset: "employees", filters: [{ field: "status", op: "eq", value: "active" }], groupBy: "department", pivotBy: "type", measure: "count" } });
  await call("post", "/api/hr/integrations/bank-layouts", { name: "Bulk payroll upload", delimiter: ",", header: false, columns: ["account_no", "amount", "name", "reference"], amountDecimals: 0, note: "Check against the bank's current template." });
  await call("post", "/api/integrations/api-keys", { name: "Door access system", scopes: ["employees.read"], expiresInDays: 365 });
  return log;
}
