using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    work_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    marital_status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    emergency_contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    emergency_contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    national_id_enc = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    tax_id_enc = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    bank_account_enc = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    termination_date = table.Column<DateOnly>(type: "date", nullable: true),
                    termination_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_employees", x => x.id);
                    table.CheckConstraint("ck_hr_employees_name_len", "length(trim(full_name)) >= 2");
                    table.CheckConstraint("ck_hr_employees_term_after_hire", "termination_date IS NULL OR termination_date >= hire_date");
                    table.ForeignKey(
                        name: "fk_hr_employees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "hr_org_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_org_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_org_units_hr_org_units_parent_id",
                        column: x => x.parent_id,
                        principalTable: "hr_org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    job_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    grade = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contract_end = table.Column<DateOnly>(type: "date", nullable: true),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manager_id = table.Column<Guid>(type: "uuid", nullable: true),
                    change_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_employments", x => x.id);
                    table.CheckConstraint("ck_hr_employments_range", "effective_to IS NULL OR effective_to >= effective_from");
                    table.ForeignKey(
                        name: "fk_hr_employments_hr_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hr_employments_hr_employees_manager_id",
                        column: x => x.manager_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_employments_hr_org_units_org_unit_id",
                        column: x => x.org_unit_id,
                        principalTable: "hr_org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_employees_tenant_id_employee_no",
                table: "hr_employees",
                columns: new[] { "tenant_id", "employee_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_employees_tenant_id_status",
                table: "hr_employees",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_employees_user_id",
                table: "hr_employees",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_employments_employee_id",
                table: "hr_employments",
                column: "employee_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_employments_manager_id",
                table: "hr_employments",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_employments_org_unit_id",
                table: "hr_employments",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_org_units_parent_id",
                table: "hr_org_units",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_org_units_tenant_id_code",
                table: "hr_org_units",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_employments");

            migrationBuilder.DropTable(
                name: "hr_employees");

            migrationBuilder.DropTable(
                name: "hr_org_units");
        }
    }
}
