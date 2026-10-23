#!/usr/bin/env bash
# [SECURITY] ปิด /api/v1/internal (API ภายในของ GaragePro Admin · docs/17 §8.6) จากภายนอกที่ Nginx ของ vhost API ที่ติดตั้งไปแล้ว
#
# ทำไมต้องเป็นสคริปต์ patch: vhost บนเครื่องจริงถูก certbot แก้ไปแล้ว (ย้ายบล็อกเดิมไป 443 + สร้างบล็อก 80 redirect)
# ติดตั้ง nginx-api.conf ทับไม่ได้ ใบรับรองจะหาย · pattern เดียวกับ add-api-rate-limit.sh
#
# ทำอะไร:
#   1. คัดบล็อก "BEGIN/END gp-api-internal-deny" จาก nginx-api.conf ไปแทรกก่อน "location / {" ทุกตัวใน vhost ของ API
#      (ข้ามถ้ามี marker อยู่แล้ว)
#   2. สำรองไฟล์ที่จะแก้ → nginx -t → ไม่ผ่านคืนไฟล์เดิมแล้วหยุด (ไม่รีโหลด) → ผ่านจึง reload
#   3. ตรวจผล: /health ผ่าน Cloudflare ยังปกติ · /api/v1/internal/sites ทั้งผ่าน Cloudflare และตรงเข้า Nginx ในเครื่อง
#      ต้องได้ 404 ที่ Nginx ตอบเอง (header X-GP-Internal-Deny: nginx) · Admin ยังเรียกได้ทาง docker network ตามปกติ
#
# ควรรันก่อนตั้ง Internal__ApiKey (Production:Internal:ApiKey) ครั้งแรก
#
# วิธีใช้ (เจ้าของเครื่อง หลัง ./scripts/deploy-production.sh ซึ่ง rsync โฟลเดอร์นี้ขึ้นไปให้):
#   ssh garage_amd
#   sudo /home/deployment/deployments/garagepro-service/add-api-internal-deny.sh
#   # vhost อยู่ที่อื่น: sudo SITE=/etc/nginx/sites-available/<ไฟล์> .../add-api-internal-deny.sh
#   # ข้ามการยิงทดสอบ: sudo SKIP_CHECKS=1 .../add-api-internal-deny.sh
# รันซ้ำได้ ส่วนที่ติดตั้งแล้วจะข้าม (แต่ยังตรวจผลทุกครั้ง)
set -Eeuo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_HOST="gpservice-api.garage-pro.net"
VHOST_SRC="$DEPLOY_DIR/nginx-api.conf"
MARKER_BEGIN="# BEGIN gp-api-internal-deny"
MARKER_END="# END gp-api-internal-deny"
CHECK_PATH="/api/v1/internal/sites"
SITE="${SITE:-}"
STAMP="$(date +%Y%m%d%H%M%S)"

