# 32 — HR System: Operations Kit

What the operations owner needs to stand the system up and keep it up: monitoring and alerting, on-call, antivirus, backups and restore drills, database roles, connecting an identity provider, and the parallel payroll run. The files live in `ops/`, `deploy/` and `scripts/`.

Everything marked **[verified]** was run in this repository (the evidence is named). Everything marked **[yours]** needs your servers, your accounts or your people; the kit makes each of those a matter of filling in values, not of designing something.

## 1. Monitoring stack

| File | What it is |
|---|---|
| `ops/docker-compose.ops.yml` | ClamAV, Prometheus and Alertmanager as an overlay on the application's compose file |
| `ops/prometheus.yml` | Scrapes `api:8080/health/metrics` with the bearer token; loads the alert rules; sends alerts to Alertmanager |
| `ops/prometheus-alerts.yml` | 11 alert rules: API down, database unreachable, error rate, latency, sign-in failures, e-mail and webhook queues, pay runs waiting, and more |
| `ops/alertmanager.example.yml` | Routing: `critical` → the person on call (repeats hourly), `warning` → team mailbox (repeats twice a day) |
| `ops/prometheus-scrape.example.yml` | The scrape job alone, for a Prometheus you already run |

Start:

1. In the environment (see `deploy/env.example`): `METRICS_TOKEN=<24+ random characters>`, `ANTIVIRUS_ENABLED=true`.
2. `cp ops/alertmanager.example.yml ops/alertmanager.yml` and fill in the SMTP relay and the two addresses (the copy is ignored by git). Mount the SMTP password as a file; do not write it into the configuration.
3. `docker compose --project-directory . -f deploy/docker-compose.coolify.yml -f ops/docker-compose.ops.yml up -d`
4. Prometheus and Alertmanager listen on the server's loopback only. Open them through a tunnel: `ssh -L 9090:localhost:9090 -L 9093:localhost:9093 <server>`.
5. Prove the chain end to end once: stop the API container, wait two minutes, and see `HrApiDown` arrive at the on-call address; start it again and see the "resolved" message. Write the date into the log in §4.

**[verified]** `promtool check config` accepts `ops/prometheus.yml` and finds 11 rules in `ops/prometheus-alerts.yml` (Prometheus v2.53.0); `amtool check-config` accepts `ops/alertmanager.example.yml` (Alertmanager v0.27.0); `docker compose config` merges the two compose files without errors. **[yours]** Step 5 on the real server: an alert that has never been seen to arrive does not count.

Checking the configuration again after you change it:

```
docker run --rm -v "$PWD/ops:/cfg:ro" --entrypoint promtool prom/prometheus:v2.53.0 check rules /cfg/prometheus-alerts.yml
docker run --rm -v "$PWD/ops:/cfg:ro" --entrypoint amtool prom/alertmanager:v0.27.0 check-config /cfg/alertmanager.yml
```

## 2. On-call

A proposal to adopt or change; what matters is that the three blanks at the end are filled in before go-live.

| Severity | Examples | Who is told | Start working within | Outside office hours |
|---|---|---|---|---|
| critical | `HrApiDown`, `HrDatabaseUnreachable`, `HrHighErrorRate` | person on call, phone push + e-mail, repeated hourly | 30 minutes | yes |
| critical during the pay window (20th–5th) | pay run cannot be calculated or locked, bank file wrong | person on call + payroll owner | 30 minutes | yes |
| warning | slow requests, e-mails waiting, webhook deliveries failing, sign-in failure spike | team mailbox, twice a day | next working day | no |

- **Rota:** one week per person, hand-over on Monday morning, at least two people in the rota. The person on call keeps a laptop and the SSH key within reach.
- **Escalation:** no acknowledgement after 30 minutes → the second person; after 60 minutes → the system owner.
- **What to do** for each alert is in `docs/24-hr-go-live-runbook.md` §5 (incident runbooks). After every critical incident: a half-page review within five working days (what happened, how it was noticed, what changes).
- **Quiet periods:** silence alerts in Alertmanager for the length of a planned deploy, never longer.

