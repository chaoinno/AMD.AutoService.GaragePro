#!/usr/bin/env bash
# รัน migration ของฐาน self-serve "dev" (GarageSelfServe_Dev) จากเครื่อง production
#
# ทำไมรันจาก server: Mac บางเครื่องต่อ GarageSelfServe_Dev ด้วย login gp_selfserve_* ไม่ได้ (TLS handshake failed —
# ดู CLAUDE.md) แต่ container API บน server ต่อ SQL Server เครื่องเดียวกันด้วย login เดียวกันได้อยู่แล้วทุก deploy
# · login เป็นของระดับ server ฐาน dev กับ production ใช้รหัสเดียวกัน ต่างกันแค่ชื่อฐาน
#
# ทำอะไร: ใช้ image API ที่ deploy อยู่ (ไม่ build ใหม่) รัน efbundle (svc_*) + efbundle-selfserve (sp_*) แบบเดียวกับ
# deploy-production.sh แต่เปลี่ยนชื่อฐานใน SelfServe__MigrationConnectionString เป็น GarageSelfServe_Dev ภายใน container
# — รหัสผ่านไม่ขึ้นบน command line และไม่ถูกพิมพ์ออกจอ
#
# ต้องรันหลัง deploy: migration ที่จะลงฐาน dev คือชุดที่อยู่ใน image ที่ deploy ล่าสุด (โค้ด main)
#
#   REMOTE_HOST=deployment@10.10.3.13 ./scripts/migrate-selfserve-dev.sh
set -Eeuo pipefail

REMOTE_HOST="${REMOTE_HOST:-garage_amd}"
REMOTE_SOURCE_DIR="${REMOTE_SOURCE_DIR:-/home/deployment/sources/AMD.AutoService.GaragePro}"
REMOTE_DEPLOY_DIR="${REMOTE_DEPLOY_DIR:-/home/deployment/deployments/garagepro-service}"
REMOTE_SECRETS_FILE="${REMOTE_SECRETS_FILE:-/home/deployment/config/garagepro-service.env}"

echo "Migrating GarageSelfServe_Dev from $REMOTE_HOST (deployed API image)..."
ssh "$REMOTE_HOST" 'bash -s' -- "$REMOTE_SOURCE_DIR" "$REMOTE_DEPLOY_DIR" "$REMOTE_SECRETS_FILE" <<'REMOTE'
set -Eeuo pipefail
export GARAGEPRO_SOURCE_DIR="$1"
export GARAGEPRO_DEPLOY_DIR="$2"
export GARAGEPRO_SECRETS_FILE="$3"

grep -Eq '^SelfServe__MigrationConnectionString=.+' "$GARAGEPRO_SECRETS_FILE" || {
  echo "SelfServe__MigrationConnectionString is not set in $GARAGEPRO_SECRETS_FILE" >&2
  exit 1
}

docker compose --project-directory "$GARAGEPRO_DEPLOY_DIR" -f "$GARAGEPRO_DEPLOY_DIR/docker-compose.yml" \
  run --rm --no-deps --entrypoint /bin/sh api -c '
    set -e
    cs="$(printf "%s" "$SelfServe__MigrationConnectionString" \
      | sed -E "s/(Database|Initial Catalog)=GarageSelfServe(;|$)/\1=GarageSelfServe_Dev\2/I")"
    # [SECURITY] กันพลาดไปลงฐาน production: ต้องเปลี่ยนเป็น _Dev สำเร็จเท่านั้น ไม่งั้นหยุด
    case "$cs" in
      *=GarageSelfServe_Dev*) ;;
      *) echo "Could not point the connection string at GarageSelfServe_Dev - stopping." >&2; exit 1 ;;
    esac
    echo "svc_* (ServiceDbContext):"
    ConnectionStrings__ServiceDb="$cs" /app/efbundle
    echo "sp_* (SelfServeDbContext):"
    SelfServe__ConnectionString="$cs" /app/efbundle-selfserve
  ' </dev/null
REMOTE
echo "Done."
