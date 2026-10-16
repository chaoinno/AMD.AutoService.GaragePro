# 16 · ตั้งค่าส่งอีเมลผ่าน Cloudflare Email Service

> สำหรับทีมที่ดูแลบัญชี Cloudflare ของ `garage-pro.net` และทีมที่ deploy ServicePro
> โค้ดฝั่ง API: `backend/AMD.AutoService.GaragePro.Infrastructure/Email/CloudflareEmailSender.cs` (branch `feat/cloudflare-email`)

## 1. ระบบส่งอีเมลอะไรบ้าง

อีเมลทั้งหมดมาจากระบบทดลองใช้ (self-serve shard `sp1`) ระบบเดิมของลูกค้า GaragePro ไม่ส่งอีเมล

| อีเมล | ส่งเมื่อ | tag |
|---|---|---|
| รหัสยืนยัน 6 หลัก | สมัครด้วยอีเมล / ขอรหัสใหม่ | `otp` |
| แจ้งว่ามีบัญชีอยู่แล้ว | สมัครซ้ำด้วยอีเมลที่ใช้งานอยู่ | `account-exists` |
| ต้อนรับ + รหัสช่างตัวอย่าง | เปิดอู่สำเร็จ | `welcome` |
| เตือนก่อนหมดอายุ | เหลือ 7 วัน และ 1 วัน | `reminder-7`, `reminder-1` |
| หมดช่วงทดลองใช้ | ครบ 30 วัน | `expired` |
| ชวนกลับมา | 45 วันหลังหมดอายุ | `comeback` |

ประมาณ 6 ฉบับต่ออู่ที่สมัคร แพ็กเกจ Workers Paid รวม 3,000 ฉบับต่อเดือน จึงพอสำหรับราว 400 อู่ต่อเดือน
เกินจากนั้นคิดเพิ่ม 0.35 ดอลลาร์ต่อ 1,000 ฉบับ

**ถ้ายังไม่ได้ตั้งค่า** ระบบยังทำงานได้ หน้าสมัครจะซ่อนช่องทาง "สมัครด้วยอีเมล" และเหลือแค่ Google

### สถานะ (2026-10-16)

ฝั่ง Cloudflare ทำครบแล้วและส่งจริงได้:

- โดเมนที่ onboard คือ **`service.garage-pro.net`** ไม่ใช่ `garage-pro.net` ตัวหลัก ที่อยู่ผู้ส่งต้องลงท้าย `@service.garage-pro.net` เสมอ
  ถ้าใช้ `@garage-pro.net` Cloudflare จะปฏิเสธ (โดเมนหลักไม่มี `cf-bounce` และ DKIM)
- DNS ครบ: MX/SPF ที่ `cf-bounce.service.garage-pro.net` · DKIM ที่ `cf-bounce._domainkey.service.garage-pro.net` · DMARC `_dmarc.service.garage-pro.net` = `v=DMARC1; p=reject;`
- ส่งทดสอบผ่าน REST API ด้วยรูปแบบเดียวกับ `CloudflareEmailSender` (`from` เป็น object `address`/`name`) จาก `ServicePro <no-reply@service.garage-pro.net>`
  ได้ HTTP 200 `queued` แล้วเข้ากล่องจดหมาย Gmail (ไม่เข้า spam) ชื่อผู้ส่งขึ้น ServicePro
- ค่า `Production:SelfServe:Email:*` (Provider, ApiKey, AccountId, FromAddress, FromName) อยู่ใน user-secrets ของ Mac แล้ว ยังไม่ได้ขึ้นเซิร์ฟเวอร์
  จะขึ้นตอนรัน `bootstrap-production-secrets.sh` แล้ว deploy ในช่วงเปิดรับสมัคร (ข้อ 4) · ยังไม่ได้ตั้ง `ReplyToAddress`

## 2. สิ่งที่ทีม Cloudflare ต้องทำ

### 2.1 เปิดแพ็กเกจ Workers Paid

