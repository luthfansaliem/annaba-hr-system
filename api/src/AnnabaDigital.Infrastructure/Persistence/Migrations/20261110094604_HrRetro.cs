using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrRetro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "retro_id",
                table: "hr_pay_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_retro_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_year = table.Column<int>(type: "integer", nullable: false),
                    from_month = table.Column<int>(type: "integer", nullable: false),
                    through_year = table.Column<int>(type: "integer", nullable: false),
                    through_month = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_retro_adjustments", x => x.id);
                    table.CheckConstraint("ck_hr_retro_nonzero", "amount <> 0");
                    table.ForeignKey(
                        name: "fk_hr_retro_adjustments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_retro_adjustments_employee_id_applied_run_id",
                table: "hr_retro_adjustments",
                columns: new[] { "employee_id", "applied_run_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_retro_adjustments");

            migrationBuilder.DropColumn(
                name: "retro_id",
                table: "hr_pay_run_lines");
        }
    }
}
