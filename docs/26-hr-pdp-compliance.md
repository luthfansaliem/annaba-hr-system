# 26 — HR System: Personal Data Protection (UU 27/2022) Working Sheet

A working sheet for the data protection owner and legal counsel, **not legal advice**. The system provides the technical controls; the organisational items need people.

## 1. Record of processing (RoPA) starting point
| Data | Purpose | Legal basis (to confirm) | Where | Protection | Retention (to confirm) |
|---|---|---|---|---|---|
| Name, contact, birth date, address, emergency contact | employment administration | employment contract | `hr_employees` | access by permission, audit | employment + legal period |
| NIK, NPWP, bank account | payroll, tax, BPJS | legal obligation | `hr_employees` (encrypted) | AES-GCM, sensitive permission + recent password check, every view audited | 10 years for tax documents (confirm with consultant) |
| Salary, allowances, tax status | payroll | contract / legal obligation | `hr_compensations` (encrypted), pay run items | `payroll.*` permissions, maker–checker, DB lock | as above |
| Leave, attendance (incl. IP of clock-in) | leave and time administration | contract | `hr_leave_*`, `hr_attendance_logs` | permission-gated | confirm |
| Position at clock-in (latitude/longitude, only when the person shares it and work locations are configured), work mode (office/home/field) | confirming office attendance | legitimate interest with a written balancing test, not consent (proposal and reasoning in `34-hr-privacy-pack.md` §1.1) | `hr_attendance_logs` | rounded to ~1 m, shown to HR only as a flag ("location not confirmed"), never on reports as coordinates | same as attendance |
| Expense claims and receipts (may show merchants, medical providers) | reimbursement | contract | `hr_expense_claims` (receipt bytes) | owner, deciding manager and claims managers only; opening someone else's receipt is audited; virus-scanned | finance retention (to confirm) |
| Religion (optional) | planning the holiday allowance (THR) date | legal obligation (THR rules); general personal data under Pasal 4(3), but handle as sensitive and collect only if THR dates differ by religion (see `34-hr-privacy-pack.md` §1.2) | `hr_employees.religion` | HR only; cleared by anonymisation | employment + confirm |
| Employee photo and optional clock-in photo | recognising colleagues; proof of attendance where needed | legitimate interest; treat as specific (biometric) data until counsel says otherwise (`34` §1.3) | `hr_photos` | type checked from bytes, virus-scanned, owner/HR only, viewing another person's clock-in photo audited | clock-in photos deleted after 90 days |
| BPJS membership numbers | BPJS reporting | legal obligation | `hr_employees` (encrypted) | as NIK/NPWP | as NIK/NPWP |
| Sick leave (health-related reason) | leave administration | contract / consent | `hr_leave_requests.reason` | treat as specific personal data; keep reasons short, restrict `leave.read_all` | confirm |

## 2. Data-subject rights → what exists
Access / portability: IAM account export, and **My HR Profile → Download my data** (JSON of the person's own HR record, behind a password check, audited). Correction: employees change contact details themselves; identity/bank/tax changes via an HR-approved request. Deletion: former employees are anonymised after `HrRetention:AnonymizeAfterYears` (default 10, **job off by default**: set `HrRetention:AutoAnonymize=true` after the retention period is confirmed) or earlier on a granted erasure request (HR configuration permission, reason, explicit force). Payroll amounts stay as tax evidence; names on locked pay-run items are replaced.

## 3. Incident handling
Notify the authority and affected people within 3 × 24 hours of a confirmed breach. Technical triage steps are in `24-hr-go-live-runbook.md` §5. Name the incident owner and the legal contact in advance **[open]**.

## 4. Organisational items before go-live **[open]**
Drafts to approve rather than write from scratch are in `34-hr-privacy-pack.md`: the legal basis for location, religion and photos, the employee privacy notice, the impact assessment (DPIA) and the decision table to sign.

Appoint the person responsible for personal data; approve the privacy notice shown to employees; sign data-processing terms with the host and e-mail provider; decide retention periods; document the lawful basis for monitoring attendance IPs.
