using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnnabaDigital.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrWeeklyPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payout_item_id",
                table: "hr_pay_run_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "hr_weekly_payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    week_end = table.Column<DateOnly>(type: "date", nullable: false),
                    holdback_pct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    employee_count = table.Column<int>(type: "integer", nullable: false),
                    total_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_weekly_payouts", x => x.id);
                    table.CheckConstraint("ck_hr_weekly_payouts_holdback", "holdback_pct BETWEEN 0 AND 50");
                    table.CheckConstraint("ck_hr_weekly_payouts_week", "extract(isodow from week_start) = 1 AND week_end = week_start + 6");
                    table.ForeignKey(
                        name: "fk_hr_weekly_payouts_legal_entities_legal_entity_id",
                        column: x => x.legal_entity_id,
                        principalTable: "hr_legal_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_weekly_payout_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_no = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    employee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    daily_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    days = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    holdback = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    settled_run_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_weekly_payout_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_weekly_payout_items_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_weekly_payout_items_hr_weekly_payouts_payout_id",
                        column: x => x.payout_id,
                        principalTable: "hr_weekly_payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_weekly_payout_items_employee_id",
                table: "hr_weekly_payout_items",
                column: "employee_id",
                filter: "settled_run_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_hr_weekly_payout_items_payout_id_employee_id",
                table: "hr_weekly_payout_items",
                columns: new[] { "payout_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_weekly_payouts_legal_entity_id",
                table: "hr_weekly_payouts",
                column: "legal_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_weekly_payouts_tenant_id_legal_entity_id_week_start",
                table: "hr_weekly_payouts",
                columns: new[] { "tenant_id", "legal_entity_id", "week_start" },
                unique: true);

            // An approved weekly payout is money that was (or is about to be) paid: its amounts never change again. Settling an item and erasing a person's name stay possible.
            migrationBuilder.Sql(@"
CREATE FUNCTION hr_weekly_payouts_guard() RETURNS trigger AS $$
BEGIN
  IF OLD.status IN ('Approved', 'Paid') THEN
    IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'an approved weekly payout cannot be deleted' USING ERRCODE = 'integrity_constraint_violation'; END IF;
    IF NEW.week_start <> OLD.week_start OR NEW.week_end <> OLD.week_end OR NEW.legal_entity_id <> OLD.legal_entity_id OR NEW.holdback_pct <> OLD.holdback_pct
       OR NEW.total_gross <> OLD.total_gross OR NEW.total_net <> OLD.total_net OR NEW.employee_count <> OLD.employee_count THEN
      RAISE EXCEPTION 'an approved weekly payout cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
    END IF;
    IF NOT (NEW.status = OLD.status OR (OLD.status = 'Approved' AND NEW.status = 'Paid')) THEN RAISE EXCEPTION 'an approved weekly payout cannot be reopened' USING ERRCODE = 'integrity_constraint_violation'; END IF;
  END IF;
  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
  RETURN NEW;
END $$ LANGUAGE plpgsql;
CREATE TRIGGER trg_hr_weekly_payouts_guard BEFORE UPDATE OR DELETE ON hr_weekly_payouts FOR EACH ROW EXECUTE FUNCTION hr_weekly_payouts_guard();

CREATE FUNCTION hr_weekly_payout_items_guard() RETURNS trigger AS $$
DECLARE parent uuid; st text;
BEGIN
  IF TG_OP = 'INSERT' THEN parent := NEW.payout_id; ELSE parent := OLD.payout_id; END IF;
  SELECT status INTO st FROM hr_weekly_payouts WHERE id = parent;
  IF st IN ('Approved', 'Paid') THEN
    IF TG_OP = 'UPDATE' AND NEW.payout_id = OLD.payout_id AND NEW.employee_id = OLD.employee_id AND NEW.daily_rate = OLD.daily_rate AND NEW.days = OLD.days
       AND NEW.gross = OLD.gross AND NEW.holdback = OLD.holdback AND NEW.net = OLD.net THEN RETURN NEW; END IF;
    RAISE EXCEPTION 'the lines of an approved weekly payout cannot be changed' USING ERRCODE = 'integrity_constraint_violation';
  END IF;
  IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
  RETURN NEW;
END $$ LANGUAGE plpgsql;
CREATE TRIGGER trg_hr_weekly_payout_items_guard BEFORE INSERT OR UPDATE OR DELETE ON hr_weekly_payout_items FOR EACH ROW EXECUTE FUNCTION hr_weekly_payout_items_guard();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_hr_weekly_payout_items_guard ON hr_weekly_payout_items; DROP TRIGGER IF EXISTS trg_hr_weekly_payouts_guard ON hr_weekly_payouts; DROP FUNCTION IF EXISTS hr_weekly_payout_items_guard(); DROP FUNCTION IF EXISTS hr_weekly_payouts_guard();");
            migrationBuilder.DropTable(
                name: "hr_weekly_payout_items");

            migrationBuilder.DropTable(
                name: "hr_weekly_payouts");

            migrationBuilder.DropColumn(
                name: "payout_item_id",
                table: "hr_pay_run_lines");
        }
    }
}
