# Enterprise Auth & User Management Starter Kit — Documentation

Status: **Phase 2 prototype implemented — awaiting Product Owner review.**
No backend has been implemented. Per the approved workflow, backend work starts only after the prototype is approved.

| Doc | Purpose | Phase |
|---|---|---|
| [00-audit.md](00-audit.md) | Repository baseline, reusable assets, defects | 1 |
| [01-prd.md](01-prd.md) | PRD with requirement IDs, priorities, acceptance mapping | 1 |
| [02-user-flows.md](02-user-flows.md) | End-to-end journeys incl. failure paths | 1 |
| [03-ia-screen-inventory.md](03-ia-screen-inventory.md) | Menu hierarchy, route map, screen × state inventory | 1 |
| [04-decisions-and-assumptions.md](04-decisions-and-assumptions.md) | Pending decisions with recommended defaults, assumptions | 1 |
| [05-prototype-review.md](05-prototype-review.md) | Prototype run guide, coverage, verification evidence, scorecard template | 2 |
| [06-architecture.md](06-architecture.md) | Layers, request lifecycle, module layout, extension points | 3 |
| [07-data-model.md](07-data-model.md) | ERD, tables, constraints, state machines, retention | 3 |
| [08-api-spec.md](08-api-spec.md) | Endpoint contracts, permissions, errors, rate limits, headers | 3 |
| [09-rbac-matrix.md](09-rbac-matrix.md) | Role × permission matrix and authorization invariants | 3 |
| [10-security-and-threat-model.md](10-security-and-threat-model.md) | Security design, STRIDE threat model, ASVS mapping | 3 |
| [11-test-plan.md](11-test-plan.md) | Strategy, test catalogue by requirement, release gate | 3 |
| [12-migration-and-rollback.md](12-migration-and-rollback.md) | Migrations, deployment sequence, rollback, key rotation | 3 |
| [13-adr-dotnet-backend.md](13-adr-dotnet-backend.md) | ADR: backend in ASP.NET Core (.NET 10), hosting & integration impact | 4 |
| [14-phase4-slice1-report.md](14-phase4-slice1-report.md) | Slice 1 (foundation + auth core): what exists, evidence, gaps | 4 |
| [15-phase4-slice2-report.md](15-phase4-slice2-report.md) | Slice 2 (registration, verification, recovery, invitation acceptance) | 4 |
| [16-phase4-slice3-report.md](16-phase4-slice3-report.md) | Slice 3 (MFA: TOTP, recovery codes, second step, policy) | 4 |
| [17-phase4-slice4-report.md](17-phase4-slice4-report.md) | Slice 4 (user & invitation management, hierarchy rule, last-admin guard) | 4 |
| [18-phase4-slice5-report.md](18-phase4-slice5-report.md) | Slice 5 (roles, permission catalog, access matrix) | 4 |
| [19-phase4-slice6-report.md](19-phase4-slice6-report.md) | Slice 6 (sessions, audit/security, dashboards, profile, settings, retention) | 4 |
| [20-phase4-slice7-report.md](20-phase4-slice7-report.md) | Slice 7 (UI cutover to the real API, Playwright journeys/a11y/responsive) | 4 |
| [21-hardening-report.md](21-hardening-report.md) | Hardening: durable e-mail outbox, wider e2e coverage, bell contrast fix | 4 |
| [22-staging-runbook.md](22-staging-runbook.md) | Staging: API Dockerfile, CI workflow, first-admin bootstrap, runbook (nothing deployed) | 4 |
| [23-prd-hr-system.md](23-prd-hr-system.md) | **PRD HR System (production-ready)**: modul, RBAC, NFR, rilis, checklist go-live | HR |
| [24-hr-go-live-runbook.md](24-hr-go-live-runbook.md) | HR go-live and operations: release checklist, SLO evidence, alerts, payroll procedure, incident runbooks | HR |
| [25-hr-threat-model.md](25-hr-threat-model.md) | HR STRIDE threat model with the test that proves each control | HR |
| [26-hr-pdp-compliance.md](26-hr-pdp-compliance.md) | UU PDP working sheet: RoPA starting point, data-subject rights, incident handling | HR |
| [27-hr-cutover-plan.md](27-hr-cutover-plan.md) | Data migration, parallel run and cutover plan | HR |
| [28-hr-api-routes.md](28-hr-api-routes.md) | Every HR API route and the permission it needs (generated from the code; a test keeps it in sync) | HR |
| [29-hr-webhooks.md](29-hr-webhooks.md) | Webhook event schemas, signature verification (Node/Python/C#), retries | HR / integrators |
| [30-hr-user-guide.md](30-hr-user-guide.md) | User guide per role (employee, manager, HR, payroll, administrator), in Indonesian | HR / everyone |
| [31-hr-regulatory-validation.md](31-hr-regulatory-validation.md) | Built-in payroll rules checked against their sources (TER, PTKP, BPJS, overtime, THR, severance): matches, corrections, assumptions and the decisions left for the tax consultant, in Indonesian | HR / payroll / consultant |
| [32-hr-operations-kit.md](32-hr-operations-kit.md) | Monitoring stack, on-call, antivirus, encrypted backups and restore drills, least-privilege database roles, connecting an identity provider (SCIM), parallel payroll run | Operations |
| [33-hr-security-self-assessment.md](33-hr-security-self-assessment.md) | What was security-tested in-house and the results, plus the brief for the external penetration test | Security |
| [34-hr-privacy-pack.md](34-hr-privacy-pack.md) | Legal basis for location, religion and photos, the employee privacy notice and the impact assessment (DPIA) as drafts to approve, in Indonesian | Legal / data protection |
| [35-hr-training-kit.md](35-hr-training-kit.md) | Training sessions per role with hands-on scenarios, quiz and sign-off sheets, in Indonesian | HR / trainers |
| [36-hr-user-manual.md](36-hr-user-manual.md) | Full user manual in Indonesian: what each module is for, step-by-step per role, end-to-end flows, FAQ and glossary, with screenshots in `manual/` | All users |
| [openapi-v1.json](openapi-v1.json) | OpenAPI 3 description of the whole API (generated; also served at `/api/openapi/v1.json`) | integrators |

## Phase tracker
| Phase | State |
|---|---|
| 1 Discovery & Requirements | Done — decisions D-01…D-14 approved by PO as recommended defaults (D-08 density reference still outstanding) |
| 2 UX & Design (interactive prototype, scorecard, PO approval) | **Prototype built — awaiting PO review/approval and scorecard** |
| 3 Technical design (ERD, API, threat model, migration plan) | **Drafted (docs 06–12) — awaiting PO approval**; open items listed in docs 10 §5 |
| 4 Implementation | **Started (PO: "Setuju, mulai Phase 4"; backend = .NET per ADR-013).** Slice 1 (foundation + auth core) **done & verified (70 .NET tests)**; **slices 2–6 done & verified (257 .NET tests total)**; slice 7 (UI cutover + Playwright) not started; UI still on prototype engine |
| 5 Validation | Not started |
| 6 Delivery | Not started |

Deliverables not yet produced: UI/UX prototype, architecture doc, ERD, API spec, RBAC matrix, threat model, test plan/evidence, setup & deployment runbooks, final report.
