# 24 — HR System: Go-Live & Operations Runbook

Status: draft for the operations owner. Everything marked **[verified]** was run in this repository (evidence named); everything marked **[open]** needs infrastructure, a person or a third party that this repository cannot supply.

The ready-to-use files for monitoring, antivirus, encrypted backups, database roles, the identity-provider connection and the parallel run are described in `32-hr-operations-kit.md`. After every deploy run `./scripts/security-smoke.sh https://<address>` (33 outside-in checks; see `33-hr-security-self-assessment.md`).

## 1. Release checklist (every release)
1. CI green: `npm run typecheck && npm run lint && npm test`, `npm run api:test`, Playwright (`npm run test:e2e`; set `E2E_DB_CONTAINER`/`E2E_DB_PORT` if port 5432 or the container name is taken). **[verified: API suite, 137 e2e incl. HR journeys + WCAG AA + responsive; GitHub CI green on the PR]**
2. Take a backup, then run `./scripts/restore-drill.sh` against it (monthly at minimum, and after every schema change). **[verified: 42 tables / 2,168 rows restored and compared by checksum in 4 s; a changed row is detected]**
3. Apply migrations as a release step, **not at boot** (`Api__AutoMigrate=false` in Production). `GET /health/ready` returns 503 with "N migration(s) pending" until it is done, so the platform will not route traffic to a build whose schema is behind. **[verified]**
4. Deploy API, then UI. Check `/health` (liveness) and `/health/ready` (readiness), sign in, open Employees, My Leave, Pay Runs.
5. Rollback: redeploy the previous image. Migrations are additive; to undo one, restore the pre-release backup (rehearsed by the drill above), never edit data by hand.

## 2. Configuration that must be set in Production
See `docs/22-staging-runbook.md` (secrets, e-mail, proxy key). HR adds nothing new, but note: **`Security__EncryptionKeys` protects salaries, NIK, NPWP and bank accounts. Losing or rotating it without keeping the old version makes those fields unreadable.** Keep every key version, store them outside the database, back them up separately from the data. The app refuses to start with the built-in development key.

HR-specific settings (environment variables; `__` separates sections):

| Setting | Production value | What it does |
|---|---|---|
| `Antivirus__Enabled`, `Antivirus__Host`, `Antivirus__Port` | `true`, the clamd host, `3310` | Every uploaded document and claim receipt is streamed to ClamAV first. Infected files are refused, **and so is every upload while the scanner cannot be reached** (fail closed). Off = files are only checked for their type |
| `Api__MetricsToken` | a random secret of ≥ 24 characters | Enables `GET /health/metrics` (Prometheus text format) for `Authorization: Bearer <token>`. Unset = the route answers 404 |
| `Api__OpenApi` | `true` (default) or `false` | Serves the OpenAPI description at `/api/openapi/v1.json` to signed-in users |
| `HrReminders__Enabled`, `Webhooks__Enabled` | `true` | Background workers (contract reminders, escalation, webhook delivery) |

**Identity provider provisioning (optional):** a super admin creates a token under *Provisioning (SCIM)* and enters, in the identity provider, the tenant URL `https://<your host>/api/scim/v2` and the token as the secret. Map `userName` to the e-mail address. Provisioned users sign in through single sign-on and start with the plain *User* role; HR links them to an employee record and administrators grant further roles inside the application.

After deploying, a super admin opens **Go-Live Check** (`/hr/readiness`): anything marked *Blocks go-live* must be fixed first.

## 3. Service levels and what is measured
| Target (PRD §7) | How it is checked | Evidence |
|---|---|---|
| p95 < 800 ms for list/detail at 5,000 employees | `HrScaleTests` (30 mixed list/search calls) | median 56 ms, p95 339 ms **[verified]** |
| Payroll for 5,000 employees < 5 min | `HrScaleTests` | 7.0 s, register export 0.3 s **[verified]** |
| Availability 99.9 % in working hours | uptime probe on `/health/ready` every minute | **[open: needs monitoring service]** |
| RPO ≤ 15 min, RTO ≤ 4 h | compose `backup` service dumps on a schedule (RPO = dump interval, so set it to ≤ 15 min or add WAL archiving / PITR on a managed database) | restore time measured by the drill (seconds at this size) **[RPO config open]** |

## 4. Monitoring and alerts
**In the repository:** the API exposes `GET /health/metrics` (Prometheus text format; needs `Api__MetricsToken`), a scrape example is in `ops/prometheus-scrape.example.yml` and alert rules are in `ops/prometheus-alerts.yml`. **Still to do on the real infrastructure [open]:** run Prometheus + Alertmanager (or load the rules into the monitoring product in use), route alerts to the on-call channel, agree who is on call.

