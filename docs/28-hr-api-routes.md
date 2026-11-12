# 28 — HR API routes

Generated from the running application's routing table by `HrApiDocTests` — do not edit by hand (`UPDATE_API_DOC=1 dotnet test api/AnnabaDigital.sln --filter HrApiDocTests` regenerates it).
Every route needs a signed-in session. "Permission" is the extra permission the route requires; routes marked *self-service* only ever touch the caller's own data (the service checks it). Errors use the envelope `{ error: { code, message, fields?, requestId } }`; see `08-api-spec.md`. Mutating calls need the `X-CSRF-Token` header like every other API call, and payroll, protected-data and document routes additionally ask for a password check in the last five minutes (`reauth_required`).

## /api/hr/advances

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/advances/` | *self-service* |
| POST | `/api/hr/advances/` | *self-service* |
| POST | `/api/hr/advances/{id}/cancel` | *self-service* |
| POST | `/api/hr/advances/{id}/decide` | `claims.approve` |
| POST | `/api/hr/advances/{id}/disburse` | `claims.manage` |
| GET | `/api/hr/advances/{id}/receipt` | *self-service* |
| POST | `/api/hr/advances/{id}/settle` | *self-service* |
| POST | `/api/hr/advances/{id}/settlement` | `claims.approve` |

## /api/hr/analytics

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/analytics/attendance` | `attendance.read_all` |
| GET | `/api/hr/analytics/claims` | `claims.manage` |
| GET | `/api/hr/analytics/leave` | `leave.read_all` |
| GET | `/api/hr/analytics/payroll` | `payroll.read` |

## /api/hr/announcements

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/announcements/` | *self-service* |
| POST | `/api/hr/announcements/` | `hrconfig.manage` |
| GET | `/api/hr/announcements/manage` | `hrconfig.manage` |
| PATCH | `/api/hr/announcements/{id}` | `hrconfig.manage` |
| POST | `/api/hr/announcements/{id}/ack` | *self-service* |
| POST | `/api/hr/announcements/{id}/read` | *self-service* |
| GET | `/api/hr/announcements/{id}/report` | `hrconfig.manage` |

## /api/hr/approval-rules

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/approval-rules/` | `hrconfig.manage` |
| POST | `/api/hr/approval-rules/` | `hrconfig.manage` |
| DELETE | `/api/hr/approval-rules/{id}` | `hrconfig.manage` |
| PATCH | `/api/hr/approval-rules/{id}` | `hrconfig.manage` |

## /api/hr/approvals

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/approvals` | *self-service* |
| POST | `/api/hr/approvals/bulk` | *self-service* |

## /api/hr/attendance

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/attendance/block-leave` | `leave.read_all` |
| POST | `/api/hr/attendance/break-end` | *self-service* |
| POST | `/api/hr/attendance/break-start` | *self-service* |
| POST | `/api/hr/attendance/clock-in` | *self-service* |
| POST | `/api/hr/attendance/clock-out` | *self-service* |
| GET | `/api/hr/attendance/corrections` | *self-service* |
| POST | `/api/hr/attendance/corrections` | *self-service* |
| POST | `/api/hr/attendance/corrections/{id}/approve` | `attendance.approve` |
| POST | `/api/hr/attendance/corrections/{id}/cancel` | *self-service* |
| POST | `/api/hr/attendance/corrections/{id}/reject` | `attendance.approve` |
| GET | `/api/hr/attendance/hourly-leave` | *self-service* |
| POST | `/api/hr/attendance/hourly-leave` | *self-service* |
| POST | `/api/hr/attendance/hourly-leave/{id}/cancel` | *self-service* |
| POST | `/api/hr/attendance/hourly-leave/{id}/decide` | `attendance.approve` |
| POST | `/api/hr/attendance/import` | `attendance.manage` |
| GET | `/api/hr/attendance/kiosk-pin` | *self-service* |
| PUT | `/api/hr/attendance/kiosk-pin` | *self-service* |
| GET | `/api/hr/attendance/kiosks` | `attendance.manage` |
| POST | `/api/hr/attendance/kiosks` | `attendance.manage` |
| DELETE | `/api/hr/attendance/kiosks/{id}` | `attendance.manage` |
| GET | `/api/hr/attendance/logs` | `attendance.read_all` |
| GET | `/api/hr/attendance/logs/{id}/photo` | *self-service* |
| GET | `/api/hr/attendance/me` | *self-service* |
| GET | `/api/hr/attendance/on-call` | `attendance.read_all` |
| POST | `/api/hr/attendance/on-call` | `attendance.manage` |
| DELETE | `/api/hr/attendance/on-call/{id}` | `attendance.manage` |
| GET | `/api/hr/attendance/overtime` | *self-service* |
| POST | `/api/hr/attendance/overtime` | *self-service* |
| POST | `/api/hr/attendance/overtime/{id}/approve` | `attendance.approve` |
| POST | `/api/hr/attendance/overtime/{id}/cancel` | *self-service* |
| POST | `/api/hr/attendance/overtime/{id}/reject` | `attendance.approve` |
| GET | `/api/hr/attendance/periods` | `attendance.read_all` |
| POST | `/api/hr/attendance/periods/{month}/close` | `attendance.manage` |
| POST | `/api/hr/attendance/periods/{month}/reopen` | `attendance.manage` |
| GET | `/api/hr/attendance/report` | `attendance.read_all` |
| GET | `/api/hr/attendance/reports/overtime` | `attendance.read_all` |
| GET | `/api/hr/attendance/roster` | `attendance.read_all` |
| PUT | `/api/hr/attendance/roster` | `attendance.manage` |
| GET | `/api/hr/attendance/roster/mine` | *self-service* |
| POST | `/api/hr/attendance/roster/pattern` | `attendance.manage` |
| GET | `/api/hr/attendance/visits` | *self-service* |
| POST | `/api/hr/attendance/visits` | *self-service* |
| GET | `/api/hr/attendance/work-locations` | `attendance.read_all` |
| POST | `/api/hr/attendance/work-locations` | `attendance.manage` |
| PATCH | `/api/hr/attendance/work-locations/{id}` | `attendance.manage` |

