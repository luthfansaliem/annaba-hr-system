using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrWorkModeGeofence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "hr_attendance_logs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "hr_attendance_logs",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "outside_zone",
                table: "hr_attendance_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "work_mode",
                table: "hr_attendance_logs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Office");

            migrationBuilder.CreateTable(
                name: "hr_work_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    radius_meters = table.Column<int>(type: "integer", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_work_locations", x => x.id);
                    table.CheckConstraint("ck_hr_work_locations_geo", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180 AND radius_meters BETWEEN 20 AND 5000");
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_work_locations_tenant_id_name",
                table: "hr_work_locations",
                columns: new[] { "tenant_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_work_locations");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "hr_attendance_logs");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "hr_attendance_logs");

            migrationBuilder.DropColumn(
                name: "outside_zone",
                table: "hr_attendance_logs");

            migrationBuilder.DropColumn(
                name: "work_mode",
                table: "hr_attendance_logs");
        }
    }
}
