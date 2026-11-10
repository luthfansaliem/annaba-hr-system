# 27 — HR System: Data Migration and Cutover Plan

## 1. Order of work
1. **Reference data:** departments (*Departments*), leave types and quotas, holidays and collective leave for the year (*Leave Setup*).
2. **Employees:** *Employees → Import CSV*. Use **Check file** first: every problem is listed with its line number and nothing is written until the whole file is clean (all-or-nothing). Include `manager_no`; managers may appear later in the file. Protected columns (`national_id`, `tax_id`, `bank_name`, `bank_account`) need an HR admin who has just re-entered their password. Running the same file twice cannot double-load (employee numbers must be unique).
3. **Accounts:** invite users (*Users → Import CSV*), then link each login to its employee (*Add Employee → Sign-in account*, or ask HR to link existing ones). Without the link a person cannot use My Leave, My Attendance or My Payslips.
4. **Compensation:** set salary, allowance, PTKP, NPWP and BPJS enrolment per employee (*Compensation*). **If you start in the middle of a year**, fill "Year-to-date from the previous system" (taxable income, JHT+JP paid, PPh 21 withheld, months paid) so the December settlement is correct.
5. **Leave balances:** annual leave is granted automatically once 12 months of service are complete; for balances already partly used, post an *Adjustment* with a note.

## 2. Parallel run (gate: difference = 0)
Run two consecutive months in the system **and** in the current process. For each employee compare gross, BPJS, PPh 21 and net; investigate every rupiah. Exit criteria: two months with zero unexplained difference, signed by the payroll owner and the tax consultant. Only then pay from the system. Keep the old process available for one more month as the rollback.

## 3. Cutover day
Freeze edits in the old files → final export → import → compare headcount and total salary to the old totals → open the month's pay run. Rollback: stop using the system, pay from the old process, restore the pre-cutover backup (`scripts/restore-drill.sh` proves the procedure).

## 4. What the migration does not cover yet
Employee documents, historical payslips before cutover, loan balances, overtime history. Plan them as separate loads or keep them in the old archive.