## /api/hr/branches

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/branches` | `employees.read` |
| POST | `/api/hr/branches` | `hrconfig.manage` |
| PUT | `/api/hr/branches/{id}` | `hrconfig.manage` |

## /api/hr/calendar

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/calendar` | *self-service* |
| POST | `/api/hr/calendar/events` | `hrconfig.manage` |
| DELETE | `/api/hr/calendar/events/{id}` | `hrconfig.manage` |
| PUT | `/api/hr/calendar/events/{id}` | `hrconfig.manage` |

## /api/hr/change-requests

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/change-requests/` | `employees.update` |
| POST | `/api/hr/change-requests/{id}/approve` | `employees.update` |
| POST | `/api/hr/change-requests/{id}/reject` | `employees.update` |

## /api/hr/claims

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/claims/` | *self-service* |
| POST | `/api/hr/claims/` | *self-service* |
| GET | `/api/hr/claims/balances` | *self-service* |
| GET | `/api/hr/claims/policies` | `claims.manage` |
| POST | `/api/hr/claims/policies` | `claims.manage` |
| PUT | `/api/hr/claims/policies/{id}` | `claims.manage` |
| POST | `/api/hr/claims/{id}/approve` | `claims.approve` |
| POST | `/api/hr/claims/{id}/cancel` | *self-service* |
| GET | `/api/hr/claims/{id}/receipt` | *self-service* |
| POST | `/api/hr/claims/{id}/reject` | `claims.approve` |

## /api/hr/custom-fields

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/custom-fields` | `employees.read` |
| POST | `/api/hr/custom-fields` | `hrconfig.manage` |
| PUT | `/api/hr/custom-fields/{id}` | `hrconfig.manage` |

## /api/hr/dashboard

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/dashboard` | `employees.read` |

## /api/hr/delegations

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/delegations/` | `leave.approve` |
| POST | `/api/hr/delegations/` | `leave.approve` |
| GET | `/api/hr/delegations/candidates` | `leave.approve` |
| DELETE | `/api/hr/delegations/{id}` | `leave.approve` |

## /api/hr/directory

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/directory` | *self-service* |

