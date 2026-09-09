#!/usr/bin/env bash
set -Eeuo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LETSENCRYPT_EMAIL="${1:-${LETSENCRYPT_EMAIL:-}}"

if [[ -z "$LETSENCRYPT_EMAIL" ]]; then
  echo "Usage: $0 <letsencrypt-email>" >&2
  exit 2
fi

command -v sudo >/dev/null || { echo "sudo is required" >&2; exit 1; }
command -v certbot >/dev/null || { echo "certbot is not installed" >&2; exit 1; }

sudo install -m 0644 "$DEPLOY_DIR/nginx-web.conf" \
  /etc/nginx/sites-available/gpservice.garage-pro.net
sudo install -m 0644 "$DEPLOY_DIR/nginx-api.conf" \
  /etc/nginx/sites-available/gpservice-api.garage-pro.net
sudo ln -sfn /etc/nginx/sites-available/gpservice.garage-pro.net \
  /etc/nginx/sites-enabled/gpservice.garage-pro.net
sudo ln -sfn /etc/nginx/sites-available/gpservice-api.garage-pro.net \
  /etc/nginx/sites-enabled/gpservice-api.garage-pro.net

sudo nginx -t
sudo systemctl reload nginx

sudo certbot --nginx \
  --non-interactive \
  --agree-tos \
  --email "$LETSENCRYPT_EMAIL" \
  --redirect \
  -d gpservice.garage-pro.net \
  -d gpservice-api.garage-pro.net

sudo nginx -t
sudo systemctl reload nginx

curl --fail --silent --show-error https://gpservice.garage-pro.net/healthz >/dev/null
curl --fail --silent --show-error https://gpservice-api.garage-pro.net/health >/dev/null
echo "Nginx, HTTPS, web, and API checks passed."
