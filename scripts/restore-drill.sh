#!/usr/bin/env bash
# Proves that a backup can actually be restored (PRD §11: "backup + restore terbukti"). Run it monthly and after every schema change; keep the printed report.
#
#   ./scripts/restore-drill.sh                          dump the database in $SRC_CONTAINER now, restore it into a scratch database, compare
#   ./scripts/restore-drill.sh path/to/file.dump        restore an existing pg_dump -Fc file (e.g. one fetched by deploy/pull-backups.sh) and compare with $SRC_CONTAINER
#   BACKUP_PASSPHRASE=... ./scripts/restore-drill.sh path/to/file.dump.enc      the same for an encrypted dump
#
# Environment: SRC_CONTAINER (default annaba-digital-dev-db) · DB_USER (annaba_digital) · DB_NAME (annaba_digital)
# The scratch database is created and dropped on the SAME server, never touching the source database. Exit code 0 only when every table has the same row count and the same content checksum.
set -euo pipefail

C="${SRC_CONTAINER:-annaba-digital-dev-db}"; U="${DB_USER:-annaba_digital}"; DB="${DB_NAME:-annaba_digital}"
SCRATCH="restore_drill_$(date -u +%Y%m%d%H%M%S)"; DUMP="${1:-}"; TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"; docker exec "$C" psql -U "$U" -d postgres -qc "DROP DATABASE IF EXISTS $SCRATCH" >/dev/null 2>&1 || true' EXIT
psql_in() { docker exec -i "$C" psql -U "$U" -d "$1" -At -v ON_ERROR_STOP=1 "${@:2}"; }

started=$(date +%s)
if [ -z "$DUMP" ]; then
  DUMP="$TMP/source.dump"; echo "1/5 dumping $DB from $C ..."
  docker exec "$C" pg_dump -U "$U" -Fc "$DB" > "$DUMP"
else
  echo "1/5 using $DUMP"
  case "$DUMP" in *.enc)   # an encrypted dump of the compose backup service: prove that the passphrase you kept really opens it
    [ -n "${BACKUP_PASSPHRASE:-}" ] || { echo "FAIL: $DUMP is encrypted; set BACKUP_PASSPHRASE" >&2; exit 1; }
    openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -md sha256 -pass env:BACKUP_PASSPHRASE -in "$DUMP" -out "$TMP/decrypted.dump" 2>/dev/null || { echo "FAIL: cannot decrypt (wrong passphrase or damaged file)" >&2; exit 1; }
    DUMP="$TMP/decrypted.dump"; echo "    decrypted with the passphrase" ;;
  esac
fi
[ "$(head -c 5 "$DUMP")" = "PGDMP" ] || { echo "FAIL: not a pg_dump custom-format file" >&2; exit 1; }
echo "    dump size: $(wc -c < "$DUMP" | tr -d ' ') bytes"

echo "2/5 restoring into scratch database $SCRATCH ..."
psql_in postgres -c "CREATE DATABASE $SCRATCH" >/dev/null
docker exec -i "$C" pg_restore -U "$U" -d "$SCRATCH" --no-owner --exit-on-error < "$DUMP"

# One query that lists every table with its row count and an order-independent checksum of its content.
SQL="SELECT string_agg(format('SELECT %L, count(*), coalesce(md5(string_agg(t::text, '''' ORDER BY t::text)), ''-'') FROM %I t', table_name, table_name), ' UNION ALL ' ORDER BY table_name) FROM information_schema.tables WHERE table_schema='public' AND table_type='BASE TABLE'"
snapshot() { local q; q="$(psql_in "$1" -c "$SQL")"; psql_in "$1" -F '|' -c "$q ORDER BY 1"; }

echo "3/5 reading every table in source and restored copy ..."
snapshot "$DB" > "$TMP/src.txt"; snapshot "$SCRATCH" > "$TMP/dst.txt"

echo "4/5 comparing ..."
tables=$(wc -l < "$TMP/src.txt" | tr -d ' '); rows=$(awk -F'|' '{s+=$2} END {print s+0}' "$TMP/src.txt")
if diff -q "$TMP/src.txt" "$TMP/dst.txt" >/dev/null; then status=OK; else status=MISMATCH; diff "$TMP/src.txt" "$TMP/dst.txt" | head -20 >&2; fi

echo "5/5 checking the restored database is usable (migrations table, HR tables, lock triggers) ..."
mig=$(psql_in "$SCRATCH" -c 'SELECT count(*) FROM "__EFMigrationsHistory"'); trg=$(psql_in "$SCRATCH" -c "SELECT count(*) FROM pg_trigger WHERE tgname LIKE 'trg_hr_pay%'")
echo
echo "RESTORE DRILL $status · $(date -u +%FT%TZ) · source $C/$DB · $tables tables · $rows rows · $mig migrations · $trg pay-run lock triggers · $(( $(date +%s) - started )) s"
[ "$status" = OK ] || exit 1
[ "$trg" -ge 3 ] || { echo "FAIL: lock triggers missing after restore" >&2; exit 1; }