## /api/hr/employees

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/employees/` | `employees.read` |
| POST | `/api/hr/employees/` | `employees.create` |
| DELETE | `/api/hr/employees/checklist/{taskId}` | `employees.update` |
| POST | `/api/hr/employees/checklist/{taskId}/done` | `employees.update` |
| POST | `/api/hr/employees/checklist/{taskId}/reopen` | `employees.update` |
| DELETE | `/api/hr/employees/documents/{docId}` | `documents.manage` |
| GET | `/api/hr/employees/documents/{docId}/download` | `documents.read` |
| GET | `/api/hr/employees/expiring` | `employees.read` |
| GET | `/api/hr/employees/export` | `employees.export` |
| POST | `/api/hr/employees/import` | `employees.create` |
| GET | `/api/hr/employees/org-chart` | `employees.read` |
| DELETE | `/api/hr/employees/scheduled-changes/{changeId}` | `employees.update` |
| GET | `/api/hr/employees/{id}` | `employees.read` |
| PATCH | `/api/hr/employees/{id}` | `employees.update` |
| POST | `/api/hr/employees/{id}/anonymize` | `hrconfig.manage` |
| GET | `/api/hr/employees/{id}/branch` | `employees.read` |
| PUT | `/api/hr/employees/{id}/branch` | `employees.update` |
| GET | `/api/hr/employees/{id}/checklist` | `employees.read` |
| POST | `/api/hr/employees/{id}/checklist` | `employees.update` |
| GET | `/api/hr/employees/{id}/custom-fields` | `employees.read` |
| PUT | `/api/hr/employees/{id}/custom-fields` | `employees.update` |
| GET | `/api/hr/employees/{id}/documents` | `documents.read` |
| POST | `/api/hr/employees/{id}/documents` | `documents.manage` |
| POST | `/api/hr/employees/{id}/employment` | `employees.update` |
| DELETE | `/api/hr/employees/{id}/photo` | `employees.update` |
| GET | `/api/hr/employees/{id}/photo` | *self-service* |
| PUT | `/api/hr/employees/{id}/photo` | `employees.update` |
| POST | `/api/hr/employees/{id}/terminate` | `employees.update` |

## /api/hr/forms

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/forms` | `hrconfig.manage` |
| POST | `/api/hr/forms` | `hrconfig.manage` |
| PUT | `/api/hr/forms/{id}` | `hrconfig.manage` |
| POST | `/api/hr/forms/{id}/close` | `hrconfig.manage` |
| POST | `/api/hr/forms/{id}/open` | `hrconfig.manage` |
| GET | `/api/hr/forms/{id}/results` | `hrconfig.manage` |

## /api/hr/helpdesk

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/helpdesk` | *self-service* |
| POST | `/api/hr/helpdesk` | *self-service* |
| GET | `/api/hr/helpdesk/{id}` | *self-service* |
| POST | `/api/hr/helpdesk/{id}/messages` | *self-service* |
| POST | `/api/hr/helpdesk/{id}/status` | *self-service* |

## /api/hr/issued-letters

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/issued-letters` | `employees.read` |
| POST | `/api/hr/issued-letters` | `employees.update` |
| POST | `/api/hr/issued-letters/preview` | `employees.update` |
| GET | `/api/hr/issued-letters/{id}/pdf` | *self-service* |

## /api/hr/leave

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/leave/balances` | *self-service* |
| POST | `/api/hr/leave/balances/adjust` | `leave.manage` |
| GET | `/api/hr/leave/holidays` | *self-service* |
| POST | `/api/hr/leave/holidays` | `leave.manage` |
| DELETE | `/api/hr/leave/holidays/{id}` | `leave.manage` |
| GET | `/api/hr/leave/requests` | *self-service* |
| POST | `/api/hr/leave/requests` | *self-service* |
| POST | `/api/hr/leave/requests/{id}/approve` | `leave.approve` |
| POST | `/api/hr/leave/requests/{id}/cancel` | *self-service* |
| POST | `/api/hr/leave/requests/{id}/reject` | `leave.approve` |
| GET | `/api/hr/leave/types` | *self-service* |
| POST | `/api/hr/leave/types` | `leave.manage` |
| PATCH | `/api/hr/leave/types/{id}` | `leave.manage` |

## /api/hr/legal-entities

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/legal-entities` | `employees.read` |
| POST | `/api/hr/legal-entities` | `hrconfig.manage` |
| PATCH | `/api/hr/legal-entities/{id}` | `hrconfig.manage` |

## /api/hr/letter-templates

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/letter-templates` | `employees.read` |
| POST | `/api/hr/letter-templates` | `hrconfig.manage` |
| PUT | `/api/hr/letter-templates/{id}` | `hrconfig.manage` |

## /api/hr/letters

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/letters/` | `employees.update` |
| POST | `/api/hr/letters/{id}/approve` | `employees.update` |
| GET | `/api/hr/letters/{id}/pdf` | `employees.update` |
| POST | `/api/hr/letters/{id}/reject` | `employees.update` |

