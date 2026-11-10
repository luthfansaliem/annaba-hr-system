using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrDocumentVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "replaced_by_id",
                table: "hr_employee_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scan_status",
                table: "hr_employee_documents",
                type: "character varying(12)",
                maxLength: 12,
                nullable: false,
                defaultValue: "not_scanned");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "hr_employee_documents",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "replaced_by_id",
                table: "hr_employee_documents");

            migrationBuilder.DropColumn(
                name: "scan_status",
                table: "hr_employee_documents");

            migrationBuilder.DropColumn(
                name: "version",
                table: "hr_employee_documents");
        }
    }
}