การส่งอีเมลออกต้องใช้ Workers Paid (แพ็กเกจ Free รับอีเมลเข้าได้อย่างเดียว) เปิดบนบัญชีเดียวกับที่ถือโดเมน `garage-pro.net`

### 2.2 Onboard โดเมนผู้ส่ง

1. Dashboard > **Compute** > **Email Service** > **Email Sending**
2. กด **Onboard Domain** เลือก `service.garage-pro.net` (ทำแล้ว) ดูรายการ DNS ก่อนกด **Done**
3. Cloudflare จะเพิ่ม DNS ให้เอง:

| ชนิด | ชื่อ | ค่า |
|---|---|---|
| MX | `cf-bounce.service.garage-pro.net` | `route1/2/3.mx.cloudflare.net` |
| TXT (SPF) | `cf-bounce.service.garage-pro.net` | `v=spf1 include:_spf.mx.cloudflare.net ~all` |
| TXT (DKIM) | `cf-bounce._domainkey.service.garage-pro.net` | กุญแจที่ Cloudflare สร้าง |
| TXT (DMARC) | `_dmarc.service.garage-pro.net` | `v=DMARC1; p=reject;` (ค่าปัจจุบัน) |

ข้อควรตรวจก่อนกด Done:

- **SPF ของโดเมนไม่ถูกแตะ** เพราะ record ส่งอยู่ที่ `cf-bounce` ถ้าโดเมนใช้ Google Workspace หรือเมลอื่นอยู่แล้วก็ไม่กระทบ
- **DMARC:** ต้องมี record เดียวต่อชื่อ อย่าให้มีสองอัน ค่าปัจจุบันเป็น `p=reject` แปลว่าอีเมลที่ SPF/DKIM ไม่ผ่านจะถูกตีกลับทั้งฉบับ
  ไม่ใช่แค่เข้า spam ถ้าส่งจากระบบอื่นในนาม `service.garage-pro.net` ด้วย ต้องตั้ง SPF/DKIM ของระบบนั้นให้ผ่านก่อน
- รอ 5 ถึง 15 นาที แล้วดูที่ **Email Sending** > **Settings** > **DNS records** ทุกแถวต้องขึ้น Locked หรือ Unlocked (ทั้งสองแบบแปลว่าถูกต้อง)
  ถ้าขึ้น Not Found ให้รอเพิ่ม (กระจายได้ถึง 24 ชั่วโมง)

### 2.3 สร้าง API token

1. Dashboard > รูปโปรไฟล์ > **API Tokens** > **Create Token** > **Custom token**
2. Permissions: **Account** · **Email Sending** · **Edit** (สิทธิ์เดียวพอ ไม่ต้องให้สิทธิ์อื่น)
3. Account Resources: เลือกเฉพาะบัญชีที่ onboard โดเมนไว้
4. (แนะนำ) Client IP Address Filtering: ใส่ IP ขาออกของเซิร์ฟเวอร์ production ที่รัน `garagepro_service_api`
5. สร้างแล้ว **คัดลอก token ทันที** (Cloudflare แสดงครั้งเดียว)

### 2.4 เลือกที่อยู่อีเมล

| ค่า | แนะนำ | หมายเหตุ |
|---|---|---|
| ผู้ส่ง | `no-reply@service.garage-pro.net` | ไม่ต้องมีกล่องจดหมายจริง · ต้องอยู่ใต้โดเมนที่ onboard (ส่งจริงแล้ว 2026-10-16) |
| ชื่อผู้ส่ง | `ServicePro` | |
| ตอบกลับ (ไม่บังคับ) | `support@garage-pro.net` | ต้องเป็นกล่องที่มีคนอ่านจริง ถ้ายังไม่มี ใช้ Email Routing ของ Cloudflare ส่งต่อเข้า Gmail ของทีมได้ฟรี |

### 2.5 ส่งค่าให้ทีม deploy

