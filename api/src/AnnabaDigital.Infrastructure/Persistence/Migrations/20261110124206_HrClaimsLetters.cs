using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrClaimsLetters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claim_id",
                table: "hr_pay_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_employment_letters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: true),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_employment_letters", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_employment_letters_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_expense_claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    receipt_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    receipt_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    receipt = table.Column<byte[]>(type: "bytea", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    paid_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_expense_claims", x => x.id);
                    table.CheckConstraint("ck_hr_claims_amount", "amount > 0");
                    table.CheckConstraint("ck_hr_claims_receipt", "receipt IS NULL OR octet_length(receipt) <= 5242880");
                    table.ForeignKey(
                        name: "fk_hr_expense_claims_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_employment_letters_employee_id_status",
                table: "hr_employment_letters",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_employment_letters_tenant_id_number",
                table: "hr_employment_letters",
                columns: new[] { "tenant_id", "number" },
                unique: true,
                filter: "number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_expense_claims_employee_id_status",
                table: "hr_expense_claims",
                columns: new[] { "employee_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_employment_letters");

            migrationBuilder.DropTable(
                name: "hr_expense_claims");

            migrationBuilder.DropColumn(
                name: "claim_id",
                table: "hr_pay_run_lines");
        }
    }
}
