using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrScheduledChangesPhotosReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "cash_out_expired",
                table: "hr_leave_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "hr_photos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_photos", x => x.id);
                    table.CheckConstraint("ck_hr_photos_size", "octet_length(content) BETWEEN 1 AND 1048576");
                    table.ForeignKey(
                        name: "fk_hr_photos_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_report_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    report = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    frequency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    day = table.Column<int>(type: "integer", nullable: false),
                    last_sent_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_report_schedules", x => x.id);
                    table.CheckConstraint("ck_hr_report_schedules_day", "(frequency = 'weekly' AND day BETWEEN 1 AND 7) OR (frequency = 'monthly' AND day BETWEEN 1 AND 28)");
                    table.ForeignKey(
                        name: "fk_hr_report_schedules_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_scheduled_employment_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    request_json = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_scheduled_employment_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_scheduled_employment_changes_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_photos_employee_id",
                table: "hr_photos",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_photos_kind_created_at",
                table: "hr_photos",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_photos_kind_owner_id",
                table: "hr_photos",
                columns: new[] { "kind", "owner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_report_schedules_user_id_report_frequency_day",
                table: "hr_report_schedules",
                columns: new[] { "user_id", "report", "frequency", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_scheduled_employment_changes_effective_from",
                table: "hr_scheduled_employment_changes",
                column: "effective_from",
                filter: "applied_at IS NULL AND cancelled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_scheduled_employment_changes_employee_id_effective_from",
                table: "hr_scheduled_employment_changes",
                columns: new[] { "employee_id", "effective_from" },
                unique: true,
                filter: "applied_at IS NULL AND cancelled_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_photos");

            migrationBuilder.DropTable(
                name: "hr_report_schedules");

            migrationBuilder.DropTable(
                name: "hr_scheduled_employment_changes");

            migrationBuilder.DropColumn(
                name: "cash_out_expired",
                table: "hr_leave_types");
        }
    }
}
