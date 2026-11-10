using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrPayroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_compensations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    basic_enc = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    allowance_enc = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ptkp_status = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    has_npwp = table.Column<bool>(type: "boolean", nullable: false),
                    kes_enrolled = table.Column<bool>(type: "boolean", nullable: false),
                    tk_enrolled = table.Column<bool>(type: "boolean", nullable: false),
                    jkk_rate = table.Column<decimal>(type: "numeric(6,5)", precision: 6, scale: 5, nullable: false),
                    opening_year = table.Column<int>(type: "integer", nullable: true),
                    opening_months = table.Column<int>(type: "integer", nullable: false),
                    opening_enc = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    change_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_compensations", x => x.id);
                    table.CheckConstraint("ck_hr_comp_ptkp", "ptkp_status IN ('TK0','TK1','TK2','TK3','K0','K1','K2','K3')");
                    table.CheckConstraint("ck_hr_comp_range", "effective_to IS NULL OR effective_to >= effective_from");
                    table.ForeignKey(
                        name: "fk_hr_compensations_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_pay_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rules_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    employee_count = table.Column<int>(type: "integer", nullable: false),
                    total_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_employer_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_pay_runs", x => x.id);
                    table.CheckConstraint("ck_hr_pay_runs_month", "month BETWEEN 1 AND 12");
                });

            migrationBuilder.CreateTable(
                name: "hr_pay_run_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pay_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ptkp = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    basic = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    allowance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    prorate_factor = table.Column<decimal>(type: "numeric(7,6)", precision: 7, scale: 6, nullable: false),
                    unpaid_deduction = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable_extras = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    non_taxable_extras = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    other_deductions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    kes_employee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    jht_employee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    jp_employee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    kes_employer = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    jht_employer = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    jp_employer = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    jkk_employer = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    jkm_employer = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_method = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    employer_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    warnings = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_pay_run_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_pay_run_items_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_pay_run_items_hr_pay_runs_pay_run_id",
                        column: x => x.pay_run_id,
                        principalTable: "hr_pay_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_pay_run_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pay_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_pay_run_lines", x => x.id);
                    table.CheckConstraint("ck_hr_pay_run_lines_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_hr_pay_run_lines_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_pay_run_lines_hr_pay_runs_pay_run_id",
                        column: x => x.pay_run_id,
                        principalTable: "hr_pay_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_compensations_employee_id",
                table: "hr_compensations",
                column: "employee_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_run_items_employee_id",
                table: "hr_pay_run_items",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_run_items_pay_run_id_employee_id",
                table: "hr_pay_run_items",
                columns: new[] { "pay_run_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_run_lines_employee_id",
                table: "hr_pay_run_lines",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_run_lines_pay_run_id_employee_id",
                table: "hr_pay_run_lines",
                columns: new[] { "pay_run_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_runs_tenant_id_year_month",
                table: "hr_pay_runs",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);

            // A locked pay run is evidence of what was paid. Refuse changes at the database too, so no bug, script or hand-typed SQL by the application's own login can rewrite it.
            migrationBuilder.Sql(@"
CREATE FUNCTION hr_pay_children_guard() RETURNS trigger AS $$
BEGIN
  IF EXISTS (SELECT 1 FROM hr_pay_runs r WHERE r.id = COALESCE(OLD.pay_run_id, NEW.pay_run_id) AND r.status IN ('Locked', 'Paid')) THEN
    RAISE EXCEPTION 'pay run is locked: its items and lines cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
  END IF;
  RETURN COALESCE(NEW, OLD);
END $$ LANGUAGE plpgsql;
CREATE TRIGGER trg_hr_pay_run_items_guard BEFORE INSERT OR UPDATE OR DELETE ON hr_pay_run_items FOR EACH ROW EXECUTE FUNCTION hr_pay_children_guard();
CREATE TRIGGER trg_hr_pay_run_lines_guard BEFORE INSERT OR UPDATE OR DELETE ON hr_pay_run_lines FOR EACH ROW EXECUTE FUNCTION hr_pay_children_guard();

CREATE FUNCTION hr_pay_runs_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    IF OLD.status IN ('Locked', 'Paid') THEN RAISE EXCEPTION 'a locked pay run cannot be deleted' USING ERRCODE = 'integrity_constraint_violation'; END IF;
    RETURN OLD;
  END IF;
  IF OLD.status IN ('Locked', 'Paid') THEN
    IF NEW.year <> OLD.year OR NEW.month <> OLD.month OR NEW.total_gross <> OLD.total_gross OR NEW.total_tax <> OLD.total_tax OR NEW.total_net <> OLD.total_net
       OR NEW.total_employer_cost <> OLD.total_employer_cost OR NEW.employee_count <> OLD.employee_count OR NEW.rules_version <> OLD.rules_version THEN
      RAISE EXCEPTION 'a locked pay run cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    IF OLD.status = 'Paid' AND NEW.status <> 'Paid' THEN RAISE EXCEPTION 'a paid pay run cannot be reopened' USING ERRCODE = 'integrity_constraint_violation'; END IF;
    IF OLD.status = 'Locked' AND NEW.status NOT IN ('Locked', 'Paid') THEN RAISE EXCEPTION 'a locked pay run cannot be reopened' USING ERRCODE = 'integrity_constraint_violation'; END IF;
  END IF;
  RETURN NEW;
END $$ LANGUAGE plpgsql;
CREATE TRIGGER trg_hr_pay_runs_guard BEFORE UPDATE OR DELETE ON hr_pay_runs FOR EACH ROW EXECUTE FUNCTION hr_pay_runs_guard();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_hr_pay_runs_guard ON hr_pay_runs; DROP TRIGGER IF EXISTS trg_hr_pay_run_lines_guard ON hr_pay_run_lines; DROP TRIGGER IF EXISTS trg_hr_pay_run_items_guard ON hr_pay_run_items; DROP FUNCTION IF EXISTS hr_pay_runs_guard(); DROP FUNCTION IF EXISTS hr_pay_children_guard();");

            migrationBuilder.DropTable(
                name: "hr_compensations");

            migrationBuilder.DropTable(
                name: "hr_pay_run_items");

            migrationBuilder.DropTable(
                name: "hr_pay_run_lines");

            migrationBuilder.DropTable(
                name: "hr_pay_runs");
        }
    }
}
