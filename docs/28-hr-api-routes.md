# 28 — HR API routes

Generated from the running application's routing table by `HrApiDocTests` — do not edit by hand (`UPDATE_API_DOC=1 dotnet test api/AnnabaDigital.sln --filter HrApiDocTests` regenerates it).
Every route needs a signed-in session. "Permission" is the extra permission the route requires; routes marked *self-service* only ever touch the caller's own data (the service checks it). Errors use the envelope `{ error: { code, message, fields?, requestId } }`; see `08-api-spec.md`. Mutating calls need the `X-CSRF-Token` header like every other API call, and payroll, protected-data and document routes additionally ask for a password check in the last five minutes (`reauth_required`).

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

## /api/hr/attendance

| Method | Path | Permission |
|---|---|---|
| POST | `/api/hr/attendance/clock-in` | *self-service* |
| POST | `/api/hr/attendance/clock-out` | *self-service* |
| GET | `/api/hr/attendance/corrections` | *self-service* |
| POST | `/api/hr/attendance/corrections` | *self-service* |
| POST | `/api/hr/attendance/corrections/{id}/approve` | `attendance.approve` |
| POST | `/api/hr/attendance/corrections/{id}/cancel` | *self-service* |
| POST | `/api/hr/attendance/corrections/{id}/reject` | `attendance.approve` |
| POST | `/api/hr/attendance/import` | `attendance.manage` |
| GET | `/api/hr/attendance/me` | *self-service* |
| GET | `/api/hr/attendance/overtime` | *self-service* |
| POST | `/api/hr/attendance/overtime` | *self-service* |
| POST | `/api/hr/attendance/overtime/{id}/approve` | `attendance.approve` |
| POST | `/api/hr/attendance/overtime/{id}/cancel` | *self-service* |
| POST | `/api/hr/attendance/overtime/{id}/reject` | `attendance.approve` |
| GET | `/api/hr/attendance/periods` | `attendance.read_all` |
| POST | `/api/hr/attendance/periods/{month}/close` | `attendance.manage` |
| POST | `/api/hr/attendance/periods/{month}/reopen` | `attendance.manage` |
| GET | `/api/hr/attendance/report` | `attendance.read_all` |

## /api/hr/change-requests

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/change-requests/` | `employees.update` |
| POST | `/api/hr/change-requests/{id}/approve` | `employees.update` |
| POST | `/api/hr/change-requests/{id}/reject` | `employees.update` |

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
| GET | `/api/hr/employees/{id}` | `employees.read` |
| PATCH | `/api/hr/employees/{id}` | `employees.update` |
| POST | `/api/hr/employees/{id}/anonymize` | `hrconfig.manage` |
| GET | `/api/hr/employees/{id}/checklist` | `employees.read` |
| POST | `/api/hr/employees/{id}/checklist` | `employees.update` |
| GET | `/api/hr/employees/{id}/documents` | `documents.read` |
| POST | `/api/hr/employees/{id}/documents` | `documents.manage` |
| POST | `/api/hr/employees/{id}/employment` | `employees.update` |
| POST | `/api/hr/employees/{id}/terminate` | `employees.update` |

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

## /api/hr/me

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/me/` | *self-service* |
| PATCH | `/api/hr/me/` | *self-service* |
| GET | `/api/hr/me/change-requests` | *self-service* |
| POST | `/api/hr/me/change-requests` | *self-service* |
| POST | `/api/hr/me/change-requests/{id}/cancel` | *self-service* |
| GET | `/api/hr/me/export` | *self-service* |

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
| GET | `/api/hr/payroll/loans` | `payroll.read` |
| POST | `/api/hr/payroll/loans` | `payroll.configure` |
| DELETE | `/api/hr/payroll/loans/{id}` | `payroll.configure` |
| GET | `/api/hr/payroll/payslips` | *self-service* |
| GET | `/api/hr/payroll/payslips/{itemId}` | *self-service* |
| GET | `/api/hr/payroll/payslips/{itemId}/pdf` | *self-service* |
| GET | `/api/hr/payroll/reports/annual-tax` | `payroll.read` |
| GET | `/api/hr/payroll/retro` | `payroll.read` |
| GET | `/api/hr/payroll/runs` | `payroll.read` |
| POST | `/api/hr/payroll/runs` | `payroll.run` |
| DELETE | `/api/hr/payroll/runs/{id}` | `payroll.run` |
| GET | `/api/hr/payroll/runs/{id}` | `payroll.read` |
| POST | `/api/hr/payroll/runs/{id}/approve` | `payroll.approve` |
| GET | `/api/hr/payroll/runs/{id}/bank-file` | `payroll.lock` |
| GET | `/api/hr/payroll/runs/{id}/bpjs` | `payroll.read` |
| POST | `/api/hr/payroll/runs/{id}/calculate` | `payroll.run` |
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

## /api/hr/shifts

| Method | Path | Permission |
|---|---|---|
| GET | `/api/hr/shifts/` | `attendance.read_all` |
| POST | `/api/hr/shifts/` | `attendance.manage` |
| GET | `/api/hr/shifts/assignments` | `attendance.read_all` |
| PUT | `/api/hr/shifts/assignments/{employeeId}` | `attendance.manage` |
| PATCH | `/api/hr/shifts/{id}` | `attendance.manage` |

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

Total: 117 routes.
