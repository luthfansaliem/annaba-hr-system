using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrApprovalFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "approval_chain",
                table: "hr_leave_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "approval_chain",
                table: "hr_expense_claims",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_approval_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    leave_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    steps = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_approval_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_approval_rules_hr_leave_types_leave_type_id",
                        column: x => x.leave_type_id,
                        principalTable: "hr_leave_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_approval_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_no = table.Column<int>(type: "integer", nullable: false),
                    approver = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_approval_steps", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_approval_rules_leave_type_id",
                table: "hr_approval_rules",
                column: "leave_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_approval_rules_tenant_id_kind_leave_type_id_min_amount",
                table: "hr_approval_rules",
                columns: new[] { "tenant_id", "kind", "leave_type_id", "min_amount" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_approval_steps_kind_request_id_step_no",
                table: "hr_approval_steps",
                columns: new[] { "kind", "request_id", "step_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_approval_rules");

            migrationBuilder.DropTable(
                name: "hr_approval_steps");

            migrationBuilder.DropColumn(
                name: "approval_chain",
                table: "hr_leave_requests");

            migrationBuilder.DropColumn(
                name: "approval_chain",
                table: "hr_expense_claims");
        }
    }
}
