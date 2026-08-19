# GaragePro Service Ops deployment

ค่าเริ่มต้นของชุด deploy นี้:

- SSH host: `ipong_server`
- Remote directory: `/home/ipong/garagepro-service-ops`
- PM2 app: `garagepro-service-ops`
- Local upstream: `127.0.0.1:3040`
- Domain: `gp-service.ipongs.com`

## Deploy หรืออัปเดตไฟล์

รันจากเครื่อง local ในโฟลเดอร์โปรเจกต์:

```bash
./deploy.sh
```

สคริปต์จะ sync ไฟล์, รัน `npm install`, ตรวจ syntax, restart แอปผ่าน PM2,
บันทึก PM2 process list และตรวจหน้าเว็บผ่าน local upstream

## ติดตั้ง Nginx vhost

หลังจากสร้าง Cloudflare DNS record แล้ว รัน:

```bash
ssh -t ipong_server '~/garagepro-service-ops/scripts/setup-nginx.sh'
```

สคริปต์จะใช้ Cloudflare Origin Certificate เดิมที่:

- `/etc/ssl/certs/cf-origin.pem`
- `/etc/ssl/private/cf-origin.key`

จากนั้นจะสร้าง `/etc/nginx/sites-available/gp-service.ipongs.com`, เปิดใช้งาน vhost,
ตรวจด้วย `nginx -t` และ reload Nginx โดยจะถามรหัสผ่าน sudo

Cloudflare DNS ให้สร้าง record แบบ Proxied:

- Type: `A`
- Name: `gp-service`
- Content: IP ของ `ipong_server`

ตั้ง SSL/TLS encryption mode ใน Cloudflare เป็น `Full (strict)`

## ให้ PM2 กลับมาเองหลัง reboot

เซิร์ฟเวอร์ยังไม่ได้เปิด PM2 systemd startup ให้รันครั้งเดียว:

```bash
ssh -t ipong_server '~/garagepro-service-ops/scripts/setup-pm2-startup.sh'
```

## คำสั่งตรวจสอบ

```bash
ssh ipong_server 'export NVM_DIR="$HOME/.nvm"; . "$NVM_DIR/nvm.sh"; pm2 status garagepro-service-ops'
ssh ipong_server 'curl -I http://127.0.0.1:3040/'
```
