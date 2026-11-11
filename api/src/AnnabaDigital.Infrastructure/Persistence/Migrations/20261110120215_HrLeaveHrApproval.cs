using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrLeaveHrApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "hr_approval_from_days",
                table: "hr_leave_types",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "manager_approved_at",
                table: "hr_leave_requests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "manager_approved_by",
                table: "hr_leave_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "needs_hr_approval",
                table: "hr_leave_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hr_approval_from_days",
                table: "hr_leave_types");

            migrationBuilder.DropColumn(
                name: "manager_approved_at",
                table: "hr_leave_requests");

            migrationBuilder.DropColumn(
                name: "manager_approved_by",
                table: "hr_leave_requests");

            migrationBuilder.DropColumn(
                name: "needs_hr_approval",
                table: "hr_leave_requests");
        }
    }
}
