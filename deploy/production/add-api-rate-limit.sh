#!/usr/bin/env bash
# [SECURITY] เปิด rate limit ของ login/signup ที่ Nginx + ตั้ง IP จริงจาก Cloudflare ให้ vhost API ที่ติดตั้งไปแล้ว (B3)
#
# ทำไมต้องเป็นสคริปต์ patch: vhost บนเครื่องจริงถูก certbot แก้ไปแล้ว (ย้ายบล็อกเดิมไป 443 + สร้างบล็อก 80 redirect)
# ติดตั้ง nginx-api.conf ทับไม่ได้ ใบรับรองจะหาย — สคริปต์นี้จึงแทรกเฉพาะส่วนที่เพิ่ม (pattern เดียวกับ add-service-domain.sh)
#
# ทำอะไร:
#   1. ติดตั้ง nginx-ratelimit.conf → /etc/nginx/conf.d/garagepro-service-ratelimit.conf
#      (real_ip จาก Cloudflare + zone gp_login/gp_signup) — รันซ้ำหลังแก้ช่วง IP ของ Cloudflare ได้ จะติดตั้งทับให้
#   2. คัดบล็อก "BEGIN/END gp-api-rate-limit" จาก nginx-api.conf ไปแทรกก่อน "location / {" ทุกตัวใน vhost ของ API
#      (ข้ามถ้ามี marker อยู่แล้ว)
#   3. สำรองไฟล์ที่จะแก้ → nginx -t → ไม่ผ่านคืนไฟล์เดิมทั้งหมดแล้วหยุด (ไม่รีโหลด) → ผ่านจึง reload
#   4. ตรวจผล: /health ผ่าน Cloudflare + ยิง POST /api/v1/auth/login (body ว่าง) รัวๆ 20 ครั้งตรงเข้า Nginx
#      ในเครื่อง ต้องเจอ 429 ที่ Nginx ตอบเอง (header X-RateLimit-Layer: nginx) อย่างน้อยหนึ่งครั้ง
#      ผลข้างเคียง: โควตา login ของ 127.0.0.1 หมดราว 1 นาที (ไม่กระทบผู้ใช้จริง)
#
# วิธีใช้ (เจ้าของเครื่อง หลัง ./scripts/deploy-production.sh ซึ่ง rsync โฟลเดอร์นี้ขึ้นไปให้):
#   ssh garage_amd
#   sudo /home/deployment/deployments/garagepro-service/add-api-rate-limit.sh
#   # vhost อยู่ที่อื่น: sudo SITE=/etc/nginx/sites-available/<ไฟล์> .../add-api-rate-limit.sh
#   # ข้ามการยิงทดสอบ: sudo SKIP_CHECKS=1 .../add-api-rate-limit.sh
# รันซ้ำได้ — ส่วนที่ติดตั้งแล้วจะข้าม
set -Eeuo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_HOST="gpservice-api.garage-pro.net"
CONF_SRC="$DEPLOY_DIR/nginx-ratelimit.conf"
VHOST_SRC="$DEPLOY_DIR/nginx-api.conf"
CONF_DST="/etc/nginx/conf.d/garagepro-service-ratelimit.conf"
MARKER_BEGIN="# BEGIN gp-api-rate-limit"
MARKER_END="# END gp-api-rate-limit"
SITE="${SITE:-}"
STAMP="$(date +%Y%m%d%H%M%S)"