## /api/hr/me

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/me/` | *self-service* |
| PATCH | `/api/hr/me/` | *self-service* |
| GET | `/api/hr/me/change-requests` | *self-service* |
| POST | `/api/hr/me/change-requests` | *self-service* |
| POST | `/api/hr/me/change-requests/{id}/cancel` | *self-service* |
| GET | `/api/hr/me/custom-fields` | *self-service* |
| PUT | `/api/hr/me/custom-fields` | *self-service* |
| GET | `/api/hr/me/export` | *self-service* |
| GET | `/api/hr/me/forms` | *self-service* |
| POST | `/api/hr/me/forms/{id}` | *self-service* |
| GET | `/api/hr/me/home` | *self-service* |
| GET | `/api/hr/me/issued-letters` | *self-service* |
| GET | `/api/hr/me/letters` | *self-service* |
| POST | `/api/hr/me/letters` | *self-service* |
| POST | `/api/hr/me/letters/{id}/cancel` | *self-service* |
| GET | `/api/hr/me/letters/{id}/pdf` | *self-service* |
| GET | `/api/hr/me/notification-preferences` | *self-service* |
| PUT | `/api/hr/me/notification-preferences` | *self-service* |
| GET | `/api/hr/me/notifications` | *self-service* |
| POST | `/api/hr/me/notifications/read-all` | *self-service* |
| POST | `/api/hr/me/notifications/{id}/read` | *self-service* |
| GET | `/api/hr/me/report-schedules` | *self-service* |
| POST | `/api/hr/me/report-schedules` | *self-service* |
| DELETE | `/api/hr/me/report-schedules/{id}` | *self-service* |

## /api/hr/org-units

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/org-units` | `employees.read` |
| POST | `/api/hr/org-units` | `hrconfig.manage` |
| PATCH | `/api/hr/org-units/{id}` | `hrconfig.manage` |

## /api/hr/payroll

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/payroll/compensation` | `payroll.read` |
| GET | `/api/hr/payroll/compensation/{employeeId}` | `payroll.read` |
| PUT | `/api/hr/payroll/compensation/{employeeId}` | `payroll.configure` |
| GET | `/api/hr/payroll/grades` | `payroll.read` |
| POST | `/api/hr/payroll/grades` | `payroll.configure` |
| PUT | `/api/hr/payroll/grades/{id}` | `payroll.configure` |
| GET | `/api/hr/payroll/loans` | `payroll.read` |
| POST | `/api/hr/payroll/loans` | `payroll.configure` |
| DELETE | `/api/hr/payroll/loans/{id}` | `payroll.configure` |
| GET | `/api/hr/payroll/payouts` | `payroll.read` |
| POST | `/api/hr/payroll/payouts` | `payroll.run` |
| GET | `/api/hr/payroll/payouts/mine` | *self-service* |
| DELETE | `/api/hr/payroll/payouts/{id}` | `payroll.run` |
| GET | `/api/hr/payroll/payouts/{id}` | `payroll.read` |
| POST | `/api/hr/payroll/payouts/{id}/approve` | `payroll.approve` |
| GET | `/api/hr/payroll/payouts/{id}/bank-file` | `payroll.lock` |
| POST | `/api/hr/payroll/payouts/{id}/calculate` | `payroll.run` |
| POST | `/api/hr/payroll/payouts/{id}/paid` | `payroll.lock` |
| GET | `/api/hr/payroll/payslips` | *self-service* |
| GET | `/api/hr/payroll/payslips/annual/{year}` | *self-service* |
| GET | `/api/hr/payroll/payslips/annual/{year}/pdf` | *self-service* |
| GET | `/api/hr/payroll/payslips/lock` | *self-service* |
| PUT | `/api/hr/payroll/payslips/lock` | *self-service* |
| GET | `/api/hr/payroll/payslips/{itemId}` | *self-service* |
| GET | `/api/hr/payroll/payslips/{itemId}/pdf` | *self-service* |
| GET | `/api/hr/payroll/recurring` | `payroll.read` |
| POST | `/api/hr/payroll/recurring` | `payroll.configure` |
| DELETE | `/api/hr/payroll/recurring/{id}` | `payroll.configure` |
| GET | `/api/hr/payroll/reports/a1` | `payroll.read` |
| GET | `/api/hr/payroll/reports/annual-tax` | `payroll.read` |
| GET | `/api/hr/payroll/reports/labour-cost` | `payroll.read` |
| GET | `/api/hr/payroll/retro` | `payroll.read` |
| GET | `/api/hr/payroll/reviews` | `payroll.read` |
| POST | `/api/hr/payroll/reviews` | `payroll.configure` |
| GET | `/api/hr/payroll/reviews/{id}` | `payroll.read` |
| POST | `/api/hr/payroll/reviews/{id}/apply` | `payroll.configure` |
| POST | `/api/hr/payroll/reviews/{id}/approve` | `payroll.approve` |
| POST | `/api/hr/payroll/reviews/{id}/cancel` | `payroll.configure` |
| POST | `/api/hr/payroll/reviews/{id}/employees` | `payroll.configure` |
| DELETE | `/api/hr/payroll/reviews/{id}/lines/{lineId}` | `payroll.configure` |
| PUT | `/api/hr/payroll/reviews/{id}/lines/{lineId}` | `payroll.configure` |
| POST | `/api/hr/payroll/reviews/{id}/return` | `payroll.approve` |
| POST | `/api/hr/payroll/reviews/{id}/submit` | `payroll.configure` |
| GET | `/api/hr/payroll/rules` | `payroll.read` |
| POST | `/api/hr/payroll/rules` | `payroll.configure` |
| GET | `/api/hr/payroll/runs` | `payroll.read` |
| POST | `/api/hr/payroll/runs` | `payroll.run` |
| DELETE | `/api/hr/payroll/runs/{id}` | `payroll.run` |
| GET | `/api/hr/payroll/runs/{id}` | `payroll.read` |
| POST | `/api/hr/payroll/runs/{id}/approve` | `payroll.approve` |
| GET | `/api/hr/payroll/runs/{id}/bank-file` | `payroll.lock` |
| GET | `/api/hr/payroll/runs/{id}/bpjs` | `payroll.read` |
| POST | `/api/hr/payroll/runs/{id}/calculate` | `payroll.run` |
| POST | `/api/hr/payroll/runs/{id}/compare` | `payroll.read` |
| GET | `/api/hr/payroll/runs/{id}/journal` | `payroll.read` |
| POST | `/api/hr/payroll/runs/{id}/lines` | `payroll.run` |
| DELETE | `/api/hr/payroll/runs/{id}/lines/{lineId}` | `payroll.run` |
| POST | `/api/hr/payroll/runs/{id}/lock` | `payroll.lock` |
| POST | `/api/hr/payroll/runs/{id}/paid` | `payroll.lock` |
| GET | `/api/hr/payroll/runs/{id}/register` | `payroll.read` |
| POST | `/api/hr/payroll/runs/{id}/reject` | `payroll.approve` |
| POST | `/api/hr/payroll/runs/{id}/severance` | `payroll.run` |
| POST | `/api/hr/payroll/runs/{id}/thr` | `payroll.run` |
| POST | `/api/hr/payroll/severance/preview` | `payroll.read` |
| GET | `/api/hr/payroll/wage-advances` | *self-service* |
| POST | `/api/hr/payroll/wage-advances` | *self-service* |
| GET | `/api/hr/payroll/wage-advances/available` | *self-service* |
| POST | `/api/hr/payroll/wage-advances/{id}/cancel` | *self-service* |
| POST | `/api/hr/payroll/wage-advances/{id}/decide` | `payroll.run` |
| POST | `/api/hr/payroll/wage-advances/{id}/disburse` | `payroll.run` |

## /api/hr/projects

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/projects/` | *self-service* |
| POST | `/api/hr/projects/` | `attendance.manage` |
| PUT | `/api/hr/projects/{id}` | `attendance.manage` |

