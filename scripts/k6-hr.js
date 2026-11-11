// Load test for the HR read paths (PRD §7.3: 2× the 5,000-employee target).
//   k6 run -e BASE=https://staging.example.com -e COOKIE='sid=...' -e CSRF=... scripts/k6-hr.js
// Sign in once in a browser against STAGING (never production), copy the session cookie into COOKIE. The thresholds are the PRD targets.
// Not run in this repository's session (k6 is not installed); the in-process equivalent that was run is api/tests/AnnabaDigital.Tests/HrScaleTests.cs.
import http from "k6/http";
import { check, sleep } from "k6";

export const options = {
  stages: [{ duration: "1m", target: 40 }, { duration: "3m", target: 40 }, { duration: "30s", target: 0 }],
  thresholds: { http_req_failed: ["rate<0.005"], http_req_duration: ["p(95)<800"] },
};

const headers = { Cookie: __ENV.COOKIE };
const paths = [
  "/api/hr/employees?page=1&pageSize=25&sort=name",
  "/api/hr/employees?q=an&pageSize=25",
  "/api/hr/employees?status=active&type=permanent&page=20&pageSize=25&sort=hireDate&dir=desc",
  "/api/hr/org-units",
  "/api/hr/leave/types",
  "/api/hr/leave/requests?scope=all&status=pending",
  "/api/hr/attendance/me",
];

export default function hrReadPaths() {
  const res = http.get(`${__ENV.BASE}${paths[Math.floor(Math.random() * paths.length)]}`, { headers });
  check(res, { "status is 200": (r) => r.status === 200 });
  sleep(1);
}
