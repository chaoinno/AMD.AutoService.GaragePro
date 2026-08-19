#!/usr/bin/env bash
set -euo pipefail

export NVM_DIR="$HOME/.nvm"
if [[ -s "$NVM_DIR/nvm.sh" ]]; then
  # shellcheck disable=SC1091
  . "$NVM_DIR/nvm.sh"
fi

if ! command -v node >/dev/null 2>&1 || ! command -v pm2 >/dev/null 2>&1; then
  echo "Node.js or PM2 was not found after loading NVM." >&2
  exit 1
fi

PM2_BIN="$(command -v pm2)"
sudo env "PATH=$PATH:/usr/bin" "$PM2_BIN" startup systemd -u "$USER" --hp "$HOME"
pm2 save

echo "PM2 startup configured for $USER."
systemctl is-enabled "pm2-${USER}.service"
