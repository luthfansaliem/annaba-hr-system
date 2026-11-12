using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_chain",
                table: "hr_overtime_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "branch_id",
                table: "hr_employees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "custom_fields_json",
                table: "hr_employees",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "approval_chain",
                table: "hr_attendance_corrections",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_branches", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_branches_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "hr_legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_company_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_company_events", x => x.id);
                    table.CheckConstraint("ck_hr_company_events_range", "end_date >= start_date");
                });

            migrationBuilder.CreateTable(
                name: "hr_custom_fields",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    options = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    visible_to_employee = table.Column<bool>(type: "boolean", nullable: false),
                    editable_by_employee = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_custom_fields", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    questions_json = table.Column<string>(type: "jsonb", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    closes_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_forms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_helpdesk_tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subject = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_helpdesk_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_helpdesk_tickets_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_letter_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    visible_to_employee = table.Column<bool>(type: "boolean", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_letter_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_form_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_form_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_form_responses_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_form_responses_hr_forms_form_id",
                        column: x => x.form_id,
                        principalTable: "hr_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_helpdesk_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    @internal = table.Column<bool>(name: "internal", type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_helpdesk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_helpdesk_messages_hr_helpdesk_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "hr_helpdesk_tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_issued_letters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    body = table.Column<string>(type: "character varying(30000)", maxLength: 30000, nullable: false),
                    visible_to_employee = table.Column<bool>(type: "boolean", nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_issued_letters", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_issued_letters_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_issued_letters_hr_letter_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "hr_letter_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_employees_branch_id",
                table: "hr_employees",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_branches_legal_entity_id",
                table: "hr_branches",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_branches_tenant_id_code",
                table: "hr_branches",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_company_events_start_date",
                table: "hr_company_events",
                column: "start_date");

            migrationBuilder.CreateIndex(
                name: "ix_hr_custom_fields_tenant_id_key",
                table: "hr_custom_fields",
                columns: new[] { "tenant_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_form_responses_employee_id",
                table: "hr_form_responses",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_form_responses_form_id_employee_id",
                table: "hr_form_responses",
                columns: new[] { "form_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_helpdesk_messages_ticket_id",
                table: "hr_helpdesk_messages",
                column: "ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_helpdesk_tickets_employee_id_status",
                table: "hr_helpdesk_tickets",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_helpdesk_tickets_tenant_id_number",
                table: "hr_helpdesk_tickets",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_issued_letters_employee_id",
                table: "hr_issued_letters",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_issued_letters_template_id",
                table: "hr_issued_letters",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_issued_letters_tenant_id_number",
                table: "hr_issued_letters",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_hr_employees_hr_branches_branch_id",
                table: "hr_employees",
                column: "branch_id",
                principalTable: "hr_branches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_hr_employees_hr_branches_branch_id",
                table: "hr_employees");

            migrationBuilder.DropTable(
                name: "hr_branches");

            migrationBuilder.DropTable(
                name: "hr_company_events");

            migrationBuilder.DropTable(
                name: "hr_custom_fields");

            migrationBuilder.DropTable(
                name: "hr_form_responses");

            migrationBuilder.DropTable(
                name: "hr_helpdesk_messages");

            migrationBuilder.DropTable(
                name: "hr_issued_letters");

            migrationBuilder.DropTable(
                name: "hr_forms");

            migrationBuilder.DropTable(
                name: "hr_helpdesk_tickets");

            migrationBuilder.DropTable(
                name: "hr_letter_templates");

            migrationBuilder.DropIndex(
                name: "ix_hr_employees_branch_id",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "approval_chain",
                table: "hr_overtime_requests");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "custom_fields_json",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "approval_chain",
                table: "hr_attendance_corrections");
        }
    }
}