- **Account ID** (หน้า Overview ของบัญชี ไม่ใช่ความลับ)
- **API token** ส่งผ่านช่องทางที่ปลอดภัยเท่านั้น (password manager หรือส่งตรงตัวต่อตัว) ห้ามส่งในกลุ่ม LINE/อีเมล/Git
- ที่อยู่ผู้ส่ง และที่อยู่ตอบกลับ

## 3. ทดสอบด้วย curl ก่อนส่งต่อ

รันบนเครื่องที่ IP อยู่ใน filter (ถ้าตั้งไว้) เปลี่ยน `YOUR_INBOX@example.com` เป็นกล่องจริงของคนทดสอบ

```bash
read -rs CF_EMAIL_TOKEN
```

วาง token แล้วกด Enter (ไม่แสดงบนจอและไม่ค้างใน history) จากนั้น

```bash
CF_ACCOUNT_ID=ใส่_account_id && curl -sS -X POST "https://api.cloudflare.com/client/v4/accounts/$CF_ACCOUNT_ID/email/sending/send" -H "Authorization: Bearer $CF_EMAIL_TOKEN" -H "Content-Type: application/json" -d '{"from":{"address":"no-reply@service.garage-pro.net","name":"ServicePro"},"to":"YOUR_INBOX@example.com","subject":"ทดสอบส่งอีเมล ServicePro","text":"ถ้าได้รับฉบับนี้ แปลว่าตั้งค่า Cloudflare Email Service สำเร็จ"}'
```

ผลที่ถูกต้อง: `"success":true` และอีเมลอยู่ใน `"delivered"` หรือ `"queued"` แล้วอีเมลเข้ากล่อง (ดูโฟลเดอร์ spam ด้วย)
ถ้าได้ error ดูตารางข้อ 5

## 4. สิ่งที่ทีม deploy ทำ

ใส่ค่าใน dotnet user-secrets ของ Mac (คีย์ `Production:SelfServe:Email:*`) แล้วให้ `bootstrap-production-secrets.sh` เขียน env file บนเซิร์ฟเวอร์
**ห้ามแก้ env file บนเซิร์ฟเวอร์ด้วยมือ** เพราะสคริปต์เขียนทับทั้งไฟล์ทุกครั้งที่รัน

```bash
cd backend/AMD.AutoService.GaragePro.API && read -rs CF_EMAIL_TOKEN && dotnet user-secrets set "Production:SelfServe:Email:ApiKey" "$CF_EMAIL_TOKEN" && unset CF_EMAIL_TOKEN
```

```bash
cd backend/AMD.AutoService.GaragePro.API && dotnet user-secrets set "Production:SelfServe:Email:Provider" "cloudflare" && dotnet user-secrets set "Production:SelfServe:Email:AccountId" "ใส่_account_id" && dotnet user-secrets set "Production:SelfServe:Email:FromAddress" "no-reply@service.garage-pro.net" && dotnet user-secrets set "Production:SelfServe:Email:FromName" "ServicePro"
```

ที่อยู่ตอบกลับ (ไม่บังคับ) ใส่เมื่อมีกล่องที่มีคนอ่านจริงแล้วเท่านั้น:

```bash
cd backend/AMD.AutoService.GaragePro.API && dotnet user-secrets set "Production:SelfServe:Email:ReplyToAddress" "support@garage-pro.net"
```

```bash
./scripts/bootstrap-production-secrets.sh
```

```bash
./scripts/deploy-production.sh
```

ตรวจหลัง deploy (ต้องเปิด `SELFSERVE_ENABLED=true` แล้ว):

1. `GET https://gpservice-api.garage-pro.net/api/v1/public/signup/config` ต้องได้ `"emailEnabled":true`
2. สมัครด้วยอีเมลจริงหนึ่งกล่อง ต้องได้รหัส 6 หลักภายในหนึ่งนาที
3. log ของ API มีบรรทัด `ส่งอีเมล otp ถึงโดเมน ... แล้ว` (log บอกแค่โดเมนผู้รับ ไม่มีที่อยู่เต็มและไม่มีรหัส)

```bash
ssh garage_amd "docker logs --since 10m garagepro_service_api 2>&1 | grep -i 'Cloudflare Email\|ส่งอีเมล'"
```

