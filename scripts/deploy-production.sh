#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE_HOST="${REMOTE_HOST:-garage_amd}"
BRANCH="${BRANCH:-main}"
REMOTE_SOURCE_DIR="${REMOTE_SOURCE_DIR:-/home/deployment/sources/AMD.AutoService.GaragePro}"
REMOTE_DEPLOY_DIR="${REMOTE_DEPLOY_DIR:-/home/deployment/deployments/garagepro-service}"
REMOTE_SECRETS_FILE="${REMOTE_SECRETS_FILE:-/home/deployment/config/garagepro-service.env}"
REPOSITORY_URL="${REPOSITORY_URL:-git@github.com:chaoinno/AMD.AutoService.GaragePro.git}"

command -v ssh >/dev/null || { echo "ssh is required" >&2; exit 1; }
command -v rsync >/dev/null || { echo "rsync is required" >&2; exit 1; }

echo "Updating $BRANCH on $REMOTE_HOST..."
ssh "$REMOTE_HOST" 'bash -s' -- "$REMOTE_SOURCE_DIR" "$BRANCH" "$REPOSITORY_URL" <<'REMOTE'
set -Eeuo pipefail
source_dir="$1"
branch="$2"
repository_url="$3"

if [[ ! -d "$source_dir/.git" ]]; then
  mkdir -p "$(dirname "$source_dir")"
  git clone --branch "$branch" --single-branch "$repository_url" "$source_dir"
fi

cd "$source_dir"
if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
  echo "Tracked files on production have local changes; refusing to overwrite them." >&2
  git status --short --untracked-files=no >&2
  exit 1
fi

git fetch --prune origin "$branch"
git checkout "$branch"
git pull --ff-only origin "$branch"
REMOTE

ssh "$REMOTE_HOST" "mkdir -p '$REMOTE_DEPLOY_DIR'"
rsync -az --delete "$ROOT_DIR/deploy/production/" "$REMOTE_HOST:$REMOTE_DEPLOY_DIR/"

echo "Building, migrating, and restarting GaragePro Service..."
# สวิตช์รับสมัครทดลองใช้อยู่ใน env file (SelfServe__Enabled ← user-secrets Production:SelfServe:Enabled → bootstrap)
# ไม่ต้องใส่อะไรตอน deploy · SELFSERVE_ENABLED ที่ส่งมาใช้ตรวจอย่างเดียว: ไม่ตรงกับ env file = หยุดก่อน build
ssh "$REMOTE_HOST" 'bash -s' -- "$REMOTE_SOURCE_DIR" "$REMOTE_DEPLOY_DIR" "$REMOTE_SECRETS_FILE" "${SELFSERVE_ENABLED:-}" <<'REMOTE'
set -Eeuo pipefail
source_dir="$1"
deploy_dir="$2"
secrets_file="$3"
requested_signup="$(printf '%s' "${4:-}" | tr '[:upper:]' '[:lower:]')"

test -s "$secrets_file" || {
  echo "Production secrets file is missing: $secrets_file" >&2
  echo "Run scripts/bootstrap-production-secrets.sh from the Mac first." >&2
  exit 1
}

signup="$(sed -n 's/^SelfServe__Enabled=//p' "$secrets_file" | tail -n 1 | tr '[:upper:]' '[:lower:]')"
signup="${signup:-false}"
case "$signup" in
  true|false) ;;
  *) echo "SelfServe__Enabled in $secrets_file must be true or false (got '$signup')." >&2; exit 1 ;;
esac
if [[ -n "$requested_signup" && "$requested_signup" != "$signup" ]]; then
  echo "SELFSERVE_ENABLED=$requested_signup does not match the env file (SelfServe__Enabled=$signup)." >&2
  echo "The signup switch now lives in the env file: set Production:SelfServe:Enabled in user-secrets," >&2
  echo "run scripts/bootstrap-production-secrets.sh, then deploy again without SELFSERVE_ENABLED." >&2
  exit 1
fi
if [[ "$signup" == true ]]; then
  echo "Self-serve signup: OPEN (SelfServe__Enabled=true in env file)"
else
  echo "Self-serve signup: CLOSED (SelfServe__Enabled not true in env file)"
fi

export GARAGEPRO_SOURCE_DIR="$source_dir"
export GARAGEPRO_DEPLOY_DIR="$deploy_dir"
export GARAGEPRO_SECRETS_FILE="$secrets_file"

# ค่าสาธารณะที่ต้องฝังใน bundle เว็บตอน build (ไม่ใช่ความลับ: Google client id · Turnstile site key) —
# อ่านจาก env file เดียวกับ API เพื่อไม่ต้องตั้งสองที่ · ว่าง = เว็บซ่อนปุ่ม Google/วิดเจ็ต Turnstile
public_value() { sed -n "s/^$1=//p" "$secrets_file" | tail -n 1; }
export VITE_GOOGLE_CLIENT_ID="$(public_value SelfServe__Google__ClientId)"
export VITE_TURNSTILE_SITE_KEY="$(public_value SelfServe__Turnstile__SiteKey)"

compose=(docker compose --project-directory "$deploy_dir" -f "$deploy_dir/docker-compose.yml")

# docker network ร่วมกับ GaragePro Admin (API ภายใน /api/v1/internal · docs/17 §8.6) compose ประกาศเป็น external
# จึงต้องมีก่อน run/up · สร้างครั้งเดียวต่อเครื่อง มีแล้วข้าม (Admin deploy สร้างพร้อมกันได้: create ชนแล้วตรวจซ้ำ)
servicepro_network=garagepro-servicepro
if ! docker network inspect "$servicepro_network" >/dev/null 2>&1; then
  docker network create "$servicepro_network" >/dev/null 2>&1 || docker network inspect "$servicepro_network" >/dev/null
  echo "Docker network $servicepro_network is ready (shared with GaragePro Admin)."
