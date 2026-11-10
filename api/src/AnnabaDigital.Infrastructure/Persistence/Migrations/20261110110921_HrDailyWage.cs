using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrDailyWage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "daily_rate",
                table: "hr_pay_run_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "days_paid",
                table: "hr_pay_run_items",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pay_basis",
                table: "hr_compensations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Monthly");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "daily_rate",
                table: "hr_pay_run_items");

            migrationBuilder.DropColumn(
                name: "days_paid",
                table: "hr_pay_run_items");

            migrationBuilder.DropColumn(
                name: "pay_basis",
                table: "hr_compensations");
        }
    }
}
