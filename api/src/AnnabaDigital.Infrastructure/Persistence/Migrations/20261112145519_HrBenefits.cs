using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrBenefits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "advance_id",
                table: "hr_pay_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "wage_advance_id",
                table: "hr_pay_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "interest_rate_pct",
                table: "hr_loans",
                type: "numeric(6,3)",
                precision: 6,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "tenor_months",
                table: "hr_loans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "total_payable",
                table: "hr_loans",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "taxable",
                table: "hr_expense_claims",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "grade_id",
                table: "hr_compensations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_cash_advances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    purpose = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    needed_by = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    disbursed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    disbursed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    spent_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    settlement_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    receipt_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    receipt_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    receipt = table.Column<byte[]>(type: "bytea", nullable: true),
                    settlement_submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settlement_approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    settled_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_cash_advances", x => x.id);
                    table.CheckConstraint("ck_hr_cash_advances_amounts", "amount > 0 AND (spent_amount IS NULL OR spent_amount >= 0)");
                    table.ForeignKey(
                        name: "fk_hr_cash_advances_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_salary_grades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    min_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mid_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_pay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_salary_grades", x => x.id);
                    table.CheckConstraint("ck_hr_salary_grades_range", "min_pay >= 0 AND min_pay <= mid_pay AND mid_pay <= max_pay");
                });

            migrationBuilder.CreateTable(
                name: "hr_salary_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_salary_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_wage_advances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    disbursed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    recovered_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_wage_advances", x => x.id);
                    table.CheckConstraint("ck_hr_wage_advances_amount", "amount > 0 AND month BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "fk_hr_wage_advances_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_claim_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    grade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    annual_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    per_claim_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    taxable = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_claim_policies", x => x.id);
                    table.CheckConstraint("ck_hr_claim_policies_limits", "(annual_limit IS NULL OR annual_limit > 0) AND (per_claim_limit IS NULL OR per_claim_limit > 0)");
                    table.ForeignKey(
                        name: "fk_hr_claim_policies_hr_salary_grades_grade_id",
                        column: x => x.grade_id,
                        principalTable: "hr_salary_grades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_salary_review_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_enc = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    proposed_enc = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    grade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    apply_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_salary_review_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_salary_review_items_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_salary_review_items_hr_salary_reviews_review_id",
                        column: x => x.review_id,
                        principalTable: "hr_salary_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_compensations_grade_id",
                table: "hr_compensations",
                column: "grade_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_cash_advances_employee_id_status",
                table: "hr_cash_advances",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_claim_policies_grade_id",
                table: "hr_claim_policies",
                column: "grade_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_claim_policies_tenant_id_category_grade_id",
                table: "hr_claim_policies",
                columns: new[] { "tenant_id", "category", "grade_id" },
                unique: true,
                filter: "active")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_hr_salary_grades_tenant_id_code",
                table: "hr_salary_grades",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_salary_review_items_employee_id",
                table: "hr_salary_review_items",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_salary_review_items_review_id_employee_id",
                table: "hr_salary_review_items",
                columns: new[] { "review_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_wage_advances_employee_id_year_month",
                table: "hr_wage_advances",
                columns: new[] { "employee_id", "year", "month" });

            migrationBuilder.AddForeignKey(
                name: "fk_hr_compensations_salary_grades_grade_id",
                table: "hr_compensations",
                column: "grade_id",
                principalTable: "hr_salary_grades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hr_compensations_salary_grades_grade_id",
                table: "hr_compensations");

            migrationBuilder.DropTable(
                name: "hr_cash_advances");

            migrationBuilder.DropTable(
                name: "hr_claim_policies");

            migrationBuilder.DropTable(
                name: "hr_salary_review_items");

            migrationBuilder.DropTable(
                name: "hr_wage_advances");

            migrationBuilder.DropTable(
                name: "hr_salary_grades");

            migrationBuilder.DropTable(
                name: "hr_salary_reviews");

            migrationBuilder.DropIndex(
                name: "ix_hr_compensations_grade_id",
                table: "hr_compensations");

            migrationBuilder.DropColumn(
                name: "advance_id",
                table: "hr_pay_run_lines");

            migrationBuilder.DropColumn(
                name: "wage_advance_id",
                table: "hr_pay_run_lines");

            migrationBuilder.DropColumn(
                name: "interest_rate_pct",
                table: "hr_loans");

            migrationBuilder.DropColumn(
                name: "tenor_months",
                table: "hr_loans");

            migrationBuilder.DropColumn(
                name: "total_payable",
                table: "hr_loans");

            migrationBuilder.DropColumn(
                name: "taxable",
                table: "hr_expense_claims");

            migrationBuilder.DropColumn(
                name: "grade_id",
                table: "hr_compensations");
        }
    }
}