run() { if [[ $EUID -eq 0 ]]; then "$@"; else sudo "$@"; fi; }
fail() { echo "ผิดพลาด: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || command -v sudo >/dev/null || fail "ต้องรันด้วย sudo"
command -v nginx >/dev/null || run test -x /usr/sbin/nginx || fail "ไม่พบ nginx"
[[ -f "$CONF_SRC" ]] || fail "ไม่พบ $CONF_SRC"
[[ -f "$VHOST_SRC" ]] || fail "ไม่พบ $VHOST_SRC"
run grep -qE '^[[:space:]]*include[[:space:]]+/etc/nginx/conf\.d/\*\.conf;' /etc/nginx/nginx.conf \
  || fail "/etc/nginx/nginx.conf ไม่ include conf.d/*.conf — zone ที่ติดตั้งจะไม่ถูกโหลด"

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
  echo "คำเตือน: ไม่เจอ symlink ใน /etc/nginx/sites-enabled ที่ชี้ไป $SITE — ตรวจว่าแก้ถูกไฟล์" >&2
fi

# ---------- เตรียม rollback ----------
TMP_DIR="$(mktemp -d)"
chmod 755 "$TMP_DIR"
trap 'rm -rf "$TMP_DIR"' EXIT

changed=0
conf_installed=0
conf_backup=""
site_backup=""

restore() {
  if [[ -n "$site_backup" ]]; then
    run cp -p "$site_backup" "$SITE"
    echo "คืน $SITE จาก $site_backup แล้ว" >&2
  fi
  if (( conf_installed )); then
    if [[ -n "$conf_backup" ]]; then
      run cp -p "$conf_backup" "$CONF_DST"
      echo "คืน $CONF_DST จาก $conf_backup แล้ว" >&2
    else
      run rm -f "$CONF_DST"
      echo "ลบ $CONF_DST ที่เพิ่งติดตั้งแล้ว" >&2
    fi
  fi
}

# ---------- 1) conf.d: real_ip + zone ----------
if run test -f "$CONF_DST" && run cmp -s "$CONF_SRC" "$CONF_DST"; then
  echo "[ข้าม] $CONF_DST ตรงกับ repo แล้ว"
else
  if run test -f "$CONF_DST"; then
    # ชื่อสำรองต้องไม่ลงท้าย .conf — conf.d/*.conf ถูก include ทั้งหมด
    conf_backup="$CONF_DST.bak-$STAMP"
    run cp -p "$CONF_DST" "$conf_backup"
    echo "สำรอง $CONF_DST ไว้ที่ $conf_backup"
  fi
  run install -m 0644 "$CONF_SRC" "$CONF_DST"
  conf_installed=1
  changed=1
  echo "[ติดตั้ง] $CONF_DST"
fi

# ---------- 2) vhost: แทรก location ก่อน "location / {" ----------
if run grep -qF "$MARKER_BEGIN" "$SITE"; then
  echo "[ข้าม] $SITE มีบล็อก rate limit อยู่แล้ว"
else
  # บล็อกต้นฉบับอยู่ใน nginx-api.conf ระหว่าง marker — ตัดย่อหน้า 4 ช่องของ repo ออกแล้วใช้ย่อหน้าของไฟล์จริงแทน
  block="$TMP_DIR/block.conf"
  sed -n "/$MARKER_BEGIN/,/$MARKER_END/p" "$VHOST_SRC" | sed 's/^    //' > "$block"
  grep -qF "$MARKER_END" "$block" || fail "ไม่พบบล็อก $MARKER_BEGIN ... $MARKER_END ใน $VHOST_SRC"

  count="$(run grep -cE '^[[:space:]]*location[[:space:]]+/[[:space:]]*\{' "$SITE" || true)"
  (( count >= 1 )) || fail "ไม่พบ 'location / {' ใน $SITE — ไม่รู้จะแทรกตรงไหน (ยังไม่ได้แก้อะไร)"

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
  echo "[แก้] แทรกบล็อก rate limit ก่อน 'location / {' ใน $SITE จำนวน $count จุด"
fi

# ---------- 3) ตรวจ config แล้ว reload ----------
if (( changed )); then
  if ! run nginx -t; then
    restore
    fail "nginx -t ไม่ผ่าน — คืนไฟล์เดิมแล้ว ไม่ได้รีโหลด (ถ้า error เรื่อง real_ip_header/zone ซ้ำ แปลว่ามีไฟล์อื่นตั้งไว้แล้ว)"
  fi
  run systemctl reload nginx
  echo "รีโหลด nginx แล้ว"
else
  echo "ไม่มีอะไรเปลี่ยน — ไม่ได้รีโหลด"
fi

# ---------- 4) ตรวจผล ----------
if [[ "${SKIP_CHECKS:-0}" == "1" ]]; then
  echo "ข้ามการตรวจผล (SKIP_CHECKS=1)"
  exit 0
fi

# ผ่าน Cloudflare จริง (เส้นทางเดียวกับผู้ใช้)
curl --fail --silent --show-error --retry 3 --retry-delay 2 "https://$API_HOST/health" >/dev/null
echo "https://$API_HOST/health ผ่าน Cloudflare ตอบปกติ"

# ยิงตรงเข้า Nginx ในเครื่อง (--resolve คง SNI/ใบรับรองเดิม) — ไม่ผ่าน Cloudflare จึงไม่โดน rate limit ของ Cloudflare ปน
# body ว่าง = ASP.NET ตอบ 400/422 ไม่แตะ Garage DB และไม่นับตัวนับต่อ username
nginx_429=0
app_429=0
codes=()
for _ in $(seq 1 20); do
  headers="$TMP_DIR/headers"
  code="$(curl --silent --output /dev/null --dump-header "$headers" --write-out '%{http_code}' \
    --resolve "$API_HOST:443:127.0.0.1" -H 'Content-Type: application/json' -X POST --data '{}' \
    "https://$API_HOST/api/v1/auth/login" || true)"
  codes+=("$code")
  if [[ "$code" == "429" ]]; then
    if grep -qi '^x-ratelimit-layer: *nginx' "$headers"; then nginx_429=$((nginx_429 + 1)); else app_429=$((app_429 + 1)); fi
  fi
done
echo "ผล 20 คำขอ: ${codes[*]}"
echo "429 จาก Nginx: $nginx_429 · 429 จาก API: $app_429"
(( nginx_429 > 0 )) || fail "ไม่เจอ 429 จาก Nginx ใน 20 คำขอ — limit_req อาจไม่ทำงาน (config ยังติดตั้งอยู่ ตรวจ $SITE)"
echo "เปิด rate limit ที่ Nginx สำเร็จ"
