#!/usr/bin/env bash
set -euo pipefail

SSH_HOST="${SSH_HOST:-ipong_server}"
REMOTE_DIR="${REMOTE_DIR:-garagepro-service-ops}"
APP_NAME="${APP_NAME:-garagepro-service-ops}"
PORT="${PORT:-3040}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Deploying ${APP_NAME} to ${SSH_HOST}:~/${REMOTE_DIR}"

ssh "$SSH_HOST" "mkdir -p \"\$HOME/${REMOTE_DIR}\""

rsync -az --delete \
  --exclude='.DS_Store' \
  --exclude='.git/' \
  --exclude='node_modules/' \
  --exclude='.thumbnail' \
  "$ROOT_DIR/" \
  "${SSH_HOST}:~/${REMOTE_DIR}/"

ssh "$SSH_HOST" bash -s -- "$REMOTE_DIR" "$APP_NAME" "$PORT" <<'REMOTE'
set -euo pipefail

REMOTE_DIR="$1"
APP_NAME="$2"
PORT="$3"

export NVM_DIR="$HOME/.nvm"
if [[ -s "$NVM_DIR/nvm.sh" ]]; then
  # shellcheck disable=SC1091
  . "$NVM_DIR/nvm.sh"
fi

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
  echo "Node.js/npm not found after loading NVM." >&2
  exit 1
fi

cd "$HOME/$REMOTE_DIR"
npm install --omit=dev
npm run check

if ! command -v pm2 >/dev/null 2>&1; then
  npm install --global pm2
fi

PORT="$PORT" pm2 startOrRestart ecosystem.config.cjs --only "$APP_NAME" --update-env
pm2 save

for attempt in {1..15}; do
  if curl --fail --silent --show-error "http://127.0.0.1:${PORT}/" >/dev/null; then
    echo "Health check passed: http://127.0.0.1:${PORT}/"
    exit 0
  fi
  sleep 1
done

echo "Health check failed." >&2
pm2 logs "$APP_NAME" --lines 50 --nostream
exit 1
REMOTE

echo
echo "Deployment complete. To install the Nginx vhost, run:"
echo "ssh -t ${SSH_HOST} '~/${REMOTE_DIR}/scripts/setup-nginx.sh'"
