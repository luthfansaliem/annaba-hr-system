using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrTaxTreatment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tax_allowance",
                table: "hr_pay_run_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax_borne",
                table: "hr_pay_run_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "tax_treatment",
                table: "hr_pay_run_items",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Gross");

            migrationBuilder.AddColumn<bool>(
                name: "non_resident",
                table: "hr_compensations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "tax_treatment",
                table: "hr_compensations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Gross");

            migrationBuilder.AddColumn<decimal>(
                name: "treaty_rate",
                table: "hr_compensations",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tax_allowance",
                table: "hr_pay_run_items");

            migrationBuilder.DropColumn(
                name: "tax_borne",
                table: "hr_pay_run_items");

            migrationBuilder.DropColumn(
                name: "tax_treatment",
                table: "hr_pay_run_items");

            migrationBuilder.DropColumn(
                name: "non_resident",
                table: "hr_compensations");

            migrationBuilder.DropColumn(
                name: "tax_treatment",
                table: "hr_compensations");

            migrationBuilder.DropColumn(
                name: "treaty_rate",
                table: "hr_compensations");
        }
    }
}
