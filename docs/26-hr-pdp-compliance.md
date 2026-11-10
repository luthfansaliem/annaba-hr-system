# 26 — HR System: Personal Data Protection (UU 27/2022) Working Sheet

A working sheet for the data protection owner and legal counsel, **not legal advice**. The system provides the technical controls; the organisational items need people.

## 1. Record of processing (RoPA) starting point
| Data | Purpose | Legal basis (to confirm) | Where | Protection | Retention (to confirm) |
|---|---|---|---|---|---|
| Name, contact, birth date, address, emergency contact | employment administration | employment contract | `hr_employees` | access by permission, audit | employment + legal period |
| NIK, NPWP, bank account | payroll, tax, BPJS | legal obligation | `hr_employees` (encrypted) | AES-GCM, sensitive permission + recent password check, every view audited | 10 years for tax documents (confirm with consultant) |
| Salary, allowances, tax status | payroll | contract / legal obligation | `hr_compensations` (encrypted), pay run items | `payroll.*` permissions, maker–checker, DB lock | as above |
| Leave, attendance (incl. IP of clock-in) | leave and time administration | contract | `hr_leave_*`, `hr_attendance_logs` | permission-gated | confirm |
| Sick leave (health-related reason) | leave administration | contract / consent | `hr_leave_requests.reason` | treat as specific personal data; keep reasons short, restrict `leave.read_all` | confirm |

## 2. Data-subject rights → what exists
Access / portability: IAM account export, and **My HR Profile → Download my data** (JSON of the person's own HR record, behind a password check, audited). Correction: employees change contact details themselves; identity/bank/tax changes via an HR-approved request. Deletion: former employees are anonymised after `HrRetention:AnonymizeAfterYears` (default 10, **job off by default**: set `HrRetention:AutoAnonymize=true` after the retention period is confirmed) or earlier on a granted erasure request (HR configuration permission, reason, explicit force). Payroll amounts stay as tax evidence; names on locked pay-run items are replaced.

## 3. Incident handling
Notify the authority and affected people within 3 × 24 hours of a confirmed breach. Technical triage steps are in `24-hr-go-live-runbook.md` §5. Name the incident owner and the legal contact in advance **[open]**.

## 4. Organisational items before go-live **[open]**
Appoint the person responsible for personal data; approve the privacy notice shown to employees; sign data-processing terms with the host and e-mail provider; decide retention periods; document the lawful basis for monitoring attendance IPs.
