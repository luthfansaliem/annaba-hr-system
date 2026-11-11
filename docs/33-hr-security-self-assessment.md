# 33 — HR System: Security Self-Assessment and Penetration Test Brief

Two things in one document: what we tested ourselves and what came out (§1–§3), and what an external tester needs to start on day one (§4). A self-assessment by the people who built the system **does not replace an independent penetration test**; it makes that test cheaper and its findings more interesting.

Date of this assessment: 2026-11-10 · commit: the head of `feature/hr-system` · assessed by: the development side (not independent).

## 1. What was run

| Check | How | Result |
|---|---|---|
| Automated security behaviour | 505 API tests and 163 end-to-end tests, of which the classes below assert a security property and fail when the control is removed | all pass |
| Deny by default | `HrAuthorizationSweepTests`, `AuthorizationTests`: every route in the real routing table must declare who may call it; the anonymous routes are an exact, reviewed list (12 plus the 13 token-protected SCIM routes) | pass |
| Outside-in smoke test | `scripts/security-smoke.sh` (33 unauthenticated checks: browser protections, the sign-in wall, error hygiene, sign-in enumeration, cross-site requests, public endpoints, bypassing the web application) against a production build | 33 passed, after one fix (§2, F-1) |
| Dependency vulnerabilities | `npm audit` (all and production-only); `dotnet list package --vulnerable --include-transitive` | 0 known vulnerabilities in both |
| Browser policy | Playwright: every page runs under an enforced nonce-based CSP without a single violation; pages cannot be framed | pass |
| Accessibility as a safety net | axe (WCAG 2.1 A/AA) over every page, including the new public `/verify` page | pass |

Where each control is proven (the test fails if the control is removed):

| Area (OWASP Top 10 2021) | Controls | Tests |
|---|---|---|
| A01 Broken access control | permission per route, ownership checks in services, manager scope, tenant filters, maker ≠ checker | `HrAuthorizationSweepTests`, `AuthorizationTests`, `HrLeaveTests`, `HrPayrollTests`, `TenantTests`, `ScimTests` |
| A02 Cryptographic failures | AES-GCM per field bound to row and column, keys outside the database, hashed tokens, encrypted backups | `HrPayrollTests`, `HrEmployeeTests`, `MfaCryptoTests`, restore drill with an encrypted dump (`docs/32` §4) |
| A03 Injection | parameterised queries only (EF Core), CSV formula neutralisation, file type from bytes, strict SCIM filter grammar | `HrEmployeeTests`, `HrOpsTests`, `ImageInspectorTests`, `ScimTests` |
| A04 Insecure design | frozen pay runs (service and database triggers), effective-dated history, approvals with separation of duties, change requests for bank details | `HrPayrollTests`, `HrSelfServiceTests`, `HrSchedulingAndPhotoTests` |
| A05 Security misconfiguration | start-up refuses weak keys and unsafe mail settings in production, metrics and API description closed by default, security headers | `UnitTests` and `SmtpEmailTests` (start-up checks), `ApiDescriptionAndMetricsTests`, `LockAndCsrfTests`, `security-smoke.sh` |
| A06 Vulnerable components | audits above; versions pinned | CI + this assessment |
| A07 Identification and authentication | lockout, throttling, MFA, recovery codes, session revocation, re-authentication for critical actions, SSO | `AuthTests`, `MfaTests`, `SsoTests`, `LockAndCsrfTests`, `NewDeviceAlertTests` |
| A08 Integrity failures | signed webhooks with timestamps, append-only ledgers, audit trail with before/after markers, verification codes on documents | `HrWebhookTests`, `HrLeaveTests`, `AuditExportTests`, `HrWorkspaceTests` |
| A09 Logging and monitoring failures | audit of every sensitive read and change, security events, metrics and alert rules | `AuditExportTests`, `ApiDescriptionAndMetricsTests`, `ops/prometheus-alerts.yml` |
| A10 SSRF | webhook targets: https and public addresses only, checked again at connect time | `HrWebhookTests` |

## 2. Findings of this round