**[yours]** Names in the rota · the on-call address or Telegram chat · who the system owner is.

## 3. Antivirus (ClamAV)

Every uploaded document, claim receipt and photo is streamed to ClamAV before it is stored. With `Antivirus__Enabled=true`, an infected file is refused **and so is every upload while the scanner cannot be reached** (fail closed).

- The `clamav` service of the overlay downloads its signature database on first start (several minutes; the API waits for it). Plan for about 1.5 GB of memory. `freshclam` inside the image keeps the signatures current.
- Prove it works after the first start: upload the harmless EICAR test file (`https://www.eicar.org/download-anti-malware-testfile/`) as an employee document. The upload must be refused with "The virus scanner rejected this file.", and the audit trail must show `hr.document.infected`. Then stop the `clamav` container and try a clean PDF: it must also be refused ("The virus scanner cannot be reached right now, so the file was not stored"). Start it again.
- If the scanner is down in production, uploads stop but nothing else does. That is the intended trade: a late receipt is better than an infected one.

**[verified]** The protocol (INSTREAM), the refusal of infected files and the fail-closed behaviour are covered by `ClamAvScannerTests` against a scripted scanner. **[yours]** The EICAR proof against the real service.

## 4. Backups and restore drills

- The `backup` service dumps the database every `BACKUP_INTERVAL_HOURS` and prunes after `BACKUP_KEEP_DAYS`. Your recovery point is that interval: for payroll, set it to 1 hour at most, or use a managed database with point-in-time recovery.
- **Encrypt the dumps:** set `BACKUP_PASSPHRASE`. Every dump is then AES-256 encrypted (key derived with PBKDF2, 200,000 rounds) and named `*.dump.enc`; with the passphrase set, the service refuses to write unencrypted dumps. Keep the passphrase in a password manager, **not on the server**: without it no backup can be restored, and that is the point.
- **Off-site copy:** `deploy/pull-backups.sh` copies the dumps (plain or encrypted) to another machine. A backup that lives only on the server it protects is not a backup.
- **Restore drill:** `./scripts/restore-drill.sh [file]` restores a dump into a scratch database and compares every table by row count and checksum. For an encrypted dump: `BACKUP_PASSPHRASE=... ./scripts/restore-drill.sh file.dump.enc`. Run it monthly, after every schema change, and whenever the passphrase changes hands.

**[verified]** An encrypted dump made inside the PostgreSQL image was restored and compared: `RESTORE DRILL OK · 69 tables · 931 rows · 42 migrations · 3 pay-run lock triggers · 4 s`. The wrong passphrase stops the drill with "cannot decrypt"; no passphrase stops it with "is encrypted; set BACKUP_PASSPHRASE".

Drill log (keep it; an auditor will ask):

| Date | Dump used | Result line | Time to restore | Who |
|---|---|---|---|---|
| | | | | |

**[yours]** The first drill against the production backups · the recovery-time target (the drill tells you the real number) · where the passphrase is kept and who can reach it.

## 5. Database roles with the least privilege

`ops/sql/least-privilege-roles.sql` creates three roles and prints a proof table at the end:

| Role | May | May not |
|---|---|---|
| `annaba_hr_reporting` | read organisation, employment, leave and attendance for dashboards; of people only number, name, gender, status and dates | read pay runs, compensation, loans, claims, documents, photos, identity or tax numbers, bank details, addresses, religion, clock-in location, sessions, credentials, the audit trail |
| `annaba_hr_backup` | read everything (for `pg_dump`) | change anything |
| `annaba_hr_monitor` | read server statistics | read application tables |

Run it once as the database owner and again after every release; a table that is not named in the file is not readable by the reporting role, so new tables start private. Roles are created without a password: nobody can use them until you set one.

Why this matters: pay-run amounts are stored as numbers, not as ciphertext, so that the database can total, compare and freeze them (the reasoning is in `docs/25-hr-threat-model.md`, "Pay-run amounts at rest"). Their privacy therefore rests on who can read the tables, on encryption of the disk and of the backups (§4), and on the audit trail.

