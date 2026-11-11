-- Sample organisation for the screen review (e2e/zz-screens.spec.ts). NOT for production: it writes rows directly, bypassing the application.
-- About 28 people in 5 departments with a year of attendance, leave, claims, overtime and six locked pay runs, so that every
-- analytics screen has something to show. Pseudo-random but repeatable (hash of the row), so two runs give the same pictures.
\set tenant '\'00000000-0000-7000-8000-000000000001\''

insert into hr_legal_entities (id, tenant_id, name, is_default, created_at, updated_at)
select gen_random_uuid(), :tenant::uuid, 'PT Annaba Digital Indonesia', true, now(), now() where not exists (select 1 from hr_legal_entities);

insert into hr_org_units (id, tenant_id, code, name, created_at)
select gen_random_uuid(), :tenant::uuid, v.code, v.name, now()
from (values ('ENG', 'Engineering'), ('FIN', 'Finance'), ('OPS', 'Operations'), ('SAL', 'Sales'), ('PPL', 'People & Culture')) v(code, name)
where not exists (select 1 from hr_org_units o where o.code = v.code);

with names(n, full_name, gender) as (values
  (1, 'Adi Nugroho', 'Male'), (2, 'Ayu Lestari', 'Female'), (3, 'Bambang Setiawan', 'Male'), (4, 'Citra Dewi', 'Female'), (5, 'Dimas Saputra', 'Male'), (6, 'Eka Putri', 'Female'),
  (7, 'Fajar Hidayat', 'Male'), (8, 'Gita Maharani', 'Female'), (9, 'Hendra Wijaya', 'Male'), (10, 'Indah Permata', 'Female'), (11, 'Joko Susilo', 'Male'), (12, 'Kartika Sari', 'Female'),
  (13, 'Lukman Hakim', 'Male'), (14, 'Maya Anggraini', 'Female'), (15, 'Nanda Pratama', 'Male'), (16, 'Oktaviani Rahma', 'Female'), (17, 'Putra Ramadhan', 'Male'), (18, 'Qori Amalia', 'Female'),
  (19, 'Reza Firmansyah', 'Male'), (20, 'Sinta Wulandari', 'Female'), (21, 'Taufik Rahman', 'Male'), (22, 'Umi Kalsum', 'Female'), (23, 'Vino Mahendra', 'Male'), (24, 'Wulan Safitri', 'Female'),
  (25, 'Yoga Prasetyo', 'Male'), (26, 'Zahra Aulia', 'Female'), (27, 'Arif Budiman', 'Male'), (28, 'Bella Kusuma', 'Female'))
insert into hr_employees (id, tenant_id, legal_entity_id, employee_no, full_name, gender, status, hire_date, termination_date, termination_reason, created_at, updated_at, version)
select gen_random_uuid(), :tenant::uuid, (select id from hr_legal_entities order by is_default desc limit 1), 'DEMO-' || lpad(n::text, 3, '0'), full_name, gender,
       case when n in (9, 20, 27) then 'Terminated' else 'Active' end,
       (current_date - ((n * 53) % 1500 + 12))::date,
       case n when 9 then current_date - 40 when 20 then current_date - 130 when 27 then current_date - 250 end,
       case when n in (9, 20, 27) then 'Resigned' end, now(), now(), 1
from names where not exists (select 1 from hr_employees e where e.employee_no = 'DEMO-' || lpad(n::text, 3, '0'));

insert into hr_employments (id, tenant_id, employee_id, effective_from, job_title, type, contract_end, org_unit_id, created_at)
select gen_random_uuid(), e.tenant_id, e.id, e.hire_date,
       (array['Analyst', 'Senior Analyst', 'Specialist', 'Team Lead', 'Officer', 'Engineer'])[1 + right(e.employee_no, 3)::int % 6],
       case when right(e.employee_no, 3)::int % 9 = 0 then 'Contract' when right(e.employee_no, 3)::int % 11 = 0 then 'Probation' when right(e.employee_no, 3)::int % 13 = 0 then 'Intern' else 'Permanent' end,
       case when right(e.employee_no, 3)::int % 9 = 0 then current_date + 45 end,
       (select id from hr_org_units o where o.code = (array['ENG', 'ENG', 'OPS', 'SAL', 'FIN', 'OPS', 'PPL', 'SAL'])[1 + right(e.employee_no, 3)::int % 8]), now()
from hr_employees e where e.employee_no like 'DEMO-%' and not exists (select 1 from hr_employments m where m.employee_id = e.id);

-- the administrator and the plain employee of the e2e seed get an employee record each, so that "my day" has something to show
update hr_employees e set user_id = u.id from users u where e.user_id is null and not exists (select 1 from hr_employees x where x.user_id = u.id)
  and ((e.employee_no = 'DEMO-002' and u.email like 'sarah.%') or (e.employee_no = 'DEMO-005' and u.email like 'rizky.%'));

