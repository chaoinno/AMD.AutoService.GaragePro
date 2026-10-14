#!/usr/bin/env bash
# provision-selfserve-db.sh <DbName> <server>
#
# สร้าง/ซ่อมฐาน self-serve (shard sp1) บน instance เดียวกับ Garage: ฐาน + ตารางรูปร่างเหมือน Garage + synonym
# + ตารางอ้างอิง + login แล้วตรวจด้วย smoke test (รันด้วย login ของแอปเพื่อพิสูจน์สิทธิ์)
# idempotent — รันซ้ำได้ ทุกขั้นข้ามสิ่งที่มีอยู่แล้ว · หยุดทันทีที่ขั้นใดล้ม (sqlcmd -b + set -e)
# ไม่สร้างตาราง svc_*/sp_* — ทำโดย migration bundle (deploy-production.sh) หรือ API โหมด Development ภายหลัง
#
# ต้องใช้: sqlcmd (mssql-tools18 หรือ go-sqlcmd) · บัญชี sysadmin (sa) · ต่อ Garage Pro VPN
# environment:
#   SQLCMDPASSWORD                 รหัสผ่านของ SQLCMD_USER (ไม่ตั้ง = ถามแบบไม่แสดงผล) — ห้ามใส่ในบรรทัดคำสั่ง
#   SQLCMD_USER                    ค่าเริ่มต้น sa
#   SELFSERVE_APP_PASSWORD         รหัสผ่านของ gp_selfserve_app (ไม่ตั้ง = ถาม) — ใช้เฉพาะตอนสร้าง login ครั้งแรก + smoke
#   SELFSERVE_MIGRATE_PASSWORD     รหัสผ่านของ gp_selfserve_migrate (ไม่ตั้ง = ถาม)
#   GARAGE_DB                      ฐาน Garage บน instance เดียวกัน (ค่าเริ่มต้น Garage)
#   SELFSERVE_APP_LOGIN / SELFSERVE_MIGRATE_LOGIN   ชื่อ login (ค่าเริ่มต้นตามแผน)
#   CHECK_SHADOW_DDL=1             ก่อนเริ่ม รัน 01-generate-shadow-ddl.sql กับ Garage (อ่านอย่างเดียว) แล้ว diff กับ
#                                  02-shadow-ddl.generated.sql — หยุดถ้า schema ของ Garage เปลี่ยนไปจากไฟล์ที่ commit
#   SKIP_LOGINS=1                  ข้าม 06-logins.sql (เช่น ทีม DBA จัดการ login เอง) — smoke จะรันด้วย SQLCMD_USER แทน
set -Eeuo pipefail

usage() {
  echo "usage: $0 <DbName> <server>" >&2
  echo "  e.g. $0 GarageSelfServe_Dev 10.10.4.11" >&2
  exit 2
}

