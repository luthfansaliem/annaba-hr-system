using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrLegalEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_hr_pay_runs_tenant_id_year_month",
                table: "hr_pay_runs");

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "hr_pay_runs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "legal_entity_id",
                table: "hr_employees",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "hr_legal_entities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tax_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_legal_entities", x => x.id);
                    table.CheckConstraint("ck_hr_legal_entities_tax_id", "tax_id IS NULL OR tax_id ~ '^[0-9]{15,16}$'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_runs_legal_entity_id",
                table: "hr_pay_runs",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_runs_tenant_id_legal_entity_id_year_month",
                table: "hr_pay_runs",
                columns: new[] { "tenant_id", "legal_entity_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_employees_legal_entity_id",
                table: "hr_employees",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_legal_entities_tenant_id",
                table: "hr_legal_entities",
                column: "tenant_id",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_hr_legal_entities_tenant_id_name",
                table: "hr_legal_entities",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            // Existing data belongs to one company per organisation: create it (named after the organisation) for every tenant that already has employees or pay runs, then point the rows at it.
            migrationBuilder.Sql(@"
INSERT INTO hr_legal_entities (id, tenant_id, name, is_default, created_at, updated_at)
SELECT gen_random_uuid(), t.id, t.name, true, now(), now() FROM tenants t
WHERE EXISTS (SELECT 1 FROM hr_employees e WHERE e.tenant_id = t.id) OR EXISTS (SELECT 1 FROM hr_pay_runs p WHERE p.tenant_id = t.id);
UPDATE hr_employees e SET legal_entity_id = (SELECT l.id FROM hr_legal_entities l WHERE l.tenant_id = e.tenant_id AND l.is_default);
UPDATE hr_pay_runs p SET legal_entity_id = (SELECT l.id FROM hr_legal_entities l WHERE l.tenant_id = p.tenant_id AND l.is_default);
");

            migrationBuilder.AddForeignKey(
                name: "fk_hr_employees_hr_legal_entities_legal_entity_id",
                table: "hr_employees",
                column: "legal_entity_id",
                principalTable: "hr_legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_hr_pay_runs_legal_entities_legal_entity_id",
                table: "hr_pay_runs",
                column: "legal_entity_id",
                principalTable: "hr_legal_entities",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // A locked pay run now also keeps its company.
            migrationBuilder.Sql(GuardSql(" OR NEW.legal_entity_id <> OLD.legal_entity_id"));
        }

        private static string GuardSql(string extra) => @"
CREATE OR REPLACE FUNCTION hr_pay_runs_guard() RETURNS trigger AS $$
BEGIN
  IF TG_OP = 'DELETE' THEN
    IF OLD.status IN ('Locked', 'Paid') THEN RAISE EXCEPTION 'a locked pay run cannot be deleted' USING ERRCODE = 'integrity_constraint_violation'; END IF;
    RETURN OLD;
  END IF;
  IF OLD.status IN ('Locked', 'Paid') THEN
    IF NEW.year <> OLD.year OR NEW.month <> OLD.month OR NEW.total_gross <> OLD.total_gross OR NEW.total_tax <> OLD.total_tax OR NEW.total_net <> OLD.total_net
       OR NEW.total_employer_cost <> OLD.total_employer_cost OR NEW.employee_count <> OLD.employee_count OR NEW.rules_version <> OLD.rules_version" + extra + @" THEN
      RAISE EXCEPTION 'a locked pay run cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    IF OLD.status = 'Paid' AND NEW.status <> 'Paid' THEN RAISE EXCEPTION 'a paid pay run cannot be reopened' USING ERRCODE = 'integrity_constraint_violation'; END IF;
    IF OLD.status = 'Locked' AND NEW.status NOT IN ('Locked', 'Paid') THEN RAISE EXCEPTION 'a locked pay run cannot be reopened' USING ERRCODE = 'integrity_constraint_violation'; END IF;
  END IF;
  RETURN NEW;
END $$ LANGUAGE plpgsql;
";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(GuardSql(""));
            migrationBuilder.DropForeignKey(
                name: "fk_hr_employees_hr_legal_entities_legal_entity_id",
                table: "hr_employees");

            migrationBuilder.DropForeignKey(
                name: "fk_hr_pay_runs_legal_entities_legal_entity_id",
                table: "hr_pay_runs");

            migrationBuilder.DropTable(
                name: "hr_legal_entities");

            migrationBuilder.DropIndex(
                name: "ix_hr_pay_runs_legal_entity_id",
                table: "hr_pay_runs");

            migrationBuilder.DropIndex(
                name: "ix_hr_pay_runs_tenant_id_legal_entity_id_year_month",
                table: "hr_pay_runs");

            migrationBuilder.DropIndex(
                name: "ix_hr_employees_legal_entity_id",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "hr_pay_runs");

            migrationBuilder.DropColumn(
                name: "legal_entity_id",
                table: "hr_employees");

            migrationBuilder.CreateIndex(
                name: "ix_hr_pay_runs_tenant_id_year_month",
                table: "hr_pay_runs",
                columns: new[] { "tenant_id", "year", "month" },
                unique: true);
        }
    }
}
