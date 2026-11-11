-- Database roles with the least privilege each job needs. Run once as the database owner:
--     psql -v ON_ERROR_STOP=1 -d annaba_digital -f ops/sql/least-privilege-roles.sql
-- then give a password to each role you will actually use:   ALTER ROLE annaba_hr_reporting PASSWORD '...';
-- Safe to run again: it creates only what is missing and re-applies the grants. Run it again after every release (new tables stay private until listed here).
--
-- Why: pay-run amounts are stored as numbers so that the database can total, compare and freeze them (docs/25-hr-threat-model.md, "Pay-run amounts at rest").
-- What keeps them private is therefore who may read the tables, encryption of the disk and of every backup, and the audit trail. This file is the first of those.
--
--   annaba_hr_reporting   read-only, for dashboards and analysts: organisation, attendance and leave. NO pay, NO identity numbers, NO location, NO documents, NO sessions or secrets.
--   annaba_hr_backup      may read everything (pg_dump needs that) and change nothing. Use it for the backup job instead of the owner.
--   annaba_hr_monitor     may read server statistics only (connections, locks, sizes). For a database exporter.
--
-- The application itself keeps using the owner role (it runs the migrations). The roles are created without a password: nobody can sign in with them until you set one.

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'annaba_hr_reporting') THEN CREATE ROLE annaba_hr_reporting LOGIN NOINHERIT CONNECTION LIMIT 5; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'annaba_hr_backup') THEN CREATE ROLE annaba_hr_backup LOGIN NOINHERIT CONNECTION LIMIT 2; END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'annaba_hr_monitor') THEN CREATE ROLE annaba_hr_monitor LOGIN CONNECTION LIMIT 2; END IF;
END $$;

-- start from nothing
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM PUBLIC, annaba_hr_reporting, annaba_hr_backup, annaba_hr_monitor;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- backup: read everything, write nothing
GRANT USAGE ON SCHEMA public TO annaba_hr_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO annaba_hr_backup;
GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO annaba_hr_backup;
ALTER ROLE annaba_hr_backup SET default_transaction_read_only = on;

-- monitoring: statistics views only, no application tables
GRANT pg_monitor TO annaba_hr_monitor;

-- reporting: an explicit list. A table that is not named here cannot be read.
GRANT USAGE ON SCHEMA public TO annaba_hr_reporting;
ALTER ROLE annaba_hr_reporting SET default_transaction_read_only = on;
ALTER ROLE annaba_hr_reporting SET statement_timeout = '30s';
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY[
    'hr_org_units', 'hr_legal_entities', 'hr_work_locations', 'hr_shifts', 'hr_shift_assignments', 'hr_holidays',
    'hr_employments', 'hr_leave_types', 'hr_leave_requests', 'hr_leave_ledger', 'hr_overtime_requests', 'hr_attendance_periods'
  ] LOOP
    IF to_regclass('public.' || t) IS NOT NULL THEN EXECUTE format('GRANT SELECT ON %I TO annaba_hr_reporting', t); ELSE RAISE NOTICE 'table % does not exist in this version, skipped', t; END IF;
  END LOOP;
  -- people: enough to count and group them; no identity or tax numbers, bank details, address, contact data, religion or family data
  EXECUTE 'GRANT SELECT (id, tenant_id, employee_no, full_name, gender, status, hire_date, termination_date, legal_entity_id, created_at) ON hr_employees TO annaba_hr_reporting';
  -- attendance: when and how, not where (no IP address, no coordinates)
  EXECUTE 'GRANT SELECT (id, tenant_id, employee_id, date, clock_in, clock_out, source, outside_zone, work_mode) ON hr_attendance_logs TO annaba_hr_reporting';
END $$;

-- Proof, printed at the end. Every row must say "f": the reporting role cannot read pay, loans, claims, documents, photos, sign-in data or the audit trail.
SELECT c.relname AS must_not_be_readable, has_table_privilege('annaba_hr_reporting', c.oid, 'SELECT') AS readable_by_reporting
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname = 'public' AND c.relkind = 'r'
  AND (c.relname LIKE 'hr_pay%' OR c.relname LIKE 'hr_weekly%' OR c.relname IN ('hr_compensations', 'hr_recurring_pay_items', 'hr_retro_adjustments', 'hr_loans', 'hr_expense_claims',
       'hr_employee_documents', 'hr_photos', 'hr_document_seals', 'users', 'sessions', 'user_credentials', 'mfa_factors', 'scim_tokens', 'system_settings', 'audit_events'))
ORDER BY 1;
