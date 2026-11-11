using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrSelfService : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anonymized_at",
                table: "hr_employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    value_enc = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_change_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_change_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_change_requests_employee_id",
                table: "hr_change_requests",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_change_requests_tenant_id_status",
                table: "hr_change_requests",
                columns: new[] { "tenant_id", "status" });

            // The one controlled exception to "a locked pay run never changes": erasing a person's NAME from its items, only inside a transaction that set hr.anonymizing (AnonymizationService)
            // and only when every figure and the employee link are untouched.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION hr_pay_children_guard() RETURNS trigger AS $$
BEGIN
  IF EXISTS (SELECT 1 FROM hr_pay_runs r WHERE r.id = COALESCE(OLD.pay_run_id, NEW.pay_run_id) AND r.status IN ('Locked', 'Paid')) THEN
    IF TG_OP = 'UPDATE' AND TG_TABLE_NAME = 'hr_pay_run_items' AND current_setting('hr.anonymizing', true) = 'on'
       AND NEW.pay_run_id = OLD.pay_run_id AND NEW.employee_id = OLD.employee_id AND NEW.employee_no = OLD.employee_no
       AND NEW.basic = OLD.basic AND NEW.allowance = OLD.allowance AND NEW.gross = OLD.gross AND NEW.taxable_gross = OLD.taxable_gross AND NEW.tax = OLD.tax AND NEW.net = OLD.net
       AND NEW.employer_cost = OLD.employer_cost AND NEW.overtime_pay = OLD.overtime_pay THEN
      RETURN NEW;
    END IF;
    RAISE EXCEPTION 'pay run is locked: its items and lines cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
  END IF;
  RETURN COALESCE(NEW, OLD);
END $$ LANGUAGE plpgsql;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION hr_pay_children_guard() RETURNS trigger AS $$
BEGIN
  IF EXISTS (SELECT 1 FROM hr_pay_runs r WHERE r.id = COALESCE(OLD.pay_run_id, NEW.pay_run_id) AND r.status IN ('Locked', 'Paid')) THEN
    RAISE EXCEPTION 'pay run is locked: its items and lines cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
  END IF;
  RETURN COALESCE(NEW, OLD);
END $$ LANGUAGE plpgsql;
");

            migrationBuilder.DropTable(
                name: "hr_change_requests");

            migrationBuilder.DropColumn(
                name: "anonymized_at",
                table: "hr_employees");
        }
    }
}
