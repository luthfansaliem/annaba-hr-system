using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrDelegation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_approval_delegations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegator_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delegate_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_approval_delegations", x => x.id);
                    table.CheckConstraint("ck_hr_deleg_range", "end_date >= start_date");
                    table.CheckConstraint("ck_hr_deleg_self", "delegator_employee_id <> delegate_employee_id");
                    table.ForeignKey(
                        name: "fk_hr_approval_delegations_employees_delegate_employee_id",
                        column: x => x.delegate_employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hr_approval_delegations_employees_delegator_employee_id",
                        column: x => x.delegator_employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_approval_delegations_delegate_employee_id",
                table: "hr_approval_delegations",
                column: "delegate_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_approval_delegations_delegator_employee_id",
                table: "hr_approval_delegations",
                column: "delegator_employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_approval_delegations");
        }
    }
}
