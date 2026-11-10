# 24 — HR System: Go-Live & Operations Runbook

Status: draft for the operations owner. Everything marked **[verified]** was run in this repository (evidence named); everything marked **[open]** needs infrastructure, a person or a third party that this repository cannot supply.

## 1. Release checklist (every release)
1. CI green: `npm run typecheck && npm run lint && npm test`, `npm run api:test` (377+ tests), Playwright smoke. **[verified locally; CI run on GitHub still open]**
2. Take a backup, then run `./scripts/restore-drill.sh` against it (monthly at minimum, and after every schema change). **[verified: 42 tables / 2,168 rows restored and compared by checksum in 4 s; a changed row is detected]**
3. Apply migrations as a release step, **not at boot** (`Api__AutoMigrate=false` in Production). `GET /health/ready` returns 503 with "N migration(s) pending" until it is done, so the platform will not route traffic to a build whose schema is behind. **[verified]**
4. Deploy API, then UI. Check `/health` (liveness) and `/health/ready` (readiness), sign in, open Employees, My Leave, Pay Runs.
5. Rollback: redeploy the previous image. Migrations are additive; to undo one, restore the pre-release backup (rehearsed by the drill above), never edit data by hand.

## 2. Configuration that must be set in Production
See `docs/22-staging-runbook.md` (secrets, e-mail, proxy key). HR adds nothing new, but note: **`Security__EncryptionKeys` protects salaries, NIK, NPWP and bank accounts. Losing or rotating it without keeping the old version makes those fields unreadable.** Keep every key version, store them outside the database, back them up separately from the data. The app refuses to start with the built-in development key.

## 3. Service levels and what is measured
| Target (PRD §7) | How it is checked | Evidence |
|---|---|---|
| p95 < 800 ms for list/detail at 5,000 employees | `HrScaleTests` (30 mixed list/search calls) | median 56 ms, p95 339 ms **[verified]** |
| Payroll for 5,000 employees < 5 min | `HrScaleTests` | 7.0 s, register export 0.3 s **[verified]** |
| Availability 99.9 % in working hours | uptime probe on `/health/ready` every minute | **[open: needs monitoring service]** |
| RPO ≤ 15 min, RTO ≤ 4 h | compose `backup` service dumps on a schedule (RPO = dump interval, so set it to ≤ 15 min or add WAL archiving / PITR on a managed database) | restore time measured by the drill (seconds at this size) **[RPO config open]** |

## 4. Monitoring and alerts to create **[open]**
Logs are structured and every response carries a request id (`error.requestId` in the JSON envelope, `ctx.TraceIdentifier` in the log line "Unhandled exception … (id)"), so a user's error report maps to one log entry. Create alerts on:
- `/health/ready` not 200 for 2 minutes · any `Unhandled exception` · 5xx rate > 0.5 % · `outbox_emails` rows older than 10 minutes.
- Audit events `hr.payroll.denied`, repeated `authz.denied`, `hr.employee.export`, `hr.payroll.bank_file`, `hr.payroll.compensation_viewed` outside business hours (review weekly).
- Pay run stuck in *Calculated* or *Approved* for more than 2 business days.

## 4a. Monthly payroll procedure
1. Close leave and attendance for the month (HR). Add holidays/collective leave for the year in *Leave Setup*.
2. Payroll Officer: *Pay Runs → New pay run*, add bonuses/THR/deductions, **Calculate**, read every warning (no bank account, no NPWP, > 30 % change, negative net, mid-month changes) and the "not in this run" list.
3. Payroll Approver (a different person): review totals against the previous month, **Approve**; send back with a reason if something is wrong.
4. Lock → download the bank file → pay → **Mark as paid**. Payslips appear for employees at lock.
5. A mistake after locking cannot be edited (service and database trigger refuse it): correct it as an adjustment line in the next month's run.

## 5. Incident runbooks
**Wrong payroll discovered after lock:** do not touch the database. Record the finding, add adjustment lines in the next run, keep the audit trail. If money was misdirected, contact the bank with the bank file from the locked run.
**Suspected data leak:** (1) revoke sessions of the account (*Users → Revoke sessions*) and suspend it; (2) pull `audit_events` for `hr.employee.sensitive_viewed`, `hr.payroll.*`, exports; (3) the PDP notification clock (3 × 24 h to the authority and affected people, see doc 26) starts when the leak is known; (4) rotate `Api__ProxyKey` and CSRF key if the source is unclear.
**Database down / corrupted:** restore the latest dump into a new instance with `pg_restore`, run `/health/ready`, point the API at it. Re-run the drill afterwards.
**Locked out (no admin):** `Api__BootstrapAdminEmail` issues a fresh super-admin invitation when no user exists; otherwise use a second super admin (keep at least two).

## 6. Access hygiene
At least two active super admins; MFA required for HR, payroll and admin roles (*Security Policy → MFA = required*); quarterly *Access Reviews* for `payroll_*`, `hr_admin`; terminate employment in the system on the last working day (it deactivates the login and signs out every device).
