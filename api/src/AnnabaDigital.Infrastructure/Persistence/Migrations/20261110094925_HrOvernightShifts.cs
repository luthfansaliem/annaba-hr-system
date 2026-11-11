using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrOvernightShifts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_shifts_time",
                table: "hr_shifts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_shifts_time",
                table: "hr_shifts",
                sql: "end_time <> start_time");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_shifts_time",
                table: "hr_shifts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_shifts_time",
                table: "hr_shifts",
                sql: "end_time > start_time");
        }
    }
}
