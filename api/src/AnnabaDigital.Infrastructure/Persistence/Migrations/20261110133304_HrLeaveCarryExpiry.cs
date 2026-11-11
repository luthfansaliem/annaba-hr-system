using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrLeaveCarryExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "hr_reminders",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<int>(
                name: "carry_over_expiry_month",
                table: "hr_leave_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_leave_types_expiry_month",
                table: "hr_leave_types",
                sql: "carry_over_expiry_month IS NULL OR carry_over_expiry_month BETWEEN 1 AND 12");

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_ledger_employee_id_leave_type_id_year1",
                table: "hr_leave_ledger",
                columns: new[] { "employee_id", "leave_type_id", "year" },
                unique: true,
                filter: "kind = 'Expire'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_leave_types_expiry_month",
                table: "hr_leave_types");

            migrationBuilder.DropIndex(
                name: "ix_hr_leave_ledger_employee_id_leave_type_id_year1",
                table: "hr_leave_ledger");

            migrationBuilder.DropColumn(
                name: "carry_over_expiry_month",
                table: "hr_leave_types");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "hr_reminders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);
        }
    }
}
