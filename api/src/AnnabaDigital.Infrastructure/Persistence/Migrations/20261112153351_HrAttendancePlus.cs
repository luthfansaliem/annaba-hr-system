using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrAttendancePlus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "block_days",
                table: "hr_leave_types",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kiosk_pin_hash",
                table: "hr_employees",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "break_minutes",
                table: "hr_attendance_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "break_started_at",
                table: "hr_attendance_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_field_visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    place = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    accuracy = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_field_visits", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_field_visits_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_hourly_leaves",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    from = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    to = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    paid = table.Column<bool>(type: "boolean", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_hourly_leaves", x => x.id);
                    table.CheckConstraint("ck_hr_hourly_leaves_range", "\"to\" > \"from\"");
                    table.ForeignKey(
                        name: "fk_hr_hourly_leaves_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_kiosk_devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    work_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_kiosk_devices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_on_call_duties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    allowance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_on_call_duties", x => x.id);
                    table.CheckConstraint("ck_hr_on_call_allowance", "allowance >= 0");
                    table.ForeignKey(
                        name: "fk_hr_on_call_duties_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    client = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_roster_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_roster_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_roster_entries_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_roster_entries_hr_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "hr_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_timesheet_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_timesheet_entries", x => x.id);
                    table.CheckConstraint("ck_hr_timesheet_hours", "hours > 0 AND hours <= 24");
                    table.ForeignKey(
                        name: "fk_hr_timesheet_entries_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_timesheet_entries_hr_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "hr_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_field_visits_employee_id_date",
                table: "hr_field_visits",
                columns: new[] { "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_hourly_leaves_employee_id_date",
                table: "hr_hourly_leaves",
                columns: new[] { "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_kiosk_devices_token_hash",
                table: "hr_kiosk_devices",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_on_call_duties_employee_id_date",
                table: "hr_on_call_duties",
                columns: new[] { "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_projects_tenant_id_code",
                table: "hr_projects",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_roster_entries_employee_id_date",
                table: "hr_roster_entries",
                columns: new[] { "employee_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_roster_entries_shift_id",
                table: "hr_roster_entries",
                column: "shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_timesheet_entries_employee_id_date",
                table: "hr_timesheet_entries",
                columns: new[] { "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_timesheet_entries_project_id",
                table: "hr_timesheet_entries",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_field_visits");

            migrationBuilder.DropTable(
                name: "hr_hourly_leaves");

            migrationBuilder.DropTable(
                name: "hr_kiosk_devices");

            migrationBuilder.DropTable(
                name: "hr_on_call_duties");

            migrationBuilder.DropTable(
                name: "hr_roster_entries");

            migrationBuilder.DropTable(
                name: "hr_timesheet_entries");

            migrationBuilder.DropTable(
                name: "hr_projects");

            migrationBuilder.DropColumn(
                name: "block_days",
                table: "hr_leave_types");

            migrationBuilder.DropColumn(
                name: "kiosk_pin_hash",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "break_minutes",
                table: "hr_attendance_logs");

            migrationBuilder.DropColumn(
                name: "break_started_at",
                table: "hr_attendance_logs");
        }
    }
}
