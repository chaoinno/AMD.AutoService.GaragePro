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
  "Jwt__Key=" + .["Jwt:Key"],
  # ฟอร์มติดต่อหน้า landing → กลุ่ม LINE (ไม่บังคับ — ไม่ตั้งแล้วฟอร์มตอบ CONTACT_UNAVAILABLE)
  # ต้องอยู่ในไฟล์นี้ ไม่งั้นรันสคริปต์ซ้ำแล้วค่าที่เคยเพิ่มด้วยมือบน server จะหายเพราะไฟล์ถูกเขียนทับทั้งไฟล์
  (select(.["LineMessaging:ChannelAccessToken"] // "" | length > 0)
    | "LineMessaging__ChannelAccessToken=" + .["LineMessaging:ChannelAccessToken"]),
  (select(.["LineMessaging:ContactGroupId"] // "" | length > 0)
    | "LineMessaging__ContactGroupId=" + .["LineMessaging:ContactGroupId"]),
  # self-serve (shard sp1) — ไม่บังคับ: ไม่ตั้ง = ไม่มีบรรทัดเหล่านี้ = deployment ทำงานแบบไม่มี self-serve
  # [SECURITY] อ่านจากคีย์ Production:SelfServe:* ไม่ใช่ SelfServe:ConnectionString ของ Mac (นั่นชี้ GarageSelfServe_Dev)
  # ถ้าคัดลอกคีย์ dev มาตรงๆ production จะไปเขียนฐาน dev โดยไม่มีอะไรฟ้อง
  (select(.["Production:SelfServe:ConnectionString"] // "" | length > 0)
    | "SelfServe__ConnectionString=" + .["Production:SelfServe:ConnectionString"],
      "LegacyShards__ConnectionStrings__sp1=" + .["Production:SelfServe:ConnectionString"]),
  (select(.["Production:SelfServe:MigrationConnectionString"] // "" | length > 0)
    | "SelfServe__MigrationConnectionString=" + .["Production:SelfServe:MigrationConnectionString"]),
  # self-serve integrations (ไม่บังคับ) — Google/Turnstile/อีเมล/LINE ทีมขาย · ไม่ตั้ง = ปิดช่องทางนั้น
  # (Google ไม่ตั้ง = ซ่อนปุ่ม · Turnstile secret ไม่ตั้ง = Production ปฏิเสธบอทเช็คทุกคำขอ · Email provider log = ปิดสมัครด้วยอีเมล)
  (["Google:ClientId","Turnstile:SiteKey","Turnstile:SecretKey","Email:Provider","Email:ApiKey","Email:FromAddress","Email:FromName","SalesLineGroupId"][] as $k
    | (.["Production:SelfServe:" + $k] // "") as $v
    | select($v | length > 0)
    | "SelfServe__" + ($k | gsub(":"; "__")) + "=" + $v)
' "$input_json" > "$temp_env"
if grep -Eq '^(SelfServe__ConnectionString|LegacyShards__ConnectionStrings__sp1|SelfServe__MigrationConnectionString)=.*GarageSelfServe_Dev' "$temp_env"; then
  rm -f "$temp_env" "$input_json"
  echo "Production self-serve connection points at GarageSelfServe_Dev - fix Production:SelfServe:* in user-secrets" >&2
  exit 1
fi
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
