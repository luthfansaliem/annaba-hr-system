using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrLeaveAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_attendance_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    clock_in = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_out = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_attendance_logs", x => x.id);
                    table.CheckConstraint("ck_hr_attendance_order", "clock_out IS NULL OR clock_out >= clock_in");
                    table.ForeignKey(
                        name: "fk_hr_attendance_logs_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_holidays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    collective = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_holidays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    annual_quota = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    carry_over_max = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    max_days_per_request = table.Column<int>(type: "integer", nullable: true),
                    paid = table.Column<bool>(type: "boolean", nullable: false),
                    allow_half_day = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_leave_types", x => x.id);
                    table.CheckConstraint("ck_hr_leave_types_quota", "annual_quota IS NULL OR annual_quota >= 0");
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_ledger",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_leave_ledger", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_leave_ledger_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_leave_ledger_hr_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "hr_leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    half_day = table.Column<bool>(type: "boolean", nullable: false),
                    days = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_leave_requests", x => x.id);
                    table.CheckConstraint("ck_hr_leave_requests_days", "days > 0");
                    table.CheckConstraint("ck_hr_leave_requests_range", "end_date >= start_date");
                    table.ForeignKey(
                        name: "fk_hr_leave_requests_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_leave_requests_hr_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "hr_leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_attendance_logs_employee_id",
                table: "hr_attendance_logs",
                column: "employee_id",
                unique: true,
                filter: "clock_out IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_attendance_logs_employee_id_date",
                table: "hr_attendance_logs",
                columns: new[] { "employee_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_holidays_tenant_id_date",
                table: "hr_holidays",
                columns: new[] { "tenant_id", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_ledger_employee_id_leave_type_id_year",
                table: "hr_leave_ledger",
                columns: new[] { "employee_id", "leave_type_id", "year" },
                unique: true,
                filter: "kind = 'Grant'");

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_ledger_leave_type_id",
                table: "hr_leave_ledger",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_requests_employee_id_start_date",
                table: "hr_leave_requests",
                columns: new[] { "employee_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_requests_leave_type_id",
                table: "hr_leave_requests",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_requests_tenant_id_status",
                table: "hr_leave_requests",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_types_tenant_id_code",
                table: "hr_leave_types",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_attendance_logs");

            migrationBuilder.DropTable(
                name: "hr_holidays");

            migrationBuilder.DropTable(
                name: "hr_leave_ledger");

            migrationBuilder.DropTable(
                name: "hr_leave_requests");

            migrationBuilder.DropTable(
                name: "hr_leave_types");
        }
    }
}
