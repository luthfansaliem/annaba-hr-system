using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrLearning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_courses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    self_enroll = table.Column<bool>(type: "boolean", nullable: false),
                    quiz_json = table.Column<string>(type: "jsonb", nullable: false),
                    pass_score = table.Column<int>(type: "integer", nullable: false),
                    certificate = table.Column<bool>(type: "boolean", nullable: false),
                    valid_months = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_courses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_course_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    lessons_done = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    quiz_score = table.Column<int>(type: "integer", nullable: true),
                    quiz_attempts = table.Column<int>(type: "integer", nullable: false),
                    certificate_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_course_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_course_enrollments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hr_course_enrollments_hr_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "hr_courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_course_lessons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    file = table.Column<byte[]>(type: "bytea", nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_course_lessons", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_course_lessons_hr_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "hr_courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_course_enrollments_course_id_employee_id",
                table: "hr_course_enrollments",
                columns: new[] { "course_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_course_enrollments_employee_id",
                table: "hr_course_enrollments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_course_lessons_course_id_order",
                table: "hr_course_lessons",
                columns: new[] { "course_id", "order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_course_enrollments");

            migrationBuilder.DropTable(
                name: "hr_course_lessons");

            migrationBuilder.DropTable(
                name: "hr_courses");
        }
    }
}