[[ $# -eq 2 ]] || usage
DB_NAME="$1"
SERVER="$2"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# [SECURITY] กันพิมพ์ผิดไปโดนฐานจริง: รับเฉพาะชื่อในตระกูล GarageSelfServe เท่านั้น (ไม่มีทางเป็น Garage/GarageService)
if [[ ! "$DB_NAME" =~ ^GarageSelfServe(_[A-Za-z0-9]+)?$ ]]; then
  echo "DbName must be GarageSelfServe or GarageSelfServe_<Suffix> (got: $DB_NAME)" >&2
  exit 2
fi

command -v sqlcmd >/dev/null || { echo "sqlcmd is required (brew install sqlcmd / mssql-tools18)" >&2; exit 1; }

# go-sqlcmd (brew install sqlcmd) อ่านไฟล์เป็น UTF-8 อยู่แล้วและไม่รู้จัก -f · ODBC sqlcmd (mssql-tools18) ต้องใช้ -f 65001
# [แก้ 2026-10-14] เครื่อง dev ที่ Homebrew ชี้ opt/openssl ไป openssl@4 — ODBC Driver 18 โหลด OpenSSL ไม่ได้ ต้องใช้ go-sqlcmd แทน
if sqlcmd --version >/dev/null 2>&1; then CODEPAGE_ARGS=(); else CODEPAGE_ARGS=(-f 65001); fi

SQL_USER="${SQLCMD_USER:-sa}"
GARAGE_DB="${GARAGE_DB:-Garage}"
APP_LOGIN="${SELFSERVE_APP_LOGIN:-gp_selfserve_app}"
MIGRATE_LOGIN="${SELFSERVE_MIGRATE_LOGIN:-gp_selfserve_migrate}"

prompt_secret() {
  local name="$1" label="$2" value
  if [[ -z "${!name:-}" ]]; then
    read -rsp "$label: " value
    echo >&2
    printf -v "$name" '%s' "$value"
  fi
  export "${name?}"
}

# sqlcmd แทนค่า $(...) แบบข้อความ — ' หรือ $( ในรหัสผ่านจะทำให้ SQL เพี้ยน จึงปฏิเสธตั้งแต่ต้น
check_password_text() {
  local name="$1"
  if [[ "${!name}" == *"'"* || "${!name}" == *'$('* ]]; then
    echo "$name must not contain ' or \$(" >&2
    exit 2
  fi
}

prompt_secret SQLCMDPASSWORD "Password for $SQL_USER@$SERVER"
if [[ "${SKIP_LOGINS:-0}" != "1" ]]; then
  prompt_secret SELFSERVE_APP_PASSWORD "Password for $APP_LOGIN (new login; ignored if it already exists)"
  prompt_secret SELFSERVE_MIGRATE_PASSWORD "Password for $MIGRATE_LOGIN (new login; ignored if it already exists)"
  check_password_text SELFSERVE_APP_PASSWORD
  check_password_text SELFSERVE_MIGRATE_PASSWORD
fi

# -b หยุดเมื่อ error · -C เชื่อใบรับรองของ instance (แบบเดียวกับ TrustServerCertificate=True ใน connection string)
# -I QUOTED_IDENTIFIER ON (filtered index) · -f 65001 อ่าน/เขียน UTF-8 · รหัสผ่านทั้งหมดไปทาง environment เท่านั้น
sql() {
  local database="$1"
  shift
  sqlcmd -S "$SERVER" -U "$SQL_USER" -d "$database" -b -C -I ${CODEPAGE_ARGS[@]+"${CODEPAGE_ARGS[@]}"} \
    -v DbName="$DB_NAME" GarageDb="$GARAGE_DB" AppLogin="$APP_LOGIN" MigrateLogin="$MIGRATE_LOGIN" \
    "$@"
}

step() {
  local database="$1" file="$2"
  echo
  echo "==> $file  (database: $database)"
  sql "$database" -i "$SCRIPT_DIR/$file"
}

echo "Preflight: $SERVER as $SQL_USER"
sql master -h -1 -W -Q "SET NOCOUNT ON;
  IF DB_ID(N'$GARAGE_DB') IS NULL RAISERROR(N'database $GARAGE_DB not found on this instance', 16, 1);
  IF CONVERT(sysname, DATABASEPROPERTYEX(N'$GARAGE_DB', 'Collation')) <> N'Thai_CI_AS'
    RAISERROR(N'$GARAGE_DB is not Thai_CI_AS - the shadow schema assumes it is', 16, 1);
  IF IS_SRVROLEMEMBER(N'sysadmin') <> 1 RAISERROR(N'$SQL_USER must be sysadmin to provision', 16, 1);
  SELECT CONCAT(@@SERVERNAME, N' / ', CONVERT(nvarchar(50), SERVERPROPERTY('ProductVersion')));"

if [[ "${CHECK_SHADOW_DDL:-0}" == "1" ]]; then
  echo
  echo "==> regenerating shadow DDL from $GARAGE_DB (read-only) and comparing with the committed file"
  generated="$(mktemp)"
  trap 'rm -f "$generated"' EXIT
  sql "$GARAGE_DB" -h -1 -y 0 -i "$SCRIPT_DIR/01-generate-shadow-ddl.sql" -o "$generated"
  if ! diff -u "$SCRIPT_DIR/02-shadow-ddl.generated.sql" "$generated"; then
    echo "Garage schema differs from 02-shadow-ddl.generated.sql - review, commit the regenerated file, then rerun." >&2
    exit 1
  fi
fi

step master 03-create-database.sql
step "$DB_NAME" 02-shadow-ddl.generated.sql
step "$DB_NAME" 04-synonyms.sql
step "$DB_NAME" 05-copy-reference-rows.sql

if [[ "${SKIP_LOGINS:-0}" != "1" ]]; then
  step master 06-logins.sql
  echo
  echo "==> 07-smoke.sql  (database: $DB_NAME, as $APP_LOGIN)"
  SQLCMDPASSWORD="$SELFSERVE_APP_PASSWORD" sqlcmd -S "$SERVER" -U "$APP_LOGIN" -d "$DB_NAME" -b -C -I ${CODEPAGE_ARGS[@]+"${CODEPAGE_ARGS[@]}"} \
    -i "$SCRIPT_DIR/07-smoke.sql"
else
  step "$DB_NAME" 07-smoke.sql
fi

cat <<EOF

Provisioned $DB_NAME on $SERVER.
Next (see deploy/selfserve/README.md):
  1. svc_* + sp_* tables: run the migration bundles with the $MIGRATE_LOGIN connection string
     (production: scripts/deploy-production.sh · dev: API in Development migrates on startup)
  2. Point the API at it: SelfServe:ConnectionString and LegacyShards:ConnectionStrings:sp1 = $APP_LOGIN connection string
EOF
