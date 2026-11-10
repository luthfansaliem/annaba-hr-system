# 25 — HR System: Threat Model (STRIDE) and Evidence

Scope: the HR modules (employees, leave, attendance, payroll) on top of the IAM threat model in `10-security-and-threat-model.md`. "Evidence" names the automated test that fails if the control is removed. **An independent penetration test is still required before go-live [open].**

| # | Threat | Control | Evidence |
|---|---|---|---|
| S1 | Someone signs in as another employee and reads their payslip | payslips are looked up by *your* employee id only; another person's slip returns 404; slips exist for employees only after the run is locked | `HrPayrollTests.Payroll_data_needs_the_permission…`, `HrAuthorizationSweepTests.Other_peoples_data…` |
| T1 | Pay run altered after approval (insider, bug, SQL) | locked/paid runs refused by service **and** database triggers (items, lines, run totals, delete, reopen) | `HrPayrollTests.A_locked_run_cannot_be_changed_through_the_api_or_the_database` |
| T2 | Salary or tax data changed without trace | every change audited with before/after as "[changed]", never values; effective-dated history kept | `HrEmployeeTests`, `HrPayrollTests` (audit assertions) |
| T3 | Stale numbers approved | any added/removed line sends a Calculated run back to Draft | `HrPayrollTests.Extra_lines_change_the_result…` |
| R1 | "I did not approve that payroll" | approve, lock, paid, bank-file, register, compensation views are audited with the actor; maker ≠ checker is enforced and the attempt is audited | `HrPayrollTests.The_person_who_calculated_cannot_approve…` |
| I1 | Salary / NIK / NPWP / bank account readable from a database dump or backup | AES-GCM per field, bound to row and column (AAD), key outside the database | `HrPayrollTests.A_monthly_run_matches…` (ciphertext does not contain the value), `HrEmployeeTests.Sensitive_fields…` |
| I2 | Sensitive values leak into logs, audit or exports | audit stores "[changed]" only; CSV export carries no protected fields; formula injection neutralised | `HrEmployeeTests.Sensitive_fields…`, `…export_neutralises_spreadsheet_formulas` |
| I3 | A manager or employee reads data of people they do not manage | server-side checks (manager sees direct reports only; other people's records 403/404) | `HrLeaveTests.Only_the_direct_manager…`, `HrAuthorizationSweepTests` |
| I4 | Any HR endpoint added later without a permission | sweep over the real routing table: unguarded routes must be on a short named list; ordinary user gets 403 on all permission-gated routes; signed-out gets 401 on all | `HrAuthorizationSweepTests` |
| D1 | Large file or huge import exhausts the server | import capped (1,000 rows, 1 MB), all-or-nothing, validated before write; paging capped at 100 (500 for calendar) | `HrEmployeeTests.Import_*` |
| D2 | Double spend of leave days by concurrent requests | row lock per employee + unique grant constraint | `HrLeaveTests.Two_simultaneous_requests…`, `…Concurrent_balance_reads…` |
| E1 | HR admin lifts own privileges into payroll | payroll permissions are separate roles; permission changes need `permissions.assign` + recent reauth (IAM) | `AuthorizationTests`, `RoleAdminTests` |
| E2 | One person prepares, approves and pays | calculator cannot approve or lock; `payroll_officer` has no approve/lock; the bank file needs the lock permission | `HrPayrollTests` |
| E3 | Stolen session used for payroll actions | every payroll endpoint and every protected-field read/write needs a password check in the last 5 minutes | `HrPayrollTests`, `HrEmployeeTests` |
| E4 | Employee terminated but still signed in | ending employment deactivates the account and revokes every session | `HrEmployeeTests.Ending_employment_deactivates…` |

## Known gaps (accepted for now, tracked)
- Payroll rule tables (TER, PTKP, BPJS rates and caps) are code, validated by hand-computed cases, **not by a tax consultant [open]**.
- Run item amounts are stored as plain numbers (needed for totals); protected by permission, audit and disk encryption, not field encryption.
- No antivirus scan or document store yet (documents module not built).
- Dependency audit: .NET packages clean; npm reports 1 high + 1 moderate (PostCSS reached through Next.js; the fix is Next 16, a breaking upgrade). PostCSS runs at build time on our own CSS, so the practical exposure is low; schedule the upgrade.
