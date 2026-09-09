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
ssh "$REMOTE_HOST" 'bash -s' -- "$REMOTE_SOURCE_DIR" "$REMOTE_DEPLOY_DIR" "$REMOTE_SECRETS_FILE" <<'REMOTE'
set -Eeuo pipefail
source_dir="$1"
deploy_dir="$2"
secrets_file="$3"

test -s "$secrets_file" || {
  echo "Production secrets file is missing: $secrets_file" >&2
  echo "Run scripts/bootstrap-production-secrets.sh from the Mac first." >&2
  exit 1
}

export GARAGEPRO_SOURCE_DIR="$source_dir"
export GARAGEPRO_DEPLOY_DIR="$deploy_dir"
export GARAGEPRO_SECRETS_FILE="$secrets_file"

compose=(docker compose --project-directory "$deploy_dir" -f "$deploy_dir/docker-compose.yml")
"${compose[@]}" config --quiet
"${compose[@]}" build --pull api web
"${compose[@]}" run --rm --no-deps --entrypoint /app/efbundle api </dev/null
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
curl --fail --silent --show-error --retry 3 --retry-delay 2 \
  https://gpservice.garage-pro.net/healthz >/dev/null
echo "Production deployment and public health checks passed."