## /api/hr/readiness

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/readiness` | `settings.manage` |

## /api/hr/shifts

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/shifts/` | `attendance.read_all` |
| POST | `/api/hr/shifts/` | `attendance.manage` |
| GET | `/api/hr/shifts/assignments` | `attendance.read_all` |
| PUT | `/api/hr/shifts/assignments/{employeeId}` | `attendance.manage` |
| PATCH | `/api/hr/shifts/{id}` | `attendance.manage` |

## /api/hr/team

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/team` | *self-service* |

## /api/hr/timesheets

| Method | Path | Permission |
|---|---|---|
| POST | `/api/hr/timesheets/decide` | `attendance.approve` |
| GET | `/api/hr/timesheets/pending` | `attendance.approve` |
| GET | `/api/hr/timesheets/report` | `attendance.read_all` |
| GET | `/api/hr/timesheets/week` | *self-service* |
| PUT | `/api/hr/timesheets/week` | *self-service* |

## /api/hr/webhooks

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/webhooks/` | `hrconfig.manage` |
| POST | `/api/hr/webhooks/` | `hrconfig.manage` |
| GET | `/api/hr/webhooks/events` | `hrconfig.manage` |
| DELETE | `/api/hr/webhooks/{id}` | `hrconfig.manage` |
| PATCH | `/api/hr/webhooks/{id}` | `hrconfig.manage` |
| GET | `/api/hr/webhooks/{id}/deliveries` | `hrconfig.manage` |
| POST | `/api/hr/webhooks/{id}/rotate-secret` | `hrconfig.manage` |
| POST | `/api/hr/webhooks/{id}/test` | `hrconfig.manage` |

Total: 285 routes.
