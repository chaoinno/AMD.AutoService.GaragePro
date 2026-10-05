#!/usr/bin/env bash
# เพิ่ม service.garage-pro.net (ชื่อใหม่หลัง rebrand เป็น ServicePro) เข้า vhost ของ gpservice ที่มีอยู่แล้ว
# **ไม่ออกใบรับรองใหม่** (ตัดสินใจกับผู้ใช้ 2026-10-05) — ผู้เข้าเว็บเห็นใบของ Cloudflare อยู่แล้ว ส่วนขา
# Cloudflare → เซิร์ฟเวอร์ใช้ใบ Let's Encrypt เดิมของ gpservice ซึ่งใช้ได้เพราะ Cloudflare ตั้ง SSL mode เป็น
# "Full" (ไม่ตรวจชื่อบนใบ)
#
# [RISK] ถ้าเปลี่ยน Cloudflare เป็น "Full (strict)" โดเมนนี้จะขึ้น 526 ทันที — ต้องออกใบที่มีชื่อนี้ก่อน
#
# ต้องรันบนเซิร์ฟเวอร์ด้วยสิทธิ์ sudo หลัง deploy-production.sh (ซึ่ง rsync ไฟล์นี้มาให้):
#   /home/deployment/deployments/garagepro-service/add-service-domain.sh
# รันซ้ำได้ — ถ้าเพิ่มไปแล้วจะข้ามไปตรวจผลอย่างเดียว
set -Eeuo pipefail

SITE=/etc/nginx/sites-available/gpservice.garage-pro.net
OLD=gpservice.garage-pro.net
NEW=service.garage-pro.net

command -v sudo >/dev/null || { echo "sudo is required" >&2; exit 1; }
sudo test -f "$SITE" || { echo "ไม่พบ $SITE — ต้องติดตั้งด้วย install-nginx.sh ก่อน" >&2; exit 1; }

# ระวัง: "gpservice.garage-pro.net" มี "service.garage-pro.net" อยู่ข้างใน — ต้องมีช่องว่างนำหน้าถึงนับว่าเป็นชื่อใหม่
if sudo grep -qE "server_name[^;]*[[:space:]]service\.garage-pro\.net;" "$SITE"; then
  echo "$NEW อยู่ใน $SITE แล้ว — ข้ามการแก้ไข"
else
  backup="$SITE.bak-$(date +%Y%m%d%H%M%S)"
  sudo cp -p "$SITE" "$backup"
  echo "สำรองไฟล์เดิมไว้ที่ $backup"

  # ต่อท้ายชื่อใหม่ในทุก server_name ของ gpservice (block 443 และ block 80 ที่ certbot สร้าง)
  sudo sed -i "s/server_name $OLD;/server_name $OLD $NEW;/" "$SITE"

  # block 80 ของ certbot redirect เฉพาะ host เดิม ที่เหลือคืน 404 — ถ้าไม่เพิ่ม http://$NEW จะเป็น 404
  # (Cloudflare ไม่ได้เปิด "Always Use HTTPS" จึงส่ง http ผ่านมาถึงเซิร์ฟเวอร์จริง)
  sudo sed -i "s|^\(\s*\)if (\$host = $OLD) {|\1if (\$host = $NEW) {\n\1    return 301 https://\$host\$request_uri;\n\1}\n\n\1if (\$host = $OLD) {|" "$SITE"

  if ! sudo nginx -t; then
    sudo cp -p "$backup" "$SITE"
    echo "nginx -t ไม่ผ่าน — คืนไฟล์เดิมแล้ว ไม่ได้รีโหลด" >&2
    exit 1
  fi
  sudo systemctl reload nginx
  echo "รีโหลด nginx แล้ว"
fi

# ตรวจผ่าน Cloudflare จริง (เส้นทางเดียวกับผู้ใช้) ไม่ใช่ 127.0.0.1
curl --fail --silent --show-error --retry 3 --retry-delay 2 "https://$NEW/healthz" >/dev/null
curl --fail --silent --show-error --retry 3 --retry-delay 2 "https://$OLD/healthz" >/dev/null
code=$(curl --silent --output /dev/null --write-out '%{http_code}' "http://$NEW/")
[[ "$code" == "301" ]] || { echo "http://$NEW ได้ $code แทนที่จะเป็น 301" >&2; exit 1; }
echo "เพิ่ม $NEW สำเร็จ — https ทั้งสองโดเมนตอบปกติ และ http เด้งไป https"
