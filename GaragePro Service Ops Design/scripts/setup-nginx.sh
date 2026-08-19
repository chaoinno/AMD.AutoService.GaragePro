#!/usr/bin/env bash
set -euo pipefail

DOMAIN="${DOMAIN:-gp-service.ipongs.com}"
PORT="${PORT:-3040}"
AVAILABLE_PATH="/etc/nginx/sites-available/${DOMAIN}"
ENABLED_PATH="/etc/nginx/sites-enabled/${DOMAIN}"
CERT_PATH="/etc/ssl/certs/cf-origin.pem"
KEY_PATH="/etc/ssl/private/cf-origin.key"

if [[ ! "$DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]]; then
  echo "Invalid DOMAIN: $DOMAIN" >&2
  exit 1
fi

if [[ ! "$PORT" =~ ^[0-9]+$ ]] || (( PORT < 1 || PORT > 65535 )); then
  echo "Invalid PORT: $PORT" >&2
  exit 1
fi

if [[ ! -f "$CERT_PATH" ]] || ! sudo test -f "$KEY_PATH"; then
  echo "Cloudflare Origin certificate or key not found." >&2
  echo "Expected: $CERT_PATH and $KEY_PATH" >&2
  exit 1
fi

if ! curl --fail --silent --show-error "http://127.0.0.1:${PORT}/" >/dev/null; then
  echo "The PM2 upstream is not responding at http://127.0.0.1:${PORT}/" >&2
  echo "Deploy or restart garagepro-service-ops before installing this vhost." >&2
  exit 1
fi

CONFIG_FILE="$(mktemp)"
trap 'rm -f "$CONFIG_FILE"' EXIT

cat >"$CONFIG_FILE" <<NGINX
server {
    listen 80;
    server_name ${DOMAIN};
    return 301 https://\$host\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name ${DOMAIN};

    ssl_certificate     ${CERT_PATH};
    ssl_certificate_key ${KEY_PATH};
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384;
    ssl_prefer_server_ciphers on;

    location / {
        proxy_pass http://127.0.0.1:${PORT};
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_read_timeout 120s;
        proxy_connect_timeout 30s;
        proxy_send_timeout 120s;
    }

    access_log /var/log/nginx/gp-service.access.log;
    error_log  /var/log/nginx/gp-service.error.log;
}
NGINX

if sudo test -f "$AVAILABLE_PATH"; then
  BACKUP_PATH="${AVAILABLE_PATH}.bak.$(date +%Y%m%d%H%M%S)"
  echo "Backing up existing vhost to $BACKUP_PATH"
  sudo cp "$AVAILABLE_PATH" "$BACKUP_PATH"
fi

sudo install -m 0644 "$CONFIG_FILE" "$AVAILABLE_PATH"
sudo ln -sfn "$AVAILABLE_PATH" "$ENABLED_PATH"

if ! sudo nginx -t; then
  echo "Nginx validation failed. The service was not reloaded." >&2
  exit 1
fi

sudo systemctl reload nginx

echo "Nginx vhost installed: $AVAILABLE_PATH"
echo "URL: https://${DOMAIN}"
