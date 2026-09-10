#!/usr/bin/env bash
set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REMOTE_HOST="${REMOTE_HOST:-garage_amd}"
REMOTE_CONFIG_DIR="${REMOTE_CONFIG_DIR:-/home/deployment/config}"
REMOTE_SECRETS_FILE="$REMOTE_CONFIG_DIR/garagepro-service.env"
API_PROJECT="$ROOT_DIR/backend/AMD.AutoService.GaragePro.API"
TEMP_JSON="$(mktemp)"
trap 'rm -f "$TEMP_JSON"' EXIT

(
  cd "$API_PROJECT"
  dotnet user-secrets list --json | sed -n '/^{/,/^}/p' > "$TEMP_JSON"
)

jq -e '[
  (.["ConnectionStrings:ServiceDb"] | strings | length > 0),
  (.["LegacyShards:ConnectionStrings:db1"] | strings | length > 0),
  (.["LegacyShards:ConnectionStrings:db2"] | strings | length > 0),
  (.["Jwt:Key"] | strings | length >= 32)
] | all' "$TEMP_JSON" >/dev/null || {
  echo "Required dotnet user-secrets are missing or invalid in $API_PROJECT" >&2
  exit 1
}

ssh "$REMOTE_HOST" "mkdir -p '$REMOTE_CONFIG_DIR' && umask 077 && install -m 600 /dev/stdin '$REMOTE_CONFIG_DIR/garagepro-service.user-secrets.json'" < "$TEMP_JSON"

ssh "$REMOTE_HOST" 'bash -s' -- "$REMOTE_CONFIG_DIR" "$REMOTE_SECRETS_FILE" <<'REMOTE'
set -Eeuo pipefail
config_dir="$1"
secrets_file="$2"
input_json="$config_dir/garagepro-service.user-secrets.json"
admin_config="$HOME/sources/AMD.GaragePro.Admin/backend/AMD.GaragePro.Admin.API/appsettings.json"
temp_env="$config_dir/garagepro-service.env.tmp"

test -f "$admin_config" || { echo "Existing Admin config not found: $admin_config" >&2; exit 1; }
jq -e '.Ftp.Host and .Ftp.Username and .Ftp.Password' "$admin_config" >/dev/null || {
  echo "Existing Admin FTP config is incomplete" >&2
  exit 1
}

umask 077
jq -r '
  "ConnectionStrings__ServiceDb=" + .["ConnectionStrings:ServiceDb"],
  "LegacyShards__ConnectionStrings__db1=" + .["LegacyShards:ConnectionStrings:db1"],
  "LegacyShards__ConnectionStrings__db2=" + .["LegacyShards:ConnectionStrings:db2"],
  "Jwt__Key=" + .["Jwt:Key"]
' "$input_json" > "$temp_env"
jq -r '
  "Ftp__Host=" + (.Ftp.Host | tostring),
  "Ftp__Port=" + ((.Ftp.Port // 21) | tostring),
  "Ftp__Username=" + .Ftp.Username,
  "Ftp__Password=" + .Ftp.Password
' "$admin_config" >> "$temp_env"
chmod 600 "$temp_env"
mv "$temp_env" "$secrets_file"
rm -f "$input_json"
echo "Created $secrets_file with mode 600 (values were not printed)."
REMOTE