ทดสอบจากเครื่อง dev ได้ด้วยคีย์ที่ไม่มี `Production:` นำหน้า (`SelfServe:Email:Provider` ฯลฯ) ระวังว่าจะส่งอีเมลจริงและนับโควตาจริง

## 5. อาการผิดพลาดที่พบบ่อย

| อาการ | สาเหตุ | แก้ |
|---|---|---|
| log ตอน API เริ่ม: `Cloudflare Email ตั้งค่าไม่ครบ (SelfServe:Email:...)` | ไม่ได้ใส่คีย์ที่บอกชื่อไว้ | ใส่ user-secrets แล้วรัน bootstrap + deploy ใหม่ ระหว่างนี้หน้าสมัครเหลือแค่ Google |
| API ไม่ขึ้น: `SelfServe:Email:Provider '...' ยังไม่รองรับ` | สะกด Provider ผิด | ใช้ `cloudflare` หรือ `log` เท่านั้น |
| HTTP 401 code 10101 | token ผิดหรือถูกลบ | สร้าง token ใหม่ (ข้อ 2.3) |
| HTTP 403 code 10102 | token ไม่มีสิทธิ์ Email Sending หรือเป็นของบัญชีอื่น | ตรวจ Permissions และ Account Resources ของ token |
| HTTP 403 code 10105 (not_entitled) | บัญชียังไม่ใช่ Workers Paid | ข้อ 2.1 |
| HTTP 403 code 10203 (sending_disabled) | การส่งของบัญชีถูกปิด | ดูหน้า Email Sending ใน dashboard หรือติดต่อ Cloudflare |
| HTTP 400 code 10001 | รูปแบบคำขอผิด หรือที่อยู่ผู้ส่งไม่ใช่โดเมนที่ onboard | ตรวจ `FromAddress` ว่าเป็น `@service.garage-pro.net` (โดเมนหลัก `garage-pro.net` ไม่ได้ onboard) |
| HTTP 429 code 10004 | เกินโควตารายวัน (บัญชีใหม่เริ่มต่ำแล้วค่อยเพิ่มตามประวัติการส่ง) | รอรอบถัดไป หรือขอเพิ่มลิมิตกับ Cloudflare |
| `permanent bounce` | กล่องปลายทางไม่มีอยู่จริง | ผู้ใช้กรอกอีเมลผิด หน้าสมัครแจ้งให้ลองใหม่ |
| ส่งสำเร็จแต่เข้า spam | DNS ยังไม่ครบ หรือโดเมนเพิ่งเริ่มส่ง | ตรวจ DKIM/SPF ในข้อ 2.2 และ `dig TXT cf-bounce._domainkey.service.garage-pro.net` |

เมื่อส่ง OTP ไม่สำเร็จ ผู้สมัครจะเห็นข้อความ "ส่งอีเมลรหัสยืนยันไม่สำเร็จ กรุณาลองใหม่..." พร้อมทางเลือกสมัครด้วย Google ส่วนอีเมลต้อนรับ/เตือนที่ส่งไม่สำเร็จจะไม่ทำให้การสมัครหรืองานรายวันล้ม

## 6. ความปลอดภัย

- token เก็บใน user-secrets และ env file บนเซิร์ฟเวอร์ (mode 600) เท่านั้น ไม่อยู่ใน Git ไม่อยู่ใน Docker image
- API ไม่ log เนื้อหาอีเมล (มีรหัส OTP) ไม่ log ที่อยู่เต็ม และไม่ log token
- token มีสิทธิ์ส่งอีเมลอย่างเดียว ถ้ารั่วให้ลบ token ใน dashboard แล้วสร้างใหม่ตามข้อ 2.3 และ 4

## อ้างอิง

- [Domain configuration](https://developers.cloudflare.com/email-service/configuration/domains/)
- [Pricing](https://developers.cloudflare.com/email-service/platform/pricing/) · [Limits](https://developers.cloudflare.com/email-service/platform/limits/)