-- attendance: weekdays of the last 30 days, about 93 % present; clock-in 08:20–09:21 WIB (late after 09:10), eight to nine and a half hours of work; today's sessions are still open
insert into hr_attendance_logs (id, tenant_id, employee_id, date, clock_in, clock_out, source, work_mode, outside_zone)
select gen_random_uuid(), e.tenant_id, e.id, d::date,
       (d::date + time '01:20' + make_interval(mins => abs(hashtext(e.id::text || d::text)) % 62)) at time zone 'UTC',
       case when d::date < current_date then (d::date + time '09:30' + make_interval(mins => abs(hashtext(d::text || e.id::text)) % 90)) at time zone 'UTC' end,
       'clock', (array['Office', 'Office', 'Office', 'Office', 'Remote', 'Remote', 'Field'])[1 + abs(hashtext(e.id::text || 'm' || d::text)) % 7], abs(hashtext(e.id::text || 'z' || d::text)) % 40 = 0
from hr_employees e cross join generate_series(current_date - 29, current_date, interval '1 day') d
where e.employee_no like 'DEMO-%' and e.hire_date <= d::date and (e.termination_date is null or e.termination_date > d::date)
  and extract(isodow from d) < 6 and abs(hashtext(e.id::text || 'p' || d::text)) % 100 < 93
  and not exists (select 1 from hr_attendance_logs l where l.employee_id = e.id and l.date = d::date);

-- leave: a few approved requests per person over the last 12 months, plus pending and refused ones
insert into hr_leave_requests (id, tenant_id, employee_id, leave_type_id, start_date, end_date, half_day, days, reason, status, decided_at, created_at, version, needs_hr_approval)
select gen_random_uuid(), e.tenant_id, e.id,
       (select id from hr_leave_types t where t.code = (array['ANNUAL', 'ANNUAL', 'ANNUAL', 'SICK', 'SICK', 'MARRIAGE', 'UNPAID'])[1 + abs(hashtext(e.id::text || k::text)) % 7] limit 1),
       s.start_date, s.start_date + s.len - 1, false, s.len, 'Sample', s.status,
       case when s.status in ('Approved', 'Rejected') then s.created + make_interval(hours => 3 + abs(hashtext(e.id::text || 'h' || k::text)) % 60) end, s.created, 1, false
from hr_employees e cross join generate_series(1, 4) k
cross join lateral (select (current_date - (abs(hashtext(e.id::text || 'l' || k::text)) % 330) + 12)::date as start_date, 1 + abs(hashtext(e.id::text || 'n' || k::text)) % 3 as len,
                           (array['Approved', 'Approved', 'Approved', 'Approved', 'Approved', 'Pending', 'Rejected'])[1 + abs(hashtext(e.id::text || 's' || k::text)) % 7] as status,
                           now() - make_interval(days => abs(hashtext(e.id::text || 'l' || k::text)) % 330 + 3) as created) s
where e.employee_no like 'DEMO-%' and e.status = 'Active' and exists (select 1 from hr_leave_types)
  and not exists (select 1 from hr_leave_requests r where r.employee_id = e.id and r.reason = 'Sample' and r.start_date = s.start_date);
-- a pending request cannot lie in the past, and nobody is on approved leave on a day they clocked in
update hr_leave_requests set start_date = current_date + 5, end_date = current_date + 5 + (end_date - start_date) where reason = 'Sample' and status = 'Pending' and start_date < current_date;
delete from hr_attendance_logs l using hr_leave_requests r where r.reason = 'Sample' and r.status = 'Approved' and r.employee_id = l.employee_id and l.date between r.start_date and r.end_date;

insert into hr_overtime_requests (id, tenant_id, employee_id, date, hours, reason, status, decided_at, created_at, version)
select gen_random_uuid(), e.tenant_id, e.id, (current_date - abs(hashtext(e.id::text || 'o' || k::text)) % 170)::date, 1 + abs(hashtext(e.id::text || 'oh' || k::text)) % 4, 'Sample',
       (array['Approved', 'Approved', 'Approved', 'Pending'])[1 + abs(hashtext(e.id::text || 'os' || k::text)) % 4], now(), now() - interval '1 day', 1
from hr_employees e cross join generate_series(1, 3) k
where e.employee_no like 'DEMO-%' and e.status = 'Active' and right(e.employee_no, 3)::int % 2 = 0 and not exists (select 1 from hr_overtime_requests o where o.employee_id = e.id and o.reason = 'Sample');

insert into hr_expense_claims (id, tenant_id, employee_id, category, expense_date, amount, description, status, decided_at, created_at, version)
select gen_random_uuid(), e.tenant_id, e.id, (array['transport', 'travel', 'meal', 'medical', 'training', 'other', 'transport', 'meal'])[1 + abs(hashtext(e.id::text || 'c' || k::text)) % 8],
       (current_date - abs(hashtext(e.id::text || 'cd' || k::text)) % 340)::date, (50 + abs(hashtext(e.id::text || 'ca' || k::text)) % 950) * 1000, 'Sample',
       s.status, case when s.status <> 'Pending' then now() - make_interval(days => abs(hashtext(e.id::text || 'cd' || k::text)) % 340) + make_interval(hours => 4 + abs(hashtext(e.id::text || 'ch' || k::text)) % 70) end,
       now() - make_interval(days => abs(hashtext(e.id::text || 'cd' || k::text)) % 340), 1
