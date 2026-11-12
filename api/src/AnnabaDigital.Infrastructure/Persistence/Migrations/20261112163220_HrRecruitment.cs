using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrRecruitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_assessment_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    kind = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    questions_json = table.Column<string>(type: "jsonb", nullable: false),
                    pass_score = table.Column<int>(type: "integer", nullable: true),
                    time_limit_minutes = table.Column<int>(type: "integer", nullable: false),
                    archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_assessment_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    cv = table.Column<byte[]>(type: "bytea", nullable: true),
                    cv_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    consent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_candidates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_job_requisitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    openings = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    salary_min = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    replacement = table.Column<bool>(type: "boolean", nullable: false),
                    justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    hired = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_job_requisitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_manpower_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    job_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: false),
                    monthly_budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_manpower_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_job_postings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requisition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    type = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    requirements = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    salary_min = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    salary_max = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    show_salary = table.Column<bool>(type: "boolean", nullable: false),
                    stages = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    closes_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_job_postings", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_job_postings_hr_job_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalTable: "hr_job_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "hr_job_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cover_letter = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    reject_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    hired_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_job_applications", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_job_applications_hr_candidates_candidate_id",
                        column: x => x.candidate_id,
                        principalTable: "hr_candidates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hr_job_applications_hr_job_postings_posting_id",
                        column: x => x.posting_id,
                        principalTable: "hr_job_postings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_application_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_application_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_application_activities_hr_job_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "hr_job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_assessment_invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: true),
                    result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    answers_json = table.Column<string>(type: "jsonb", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_assessment_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_assessment_invites_hr_assessment_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "hr_assessment_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_assessment_invites_hr_job_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "hr_job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_interviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    interviewers = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    recommendation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_interviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_interviews_hr_job_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "hr_job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_job_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    monthly_salary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    contract_end = table.Column<DateOnly>(type: "date", nullable: true),
                    terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    response_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_job_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_job_offers_hr_job_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "hr_job_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_application_activities_application_id",
                table: "hr_application_activities",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_assessment_invites_application_id",
                table: "hr_assessment_invites",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_assessment_invites_template_id",
                table: "hr_assessment_invites",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_assessment_invites_token_hash",
                table: "hr_assessment_invites",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_candidates_tenant_id_email",
                table: "hr_candidates",
                columns: new[] { "tenant_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_interviews_application_id",
                table: "hr_interviews",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_applications_candidate_id",
                table: "hr_job_applications",
                column: "candidate_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_applications_posting_id_candidate_id",
                table: "hr_job_applications",
                columns: new[] { "posting_id", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_offers_application_id",
                table: "hr_job_offers",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_offers_token_hash",
                table: "hr_job_offers",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_postings_public_id",
                table: "hr_job_postings",
                column: "public_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_postings_requisition_id",
                table: "hr_job_postings",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_job_requisitions_tenant_id_number",
                table: "hr_job_requisitions",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_manpower_plans_tenant_id_year",
                table: "hr_manpower_plans",
                columns: new[] { "tenant_id", "year" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_application_activities");

            migrationBuilder.DropTable(
                name: "hr_assessment_invites");

            migrationBuilder.DropTable(
                name: "hr_interviews");

            migrationBuilder.DropTable(
                name: "hr_job_offers");

            migrationBuilder.DropTable(
                name: "hr_manpower_plans");

            migrationBuilder.DropTable(
                name: "hr_assessment_templates");

            migrationBuilder.DropTable(
                name: "hr_job_applications");

            migrationBuilder.DropTable(
                name: "hr_candidates");

            migrationBuilder.DropTable(
                name: "hr_job_postings");

            migrationBuilder.DropTable(
                name: "hr_job_requisitions");
        }
    }
}
