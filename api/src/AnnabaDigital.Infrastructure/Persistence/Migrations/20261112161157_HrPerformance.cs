using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_competencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_competencies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_development_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    aim = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    competency_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actions_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_development_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_development_plans_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_goals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    start_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    target_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    current_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_goals", x => x.id);
                    table.CheckConstraint("ck_hr_goals_weight", "weight >= 0 AND weight <= 100");
                    table.ForeignKey(
                        name: "fk_hr_goals_employees_owner_employee_id",
                        column: x => x.owner_employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_goals_hr_goals_parent_id",
                        column: x => x.parent_id,
                        principalTable: "hr_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_review_cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    self_review = table.Column<bool>(type: "boolean", nullable: false),
                    manager_review = table.Column<bool>(type: "boolean", nullable: false),
                    peer_review = table.Column<bool>(type: "boolean", nullable: false),
                    goals_weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    competency_weight = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    competency_ids = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    launched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_review_cycles", x => x.id);
                    table.CheckConstraint("ck_hr_review_cycles_weights", "goals_weight + competency_weight = 100");
                });

            migrationBuilder.CreateTable(
                name: "hr_succession_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    incumbent_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    critical = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_succession_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_goal_check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_goal_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_goal_check_ins_hr_goals_goal_id",
                        column: x => x.goal_id,
                        principalTable: "hr_goals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_review_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    submitted = table.Column<bool>(type: "boolean", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_review_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_review_assignments_hr_review_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "hr_review_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_review_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    goal_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    competency_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    final_score = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    calibrated_rating = table.Column<int>(type: "integer", nullable: true),
                    potential = table.Column<int>(type: "integer", nullable: true),
                    calibration_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    released = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_review_results", x => x.id);
                    table.CheckConstraint("ck_hr_review_results_ranges", "(calibrated_rating IS NULL OR calibrated_rating BETWEEN 1 AND 5) AND (potential IS NULL OR potential BETWEEN 1 AND 3)");
                    table.ForeignKey(
                        name: "fk_hr_review_results_hr_review_cycles_cycle_id",
                        column: x => x.cycle_id,
                        principalTable: "hr_review_cycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_succession_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    readiness = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_succession_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_succession_candidates_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_succession_candidates_hr_succession_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "hr_succession_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_competencies_tenant_id_code",
                table: "hr_competencies",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_development_plans_employee_id",
                table: "hr_development_plans",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_goal_check_ins_goal_id",
                table: "hr_goal_check_ins",
                column: "goal_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_goals_owner_employee_id_year",
                table: "hr_goals",
                columns: new[] { "owner_employee_id", "year" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_goals_parent_id",
                table: "hr_goals",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_review_assignments_cycle_id_employee_id_reviewer_employe",
                table: "hr_review_assignments",
                columns: new[] { "cycle_id", "employee_id", "reviewer_employee_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_review_assignments_reviewer_employee_id",
                table: "hr_review_assignments",
                column: "reviewer_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_review_results_cycle_id_employee_id",
                table: "hr_review_results",
                columns: new[] { "cycle_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_succession_candidates_employee_id",
                table: "hr_succession_candidates",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_succession_candidates_plan_id_employee_id",
                table: "hr_succession_candidates",
                columns: new[] { "plan_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_competencies");

            migrationBuilder.DropTable(
                name: "hr_development_plans");

            migrationBuilder.DropTable(
                name: "hr_goal_check_ins");

            migrationBuilder.DropTable(
                name: "hr_review_assignments");

            migrationBuilder.DropTable(
                name: "hr_review_results");

            migrationBuilder.DropTable(
                name: "hr_succession_candidates");

            migrationBuilder.DropTable(
                name: "hr_goals");

            migrationBuilder.DropTable(
                name: "hr_review_cycles");

            migrationBuilder.DropTable(
                name: "hr_succession_plans");
        }
    }
}