**[verified]** On a copy of the test database: the reporting role reads `employee_no, full_name` and is refused `national_id_enc`, `address`, `hr_pay_run_items` and the coordinates in `hr_attendance_logs`; the backup role reads pay tables and is refused a `DELETE`; a second run changes nothing.

## 6. Connecting an identity provider (SCIM 2.0)

The application side is on **Settings → User Provisioning**: the base URL, the tokens, and which roles the provider may manage.

1. Create a token (shown once). Paste the base URL (`https://<your origin>/api/scim/v2`) and the token into the provider.
2. Map the attributes. Only these are kept; everything else the provider sends is ignored:

   | Provider attribute | SCIM attribute | Note |
   |---|---|---|
   | work e-mail address | `userName` | must be the address the person signs in with; it cannot be changed afterwards |
   | first name, last name | `name.givenName`, `name.familyName` | or `displayName` |
   | account enabled | `active` | switching it off deactivates the account and ends its sessions at once |
   | the provider's own id | `externalId` | lets the provider find the account again |

3. Groups (optional). A group is a role that you opened up on the provisioning page. The provider can read it and add or remove members; it cannot create, rename or delete groups, and protected roles (Super Admin) can never be opened up. Name the group in the provider **exactly like the role** so that it is matched rather than created.
4. Provisioned people sign in through single sign-on (`Sso__*` settings) and start with the plain User role; anything more comes from a group or from an administrator.
5. Test with one person before assigning everyone: create, rename, disable, enable. Each step is in the audit trail as `scim.user.*` or `scim.group.members`, with the token's name as the actor.

Vendor notes, taken from each vendor's public SCIM guidance:

- **Microsoft Entra ID:** Enterprise application → Provisioning → Automatic; "Tenant URL" is the base URL, "Secret Token" the token. Entra sends `active` as the text "False"; that is handled. Map `userPrincipalName` or `mail` to `userName` (whichever is the person's e-mail address) and `objectId` to `externalId`.
- **Okta:** an application with SCIM 2.0 and "HTTP Header" authentication (Bearer). Enable "Push New Users", "Push Profile Updates" and, for roles, "Push Groups" linked to the existing group.
- **Google Workspace:** automatic provisioning exists only for applications in Google's catalogue; use Entra or Okta in between, or stay with invitations.

**[verified]** `ScimTests` cover create, find, rename, deactivate, reactivate, group membership (add, remove with the member in the path, replace), tenant isolation and that no route answers without a valid token. **[yours]** A run against your real provider in a test tenant; providers differ in small ways that only a live connection shows.

## 7. Parallel payroll run

The gate for go-live is two consecutive months in which this system and the old payroll agree for every employee, or every difference is explained in writing (`docs/27-hr-cutover-plan.md` §2).

1. Calculate the month here (do not lock yet).
2. Export the same month from the old payroll as CSV: one row per employee with the employee number and any of gross, BPJS (employee share), PPh 21 and take-home pay. Headings are recognised in English and Indonesian (`NIK`, `Bruto`, `PPh 21`, `Gaji Bersih`, …); `9.338.650`, `9,338,650` and `Rp 9.338.650,00` are all read correctly; `,` `;` and tab separators work.
3. On the pay run page choose **Compare with the old payroll** and upload the file. You get: how many employees match, differ, or exist on one side only; totals; and per employee the difference for each figure. The tolerance (default Rp 1) absorbs rounding.
4. **Download differences** and write the explanation next to each row. Typical causes: a different PTKP status, an allowance treated as taxable on one side only, BPJS wage ceilings, proration for joiners and leavers, the old system still using the pre-2024 tax method.
5. Fix data or rules (not the result), recalculate, compare again. When a month ends with no unexplained difference, record it in the table below.

The file is compared in memory and not stored. Who compared, when and with what outcome is in the audit trail (`hr.payroll.parallel_compare`); amounts are not.

| Month | Employees | Match | Different | Explained | Signed off by (payroll owner) |
|---|---|---|---|---|---|
| | | | | | |
| | | | | | |

**[verified]** `A_parallel_run_lays_the_old_payrolls_figures_next_to_this_run_and_names_every_difference` in `HrPayrollTests`. **[yours]** The two months themselves.