| # | Severity | Finding | Status |
|---|---|---|---|
| F-1 | Low (availability of a feature, not a leak) | The API description (`/api/openapi/v1.json`) could not be reached through the web application: the proxy treated the `.json` name as a static file and answered 404. Found by the smoke test. | **Fixed**: every `/api` path is proxied; unit test and end-to-end test added |
| F-2 | Informational | `TRACE` requests are answered with 500 by the web framework before application code runs. Nothing is echoed back, so there is no cross-site tracing risk, but it is noise in error statistics. | **Open, yours**: refuse `TRACE` at the reverse proxy |
| F-3 | Informational | A response-schema generator crashed on two response types with a default value for an identifier; the API description answered 500 while response schemas were being added. | **Fixed** before release; the description test now covers responses |
| F-4 | Accepted risk | Pay-run amounts are numbers in the database, not ciphertext (see `docs/25`, "Pay-run amounts at rest"). | **Mitigated**: least-privilege roles, encrypted backups, disk encryption **[yours]**, audit |
| F-5 | Accepted limitation | The document verification code proves that this system issued a document with those facts. It is not a certificate-based digital signature (no PAdES, no certified electronic signature under UU ITE). | **Documented**; see §5 |

## 3. What a self-assessment cannot tell you

- Whether the **deployed** system matches the repository: TLS configuration, the reverse proxy, firewall rules, who holds SSH keys, whether the database port is reachable. Run `scripts/security-smoke.sh https://<production address>` after every deploy; it covers the application's side of this.
- Logic flaws that the authors did not think of. The tests prove the controls we designed; a tester looks for the ones we did not.
- Social engineering, phishing resistance of the people, and the physical side.
- Load and denial of service beyond the limits tested in `scripts/k6-hr.js`.

## 4. Brief for the external penetration test

**Goal:** an independent grey-box test of the HR system before go-live, with a retest of every finding rated medium or higher.

**Scope**
- The web application and its API on the staging address (same build and configuration as production, synthetic data only). Not in scope: the hosting provider's infrastructure, denial of service, social engineering.
- SCIM endpoints (`/api/scim/v2`) with a provisioning token; the public document check (`/verify`); webhooks (as a receiver you control).

**What the tester gets**
- One account per role: employee, manager, HR admin, payroll officer, payroll approver, super admin; two employees in different teams and two organisations (tenants) to test isolation.
- `docs/openapi-v1.json` and `docs/28-hr-api-routes.md` (every route with its permission), `docs/25-hr-threat-model.md`, this document.
- A provisioning token and a webhook endpoint secret for the staging tenant.

**Where we would look first (highest damage if wrong)**
1. Payroll integrity: change a locked or approved pay run or weekly payout by any route; approve what you prepared; replay or reorder approve/lock/paid; race two approvals.
2. Horizontal access: another employee's payslip, documents, photo, clock-in photo, leave, claims receipts, letters and personal-data export; a manager reaching outside their reports; the skip-level approver step.
3. Tenant isolation: any identifier from tenant A used in tenant B, including SCIM and the public document check.
4. Sensitive fields: national ID, tax number, bank account, BPJS numbers and compensation without the permission or without a recent password check; in exports, audit entries, error messages and logs.
5. Bank-detail change requests as a payroll-fraud path (hijacked session → change account → approve).
6. File handling: polyglot files, oversized files, SVG, PDF with script, path tricks in names, the antivirus fail-closed path.
7. The public document check: enumeration of codes, the amount oracle (30 attempts per 10 minutes per address), what an answer reveals.
8. Session handling: fixation, reuse after sign-out or deactivation, CSRF on every state-changing route, the proxy key, SSO callback and state.
9. SCIM: privilege through groups (only roles an administrator opened up; never protected roles), filter injection, token handling.
10. Webhooks: SSRF through redirects and DNS rebinding, signature and replay.

**Rules of engagement** to agree: test window and contact person; no testing against production; data created by the tester is deleted with the staging database; findings reported with reproduction steps, impact and CVSS; critical findings reported the same day.

**Exit criteria for go-live:** no open critical or high finding; medium findings fixed or accepted in writing by the system owner; the retest report on file.

## 5. About "digital signatures" on payslips and letters

What exists: every payslip and employment letter PDF carries a verification code (60 random bits). Anyone holding the paper can enter it at `/verify` and gets the company, the reference (letter number or pay period), the holder (masked on payslips) and the issue date; for a payslip they can type the take-home pay they were shown and learn only whether it matches. This defeats the common fraud (an edited PDF or a fabricated letter shown to a bank) because the check goes back to the issuer.

What it is not: an electronic signature backed by a certificate. If a counterparty requires a **certified electronic signature** (tanda tangan elektronik tersertifikasi under UU ITE and PP 71/2019), the PDF has to be signed through an Indonesian certification authority (PSrE) such as Privy, VIDA, Peruri or BSrE. That is a commercial integration with identity verification of the signer; the place to add it is where the PDF is produced (`LetterService`, `PayrollExtrasService`), and the verification code can stay alongside it.
