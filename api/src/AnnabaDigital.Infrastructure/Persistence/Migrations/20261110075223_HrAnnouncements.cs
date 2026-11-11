using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requires_ack = table.Column<bool>(type: "boolean", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_announcements", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_announcements_org_units_org_unit_id",
                        column: x => x.org_unit_id,
                        principalTable: "hr_org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_announcement_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    announcement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_announcement_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_announcement_receipts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hr_announcement_receipts_hr_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalTable: "hr_announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_announcement_receipts_announcement_id_employee_id",
                table: "hr_announcement_receipts",
                columns: new[] { "announcement_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_announcement_receipts_employee_id",
                table: "hr_announcement_receipts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_announcements_org_unit_id",
                table: "hr_announcements",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_announcements_tenant_id_published_at",
                table: "hr_announcements",
                columns: new[] { "tenant_id", "published_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_announcement_receipts");

            migrationBuilder.DropTable(
                name: "hr_announcements");
        }
    }
}