from hr_employees e cross join generate_series(1, 5) k
cross join lateral (select (array['Paid', 'Paid', 'Paid', 'Approved', 'Pending', 'Rejected'])[1 + abs(hashtext(e.id::text || 'cs' || k::text)) % 6] as status) s
where e.employee_no like 'DEMO-%' and e.status = 'Active' and not exists (select 1 from hr_expense_claims c where c.employee_id = e.id and c.description = 'Sample');

insert into hr_loans (id, tenant_id, employee_id, principal, installment, remaining, note, status, created_at)
select gen_random_uuid(), e.tenant_id, e.id, 6000000, 500000, 500000 * (2 + right(e.employee_no, 3)::int % 9), 'Sample', 'Active', now()
from hr_employees e where e.employee_no in ('DEMO-003', 'DEMO-011', 'DEMO-018') and not exists (select 1 from hr_loans l where l.employee_id = e.id);

-- pay: the six months before this one, locked. Items first (a locked run refuses new rows), then the totals and the lock.
insert into hr_pay_runs (id, tenant_id, year, month, status, rules_version, rules_json, created_by, created_at, note, employee_count, total_gross, total_tax, total_net, total_employer_cost, version, deduct_absence, legal_entity_id)
select gen_random_uuid(), :tenant::uuid, extract(year from m)::int, extract(month from m)::int, 'Calculated', 'ID-2026.1', '{}'::jsonb, (select id from users order by created_at limit 1), now(), 'Sample', 0, 0, 0, 0, 0, 1, false,
       (select id from hr_legal_entities order by is_default desc limit 1)
from generate_series(date_trunc('month', current_date) - interval '6 months', date_trunc('month', current_date) - interval '1 month', interval '1 month') m
where not exists (select 1 from hr_pay_runs r where r.year = extract(year from m)::int and r.month = extract(month from m)::int);

insert into hr_pay_run_items (id, tenant_id, pay_run_id, employee_id, employee_no, employee_name, ptkp, basic, allowance, prorate_factor, unpaid_deduction, taxable_extras, non_taxable_extras, other_deductions, gross, taxable_gross,
                              kes_employee, jht_employee, jp_employee, kes_employer, jht_employer, jp_employer, jkk_employer, jkm_employer, tax, tax_method, net, employer_cost, overtime_pay)
select gen_random_uuid(), r.tenant_id, r.id, e.id, e.employee_no, e.full_name, 'TK0', p.basic, p.allowance, 1, 0, p.extras, 0, 0, p.gross, p.gross,
       round(p.basic * 0.01), round(p.basic * 0.02), round(p.basic * 0.01), round(p.basic * 0.04), round(p.basic * 0.037), round(p.basic * 0.02), round(p.basic * 0.0024), round(p.basic * 0.003),
       round(p.gross * 0.045), 'TER', p.gross - round(p.basic * 0.04) - round(p.gross * 0.045), p.gross + round(p.basic * 0.0994), p.overtime
from hr_pay_runs r join hr_employees e on e.employee_no like 'DEMO-%' and e.hire_date <= make_date(r.year, r.month, 1) and (e.termination_date is null or e.termination_date > make_date(r.year, r.month, 28))
cross join lateral (select b.basic, b.allowance, b.overtime, b.extras, b.basic + b.allowance + b.overtime + b.extras as gross
                    from (select (6 + right(e.employee_no, 3)::int % 14) * 1000000::numeric as basic, (5 + right(e.employee_no, 3)::int % 10) * 100000::numeric as allowance,
                                 (abs(hashtext(e.id::text || r.id::text)) % 5) * 150000::numeric as overtime,
                                 case when abs(hashtext(r.id::text || e.id::text)) % 9 = 0 then 1500000::numeric else 0 end as extras) b) p
where r.status = 'Calculated' and r.note = 'Sample' and not exists (select 1 from hr_pay_run_items i where i.pay_run_id = r.id);

update hr_pay_runs r set employee_count = s.n, total_gross = s.gross, total_tax = s.tax, total_net = s.net, total_employer_cost = s.cost, status = 'Locked', locked_at = now()
from (select pay_run_id, count(*) n, sum(gross) gross, sum(tax) tax, sum(net) net, sum(employer_cost) cost from hr_pay_run_items group by pay_run_id) s
where s.pay_run_id = r.id and r.status = 'Calculated' and r.note = 'Sample';

select (select count(*) from hr_employees) as employees, (select count(*) from hr_attendance_logs) as attendance, (select count(*) from hr_leave_requests) as leave,
       (select count(*) from hr_expense_claims) as claims, (select count(*) from hr_pay_runs where status = 'Locked') as locked_runs;