fi
if grep -Eq '^Internal__ApiKey=.{32,}' "$secrets_file"; then
  echo "Internal API for GaragePro Admin: ENABLED (Internal__ApiKey in env file)"
else
  echo "Internal API for GaragePro Admin: DISABLED (no Internal__ApiKey of 32+ characters; /api/v1/internal returns 404)"
fi

"${compose[@]}" config --quiet
"${compose[@]}" build --pull api web
"${compose[@]}" run --rm --no-deps --entrypoint /app/efbundle api </dev/null
# Global policy context: run once on central ServiceDb, never against tenant databases.
"${compose[@]}" run --rm --no-deps --entrypoint /app/efbundle-app-updates api </dev/null

# Self-serve shard (sp1 -> GarageSelfServe): only when the env file configures it, so deployments without
# self-serve keep the single migration above. Two targets in the same database:
#   svc_* : the same efbundle, pointed at GarageSelfServe by overriding ConnectionStrings__ServiceDb inside the
#           container (the design-time factory reads it) - the secret never appears on a command line
#   sp_*  : efbundle-selfserve (SelfServeDbContext, history table __EFMigrationsHistory_SelfServe)
# Uses SelfServe__MigrationConnectionString (gp_selfserve_migrate, has DDL rights) when present, otherwise
# SelfServe__ConnectionString. Provision the database first: deploy/selfserve/provision-selfserve-db.sh
if grep -Eq '^SelfServe__ConnectionString=.+' "$secrets_file"; then
  echo "Migrating self-serve database (svc_* and sp_*)..."
  "${compose[@]}" run --rm --no-deps --entrypoint /bin/sh api -c '
    set -e
    cs="${SelfServe__MigrationConnectionString:-$SelfServe__ConnectionString}"
    ConnectionStrings__ServiceDb="$cs" /app/efbundle
    SelfServe__ConnectionString="$cs" /app/efbundle-selfserve
  ' </dev/null
else
  echo "SelfServe__ConnectionString not set - skipping self-serve migrations."
fi

"${compose[@]}" up -d --remove-orphans --wait

curl --fail --silent --show-error http://127.0.0.1:5081/health >/dev/null
curl --fail --silent --show-error http://127.0.0.1:3005/healthz >/dev/null

cd "$source_dir"
printf 'DEPLOYED_COMMIT='
git rev-parse HEAD
"${compose[@]}" ps
echo "Local API and web health checks passed."
REMOTE

echo "Checking public endpoints..."
if [[ "${SKIP_PUBLIC_CHECKS:-0}" == "1" ]]; then
  echo "Public checks skipped (initial deployment before Nginx installation)."
  exit 0
fi

curl --fail --silent --show-error --retry 3 --retry-delay 2 \
  https://gpservice-api.garage-pro.net/health >/dev/null
code=$(curl --silent --show-error --retry 3 --retry-delay 2 --output /dev/null --write-out '%{http_code}' \
  https://service.garage-pro.net/healthz)
[[ "$code" == "200" ]] || { echo "Canonical web healthz returned $code; expected 200." >&2; exit 1; }
code=$(curl --silent --show-error --retry 3 --retry-delay 2 --output /dev/null --write-out '%{http_code}' \
  https://gpservice.garage-pro.net/healthz)
case "$code" in
  301) echo "Legacy web hostname redirects to the canonical hostname." ;;
  200) echo "Hint: install make-service-canonical.sh on the origin to redirect gpservice to service." ;;
  *) echo "Legacy web healthz returned $code; expected 200 or 301." >&2; exit 1 ;;
esac

# [SECURITY] API ภายในต้องเข้าได้ทาง docker network เท่านั้น · Nginx ตอบ 404 เองพร้อม X-GP-Internal-Deny (add-api-internal-deny.sh)
# 404 ไม่มี header = API ตอบเอง (ยังไม่ได้ตั้งคีย์) แต่ Nginx ยังไม่กั้น · 401/200 = เปิดสู่อินเทอร์เน็ตแล้ว (ตั้งคีย์โดยยังไม่กั้น)
internal_headers="$(mktemp)"
code=$(curl --silent --show-error --retry 3 --retry-delay 2 --output /dev/null --dump-header "$internal_headers" \
  --write-out '%{http_code}' https://gpservice-api.garage-pro.net/api/v1/internal/sites || true)
if [[ "$code" == "404" ]] && grep -qi '^x-gp-internal-deny: *nginx' "$internal_headers"; then
  echo "Internal API is blocked at Nginx for the public hostname."
elif [[ "$code" == "404" ]]; then
  echo "WARNING: /api/v1/internal is not blocked at Nginx yet (the API answered 404 because Internal__ApiKey is unset)." >&2
  echo "Run on the server before setting the key: sudo $REMOTE_DEPLOY_DIR/add-api-internal-deny.sh" >&2
elif [[ "$code" == "000" ]]; then
  echo "WARNING: could not reach https://gpservice-api.garage-pro.net to check that /api/v1/internal is blocked." >&2
else
  rm -f "$internal_headers"
  echo "SECURITY: https://gpservice-api.garage-pro.net/api/v1/internal/sites returned $code - the internal API is reachable" >&2
  echo "from the internet. Run on the server now: sudo $REMOTE_DEPLOY_DIR/add-api-internal-deny.sh" >&2
  exit 1
fi
rm -f "$internal_headers"
echo "Production deployment and public health checks passed."