| Metric | Meaning | Alert in the rules file |
|---|---|---|
| `http_requests_total{method,route,status}` · `http_request_duration_seconds` | requests and latency per route *template* (ids never appear in labels) | 5xx > 2 % for 10 min (critical) · p95 > 1 s for 15 min · spike of failed sign-ins |
| `database_up` | the API reached its database during the scrape | 0 for 2 min (critical) |
| `outbox_emails_pending` · `…_oldest_pending_seconds` · `…_failed` | e-mail queue | oldest > 15 min · any given up on |
| `webhook_deliveries_pending` · `…_dead` | webhook queue | backlog > 200 · any given up on |
| `hr_leave_requests_pending_over_3d` · `hr_pay_runs_awaiting_approval` | work waiting for people | informational |
| `process_uptime_seconds` · `process_resident_memory_bytes` · `dotnet_gc_heap_bytes` | the instance itself | — |

Logs are structured and every response carries a request id (`error.requestId` in the JSON envelope, `ctx.TraceIdentifier` in the log line "Unhandled exception … (id)"), so a user's error report maps to one log entry. Also alert on (from logs / audit, not metrics):
- `/health/ready` not 200 for 2 minutes · any `Unhandled exception`.
- Audit events `hr.payroll.denied`, repeated `authz.denied`, `hr.employee.export`, `hr.payroll.bank_file`, `hr.payroll.compensation_viewed`, `hr.document.infected` outside business hours (review weekly).

## 4b. Background jobs
`HrReminderWorker` (daily, first run 5 minutes after start) e-mails contract-ending reminders; it is idempotent and safe with several API instances (a unique `hr_reminders` row is claimed before anything is mailed). Disable with `HrReminders__Enabled=false`. Mail goes through the normal outbox, so check *Email Delivery* if reminders seem missing. The job logs `HR contract reminders queued: N` and `HR reminder run failed` (alert on the latter).

`HrReminderWorker` also sends the daily *escalation digest* to HR (leave/correction/overtime requests undecided for 48 hours) and, only when `HrRetention__AutoAnonymize=true`, anonymises former employees past `HrRetention__AnonymizeAfterYears`. Erasure cannot be undone: confirm the retention period with the tax consultant and take a backup before turning it on.

`WebhookWorker` (every 30 s, `Webhooks__Enabled`, `Webhooks__IntervalSeconds`) delivers queued webhook events. Check *Webhooks → Deliveries* for failures ("failed for good" = gave up after six tries); alert on the log line `Webhook dispatch failed`. Receivers must verify `X-Annaba-Signature` and reject timestamps older than a few minutes. The API host needs outbound https to the receivers' public addresses; internal receivers are deliberately not allowed (put a public relay in front of them).

## 4a. Monthly payroll procedure
0. Before the first run, and whenever a regulation changes: **Payroll Rules** shows the rates, tax-free allowances and tax tables in force. A change is a new version that applies from the 1st of a month no locked run covers; enter the regulation number as the reason. Confirm the values with the tax consultant before the first real run **[open]**.
   Several companies (own NPWP)? Add them under **Companies** first, give each employee a company, and create one pay run per company per month.
   Daily-wage workers: set their *Pay basis* to daily in Compensation; **close the month's attendance** before calculating (their pay is days worked × rate).
1. Close leave and attendance for the month (HR). Add holidays/collective leave for the year in *Leave Setup*.
2. Payroll Officer: *Pay Runs → New pay run*, add bonuses/THR/deductions, **Calculate**, read every warning (no bank account, no NPWP, > 30 % change, negative net, mid-month changes) and the "not in this run" list.
3. Payroll Approver (a different person): review totals against the previous month, **Approve**; send back with a reason if something is wrong.
   Paying daily-wage workers every week? **Weekly Payouts**: after the week has ended, create the payout (company, Monday, holdback %), **Calculate**, a second person **Approves**, download the bank file, **Mark as paid**. The month-end run deducts these payments automatically; a worker's month still adds up to the normal monthly result.
4. Lock → download the bank file → pay → **Mark as paid**. Payslips appear for employees at lock (each gets a bell notification); approvers are notified when a run is calculated.
5. A mistake after locking cannot be edited (service and database trigger refuse it): correct it as an adjustment line in the next month's run.

## 5. Incident runbooks
**Wrong payroll discovered after lock:** do not touch the database. Record the finding, add adjustment lines in the next run, keep the audit trail. If money was misdirected, contact the bank with the bank file from the locked run.
**Suspected data leak:** (1) revoke sessions of the account (*Users → Revoke sessions*) and suspend it; (2) pull `audit_events` for `hr.employee.sensitive_viewed`, `hr.payroll.*`, exports; (3) the PDP notification clock (3 × 24 h to the authority and affected people, see doc 26) starts when the leak is known; (4) rotate `Api__ProxyKey` and CSRF key if the source is unclear.
**Database down / corrupted:** restore the latest dump into a new instance with `pg_restore`, run `/health/ready`, point the API at it. Re-run the drill afterwards.
**Locked out (no admin):** `Api__BootstrapAdminEmail` issues a fresh super-admin invitation when no user exists; otherwise use a second super admin (keep at least two).

## 6. Access hygiene
At least two active super admins; MFA required for HR, payroll and admin roles (*Security Policy → MFA = required*); quarterly *Access Reviews* for `payroll_*`, `hr_admin`; terminate employment in the system on the last working day (it deactivates the login and signs out every device).
