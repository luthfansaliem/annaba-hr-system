using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrEmployeeBpjsReligion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bpjs_kes_no_enc",
                table: "hr_employees",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bpjs_tk_no_enc",
                table: "hr_employees",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "religion",
                table: "hr_employees",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bpjs_kes_no_enc",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "bpjs_tk_no_enc",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "religion",
                table: "hr_employees");
        }
    }
}