run() { if [[ $EUID -eq 0 ]]; then "$@"; else sudo "$@"; fi; }
fail() { echo "ผิดพลาด: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || command -v sudo >/dev/null || fail "ต้องรันด้วย sudo"
command -v nginx >/dev/null || run test -x /usr/sbin/nginx || fail "ไม่พบ nginx"
[[ -f "$VHOST_SRC" ]] || fail "ไม่พบ $VHOST_SRC"

# ---------- หา vhost ของ API ----------
if [[ -z "$SITE" ]]; then
  mapfile -t candidates < <(run find /etc/nginx/sites-available -maxdepth 1 -type f -name 'gpservice-api*' \
    ! -name '*.bak-*' ! -name '*~' | sort)
  if (( ${#candidates[@]} != 1 )); then
    echo "หา vhost ของ API ไม่ได้แน่ชัด (เจอ ${#candidates[@]} ไฟล์):" >&2
    printf '  %s\n' "${candidates[@]}" >&2
    fail "ระบุเองด้วย SITE=/etc/nginx/sites-available/<ไฟล์>"
  fi
  SITE="${candidates[0]}"
fi
run test -f "$SITE" || fail "ไม่พบ $SITE"
echo "vhost ของ API: $SITE"
if ! run find /etc/nginx/sites-enabled -maxdepth 1 -lname "$SITE" | grep -q .; then
  echo "คำเตือน: ไม่เจอ symlink ใน /etc/nginx/sites-enabled ที่ชี้ไป $SITE ตรวจว่าแก้ถูกไฟล์" >&2
fi

# ---------- เตรียม rollback ----------
TMP_DIR="$(mktemp -d)"
chmod 755 "$TMP_DIR"
trap 'rm -rf "$TMP_DIR"' EXIT

changed=0
site_backup=""

restore() {
  if [[ -n "$site_backup" ]]; then
    run cp -p "$site_backup" "$SITE"
    echo "คืน $SITE จาก $site_backup แล้ว" >&2
  fi
}

# ---------- 1) vhost: แทรก location ก่อน "location / {" ----------
if run grep -qF "$MARKER_BEGIN" "$SITE"; then
  echo "[ข้าม] $SITE มีบล็อกปิด /api/v1/internal อยู่แล้ว"
else
  # บล็อกต้นฉบับอยู่ใน nginx-api.conf ระหว่าง marker · ตัดย่อหน้า 4 ช่องของ repo ออกแล้วใช้ย่อหน้าของไฟล์จริงแทน
  block="$TMP_DIR/block.conf"
  sed -n "/$MARKER_BEGIN/,/$MARKER_END/p" "$VHOST_SRC" | sed 's/^    //' > "$block"
  grep -qF "$MARKER_END" "$block" || fail "ไม่พบบล็อก $MARKER_BEGIN ... $MARKER_END ใน $VHOST_SRC"

  count="$(run grep -cE '^[[:space:]]*location[[:space:]]+/[[:space:]]*\{' "$SITE" || true)"
  (( count >= 1 )) || fail "ไม่พบ 'location / {' ใน $SITE ไม่รู้จะแทรกตรงไหน (ยังไม่ได้แก้อะไร)"

  site_backup="$SITE.bak-$STAMP"
  run cp -p "$SITE" "$site_backup"
  echo "สำรอง $SITE ไว้ที่ $site_backup"

  patched="$TMP_DIR/site.conf"
  run cat "$SITE" | awk -v block_file="$block" '
    BEGIN { while ((getline line < block_file) > 0) block[++n] = line }
    /^[[:space:]]*location[[:space:]]+\/[[:space:]]*\{/ {
      match($0, /^[[:space:]]*/); indent = substr($0, 1, RLENGTH)
      for (i = 1; i <= n; i++) print (block[i] == "" ? "" : indent block[i])
      print ""
    }
    { print }
  ' > "$patched"
  # เขียนทับด้วย tee (คงเจ้าของ/สิทธิ์ของไฟล์เดิม)
  run tee "$SITE" < "$patched" >/dev/null
  changed=1
  echo "[แก้] แทรกบล็อกปิด /api/v1/internal ก่อน 'location / {' ใน $SITE จำนวน $count จุด"
fi

# ---------- 2) ตรวจ config แล้ว reload ----------
if (( changed )); then
  if ! run nginx -t; then
    restore
    fail "nginx -t ไม่ผ่าน คืนไฟล์เดิมแล้ว ไม่ได้รีโหลด"
  fi
  run systemctl reload nginx
  echo "รีโหลด nginx แล้ว"
else
  echo "ไม่มีอะไรเปลี่ยน ไม่ได้รีโหลด"
fi

# ---------- 3) ตรวจผล ----------
if [[ "${SKIP_CHECKS:-0}" == "1" ]]; then
  echo "ข้ามการตรวจผล (SKIP_CHECKS=1)"
  exit 0
fi

curl --fail --silent --show-error --retry 3 --retry-delay 2 "https://$API_HOST/health" >/dev/null
echo "https://$API_HOST/health ผ่าน Cloudflare ตอบปกติ"

# $1 = ชื่อเส้นทาง · ที่เหลือ = อาร์กิวเมนต์ของ curl · ผ่านเมื่อได้ 404 พร้อม header ของ Nginx (ไม่ใช่ 404/401 ของ API)
check_denied() {
  local label="$1"; shift
  local headers="$TMP_DIR/headers"
  local code
  code="$(curl --silent --output /dev/null --dump-header "$headers" --write-out '%{http_code}' "$@" || true)"
  if [[ "$code" == "404" ]] && grep -qi '^x-gp-internal-deny: *nginx' "$headers"; then
    echo "[ผ่าน] $label: 404 จาก Nginx"
    return 0
  fi
  echo "[ไม่ผ่าน] $label: ได้ HTTP $code โดยไม่มี X-GP-Internal-Deny" >&2
  return 1
}

ok=1
# ผ่าน Cloudflare จริง (เส้นทางเดียวกับคนนอก) · ตัวพิมพ์ใหญ่ทดสอบว่า regex ไม่สนตัวพิมพ์
check_denied "ผ่าน Cloudflare" --retry 3 --retry-delay 2 "https://$API_HOST$CHECK_PATH" || ok=0
check_denied "ผ่าน Cloudflare (ตัวพิมพ์ใหญ่)" "https://$API_HOST/API/V1/Internal/sites" || ok=0
# ตรงเข้า Nginx ในเครื่อง (--resolve คง SNI/ใบรับรองเดิม) · ไม่ผ่าน Cloudflare
check_denied "ตรงเข้า Nginx" --resolve "$API_HOST:443:127.0.0.1" "https://$API_HOST$CHECK_PATH" || ok=0
(( ok )) || fail "ยังเรียก /api/v1/internal จากภายนอกได้ (config ยังติดตั้งอยู่ ตรวจ $SITE)"
echo "ปิด /api/v1/internal จากภายนอกสำเร็จ · Admin เรียกผ่าน http://garagepro_service_api:8080 ทาง docker network ได้ตามเดิม"
