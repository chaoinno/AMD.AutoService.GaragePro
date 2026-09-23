# CLAUDE.md

คำแนะนำสำหรับ Claude Code เมื่อทำงานกับ repository นี้

## ภาพรวม

**AMD.AutoService.GaragePro** — ระบบปฏิบัติการงานบริการ (service ops) สำหรับอู่ซ่อมรถ
สร้างจาก design prototype ที่อนุมัติแล้ว ต่อยอดบนข้อมูลของระบบ GaragePro เดิม

Mono-repo 3 ส่วน:
```
backend/   .NET 8 Web API      (Domain / Application / Infrastructure / API / Tests)
mobile/    Flutter             หน้าร้าน + ช่าง + ลูกค้าอนุมัติราคา
web/       Vite + React 19     ผู้จัดการ ธุรการ แคชเชียร์
docs/      เอกสารที่สกัดจาก design + แผนงาน
GaragePro Service Ops Design/  prototype ต้นฉบับ (read-only — เป็น source of truth ของ UI)
```

## อ่านก่อนเริ่ม

| ไฟล์ | เมื่อไหร่ |
|---|---|
| **[docs/10-handover.md](docs/10-handover.md)** | **สรุปสั้น: อยู่ตรงไหน · ค้างอะไร · ทำอะไรต่อ — อ่านก่อนไฟล์อื่นทั้งหมด** |
| [docs/01-workflow.md](docs/01-workflow.md) | state machine 10 สถานะ · 12 transition · business rules · **9 open questions** |
| [docs/02-domain-model.md](docs/02-domain-model.md) | entity + **15 invariant ที่ต้อง enforce ที่ API** |
| [docs/03-api-contract.md](docs/03-api-contract.md) | endpoint map ทุกหน้าจอ |
| [docs/04-project-plan.md](docs/04-project-plan.md) | แผน 10 phase |
| [docs/05-legacy-db-mapping.md](docs/05-legacy-db-mapping.md) | **สิ่งที่ reuse จาก Garage DB ได้/ไม่ได้ + ความเสี่ยง** |
| [docs/06-customer-vehicle-management.md](docs/06-customer-vehicle-management.md) | ลูกค้า/รถ · SQL scope ตามสาขา · ข้อจำกัดข้อมูล legacy ร่วมกัน |
| [docs/staff-api.md](docs/staff-api.md) | API พนักงาน · อ่านคู่กับข้อจำกัดสาขาล่าสุดด้านล่าง (Admin ไม่ได้สิทธิ์ข้ามสาขา) |
| [docs/06-purchasing-fifo.md](docs/06-purchasing-fifo.md) | PR/PO/GRN · Stock FIFO · สิทธิ์ · transaction/idempotency · ขอบเขตที่ยังไม่รวม |
| [docs/07-quotation-adhoc-line.md](docs/07-quotation-adhoc-line.md) | รายการนอกแคตตาล็อกในใบเสนอราคา (ad-hoc line) · ฟิลด์บังคับ · สิทธิ์ · ผลกระทบต่อเบิกสต็อก/QC |
| [docs/08-quotation-template.md](docs/08-quotation-template.md) | เทมเพลตใบเสนอราคา · การแปลงเทมเพลต→บรรทัด (ราคาสดจากแคตตาล็อก) · สิทธิ์ · error code ใหม่ |
| [docs/09-technician-time-tracking.md](docs/09-technician-time-tracking.md) | จับเวลาช่างต่อจ๊อบ · สลับคัน · โหมดโฟกัสบนมือถือ — **เก็บเวลาทำแล้ว · รายงานประเมินยังไม่ได้ทำ** |

> Design เขียนไว้ชัด: *"ห้ามตีความจากหน้าจอเพียงอย่างเดียว เพราะต้นแบบเลือกทางที่เดินเรื่องได้ ไม่ใช่ทางที่องค์กรอนุมัติแล้ว"*

---

## คำสั่งที่ใช้บ่อย

```bash
# Backend
dotnet build AMD.AutoService.GaragePro.sln
dotnet test backend/AMD.AutoService.GaragePro.Tests
cd backend/AMD.AutoService.GaragePro.API && dotnet run   # พอร์ต 5080 ตั้งใน Properties/launchSettings.json
# → Swagger ที่ http://localhost:5080/swagger

# Migration (ห้ามใช้ EnsureCreated — มันไม่เพิ่มตารางให้ฐานที่มีอยู่แล้ว)
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add <ชื่อ> --project backend/AMD.AutoService.GaragePro.Infrastructure \
  --startup-project backend/AMD.AutoService.GaragePro.API --output-dir Persistence/Migrations
dotnet ef database update --project backend/AMD.AutoService.GaragePro.Infrastructure \
  --startup-project backend/AMD.AutoService.GaragePro.API

# ข้อมูลทดสอบ (ต้องรัน API ก่อน) — สร้างใบเสนอราคาครบทุกสถานะผ่าน API จริง
cd tools/devseed && dotnet run

# Web
cd web && pnpm install && pnpm dev          # http://localhost:5173
pnpm exec tsc -b && pnpm build              # ใช้ -b เพราะ tsconfig หลักเป็น project references
node --test tests/tableSort.test.mjs        # ทดสอบ comparator และ TanStack sorting (ตรวจด้วย Node 24)

# Mobile
export PATH="$PATH:/Users/pongsathon/Developement/flutter/bin"
cd mobile && dart analyze lib/
flutter run -d <udid> --dart-define=API_BASE_URL=http://localhost:5080
# Android emulator ใช้ http://10.0.2.2:5080
```

**ต้องต่อ Garage Pro VPN ก่อนเสมอ** ถึงจะเข้าฐานข้อมูลได้
```bash
scutil --nc start "Garage Pro VPN" && sleep 10 && scutil --nc status "Garage Pro VPN"
```

## Production deployment — Web + API

Production รันที่ `ssh garage_amd` จาก GitHub branch `main`; รอบนี้ deploy เฉพาะ `web/` และ `backend/`
ไม่ build/deploy `mobile/`:

| รายการ | ค่า |
|---|---|
| Source บน server | `/home/deployment/sources/AMD.AutoService.GaragePro` |
| Deployment overlay | `/home/deployment/deployments/garagepro-service` |
| Secret file (นอก Git, mode 600) | `/home/deployment/config/garagepro-service.env` |
| Web | `https://gpservice.garage-pro.net` → Nginx → `127.0.0.1:3005` |
| API | `https://gpservice-api.garage-pro.net` → Nginx → `127.0.0.1:5081` |
| Containers | `garagepro_service_web`, `garagepro_service_api` |

ไฟล์หลักอยู่ใน `deploy/production/`: multi-stage Dockerfiles, Compose, Nginx vhosts และ migration bundle
โดย `.dockerignore` กัน `appsettings.Development.json` ที่มี secret ไม่ให้เข้า image เด็ดขาด รูปพนักงาน/รถอยู่ใน
named volume; ไฟล์แนบยังอยู่ FTP ตาม `Ftp__RootPath` เดิม

> **[แก้ 2026-09-17] ชื่อ volume จริงคือ `garagepro-service_garagepro_service_storage`** (Compose เติม prefix ชื่อ
> project ให้) mount ที่ `/app/storage` ของ `garagepro_service_api` — **ไม่ใช่ `garagepro_service_storage`** ที่เคย
> บันทึกไว้ตรงนี้ บนเครื่องมี volume ชื่อนั้นอยู่จริงแต่ **ว่างเปล่า ไม่ได้ถูก mount ที่ไหน** (น่าจะค้างจาก
> `docker compose` รอบเก่าที่ตั้งชื่อ project ต่างกัน) — **อันตราย: ถ้าสำรอง/กู้คืนตามชื่อเดิมจะได้ของว่าง**
> ตรวจชื่อจริงทุกครั้งด้วย `docker inspect garagepro_service_api --format '{{range .Mounts}}{{.Name}}{{end}}'

ครั้งแรกบน Mac ให้สร้าง secret file โดยไม่พิมพ์ค่าออก terminal (ดึง DB/JWT จาก dotnet user-secrets ของ repo นี้
และ FTP จาก config ของ AMD.GaragePro.Admin ที่ server):

```bash
./scripts/bootstrap-production-secrets.sh
```

Deploy ปกติจาก Mac (สคริปต์ไม่ commit/push):

```bash
./scripts/deploy-production.sh
```

ลำดับภายในคือ SSH ไป `git fetch` + `git pull --ff-only` (หยุดถ้า tracked file บน server ถูกแก้), sync เฉพาะ
deployment overlay จาก Mac, build image, รัน EF migration bundle, `docker compose up -d --wait` แล้วตรวจ local/public
health ถ้าเป็นครั้งแรกและยังไม่ลง vhost ให้ใช้ `SKIP_PUBLIC_CHECKS=1 ./scripts/deploy-production.sh` ก่อน

งานที่ต้อง `sudo` แยกไว้ให้เจ้าของเครื่องรันและใส่รหัสเอง สคริปต์นี้ติดตั้งสอง vhost, ตรวจ `nginx -t`, ออก/ต่ออายุ
Let's Encrypt certificate ผ่าน Certbot, เปิด HTTPS redirect และตรวจ public health:

```bash
ssh garage_amd
/home/deployment/deployments/garagepro-service/install-nginx.sh <letsencrypt-email>
```

ตรวจ/แก้เหตุขัดข้องโดยไม่ restart บริการอื่น:

```bash
ssh garage_amd
cd /home/deployment/deployments/garagepro-service
export GARAGEPRO_SOURCE_DIR=/home/deployment/sources/AMD.AutoService.GaragePro
export GARAGEPRO_DEPLOY_DIR=$PWD
export GARAGEPRO_SECRETS_FILE=/home/deployment/config/garagepro-service.env
docker compose ps
docker compose logs --tail=200 api web
curl -fsS http://127.0.0.1:5081/health
curl -fsS http://127.0.0.1:3005/healthz
```

> ห้ามใส่ secret ใน Compose/CLAUDE.md/Git และห้ามใช้ `docker compose down -v` บน production เพราะจะลบ volume รูป
> Deployment จะ migrate ฐานข้อมูลก่อนเปลี่ยน container; ถ้า migration หรือ health check ไม่ผ่าน สคริปต์จะหยุดทันที

บันทึก initial deploy 2026-09-09: clone `main` application commit `d11deb9` แล้ว, secret file mode 600 ครบทุก key,
EF bundle ตรวจแล้วว่า ServiceDb ไม่มี migration ค้าง และ container web/API healthy ที่ loopback ทั้งคู่
production image ไม่มี `appsettings.Development.json`; bundle ของ frontend ชี้ API ไป
`https://gpservice-api.garage-pro.net` ถูกต้อง ติดตั้ง Nginx vhost + Let's Encrypt แล้ว (certificate หมดอายุ
2026-12-08) public web `/`/`/login` และ `/healthz` คืน 200, public API `/health` คืน 200 และ CORS preflight
จาก `https://gpservice.garage-pro.net` คืน 204 พร้อม origin ที่ถูกต้อง
ผลตรวจรอบนี้: backend 136 ผ่าน/3 skipped (SQL/FTP integration ที่ต้องมี environment), web comparator 3 ผ่าน,
TypeScript + Vite production build ผ่าน, Docker build/health ผ่าน และ Nginx config syntax ผ่าน
มี warning เดิม ImageSharp `NU1902` ระดับ moderate กับ Vite chunk ~848 kB ซึ่งยังไม่ได้แก้ในงาน deploy นี้

---

## สถาปัตยกรรมที่ต้องเข้าใจก่อนแก้โค้ด

### 1. Hybrid: อ่าน legacy / เขียนฐานใหม่ (read-only ล้วน — ไม่มีข้อยกเว้นแล้ว)

| | ที่ไหน | ทำอะไร |
|---|---|---|
| `Garage` (10.10.4.11) | legacy | อ่านข้อมูลหลักเท่านั้น — ห้าม write |
| `GarageService` (10.10.4.11) | ของเรา | ตาราง `svc_*` ทั้งหมด รวมถึง `svc_Job` |

`PJCarPickUp` มี lock convoy อยู่แล้ว (lock wait 92–96% ของทั้งระบบ,
lock escalation 1.37 ล้านครั้ง, RCSI ปิด) ทุก query ที่ `LegacyReader`/`LegacyUserReader` ต้อง:
1. เลือกเฉพาะคอลัมน์ที่ใช้ — ห้าม `SELECT *` (EF6 เดิมอ่านแถวละ ~18 MB เพราะดึง LOB)
2. ใส่ `WITH (READUNCOMMITTED)`
3. ไม่มีคำสั่งเขียนใดๆ

**ข้อยกเว้น 2026-08-26 ถูกยกเลิกแล้ว (2026-08-31):** เดิมอนุญาตให้หน้า Web `/jobs` เปิดจ๊อบลง
Garage DB ตาม `ProjectAdd.aspx` ผ่าน `ILegacyJobWriter`/`LegacyJobWriter` — **ตัดสินใจใหม่แล้ว
ยกเลิกข้อยกเว้นนี้ทั้งหมด** `LegacyJobWriter`/`ILegacyJobWriter` ถูกลบออกจากโค้ดแล้ว
จ๊อบทั้งหมดสร้างและเก็บใน `svc_Job` (GarageService) เท่านั้น อ้างอิงลูกค้า/รถด้วย `CustomerId`/`VehicleId`
(legacy id, อ่านอย่างเดียวผ่าน `CustomerVehicleService` ที่มีอยู่แล้ว — คนละ flow กับที่ถูกลบ)
`Quotation`/`IntakeChecklist`/`Attachment`/`ActivityEvent` ผูกกับ `Job.Id` (Guid) โดยตรงแทน
composite `(LegacyShardKey, LegacyBranchId, LegacyJobId)` เดิม

> **[RISK — ตรวจพบ 2026-09-04] นโยบายกับโค้ดจัดการข้อมูลหลักยังไม่ตรงกัน:**
> `CustomerVehicleRepository` ยังมี INSERT/UPDATE ของ Customer, Car, CarCustomer;
> `StaffRepository` ยังเขียน Staff, User และข้อมูลประกอบพนักงานใน Garage เดิม
> การจำกัดสาขาที่ทำล่าสุดไม่ใช่การย้ายข้อมูลเหล่านี้ไป GarageService และไม่ใช่การอนุมัติข้อยกเว้นใหม่
> ก่อนเพิ่ม/ทดสอบคำสั่งเขียน legacy ต้องยืนยันแนวทางกับเจ้าของระบบหรือออกแบบย้ายการเขียนให้ตรงนโยบายนี้
> ห้ามถือข้อความอธิบายพฤติกรรมปัจจุบันในเอกสารโมดูลเป็นสิทธิ์ให้เขียนฐานจริง
> โมดูลจัดซื้อ/FIFO ใหม่เขียนเฉพาะ ServiceDb (`svc_*`) ไม่ได้เขียน Garage legacy

### 2. GaragePro เป็น multi-tenant SaaS แบ่ง shard

`Branch.Id` **ไม่ unique ข้าม shard** — Id 30 ใน db1 กับ db2 เป็นคนละอู่

```
db1    10.10.4.14   147 อู่
db2    10.10.4.11   325 อู่   ← shard เป้าหมายของระบบใหม่
maindb 10.10.4.16   = db2 (replica คนละ host)
```

**ทุก reference ต้องเป็นคู่ `(LegacyShardKey, LegacyBranchId, id)`** — ทุกตาราง `svc_*` จึงมี 3 คอลัมน์นี้
`ShardKey` มาจาก JWT claim ไม่ใช่จาก request body

### 3. Legacy DB เป็นอู่สีตัวถัง+เคลม ไม่ใช่ service

`Pjstatus` มี 61 สถานะแนวเคาะ-พ่นสี · `PjquotationStatus` มีแค่ 3 ค่า (E-Claim) ·
`Gpscode` 19,823 รหัสเป็นอะไหล่ตัวถังล้วน

→ **แคตตาล็อก service (`svc_CatalogItem`) สร้างใหม่ทั้งหมด** ใช้ `Gpscode` แทนไม่ได้

---

## กฎที่ห้ามละเมิด (business rules)

กฎเหล่านี้ต้อง enforce **ที่ API** ไม่ใช่แค่ซ่อนปุ่มที่ client

1. **Quotation เป็น version-first** — ออกฉบับแก้ไข → ฉบับเดิม `Superseded`, **ApprovalRecord เดิมเป็นโมฆะ**, ทุกบรรทัดกลับเป็น `Pending`
2. **ลายเซ็นผูกกับ `QuotationVersion`** — ต้อง validate ก่อนให้เริ่มซ่อมเสมอ
3. **เฉพาะบรรทัดที่ `Approved`** เท่านั้นที่เข้าสู่การซ่อมและการเรียกเก็บเงิน
4. **ไม่อนุมัติต้องมีเหตุผลเสมอ** (5 ตัวเลือกตายตัว)
5. **ค่าแรงต้องระบุช่าง** ก่อนส่งใบเสนอราคา · ห้ามมีรายการซ้ำ
6. `Available = OnHand − Reserved` — **ไม่รวม OnOrder ไม่รวม Damaged**
7. **ต้นทุน/กำไร/คอมมิชชัน strip ที่ serializer ตาม role** (`QuotationMapper`) ไม่ใช่ให้ client ซ่อน
8. **ทุก state change เขียน `ActivityEvent` พร้อม `Source`** (mobile/web/system)
9. **ช่างและหัวหน้าช่างไม่มีสิทธิ์ปิดกะ** (`RoleMapper.CanCloseShift`)
10. **เอกสารที่ให้ลูกค้าห้ามแสดงต้นทุน/กำไร** แม้ role จะเห็นได้

### ค่า `[ASSUME]` ที่ยังไม่ได้รับการยืนยัน
ส่วนลด >10% ต้องผู้จัดการ · margin <15% เตือน · SLA 90% · คอมมิชชัน 8/10/12%
ตอนนี้เป็น const ใน `QuotationCalculator` — **ต้องย้ายไป config table ก่อน production**

โมดูลจัดซื้อใช้ `Purchasing:ManagerApprovalThreshold` สำหรับวงเงินอนุมัติ PO แล้ว
(ค่าเริ่มต้น 10,000 บาทยังเป็น `[ASSUME]` ต้องยืนยันก่อน production) ไม่ใช่ const ของ QuotationCalculator

### กฎใหม่: Job / รับรถ (เพิ่ม 2026-08-31)
11. **`IntakeChecklistItem.ItemCode` ต้องมาจาก `IntakeChecklistTemplate` เท่านั้น** — code แปลกปลอมจาก client ถูกปฏิเสธ (`INTAKE_ITEM_UNKNOWN`)
12. **ผล `Issue`/`NotApplicable` ต้องมี `Note` เสมอ** (`INTAKE_NOTE_REQUIRED`) · checklist ล็อกอ่านอย่างเดียวหลัง submit (`INTAKE_LOCKED`) · submit ไม่ได้ถ้ายังมีรายการ `Pending` (`INTAKE_INCOMPLETE`)
13. **1 รถ 1 job ที่เปิดอยู่ต่อสาขา** — เปิดซ้ำถูกบล็อกด้วย `JOB_DUPLICATE_OPEN`
14. **`JobTypeId` ต้องเป็น 9 (รถในอู่) หรือ 10 (รถนัดหมาย) เท่านั้น** ตอนเปิดจ๊อบ (`JOB_VALIDATION`) · เปลี่ยนสถานะแบบ manual (ไม่มี guard อัตโนมัติรองรับ) ต้องระบุเหตุผลเสมอ (`JOB_TRANSITION_NEEDS_REASON`)
    · **[เพิ่ม 2026-09-09] ถึงสถานะจบ (`Completed`/`Cancelled`) แล้ว ระบบเปลี่ยน `JobTypeId`/`JobTypeName` เป็น `11`/"ปิดจ๊อบ" ให้เองใน `JobService.TransitionAsync`**
      (ตรวจด้วย `JobStateMachine.IsTerminal`) — ค่านี้เลือกตอนเปิดจ๊อบไม่ได้ (ยังคงบังคับ 9/10 เท่านั้นที่ `CreateAsync`)
      เป็นค่าที่ระบบตั้งเองครั้งเดียวตอนจบงานและเป็น terminal อยู่แล้วจึงไม่มีทางเปลี่ยนกลับ · ทำให้ตัวกรอง "รถในอู่"
      (ที่ตอนนี้เป็นค่าเริ่มต้นของหน้ารายการจ๊อบ) ไม่ดึงงานที่ปิดแล้วมาปนอัตโนมัติ โดยไม่ต้องพึ่งตัวกรองสถานะเพิ่ม
15. **[เพิ่ม 2026-09-16] `JobTypeId=10` (รถนัดหมาย) ⟹ `Job.AppointmentAt` ไม่ว่าง** บังคับที่ API ตอนเปิดจ๊อบ/
    แก้ไขนัดหมาย (type 10 ต้องมีค่า → `JOB_VALIDATION` field `appointmentAt` ถ้าไม่ส่ง · type อื่นห้ามส่งค่ามา →
    `JOB_VALIDATION` เช่นกัน) **[แก้ไข 2026-09-17] ไม่ใช่ทิศทางเดียวกัน (⟺) อีกต่อไป** — ตั้งแต่มี
    `ConvertToInShopAsync` (กฎข้อ 17) ค่า `AppointmentAt` อาจไม่ว่างได้แม้ `JobTypeId=9` แล้ว (ไม่ถูกล้างตอนแปลง
    ประเภท เก็บไว้เป็นประวัติ) · **วันเวลานัดหมายห้ามน้อยกว่าวันเวลาปัจจุบัน** (ยืนยันกับผู้ใช้ 2026-09-17 — เผื่อ
    tolerance 5 นาทีกันเวลา client/server คลาดกัน ไม่ใช่ grace period ให้เลือกอดีตจริงๆ) แก้ไข/เลื่อนนัดได้ภายหลัง
    ผ่าน `PUT /jobs/{id}/appointment` จนกว่าจะถึงสถานะจบ (`JOB_APPOINTMENT_LOCKED`) · ใช้เป็นตัวกรองมุมมองปฏิทิน
    นัดหมาย (`GET /jobs/calendar`) แทน `JobTypeId==10` เพราะกฎข้อ 14 เปลี่ยน `JobTypeId` เป็น 11 เองตอนปิดงาน —
    ถ้ากรองด้วยประเภทนัดหมายเก่าที่ปิดแล้วจะหายจากปฏิทินย้อนหลัง (ปฏิทินยังแสดงจ๊อบที่ถูกแปลงเป็นรถในอู่แล้วต่อไป
    ตามช่วงวันนัดเดิม — เป็นพฤติกรรมที่ตั้งใจ ดูกฎข้อ 17)
16. **[เพิ่ม 2026-09-16] เทมเพลตใบเสนอราคา** — สร้าง/แก้ไข/เปิดปิดใช้งาน = ผู้จัดการเท่านั้น (`MASTER_DATA_MANAGE_FORBIDDEN`)
    นำไปใช้ (`POST /quotations/{id}/lines/from-template`) = ทุกคนที่แก้ใบเสนอราคาได้ · บรรทัดแคตตาล็อกอ่านราคา/ชื่อ
    **สดจากแคตตาล็อกเสมอตอนนำไปใช้** ไม่ใช่ค่าที่ cache ไว้ในเทมเพลต · รหัสแคตตาล็อกที่หายไปแล้ว → ปฏิเสธทั้งชุดไม่เพิ่ม
    สักบรรทัด (`QUOTE_TEMPLATE_ITEM_MISSING`) · รายละเอียดเต็มดู [docs/08-quotation-template.md](docs/08-quotation-template.md)
17. **[เพิ่ม 2026-09-17] แปลงงานนัดหมายเป็นรถในอู่ (`PUT /jobs/{id}/convert-to-in-shop`)** — เปลี่ยน `JobTypeId`
    10→9 พร้อมบันทึก `Job.ActualArrivalAt` (วันเวลาที่รถเข้าอู่จริง) **ไม่ผูกกับวันนัดหมายที่ตั้งไว้เลย** (มาก่อน/
    หลังนัดก็แปลงได้ ไม่ตรวจสอบว่า "ถึงวันนัด" แล้วหรือยัง — เป็นการยืนยันด้วยพนักงานล้วนๆ) `AppointmentAt` เดิม
    **ไม่ถูกล้าง** (ดูข้อยกเว้นในกฎข้อ 15) ปฏิเสธถ้า `JobTypeId != 10` (`JOB_TYPE_CONVERSION_NOT_ALLOWED`) หรือ
    ถ้า `ActualArrivalAt` เป็นอนาคต (`JOB_VALIDATION`) — ไม่แตะ `JobStateMachine` เลย (ไม่ใช่ state transition
    แค่เปลี่ยนหมวดหมู่ + timestamp) เขียน `ActivityEvent` `job.converted_to_in_shop` เสมอ
18. **[เพิ่ม 2026-09-17] ต้องออกใบเสร็จก่อน จึงจะให้ลูกค้าเซ็นรับรถได้** (`HANDOVER_RECEIPT_REQUIRED`) —
    ลำดับที่ร้านใช้จริงคือ **ลูกค้าจ่ายเงินที่เคาน์เตอร์ → ออกใบเสร็จ → ค่อยส่งมอบรถ** (ยืนยันกับเจ้าของระบบ)
    เดิมทั้งสามเงื่อนไข (ชำระครบ/ออกใบเสร็จ/ส่งมอบ) ถูกตรวจ **พร้อมกันตอนปิดงานเท่านั้น** จึงเซ็นรับรถก่อนจ่ายเงิน
    ได้จริง แล้วรถออกจากอู่ไปโดยงานค้างปิดไม่ได้ — `HandoverService.SubmitAsync` ตรวจ `Receipt` ของงานนั้นก่อนเสมอ
    · ตรวจ **ใบเสร็จ** (ไม่ใช่ยอดคงเหลือ) เพียงพอ เพราะ `PosService.IssueReceiptAsync` ปฏิเสธด้วย
    `POS_BALANCE_NOT_SETTLED` ถ้ายอดยังไม่เป็นศูนย์ → มีใบเสร็จ ⟹ จ่ายครบแล้วเสมอ
    · **`GetOrCreateAsync`/`SaveItemAsync` ไม่ถูกบล็อก** โดยตั้งใจ — ติ๊กของในรถล่วงหน้าคู่ขนานกับที่แคชเชียร์
    กำลังเก็บเงินได้ ด่านอยู่ที่ขั้นเซ็น+ล็อกเท่านั้น · `HandoverDto` เพิ่ม `ReceiptIssued`/`ReceiptDocumentNo`
    ให้หน้าจอบอกเหตุผลปุ่มที่ปิดอยู่ได้โดยไม่ต้องเรียก `/payments` (ซึ่งเปิดเฉพาะ Cashier/Office/Manager
    — ช่างเรียกเองจะได้ 403) **ส่งแค่ "ออกแล้วหรือยัง" กับเลขที่เอกสาร ไม่มียอดเงิน** ตาม `docs/01-workflow.md` §4
19. **[เพิ่ม 2026-09-17] ส่งมอบรถ + ปิดงาน เปิดให้ทุกบทบาทปฏิบัติการ รวมช่าง/หัวหน้าช่าง** —
    `HandoverService.ValidateAsync` และ `JobStateMachine` (`Ready→Completed`) รับครบทั้ง 6 role
    (เดิม handover = FrontDesk/Cashier/Office/Manager · close = Cashier/Office/Manager ซึ่งขัดกันเองอยู่แล้ว
    เพราะหน้าร้านส่งมอบได้แต่ปิดงานของตัวเองไม่ได้) เหตุผล: คนที่ยืนอยู่กับลูกค้าตอนเซ็นรับรถคือคนที่เข็นรถออกมา
    ไม่ใช่คนหลังเคาน์เตอร์ · **ไม่ได้ลดการตรวจสอบเลย** — guard ทั้ง 3 ตัวของ `Ready→Completed` เป็น
    `isComputable = true` คำนวณจาก `Payment`/`Receipt`/`HandoverRecord` จริงเสมอ manual-override ด้วย `reason` ไม่ได้
    · **`PosService` ไม่แตะ** ยังเป็น Cashier/Office/Manager — ช่างยังรับเงิน/ออกใบเสร็จเองไม่ได้ตาม §4
    · รายชื่อ role ที่ `HandoverService` ยังเขียนครบทุกค่า (ไม่ได้ลบทิ้ง) เพื่อให้ role ใหม่ที่เพิ่มทีหลัง
    ต้องถูกพิจารณาก่อน ไม่ได้สิทธิ์เงียบๆ

> **[RISK]** `JobsController`/`IntakeChecklistController` ตอนนี้ gate ด้วย `[RequireShiftSession]` เท่านั้น
> ยังไม่ผูก role ตาม `docs/01-workflow.md §4` (เช่น ใครก็ตามที่ login แล้วมีกะเปิดอยู่สร้าง/เปลี่ยนสถานะ job ได้หมด) — ต้องปิดช่องนี้ก่อน production
> ปฏิทินนัดหมาย, เทมเพลตใบเสนอราคา และปุ่มแปลงเป็นรถในอู่ที่เพิ่งเพิ่มสืบทอดช่องโหว่นี้เช่นกัน ไม่ได้แก้ในงานนี้

---

## บทเรียนที่เจ็บมาแล้ว (อย่าทำซ้ำ)

### 🔴 HTTP header รับได้เฉพาะ ASCII
ใส่ชื่อภาษาไทยใน header ทำให้ **Dart โยน `FormatException` และ browser `fetch` ไม่ยิง request เลย**
(`curl` ปล่อยผ่าน เลยไม่เจอตอนเทสต์ backend)
→ ตัวตนทั้งหมดอยู่ใน **JWT claim** แล้ว ห้ามส่งชื่อผู้ใช้ทาง header อีก

### 🔴 Guid PK ต้อง `ValueGeneratedNever()`
entity กำหนด `Id = Guid.NewGuid()` เอง ถ้าไม่ประกาศ EF จะเดาว่าเป็นแถวเดิมแล้วยิง `UPDATE` แทน `INSERT`
อาการ: `DbUpdateConcurrencyException: expected to affect 1 row(s), but actually affected 0`

### 🔴 `EnsureCreated()` ไม่เพิ่มตารางให้ฐานที่มีอยู่แล้ว
ตารางใหม่หายไปเงียบๆ จนกว่าจะมีคนเรียกใช้ → `Invalid object name`
→ **ใช้ `Database.MigrateAsync()` เท่านั้น**

### 🔴 Dart: `whenComplete(() => map.remove(key))` ทำให้ future รอตัวเอง
แคชรูปใน `mobile/lib/features/attachments/data/attachment_cache.dart` เคยเขียนว่า
`_inFlight[key] ??= fetch().whenComplete(() => _inFlight.remove(key))`
`Map.remove` **คืนค่าที่ถูกลบ** ซึ่งคือ Future ตัวที่ `whenComplete` กำลังสร้างอยู่พอดี และ `whenComplete`
จะรอ Future ที่ callback คืนมาให้เสร็จก่อน = **รอตัวเอง ค้างถาวร**
อาการ: รูปทุกใบในแอปหมุนไม่รู้จบ ไม่มี error ให้เห็น ไม่มีอะไรใน log ทั้งฝั่งแอปและ server
(หลงคิดว่าเป็นปัญหาเครือข่าย/FTP/timeout อยู่นาน) — `receiveTimeout` ก็ไม่ช่วยเพราะคำขอ HTTP จบไปแล้วตั้งแต่แรก
→ callback ของ `whenComplete` ต้องเป็น block ที่คืน `void` เสมอ: `.whenComplete(() { map.remove(key); })`
→ มีเทสต์กันไว้แล้วที่ `mobile/test/attachment_cache_test.dart` และ `mobile/test/auth_image_test.dart`
→ **`pumpAndSettle()` ใช้จับบั๊กแบบนี้ไม่ได้** เพราะ `CircularProgressIndicator` หมุนตลอด ทำให้ timeout เสมอ
   ไม่ว่าโค้ดจะถูกหรือผิด — ต้องใช้ `pump(Duration(...))` แล้วเช็ค widget ที่คาดหวังแทน

### 🟡 iOS ATS บล็อก HTTP
`mobile/ios/Runner/Info.plist` มี `NSAllowsLocalNetworking` + exception domain สำหรับ `localhost` (dev เท่านั้น)

### 🟡 `vatRate` เป็นสัดส่วน ไม่ใช่เปอร์เซ็นต์
API ส่ง `0.07` — ต้องคูณ 100 ก่อนแสดง ไม่งั้นได้ "ภาษี 0.07%"

### 🟡 EF Core ห้ามใช้ `.SingleAsync()`/`.FirstAsync()` ต่อท้าย raw SQL ที่มี `OUTPUT`
`JobNumberGenerator` ออกเลขจ๊อบด้วย `MERGE ... WITH (HOLDLOCK)` + `OUTPUT inserted.LastSequence` ผ่าน `SqlQueryRaw`
(กันชนเลขซ้ำเวลาออกพร้อมกันหลาย request ต่อ branch/วันเดียวกัน) — ถ้าต่อท้ายด้วย `.SingleAsync()`/`.FirstAsync()`
EF จะห่อ query เป็น subquery ทำให้ `MERGE`/`OUTPUT` composable ไม่ได้ (SQL error)
→ ต้องใช้ `.ToListAsync()` แล้วดึงตัวแรกเอง

### 🔴 [แก้แล้ว 2026-09-16] DateTime ต้องระบุ `Kind=Utc` ทั้งขาเข้า/ขาออก ไม่งั้นเวลาที่แสดงคลาดเคลื่อน +7 ชม.
ก่อนหน้านี้ไม่มีที่ไหนระบุ `DateTimeKind` เลย (ไม่มี converter ที่ `ServiceDbContext`, ไม่มี JSON date converter
ที่ `Program.cs`) — EF อ่าน `datetime2` กลับมาเป็น `Kind=Unspecified` เสมอ ทำให้ JSON ที่ API ส่งออกไม่มี `Z`
ต่อท้าย แล้ว `new Date(...)` ฝั่งเว็บตีความเป็นเวลาท้องถิ่นแทนที่จะเป็น UTC → **เวลาทุกจุดบนเว็บ (createdAt,
promiseAt ฯลฯ) แสดงเร็วกว่าเวลาไทยจริง 7 ชั่วโมงมาตลอด** โดยไม่มีใครสังเกตเพราะ `curl`/backend test ไม่โดน
→ แก้ที่จุดเดียวด้วย `ServiceDbContext.ConfigureConventions` (ผูก `ValueConverter<DateTime,DateTime>`/
`<DateTime?,DateTime?>` ที่บังคับ `DateTime.SpecifyKind(v, DateTimeKind.Utc)` ตอนอ่านทุกคอลัมน์ทั่วทั้งโมเดล
โดยไม่ต้องแก้ทีละ DTO) — ฝั่งเว็บเพิ่ม `parseApiInstant` ใน `web/src/lib/format.ts` เป็นชั้นป้องกันสุดท้าย
(เติม `Z` เองถ้าค่าที่ได้ไม่มีโซนเวลา เช่นจาก DTO ที่มาจาก Dapper อ่าน legacy ที่ไม่ผ่าน EF)

---

## ความปลอดภัย — เรื่องที่ต้องรู้

### 🔴 รหัสผ่านใน Garage DB เป็น plaintext
ตรวจแล้ว ~9,500 บัญชี **ไม่มี hash เลย** (ความยาว 3–17 ตัว, 0 รายการขึ้นต้นด้วย `$2`)
`AuthService.PasswordMatches` จึงต้องเทียบแบบ plaintext เพื่อให้ผู้ใช้เดิมเข้าได้ แต่:
- เทียบแบบ **constant-time** (`CryptographicOperations.FixedTimeEquals` บน SHA-256 ของทั้งสองฝั่ง)
- **ห้าม log / ส่งกลับ / ใส่ใน exception** ค่ารหัสผ่านทุกกรณี
- **ต้องวางแผนย้ายไป hash โดยเร็ว** — เป็นความเสี่ยงที่ใหญ่ที่สุดของระบบตอนนี้

### 🔴 ห้าม commit ความลับ
`Jwt:Key`, connection string ทั้งหมด, **`Ftp:Username`/`Ftp:Password`** (เพิ่ม 2026-09-09 พร้อมระบบไฟล์แนบใหม่)
อยู่ใน **dotnet user-secrets** เท่านั้น `appsettings.Development.json` ควรมีแค่ placeholder

> อ้างอิง: `AMD.GaragePro.Admin/backend/.../appsettings.json` มีรหัส `sa`, รหัส FTP, JWT key เป็น plaintext ใน repo — **อย่าทำตาม pattern นั้น**

> **[RISK — พบ 2026-09-09]** `backend/AMD.AutoService.GaragePro.API/appsettings.Development.json` ของโปรเจกต์นี้เอง
> ตอนนี้มี `ConnectionStrings:ServiceDb` และ `LegacyShards:ConnectionStrings` เป็นรหัสผ่านจริงในไฟล์ (ไม่ใช่ placeholder)
> ขัดกับนโยบายบรรทัดบน — พบระหว่างแก้ระบบไฟล์แนบรอบนี้ ไม่ได้แก้ให้เพราะไม่ใช่ขอบเขตงานและอาจกระทบ dev setup ที่ใช้อยู่
> ต้องยืนยันกับเจ้าของระบบก่อนย้ายไป user-secrets จริง (เหมือนที่ `Jwt:Key` ทำอยู่แล้ว)

### สิ่งที่ยังต้องทำ
- `X-Client-Source` ยังเป็น header ที่ปลอมได้ (ไม่กระทบสิทธิ์ แต่ทำให้ audit log ระบุแหล่งที่มาผิดได้)
- ยังไม่มี refresh token — token หมดอายุ 12 ชม. แล้วต้อง login ใหม่
- ยังไม่มี rate limit บน `/auth/login`

---

## โครง Backend

```
Domain/          ไม่มี dependency ภายนอกเลย
  StateMachine/  JobStateMachine — transition 12 เส้นทาง + guard 13 ตัว (แหล่งความจริงเดียว)
  Common/        QuotationCalculator (สูตรเงิน) · QuotationValidator · RoleMapper
  Entities/      Job · IntakeChecklist (+ IntakeChecklistItem) · JobNumberCounter
                 Quotation · QuotationLine · QuotationApproval · CatalogItem
                 Shift · ShiftSession · UserRoleOverride · Attachment · ActivityEvent
Application/     service + DTO + abstraction (interface ทั้งหมดอยู่ที่นี่)
Infrastructure/  EF Core (write) · Dapper (อ่าน legacy) · JWT · file storage
API/             controller บางๆ — logic อยู่ที่ Application
```

**หลักการ:** กฎ lifecycle อยู่ที่ `JobStateMachine` ที่เดียว ห้ามกระจายไปอยู่ใน controller หรือ service

### Auth flow
```
Web + Mobile: POST /auth/login → token ใช้งานได้ทันที (มี branch จาก Staff.BranchId ใน claim)
```
**[เปลี่ยน 2026-09-23] มือถือไม่มีขั้นเลือกสาขา/กะแล้ว** (คำขอผู้ใช้) — login แล้วเข้าระบบทันทีเหมือนเว็บ
ลบ `BranchShiftPage`/`pendingLoginProvider`/route `/auth/shift` และปุ่ม "ปิดกะ" ในแอปออก
`Session` (mobile) เหลือแค่ token/user/branch · endpoint `/auth/branches/{id}/shifts` และ `/auth/shift-sessions`
**ยังอยู่ที่ backend ไม่ได้ลบ** แต่ไม่มี client เรียกแล้ว · `[RequireShiftSession]` ยังตรวจแค่ว่ามี branch claim
· ผลต่อการจับเวลาช่าง: คาบใหม่จากมือถือมี `ShiftSessionId = null` ⇒ auto-cap ใช้ `MaxOpenIntervalHours`
`[ASSUME] 12` ชม. แทน `Shift.EndTime` และไม่มี hook ปิดคาบตอนปิดกะอีก (lazy cap 3 จุดยังทำงานตามเดิม)

### Envelope
```jsonc
{ "success": true,  "data": {...}, "error": null, "traceId": "..." }
{ "success": false, "data": null,  "error": { "code": "...", "messageTh": "..." }, "traceId": "..." }
```
`messageTh` เป็นภาษาไทยพร้อมแสดงผล — **client ห้ามแปลหรือแต่งใหม่**

### ไฟล์แนบ (Attachment) เก็บบน FTP ตั้งแต่ 2026-09-09

`IAttachmentStorage` (ลายเซ็น/รูปรับรถ/รูปตรวจเช็ค/เอกสาร — เข้าถึงผ่าน `AttachmentsController`/`AttachmentService`)
เปลี่ยนจากดิสก์ในเครื่องเป็น **FTP** ตามคำขอผู้ใช้ ใช้เครื่องเดียวกับ `AMD.GaragePro.Admin` (`10.10.3.11`) แต่คนละ
`RootPath` (`/AutoServiceGaragePro/attachments/`) กันชนกัน — implementation: `FtpAttachmentStorage.cs`
(แทนที่ `AttachmentStorage.cs` เดิมที่ลบไปแล้ว) ใช้ package `FluentFTP 50.0.0` (`AsyncFtpClient`)

**ต้นแบบที่ผู้ใช้ให้มา** (`AMD.GaragePro.Admin/.../FileManagerController.cs`) **ไม่ได้ก็อปมาตรงๆ** เพราะมีช่องโหว่จริง —
`Delete`/`Download` ต่อ path จาก query string ตรงๆ ไม่มีการกัน path traversal เลย, ไม่จำกัดขนาด/ชนิดไฟล์,
ใช้ `[Authorize(Roles = "Admin")]` ซึ่งไม่ตรงกับ auth model ของโปรเจกต์นี้ (branch/shard จาก JWT claim)
`FtpAttachmentStorage` คงคุณสมบัติของระบบเดิมไว้ครบแทน:
- `Validate()` เดิม (ขนาด/ชนิดไฟล์) ไม่เปลี่ยน
- สร้างชื่อไฟล์เป็น GUID เสมอ (ไม่ใช้ชื่อจาก client) และ sanitize ทุก path segment (`shardKey`/`branchId`/`jobId`/`kind`) แบบเดียวกับของเดิม
- **`IsSafeRelativePath`**: ปฏิเสธ path ที่ขึ้นต้นด้วย `/`, มี `\`, หรือมี segment `.`/`..` — ก่อนต่อ FTP path เสมอ (แก้ช่องโหว่ที่ reference ไม่มี)
- แฮช SHA-256 + จำกัดขนาดไฟล์ **ระหว่างสตรีมอัปโหลดจริง** ผ่าน `HashingLimitedStream` (stream decorator ที่ห่อ
  ต้นทางแล้วส่งเข้า `client.UploadStream()` ตรงๆ — ไม่ต้องพักไฟล์ไว้ที่ดิสก์ในเครื่องก่อนเหมือน draft แรก)

Interface เปลี่ยน: `IAttachmentStorage.TryResolve(path, out fullPath)` (คืน local path ใช้กับ `PhysicalFile()`)
ถูกแทนด้วย `OpenReadAsync(path)` (คืน `Stream?` ดาวน์โหลดจาก FTP) — `AttachmentsController.GetFile` เปลี่ยนจาก
`PhysicalFile(...)` เป็น `File(stream, ...)` ตาม `AttachmentFile.Content` ที่เป็น `Stream` แทน `FullPath` (string)

**ขอบเขต**: ครอบคลุมเฉพาะ `IAttachmentStorage` (job attachments) — `IStaffImageStorage`/`IVehicleImageStorage`
(รูปพนักงาน/รถ) **ยังเป็นดิสก์ในเครื่องเหมือนเดิม ไม่ได้ย้าย** เพราะผู้ใช้ระบุขอบเขตเฉพาะ `IAttachmentStorage`
ตอนถาม — ถ้าต้องการย้ายด้วยต้องยืนยันแยก

**ทดสอบแล้ว**: `dotnet build` ทั้ง solution ผ่าน, `dotnet test` ผ่านทั้ง 105 (ไม่รวม 3 ที่ skip เพราะต้องต่อ
SQL/FTP จริง) — เทสต์ `Validate()` และ `IsSafeRelativePath` (กัน path traversal) รันได้จริงไม่ต้องต่อเครือข่าย
ส่วนเทสต์ round-trip Save/OpenRead/Delete จริงต้องตั้ง `GARAGEPRO_FTP_HOST`/`GARAGEPRO_FTP_USERNAME`/
`GARAGEPRO_FTP_PASSWORD` (env var) ก่อนถึงจะรัน (เหมือน `PurchasingSqlFactAttribute`)

> **[อัปเดต 2026-09-17] พิสูจน์กับ FTP เครื่องจริงแล้วเป็นครั้งแรก** — `AttachmentStorageTests` ผ่าน **7/7 skipped 0**
> (เดิม round-trip ถูก skip มาตลอดทุกครั้งที่บันทึกผลตรวจ) ยืนยันว่า upload/download/delete กับ `10.10.3.11` ทำงานจริง
> เครื่อง dev เครื่องนี้ตั้ง `Ftp:Username`/`Ftp:Password` ใน user-secrets แล้ว (ดึงจาก
> `garage_amd:~/sources/AMD.GaragePro.Admin/backend/AMD.GaragePro.Admin.API/appsettings.json` แหล่งเดียวกับที่
> `scripts/bootstrap-production-secrets.sh` ใช้) `Host`/`Port`/`RootPath` อยู่ใน `appsettings.Development.json` อยู่แล้ว
> จึงไม่ต้องตั้งซ้ำ · **เครื่อง dev เครื่องอื่นยังต้องตั้งเองก่อนใช้งาน** ไม่งั้นทุกการอัปโหลด (ลายเซ็น/รูปรับรถ/รูปแชท)
> จะได้ `FtpAuthenticationException: 501 Invalid number of parameters` ตอน `Connect()` เพราะส่ง `USER` เปล่าไป
>
> **[RISK] exception ของ FTP หลุดไปถึง middleware กลายเป็น HTTP 500 ดิบ** ไม่ใช่ envelope `{success,error{messageTh}}`
> ตามสัญญาของระบบ — ฝั่ง client จึงได้ข้อความกลางๆ "เซิร์ฟเวอร์ตอบกลับผิดพลาด (HTTP 500)" แทนสาเหตุจริง
> `AttachmentService.UploadAsync` ไม่ได้ดัก `FtpException` เลยสักตัว ควรแปลงเป็น error code ของระบบ
> (เช่น `ATTACHMENT_STORAGE_UNAVAILABLE`) — **ยังไม่ได้แก้ในงานนี้** พบระหว่างทดสอบ 2026-09-17

---

## กฎ UI (ทั้ง Flutter และ React)

- ทุกสถานะสื่อด้วย **สี + ไอคอน + ข้อความ** — ห้ามใช้สีอย่างเดียว
- ทุก state (`loading` `empty` `error` `forbidden` `stale`) ต้องมี **สาเหตุ + ปุ่มถัดไป + traceId**
- **ปุ่มที่ปิดใช้งานต้องบอกเหตุผลเสมอ** — ห้าม disable เฉยๆ
- ตัวเลขเงิน: `IBM Plex Mono` + `tabular-nums` + format `1,234.56` เสมอ
- **[เพิ่ม 2026-09-09] ฟอนต์หลัก Web = `Noto Sans Thai`/`Noto Sans`** (เดิม `IBM Plex Sans` เป็น fallback ตัวที่สอง
  — เปลี่ยนเป็น `Noto Sans` ตามคำขอผู้ใช้ ทั้งที่ `:root` และ `[data-sonner-toaster]` ใน `web/src/index.css`)
  โหลดจริงผ่าน Google Fonts `<link>` ใหม่ใน `web/index.html` (น้ำหนัก 400/500/600/700/800 ตามที่ CSS ใช้จริง
  — เดิมไม่มี `<link>` โหลดฟอนต์เลยสักตัว พึ่ง fallback ของ browser ทั้งหมด) **ตัวเลขเงิน (`IBM Plex Mono`) ไม่ถูกแตะ**
  ยังคงฟอนต์เดิมตามกฎด้านบน — เปลี่ยนเฉพาะฟอนต์ข้อความทั่วไป
  · **ขนาดตัวอักษรทั้งระบบเพิ่มขึ้น +1px ทุกจุด** (`font-size: Npx` → `(N+1)px`, 211 จุดใน `index.css` รวม body
  base `14px→15px`) ตามคำขอ "เพิ่มขนาดอีกซัก 1 size" — ทำแบบ mechanical (สคริปต์ไล่ทุก `font-size` เพิ่มทีละ 1px
  รวมค่าที่อยู่ใน `clamp()`) ไม่ได้เลือกยกเว้นจุดใด แม้แต่ badge วงกลมเล็ก 12×12px ที่ font-size 7-8px เดิม (อาจดู
  แน่นขึ้นเล็กน้อยแต่ยังไม่ล้นกรอบจากการตรวจด้วยตา) — **ยังไม่ได้ตรวจรอบสุดท้ายในเบราว์เซอร์จริงว่าไม่มีจุดไหนล้น/ตัดข้อความ**
  · ปรับเฉพาะ Web — `mobile/lib/core/tokens.dart` (Flutter) ไม่ได้แตะ เพราะฟอนต์/ขนาดไม่ได้อยู่ใน `web/src/lib/tokens.ts`
  (มีแค่สี) จึงไม่ขัดกฎ "ต้องแก้ให้ตรงกันทั้งสองที่" ด้านล่าง — ถ้าต้องการให้ mobile ใช้ฟอนต์/ขนาดเดียวกันต้องทำแยก
- Mobile: touch ≥48px · CTA 54–56px · ฟอร์มยาวใช้ sticky bar + บันทึกร่าง
- Web: desktop 1440 หลัก · 1024 ย่อ sidebar · **< 1024px ไม่รองรับ**
- ข้อความ UI เป็นภาษาไทยทั้งหมด · โค้ดและตัวแปรเป็นอังกฤษ

### ตารางจัดการข้อมูล (อัปเดต 2026-09-04 ตามคำขอผู้ใช้)

- ใช้หัวตารางแบบ Jobs ผ่าน `DataTable`; ตารางรายการใหม่ใช้ `ManagementTable` ซึ่งต่อกับ DataTable เดียวกัน
- คอลัมน์ข้อมูลต้องมี `accessorFn` หรือ `value` ที่ตรงกับค่าที่แสดง การใส่ `sortable` อย่างเดียวไม่เพียงพอ
- ข้อความใช้ comparator ภาษาไทยใน `web/src/lib/tableSort.ts`; จำนวน/เงินใช้ number และวันที่ใช้ timestamp ไม่ใช้ข้อความที่ format แล้ว
- มีลูกศรขึ้น/ลง การยกเลิกเรียง `aria-sort` และรองรับคีย์บอร์ด; รูปภาพ/ปุ่มดำเนินการไม่ต้องเรียง
- **เป็นการเรียงฝั่ง client เฉพาะข้อมูลที่โหลดแล้ว**; ตารางแบ่งหน้าเรียงเฉพาะหน้าปัจจุบัน มีข้อความแจ้งขอบเขต ห้ามอ้างว่าเรียงทั้งฐาน
- หมวดหมู่ใช้ subrows/expanded ของ TanStack เรียงเฉพาะหมวดระดับเดียวกันโดยคงแม่–ลูก ไม่ flatten แล้วเรียงปนกัน
- ตารางรายการหลัก: หัว 14px · เนื้อหา 15–16px · ข้อความรอง 14px · เพิ่ม contrast/ระยะห่าง/สลับสีแถว และเลื่อนแนวนอนได้
- ครอบคลุมลูกค้า รถ พนักงาน สินค้า ซัพพลายเออร์ คลัง หมวดหมู่ PR/PO สต็อก ล็อต/ประวัติ และรายการซัพพลายเออร์ของสินค้า
  ตารางกรอกบรรทัดในฟอร์มจัดซื้อ/รับสินค้าเพิ่มความชัดของตัวหนังสือ แต่ยังไม่ได้เพิ่มการเรียงระหว่างกรอก
- การปรับล่าสุดอยู่ใน CSS ของตาราง Web ไม่ได้เปลี่ยน shared design tokens หรือ UI ฝั่ง Flutter

Design token อยู่ที่ `mobile/lib/core/tokens.dart` และ `web/src/lib/tokens.ts` —
**ค่ามาจาก prototype ที่อนุมัติแล้ว ห้ามแก้ข้างเดียว ต้องแก้ให้ตรงกันทั้งสองที่**

---

## สถานะปัจจุบัน

### เสร็จแล้ว
- ✅ Auth: login ด้วยบัญชีเดิม · ตรวจ User/Staff active · Web ผูก Staff.BranchId อัตโนมัติ · JWT · role mapping
- ✅ Quotation: สร้าง · แก้บรรทัด · validate · ส่ง · **ออกฉบับแก้ไข** · อนุมัติรายบรรทัด · เซ็น
  · **[เพิ่ม 2026-09-15] รายการนอกแคตตาล็อก (ad-hoc line)**: ทุกคนที่แก้ใบเสนอราคาได้เพิ่มรายการที่ไม่มีในแคตตาล็อกได้
  คีย์แค่ ประเภท + ชื่อ + ราคา/หน่วย (จำนวน default 1, หน่วย default ตามประเภท, ต้นทุนไม่บังคับ) จากปุ่ม "เพิ่มรายการเอง
  (ไม่มีในแคตตาล็อก)" ใน `CatalogPanel.tsx` (แทนที่ปุ่ม "เพิ่มรายการใหม่" เดิมที่เปิด `CatalogFormModal` เต็มรูปและ
  gate ด้วย `canSeeCost` เท่านั้น) — มี checkbox "บันทึกเข้าแคตตาล็อกเพื่อใช้ครั้งต่อไป" ให้เลือกได้ (แสดงเฉพาะ
  `canSeeCost` เพราะ `CatalogService.EnsureCanManage` บังคับสิทธิ์นี้อยู่แล้ว) พร้อมรหัสอัตโนมัติ `AD-{yyMMdd}-{HHmmss}`
  · **ตัวแทน**: `ad-hoc line = QuotationLine.CatalogCode ว่าง` (sentinel, ไม่มีคอลัมน์ใหม่ ไม่มี migration — เดิม
  ไม่มี FK ไป `CatalogItem` อยู่แล้วเพราะเป็น snapshot ตาม version-first) `QuotationLineDto`/`QuotationLine` (web) เพิ่ม
  `isAdHoc` (derived) ให้ client ไม่ต้องเดาจาก string ว่าง · ชื่อ/หน่วย/ต้นทุนของบรรทัด ad-hoc **แก้ได้ในที่** ผ่าน
  `LineEditor.tsx` (บรรทัดจากแคตตาล็อกยังล็อกชื่อ/หน่วย/ต้นทุนเหมือนเดิม — `UpdateLineAsync` เช็ค `isAdHoc` ก่อนยอมแก้)
  ต้นทุนยังเป็นค่าที่ server ควบคุมตามกฎข้อ 7 เดิม (ไม่มีสิทธิ์เห็นต้นทุน → บังคับเก็บเป็น 0 เสมอแม้ client ส่งมา)
  · `QuotationValidator.ValidateForSend` เดิมเช็ครายการซ้ำด้วย `CatalogCode` — เพิ่ม branch แยกสำหรับ ad-hoc ให้เทียบ
  ด้วยชื่อแทน (ไม่งั้นทุกรายการนอกแคตตาล็อกจะถูกมองว่าซ้ำกันหมดเพราะ code ว่างเท่ากัน) กฎอื่นยังบังคับเหมือนเดิมทุกข้อ
  (ค่าแรงต้องระบุช่างก่อนส่ง ฯลฯ) · รายละเอียด/เหตุผลการตัดสินใจเต็มดูที่ [docs/07-quotation-adhoc-line.md](docs/07-quotation-adhoc-line.md)
  · **ข้อจำกัด**: บรรทัด ad-hoc **เบิกจากสต็อกไม่ได้** (`StockWithdrawalModal` ข้ามให้อัตโนมัติพร้อมข้อความแจ้งจำนวนที่ข้าม
  แทนที่จะยิงค้นหาแล้วปล่อยหายเงียบๆ แบบเดิม) แต่ยังเข้าเช็คลิสต์ QC ได้ปกติ (QC สร้างจาก snapshot ของบรรทัดอนุมัติ
  โดยตรง ไม่แตะ `CatalogItem`) · ทดสอบแล้ว: `dotnet build`/`dotnet test` ผ่านทั้ง 183 (เพิ่ม `QuotationServiceTests.cs`
  16 ผ่านใหม่) · Web `tsc -b`/`vite build` ผ่าน · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง**
- ✅ Attachment: อัปโหลด · เปิดไฟล์ · ลายเซ็นจากมือถือขึ้น server จริง
- ✅ Job (ใหม่, เว็บเท่านั้น): เปิดจ๊อบลง `svc_Job` · เลขจ๊อบ `JB{yyMMdd}{BranchId:D4}{seq:D3}` ต่อสาขา/วัน
  (กันชนด้วย `MERGE ... WITH (HOLDLOCK)`) · การ์ดจ๊อบ 6 stage ขับเคลื่อนด้วย `Job.Status` ผ่าน `JobCardModal`
  (แทน `JobDetailModal` เดิมที่ลบไปแล้ว) — stage ซ่อม/QC/ชำระเงินยังเป็น placeholder
  · **[ASSUME] ปุ่ม "ยืนยันลูกค้าอนุมัติ (ชั่วคราว)" (เพิ่ม 2026-09-08)**: ให้ Office/Manager กดยืนยันแทนลูกค้า
  จากเว็บตอน job อยู่ `waitapprove` (`JobCardModal.tsx` `QuoteStage`) — เรียก decide-line (อนุมัติทุกบรรทัดที่ค้าง) →
  sign (ด้วยข้อมูล placeholder เช่น `signatureImagePath: 'web-manual-confirmation'`) → transition เป็น `approved`
  ใช้ endpoint decide/sign เดิมที่มีอยู่แล้ว (ไม่ได้ข้าม guard ใดๆ — `JobService.ComputeGuardAsync` ยังคำนวณจาก
  QuotationApproval จริงเสมอ) เปิดสิทธิ์ Office/Manager/Web ที่ transition `WaitApprove→Approved` ใน
  `JobStateMachine.cs` เพิ่มเติม (เดิมมีแค่ FrontDesk/Mobile) ตาม pattern เดียวกับ 3 transition ที่ 0e72ff6 เปิดไว้แล้ว
  — **ตัดสิทธิ์ Web/Office/Manager ออกทันทีที่หน้าอนุมัติของลูกค้าบนมือถือ (docs/01-workflow.md §3.4) ทำงานได้จริง**
  ยังไม่มี UI ให้ปฏิเสธบางบรรทัดจากเว็บ (กดแล้วอนุมัติทุกบรรทัดที่ค้างเท่านั้น)
  · **[BUG พบระหว่างทดสอบปุ่มนี้ — แก้แล้ว]** เดิม `sendQuotation` (`QuotationEditorModal.tsx` ปุ่ม "ส่งให้ลูกค้าอนุมัติ")
  เปลี่ยนแค่สถานะใบเสนอราคาเป็น `sent` แต่ไม่เคยเรียก transition ให้ job ขยับจาก `waitquote` ไป `waitapprove`
  เลย — เป็นช่องว่างเดิมของระบบ (คนละจุดกับปุ่มใหม่) ทำให้ปุ่ม "ยืนยันลูกค้าอนุมัติ" ไม่โผล่แม้ส่งใบเสนอราคาไปแล้ว
  เพราะเงื่อนไขเช็ค `job.status === 'waitapprove'` แต่ job ค้างที่ `waitquote` ตลอด — แก้โดยให้ `sendMutation`
  ใน `QuotationEditorModal.tsx` เรียก `transitionJob(jobId, { toStatus: 'waitapprove' })` ต่อทันทีหลังส่งสำเร็จ
  (guard ผ่านชัวร์เพราะใช้ `ValidateForSend` ตัวเดียวกับที่ `SendAsync` เพิ่งตรวจผ่านมา) และเผื่อ backward-compat
  ให้ปุ่ม "ยืนยันลูกค้าอนุมัติ" เองใน `JobCardModal.tsx` แสดงตอน `job.status === 'waitquote'` ได้ด้วยถ้ามีใบที่
  `sent`/`partial` อยู่แล้ว (เรียก transition ไป `waitapprove` ให้ก่อนภายใน mutation เดียวกัน) — ครอบคลุมจ๊อบเก่าที่
  ค้างอยู่ก่อนแก้จุดนี้ด้วย
  · **[ต้นเหตุจริงที่พบระหว่างทดสอบ]** ผู้ใช้ทดสอบแล้วปุ่มยังไม่โผล่ เพราะ job จริงค้างที่ **`waitinspect`**
  (ไม่ใช่ `waitquote` ตามที่แก้ข้างบนคาดไว้ตอนแรก) — มีคนสร้าง+ส่งใบเสนอราคาได้ทั้งที่ job ยังไม่เคยผ่าน
  `WaitInspect→WaitQuote` จริง (transition นี้เดิมสงวนให้ Technician/Mobile เท่านั้นตามคอมเมนต์ตั้งใจใน
  `JobCardModal.tsx`) ยืนยันกับผู้ใช้แล้วว่าให้เปิดสิทธิ์ชั่วคราวแบบเดียวกับ 4 transition ที่เปิดไปแล้ว —
  เพิ่ม `[ASSUME] UserRole.Office/Manager` + `EventSource.Web` ให้ `WaitInspect→WaitQuote` ใน
  `JobStateMachine.cs:16-22` ด้วย (guard `InspectionComplete` **ไม่ใช่** isComputable จึงบังคับส่ง `reason`
  เสมอเมื่อยืนยันจากเว็บ = manual override เต็มรูปแบบ ไม่ใช่ตรวจจริง) ฝั่งเว็บ `confirmCustomerApprovalMutation`
  ใน `JobCardModal.tsx` `QuoteStage` เดียวกันนี้ไล่ chain ให้เองถ้า `job.status === 'waitinspect'`
  (`waitinspect→waitquote→waitapprove→approved` ในคลิกเดียว) และปุ่มจะ disable พร้อมบอกเหตุผลถ้ายังไม่ได้ส่ง
  checklist สภาพรถขณะรับ (`checklistDone`) กันไม่ให้ข้ามขั้นตรวจสอบทั้งที่ยังไม่มีหลักฐานอะไรเลย
  · **ทั้งหมดนี้ยังทดสอบ end-to-end ในเบราว์เซอร์จริงไม่สำเร็จ ณ ตอนบันทึก** — ตรวจแค่ compile ทั้งสองฝั่งเท่านั้น
  · **[แก้ไข 2026-09-08] ปุ่ม "อนุมัติซ่อม" ไม่ข้ามไปเสร็จงานทั้งหมดอีกต่อไป**: เดิมปุ่มนี้ (ตอน job อยู่
  `approved`) ไล่ transition รวดเดียวจาก `approved` ไปจนถึง `completed` (ข้ามขั้นเบิกสินค้า/QC/ชำระเงินทั้งหมด
  ด้วย manual-override reason เดียว) ผู้ใช้ทดสอบแล้วพบว่าไม่ถูกต้อง — ตอนนี้ปุ่มนี้ทำแค่ `Approved→InProgress`
  ขั้นตอนเดียว (`QuoteStage` ใน `JobCardModal.tsx`) ซึ่ง guard คำนวณได้จริง (`JobService.ComputeGuardAsync`
  isComputable=true จากบรรทัดที่อนุมัติแล้ว) จึง **ไม่ต้องส่ง reason** ต่างจาก manual-override เดิม ปุ่ม "ไล่จน
  เสร็จงาน" (ยืนยันเองแทน QC/ชำระเงินที่ยังไม่มีระบบจริง) ถูกย้ายไปอยู่ในขั้น "เบิกสินค้า/ดำเนินการซ่อม" แทน
  (ชื่อใหม่ "ยืนยันซ่อมเสร็จ (ปิดงาน ชั่วคราว)") ให้เริ่มไล่จาก `inprogress`/`waitparts`/`qc`/`ready` เท่านั้น
  · **ใหม่ — เบิกสินค้าจริงในขั้น "เบิกสินค้า/ดำเนินการซ่อม" (แทน placeholder เดิม)**: `RepairStage` ใน
  `JobCardModal.tsx` เรียกใช้ `StockWithdrawalModal`/`StockWithdrawalDocumentModal` (ใหม่ ใน
  `web/src/features/purchasing/`) ให้สร้างใบเบิกสินค้าหลายรายการในเอกสารเดียว ผูกกับ job นี้โดยตรง พร้อมพิมพ์ใบเบิก
  (ช่องเซ็นผู้เบิก/ผู้จ่าย เหมือนใบรับรถ A4 — ไม่มีเซ็นดิจิทัล) และแสดงประวัติใบเบิกของ job นี้ (เรียกซ้ำเพื่อพิมพ์ได้)
  Backend: `StockMovement` เพิ่มคอลัมน์ `JobId`/`RequesterStaffId`/`RequesterName` (migration
  `20260908163106_AddStockWithdrawalRequesterAndJobLink` — **ยังไม่ได้รัน `dotnet ef database update` จริง
  เพราะ sandbox นี้ไม่ได้ต่อ Garage Pro VPN ต้องรันก่อน deploy**) endpoint ใหม่ใน `StockFIFOController`:
  `POST /api/v1/inventory/withdrawals` (หลายบรรทัด/หลายสินค้าในเอกสารเดียว ออกเลข `WD-` แยกจาก `ISS-` เดิม),
  `GET /api/v1/inventory/withdrawals/{operationId}` (พิมพ์ซ้ำ), `GET /api/v1/inventory/withdrawals/by-job/{jobId}`
  (ประวัติของ job) — สิทธิ์ Manager/Office เท่านั้น (เหมือนโมดูลจัดซื้อเดิม) ผู้เบิก (`RequesterStaffId`) ตรวจกับ
  `IStaffRepository` ว่าเป็นพนักงาน active ของสาขาปัจจุบันจริง ส่วนผู้จ่าย (`PerformedByName`) คือผู้ล็อกอินเดิม
  (ActivityEvent เดิม ไม่ได้เพิ่มฟิลด์ใหม่) เบิกใช้ FIFO allocation เดิมทุกประการ (`PurchasingRules.Allocate`)
  · endpoint เบิกทีละ 1 รายการเดิม (`POST /inventory/issues`) จากหน้า `/inventory` **ยังคงอยู่ ไม่ได้แก้** ใช้คู่ขนานกัน
  · **ยังไม่มีหน้า `/inventory/withdrawals` แบบ standalone** (เบิกได้จาก `/inventory` ทีละสินค้าเดิม หรือจาก
  JobCardModal แบบผูก job หลายรายการเท่านั้น) — ยังไม่ได้ทำเพราะยังไม่ได้รับการยืนยันขอบเขตเพิ่มจากผู้ใช้
  · **[เพิ่ม 2026-09-08 รอบที่สาม] เตรียมรายการสินค้าจากใบเสนอราคาให้อัตโนมัติ**: `StockWithdrawalModal` เมื่อเปิดผูก
  `jobId` จะดึงใบเสนอราคาที่ `status === 'approved'` ของ job นั้น กรองเฉพาะบรรทัด `type === 'part'` (ไม่รวมค่าแรง)
  ที่ `approvalStatus === 'approved'` มาใส่ในตะกร้าให้อัตโนมัติ (จำนวนตามใบเสนอราคา แก้/ลบได้ก่อนบันทึก) —
  ต้องแปลง `QuotationLine.CatalogCode` (เป็น snapshot ข้อความล้วน ไม่มี FK ไปยัง `CatalogItem` เพราะใบเสนอราคาเป็น
  version-first เก็บสแนปช็อตตอนสร้างใบตามหลักการ) กลับเป็น `CatalogItem.Id` จริงด้วยการค้นหาทีละรหัสผ่าน
  `getCatalogItems({ type: 'part', keyword: code })` แล้วจับคู่ `code` ตรงกัน — สินค้าที่โค้ดถูกลบ/เปลี่ยนชื่อไปแล้ว
  จะหาไม่เจอและถูกข้ามเงียบๆ (ผู้ใช้เพิ่มเองจากช่องค้นหาแทนได้) ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง —
  ตรวจแค่ `tsc -b` และ `vite build` ผ่านเท่านั้น
  · **[เพิ่ม 2026-09-09] Combobox ใหม่แทนคู่ Input+Select เดิม**: ผู้ใช้ขอให้ช่อง "ผู้เบิก" และ "เพิ่มสินค้า" ใน
  `StockWithdrawalModal` เป็น text-search autocomplete ในตัวแบบ select2 แทนช่องค้นหา + native `<select>` แยกกัน —
  ไม่มี dependency แบบ select2/react-select ในสแตกนี้ (component ui/ ทั้งหมดเขียนเองไม่พึ่ง Radix ฯลฯ) จึงเพิ่ม
  `web/src/components/ui/combobox.tsx` (`<Combobox>`) เป็น primitive ใหม่ที่ใช้ซ้ำได้ — พิมพ์กรอง, ลูกศรขึ้น/ลง +
  Enter เลือก, Escape ปิด, role="combobox"/listbox/option ตาม ARIA ผู้เรียกควบคุม query เอง (ยิง API ค้นหา) และ
  `onSelect` ตัดสินใจว่าจะเก็บค่าค้างไว้ (ผู้เบิก) หรือเพิ่มแถวแล้วล้างช่องเพื่อค้นต่อ (เพิ่มสินค้า) คลังยังคงเป็น
  native `<select>` เดิม (ตัวเลือกน้อย ไม่ต้องค้นหา) ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง — ตรวจแค่ `tsc -b`
  และ `vite build` ผ่านเท่านั้น
  · **[แก้ไข 2026-09-09] ปุ่ม "ยืนยันซ่อมเสร็จ" ไม่ปิดงานอีกต่อไป + เพิ่มขั้นตอน QC จริง**: เดิมปุ่มนี้ใน
  `RepairStage` ไล่ transition จากสถานะปัจจุบันยาวไปจน `completed` ทีเดียว (ข้าม QC และชำระเงินทั้งคู่) ผู้ใช้ทดสอบ
  แล้วพบว่าไม่ถูกต้องเช่นเดียวกับปุ่ม "อนุมัติซ่อม" ก่อนหน้า — ตอนนี้ปุ่มนี้ (label ใหม่ "ยืนยันซ่อมเสร็จ → ส่งตรวจ QC")
  ทำแค่ไล่ไปถึง `qc` เท่านั้น (`InProgress→Qc`, ถ้าเริ่มจาก `waitparts` จะไล่ `WaitParts→InProgress→Qc` ให้ก่อน)
  ด้วย reason คงที่ (manual-override เพราะ guard `AllTasksDoneWithPhotos` ไม่ใช่ isComputable) เพิ่มหน้าจริงสำหรับ
  ขั้น QC (`QcStage` แทน `NotYetAvailableStage` เดิม) แสดงข้อความอธิบายว่ายังไม่มีเช็คลิสต์ QC จริง (รูปก่อน-หลัง/
  ผลทดลองขับ) พร้อมปุ่ม "ผ่าน QC → ส่งไปชำระเงิน/ส่งมอบ" (`Qc→Ready`, reason คงที่เช่นกัน) — ทั้งสอง transition
  (`InProgress→Qc` และ `Qc→Ready`) เปิดให้ Office/Manager/Web อยู่แล้วใน `JobStateMachine.cs` (ไม่ต้องแก้ state
  machine) **ยังไม่ได้ทำเส้นทาง QC ตีกลับ** (`Qc→InProgress`) เพราะ transition นี้ยังจำกัดแค่
  `[Technician, Manager]` + `[Mobile]` เท่านั้นในโค้ด — ผู้ใช้ยังไม่ได้ขอ ต้องเปิด `[ASSUME]` เพิ่มแบบเดียวกับ
  transition อื่นก่อนถ้าต้องการทำจากเว็บ ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง — ตรวจแค่ `tsc -b` และ
  `vite build` ผ่านเท่านั้น
  · **[เพิ่ม 2026-09-09 — เช็คลิสต์ QC จริง แทนปุ่มยืนยันเอง]** `QcStage` ใน `JobCardModal.tsx` เดิมมีแค่ปุ่ม
  "ผ่าน QC" ที่ยืนยันเองแทนด้วย reason คงที่ (manual-override) ตอนนี้เปลี่ยนเป็นเช็คลิสต์จริง: รายการมาจากบรรทัดที่
  ลูกค้า **อนุมัติ** ในใบเสนอราคาปัจจุบันของ job นั้นโดยตรง (ตามคำขอผู้ใช้ — "อิงรายการซ่อมที่อนุมัติ" ไม่ใช่หัวข้อ
  ตายตัวแบบ 6 ข้อของ docs/01-workflow.md §3.6 เดิม ซึ่งต้องมี `RepairTask`/รูปก่อน-หลังที่ยังไม่มีในระบบ)
  · **[BIZ] ไม่มี process ตีกลับ/สถานะ "ไม่ผ่าน" เลย** (คำขอผู้ใช้ชัดเจน 2026-09-09: "QC ต้องกดผ่านเท่านั้น
  ไม่มีไม่ผ่าน เพราะถ้ายังไม่ผ่านก็ต้องเดินไปบอกให้แก้ให้ผ่านอยู่แล้ว") — `QcItemResult` มีแค่ `Pending`/`Pass`
  ไม่มี `Fail` เมื่อรายการไหนยังไม่เรียบร้อยให้ไปแจ้งช่างแก้นอกระบบแล้วย้อนมาติ๊กผ่านทีหลัง (ไม่ต้องมี note ก็ติ๊กผ่านได้)
  · เอนทิตีใหม่ `QcChecklist`/`QcChecklistItem` (`backend/.../Domain/Entities/QcChecklist.cs`) — 1 job = 1
  checklist (`HasIndex(JobId).IsUnique()`) แต่ละ item snapshot `CatalogCode`/`Name`/`Type` จากตอนสร้าง (กันปัญหา
  ถ้าใบเสนอราคาถูกแก้ทีหลัง เหมือนหลักการ version-first เดิม) header เก็บผลทดลองขับ (`TestDriveKm`/`TestDriveNote`/
  `TestDriveRecordedAt`) เป็นเงื่อนไขคงที่แยกจากรายการซ่อม ล็อกอ่านอย่างเดียวหลัง QC ผ่าน (`IsLocked` = มี
  `SubmittedAt`) migration `20260909033051_AddQcChecklist` — **ยังไม่ได้รัน `dotnet ef database update` จริง**
  (sandbox ไม่ได้ต่อ VPN เหมือนเดิม)
  · Service ใหม่ `QcChecklistService` (`Application/Qc/`) สร้าง draft checklist อัตโนมัติจากบรรทัดที่ `Approved`
  ของใบเสนอราคาล่าสุด (`quotations.GetLatestForJobAsync`) — ถ้าไม่มีบรรทัดอนุมัติเลยคืน `QC_NO_APPROVED_LINES`
  API ใหม่ 3 endpoint ใน `QcChecklistController`: `GET/PUT items/{itemId}/PUT test-drive` ทั้งหมดใต้
  `/api/v1/jobs/{jobId}/qc-checklist` (ไม่มี submit endpoint แยก — ล็อกอัตโนมัติตอน transition ไป `ready` สำเร็จ)
  · **[BIZ] `JobService.ComputeGuardAsync` คำนวณ `QcPassed` จริงแล้ว** — เพิ่ม `(Qc→Ready)` เข้า `isComputable`
  list (เดิมมีแค่ 3 transition ของ quotation) โดยอ่านจาก `QcChecklist` จริง: ผ่านทุกบรรทัด + มีผลทดลองขับบันทึกแล้ว
  → `Qc→Ready` **ไม่ต้องส่ง reason อีกต่อไป** (เหมือน `Approved→InProgress`) ถ้ายังไม่ครบ backend ปฏิเสธด้วย
  `JOB_GUARD_NOT_SATISFIED` (ข้อความ "QC ยังไม่ผ่านครบทุกหัวข้อ หรือยังไม่มีผลทดลองขับ" มีอยู่แล้วใน
  `JobStateMachine.DescribeGuard`) ฝั่งเว็บตัด `QC_PASS_REASON` คงที่ออกแล้ว เรียก `transitionJob` เปล่าๆ
  · **`Qc→InProgress` (ตีกลับ) ไม่ได้แตะเลย** เพราะไม่มี process ตีกลับตามคำขอ — transition นี้ยังจำกัดแค่
  `[Technician, Manager]` + `[Mobile]` เหมือนเดิมในโค้ด ไม่มี UI ฝั่งเว็บเรียกใช้
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน, `dotnet test` ผ่าน 114 (เพิ่ม `QcChecklistServiceTests.cs` 6 ผ่าน
  + เพิ่ม 3 test ใน `JobServiceTests.cs` ครอบคลุม Qc→Ready ปฏิเสธเมื่อ checklist ไม่ครบ/ทดลองขับหาย และผ่านได้จริง
  โดยไม่ส่ง reason) · Web `tsc -b` และ `vite build` ผ่าน · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง**
  (สร้างเช็คลิสต์จริงจาก job ที่มีใบเสนอราคาอนุมัติแล้ว, ติ๊กผ่านทีละรายการ, บันทึกผลทดลองขับ, กดผ่าน QC จริง)
  · **[เพิ่ม 2026-09-09 — หน้า "ชำระเงิน/ส่งมอบ" (PaymentStage) แทน placeholder เดิม]** MVP บนเว็บทั้งชำระเงินและ
  ส่งมอบรถรวมในสเต็ปเดียวตามที่ UI เดิมตั้งชื่อไว้แล้ว — เอกสาร design (`docs/01-workflow.md` §3.9/§11,
  `docs/04-project-plan.md` P5/P8) วางเป็น 2 เฟสแยกกัน (POS เต็มรูป = Web/P5, ส่งมอบรถ = **มือถือ** `[GAP·สูง]`
  ยังไม่ออกแบบ/P8) — ยืนยันกับผู้ใช้แล้วให้ทำ MVP รวบตอนนี้: **ชำระเงิน** บันทึกยอดเดียวต่อครั้ง (เลือกช่องทาง
  cash/transfer/card/qr เป็น label เท่านั้น ไม่ต่อ EDC/QR gateway จริง ไม่มี split/สถานะ `checking`) ยอดคงเหลือคิดตรง
  จาก `ApprovedTotals.GrandTotal` ของใบเสนอราคาล่าสุด **ข้ามขั้น reconciliation** (ไม่ผูก `StockWithdrawal` กับ
  `QuotationLine`) **ไม่มี `AccountsReceivable`** (ลูกหนี้ — OQ#5 ยังไม่ตอบ, `BalanceSettled` พอใจได้ทางเดียวคือ
  ยอดคงเหลือ ≤ 0) ออกได้แค่ใบเสร็จ `RC-{yy}-{seq:D4}` ใบเดียวต่อ job **ไม่มีใบกำกับภาษี/reprint/void** (OQ#6-7
  ยังไม่ตอบ) — มีแค่ "ลบ" รายการชำระที่บันทึกผิดได้ก่อนออกใบเสร็จเท่านั้น (ต้องระบุเหตุผล) · **ส่งมอบรถ**
  `[ASSUME]` ยืนยันชั่วคราวบนเว็บ (Cashier/Office/Manager แทนลูกค้า เหมือน pattern ปุ่ม "ยืนยันลูกค้าอนุมัติ" เดิม)
  เช็คลิสต์ของในรถ 5 รายการคงที่ (กุญแจ/คู่มือ/ยางอะไหล่+แม่แรง/ของใช้ส่วนตัว/อุปกรณ์เสริม — `[ASSUME]` รอยืนยัน
  รายการจริงจากฝ่ายปฏิบัติการ) + ลายเซ็นจับจริงบนเว็บผ่าน `<canvas>` ใหม่ (`components/ui/signature-pad.tsx` —
  ยังไม่เคยมีคอมโพเนนต์นี้บนเว็บมาก่อน อัปโหลดผ่าน endpoint แนบไฟล์เดิม `kind="handover-signature"`)
  · เอนทิตีใหม่ `Payment`/`Receipt`/`ReceiptNumberCounter` (`Domain/Entities/Payment.cs`) และ
  `HandoverRecord`/`HandoverChecklistItem` (`Domain/Entities/HandoverRecord.cs` — เติมของที่
  `docs/02-domain-model.md` วางเป็น placeholder ไว้เฉยๆ) `Payment` มี `RequestId`+`RequestHash` กันชำระซ้ำ
  (invariant #8) `Receipt.JobId` unique (ออกได้ใบเดียว) `HandoverRecord.JobId` unique + `IsLocked` จาก
  `SubmittedAt` (มิเรอร์ `QcChecklist` ทุกประการ) เลขใบเสร็จออกด้วย `ReceiptNumberGenerator`
  (`MERGE...HOLDLOCK` คีย์ (ชาร์ด, สาขา, ปี) — คัดจาก `JobNumberGenerator` มาแก้ ใช้ `.ToListAsync()` ไม่ใช่
  `.SingleAsync()` ตามบทเรียนเดิมเรื่อง raw SQL+OUTPUT) migration `20260909054704_AddPaymentAndHandover` —
  **ยังไม่ได้รัน `dotnet ef database update` จริง** (sandbox ไม่ได้ต่อ VPN เหมือนทุกครั้ง)
  · Service ใหม่ `PosService` (`Application/Pos/`) และ `HandoverService` (`Application/Handover/`) —
  ทั้งคู่จำกัดเฉพาะ role Cashier/Office/Manager (ตรงกับ role ที่อนุญาต transition `Ready→Completed` อยู่แล้ว)
  API ใหม่ผ่าน `PosController`/`HandoverController`: `GET/POST/DELETE .../payments`, `POST .../receipt`,
  `GET/PUT .../handover`, `PUT .../handover/items/{id}`, `PUT .../handover/submit` (ทั้งหมดใต้ `/api/v1/jobs/{jobId}`)
  · **[BIZ] `JobService.ComputeGuardAsync` คำนวณ `BalanceSettled`/`DocumentIssued`/`VehicleHandedOver` จริงแล้ว**
  — เพิ่ม `(Ready→Completed)` เข้า `isComputable` list (เดิมไม่มีเลย ตกไป manual-override ที่ใครมีสิทธิ์ก็พิมพ์
  เหตุผลอะไรก็ปิดงานได้ทันทีโดยไม่ตรวจอะไรจริงสักครั้ง — **นี่คือช่องโหว่ทางธุรกิจตัวใหญ่ที่สุดที่เพิ่งปิดไป**)
  ไม่ต้องแก้ `JobStateMachine.cs` เลย (guard bits/roles/ข้อความ error มีอยู่ครบแล้วตั้งแต่แรก) เว็บตัด reason
  คงที่ออก เรียก `transitionJob` เปล่าๆ เหมือน `Qc→Ready`
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน, `dotnet test` ผ่าน 129 (เพิ่ม `PosServiceTests.cs` 7 +
  `HandoverServiceTests.cs` 5 + เพิ่ม 3 test ใน `JobServiceTests.cs` ครอบคลุม Ready→Completed ปฏิเสธเมื่อยอด
  ไม่ถึง/ไม่มีใบเสร็จ-ส่งมอบ และผ่านได้จริงโดยไม่ส่ง reason) · Web `tsc -b` และ `vite build` ผ่าน (ผ่าน
  `node_modules/.bin/tsc`/`vite` ตรงๆ เพราะ `pnpm`/corepack ในเครื่องนี้ verify signature ไม่ผ่าน — ไม่เกี่ยวกับ
  โค้ด) · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (บันทึกชำระจนครบ, ออกใบเสร็จ+พิมพ์, ติ๊กเช็คลิสต์ส่งมอบ
  + เซ็นจริง, ปิดงานจริง) และยังไม่ได้รัน migration กับ ServiceDb จริง — ต้องทำทั้งสองก่อนใช้งานจริง
  · **[แก้ไข 2026-09-09 รอบสอง]** แก้บั๊ก "ยอดรวมทั้งสิ้น" ใน `JobCardModal.tsx` ตัวอักษรกลืนกับพื้นกล่อง — ใส่
  `className="money-summary__row money-summary__total"` ผิด (สองคลาสรวมกัน ทำให้ `.money-summary__row .money`
  (สีเน วี-900) ชนะ `.money-summary__total`'s `color:white` เพราะ selector มี specificity เท่ากันแต่ไม่ได้ set
  สีบน `.money` โดยตรง) ตัวเลขเลยเป็นสีเนวี-900 บนพื้นเนวี-900 ที่มองไม่เห็น — ของจริงต้องใช้ `.money-summary__total`
  เดี่ยวๆ เหมือนที่ `MoneySummary.tsx` ใช้อยู่แล้ว (แก้แล้ว ไม่พบปัญหาเดียวกันใน `PaymentReceiptDocument.tsx`)
  · **[เพิ่ม 2026-09-09 รอบสอง] ตัวเลือกคิด/ไม่คิด VAT ต่องาน** — checkbox ใหม่ในการ์ด "ชำระเงิน" (default ติ๊ก)
  ยืนยันกับผู้ใช้แล้วว่า **ไม่ติ๊ก = ลดยอดที่ต้องชำระจริง** (ตัด VAT 7% ออกจาก grandTotal ที่ใช้เทียบยอดชำระ ไม่ใช่
  แค่เปลี่ยนการแสดงผล/พิมพ์เอกสาร) — เพิ่ม `Job.VatIncluded` (bool, default true, migration
  `AddJobVatIncluded` เพิ่มคอลัมน์ `bit NOT NULL DEFAULT 1` ปลอดภัยกับแถวเดิม) เก็บที่ระดับ `Job` เพราะต้องมีค่าใช้ได้
  ตั้งแต่ก่อนมี `Payment`/`Receipt` แถวแรกด้วยซ้ำ (ตอนแรกพิจารณาเก็บที่ `Receipt` อย่างเดียวแต่จะไม่ตรงกับยอดที่ไป
  บังคับเก็บเงินไว้ก่อนหน้านั้นแล้ว) **ล็อกแก้ไม่ได้ทันทีที่เริ่มบันทึกชำระเงินหรือออกใบเสร็จแล้ว**
  (`POS_VAT_LOCKED`) `QuotationCalculator.CalculateApprovedTotals` เพิ่ม parameter `vatIncluded = true` (ค่าเดิม
  ยังคงพฤติกรรมเดิมทุกจุดที่เรียกอยู่แล้วในระบบ) `PosService.BuildSummaryAsync`/`JobService.ComputeGuardAsync`
  (branch `Ready→Completed`) เรียกด้วย `job.VatIncluded` ทั้งคู่เพื่อให้ `BalanceSettled` ตรงกับยอดที่หน้าเว็บ
  แสดง endpoint ใหม่ `PUT /api/v1/jobs/{jobId}/payment-vat` (`PosService.SetVatIncludedAsync`) คืน
  `PaymentSummaryDto` ที่คำนวณใหม่ทันที (`PaymentSummaryDto` เพิ่มฟิลด์ `vatIncluded`/`vatLocked`)
  · ทดสอบแล้ว: `dotnet build`/`dotnet test` ผ่าน 133 (เพิ่ม 3 test ใน `PosServiceTests.cs` + 1 test ใน
  `JobServiceTests.cs` ครอบคลุมคิด/ไม่คิด VAT ต่อยอดคงเหลือและล็อกหลังมีรายการชำระ) · Web `tsc -b`/`vite build`
  ผ่าน · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง และยังไม่ได้รัน migration ทั้งสองตัวกับ ServiceDb จริง**
  · **[BUG พบระหว่างทดสอบจริงในเบราว์เซอร์ 2026-09-09 — แก้แล้ว]** กดยืนยันส่งมอบไม่สำเร็จ: "ชนิดไฟล์แนบ
  \"handover-signature\" ไม่ถูกต้อง" (`ATTACHMENT_KIND_INVALID`) — ตอนเพิ่ม `AttachmentKind` ใหม่ฝั่งเว็บ
  (`web/src/api/types.ts`) ลืมเพิ่มค่าเดียวกันใน allowlist ฝั่ง backend `AttachmentService.AllowedKinds`
  (`Application/Attachments/AttachmentService.cs`) ที่ตรวจ kind ก่อนรับอัปโหลดจริง — เพิ่ม `"handover-signature"`
  เข้า array แล้ว **ต้อง restart API ให้โค้ดใหม่มีผลก่อนกดทดสอบซ้ำ** (`dotnet build`/`dotnet test` ผ่าน 133 เหมือนเดิม
  ไม่มี test ครอบคลุม allowlist นี้มาก่อน)
  · **[RISK พบระหว่างทดสอบจริง 2026-09-09 — ไม่ใช่บั๊กของงานนี้]** อัปโหลดลายเซ็นจริงยังพังอยู่หลังแก้ข้อบนแล้ว —
  `FluentFTP.Exceptions.FtpAuthenticationException: 501 Invalid number of parameters` ตอน `Connect()`/login เข้า
  FTP จริง (`10.10.3.11`) เพราะเครื่อง dev เครื่องนี้ยังไม่เคยตั้ง `dotnet user-secrets set "Ftp:Username"`/
  `"Ftp:Password"` มาก่อน (ตรวจแล้วมีแค่ `LegacyShards`/`Jwt:Key`/`ConnectionStrings:ServiceDb`) ทำให้ส่ง `USER`
  แบบว่างเปล่าไปที่ server จริง — **ไม่ใช่บั๊กโค้ดของฟีเจอร์ชำระเงิน/ส่งมอบ** เป็นเรื่อง environment setup ของเครื่อง
  dev แต่ละเครื่องที่ต้องตั้งเองตามที่ CLAUDE.md เตือนไว้แล้วตั้งแต่รอบ FTP storage เดิม (2026-09-09 รอบแรก)
  · **[เพิ่ม 2026-09-09 รอบสาม] ปุ่ม "พิมพ์ใบส่งมอบรถ"** — เพิ่มในการ์ด "เอกสาร & ส่งมอบรถ" ข้างปุ่มออก/พิมพ์ใบเสร็จ
  พิมพ์ได้ทุกสถานะ (ก่อน/หลังยืนยันส่งมอบ เหมือนใบรับรถ/ใบเบิกสินค้าเดิม) ใหม่: `HandoverDocument.tsx`
  (presentational, ใช้ CSS class เดิม `quotation-document`/`document-*`/`signature-slots`) +
  `HandoverDocumentModal.tsx` (มิเรอร์ `PaymentReceiptModal.tsx`: `printing-handover-document` body class,
  `.handover-document-print-area`) — ช่องลายเซ็นซ้าย ("ลายเซ็นลูกค้า (ผู้รับรถ)") แสดงลายเซ็นดิจิทัลที่จับไว้แล้ว
  จริง ถ้ายังไม่ยืนยันส่งมอบจะเป็นเส้นว่างให้เซ็นปากกาแทน (fallback กระดาษ) ช่องขวา ("ผู้ส่งมอบ") เป็นเส้นว่างเสมอ
  (ไม่มีลายเซ็นดิจิทัลของพนักงาน) ตารางกลางเอกสารแสดงเช็คลิสต์ของในรถ + สถานะ + หมายเหตุจากข้อมูลจริง
  · **[BUG พบระหว่างทดสอบจริงในเบราว์เซอร์ — แก้แล้ว]** รูปลายเซ็นไม่ขึ้น (broken image) ตอนแรกใส่
  `<img src={attachmentFileUrl(handover.signatureImagePath)}>` ตรงๆ — **`GET /api/v1/attachments/file` ต้องมี
  Bearer token เสมอ (`[Authorize]` ที่ `AttachmentsController`) แต่ `<img src>` เพียวๆ ไม่แนบ header ให้ browser**
  เลยโดน 401 เงียบๆ กลายเป็นไอคอนรูปพัง — แก้โดยย้ายไปใช้ pattern เดียวกับ `StaffAvatar.tsx`/`getStaffImage`
  (fetch ผ่าน `apiDownload` ที่แนบ Authorization header จริง → `URL.createObjectURL` → เซ็ต src เป็น blob URL →
  `revokeObjectURL` ตอน unmount) เพิ่ม `downloadAttachment()` ใน `web/src/api/attachments.ts`
  `HandoverDocumentModal.tsx` เป็นคนโหลด blob แล้วส่ง `signatureImageUrl` (blob URL หรือ null) ลงไปเป็น prop ให้
  `HandoverDocument.tsx` แทนที่จะให้ document component คำนวณ URL เอง
  · **[RISK พบระหว่างแก้บั๊กนี้ — ไม่ได้แก้ เพราะอยู่นอกขอบเขต]** `attachmentFileUrl()` (เดิม, ยัง export อยู่เพื่อ
  backward-compat) มีปัญหาเดียวกันนี้ทุกจุดที่ใช้อยู่ก่อนหน้านี้แล้ว: `IntakeStage`/`IntakeChecklistPanel.tsx`/
  `IntakeReceiptDocument.tsx` (`<img src={attachmentFileUrl(...)}>` ตรงๆ ทั้งหมด) — รูปถ่ายรับรถ/ตรวจเช็ค/ใบรับรถ
  พิมพ์น่าจะโหลดไม่ขึ้นเหมือนกันด้วยเหตุผลเดียวกัน ยังไม่ได้ยืนยันในเบราว์เซอร์จริงว่าพังจริงหรือไม่ (อาจมีเหตุผลอื่น
  ที่ทำให้ใช้ได้ เช่น cache/browser เคย login แบบอื่นมาก่อน) แต่ตามโค้ดแล้วน่าจะพังเหมือนกัน — ต้องตรวจสอบและแก้ทีหลัง
  ถ้ายืนยันว่าพังจริง โดยย้ายไปใช้ pattern `downloadAttachment`+blob URL แบบเดียวกันทั้งหมด
  · Web `tsc -b`/`vite build` ผ่าน · **ยังไม่ได้ทดสอบพิมพ์จริงพร้อมลายเซ็นในเบราว์เซอร์** (ยังติด FTP risk ข้างบน
  อยู่ ลายเซ็นจริงยังอัปโหลดไม่ได้ในเครื่องนี้ — ทดสอบได้แค่กรณียังไม่มีลายเซ็น/เส้นว่าง)
- ✅ รับรถ (Intake) — **บางส่วน**: checklist สภาพรถขณะรับ 4 หมวด 20 รายการ (ทำผ่าน `IntakeChecklistPanel`
  บนเว็บเท่านั้น — คนละอันกับ spec "ตรวจเช็ค 8 หมวด 31 รายการ" ของช่างใน docs/01-workflow.md §3.2)
  + ใบรับรถ A4 พร้อมพิมพ์ 2 จุดเซ็น (ลูกค้า/พนักงานรับรถ) — **ยังไม่ใช่** flow มือถือ 6 ขั้นเดิม
  (ไม่มีค้นหา/ยืนยันนัดหมาย/ถ่ายรูป 5 มุม/QR บนมือถือ)
- ✅ Mobile: login · เลือกสาขา/กะ · คิว (pull-to-refresh) · อนุมัติ · ลายเซ็น · โปรไฟล์/ปิดกะ
- ✅ Web: คิว · editor 3 พาเนล · เอกสาร A4 พร้อมพิมพ์
- ✅ ข้อมูลหลัก Web: ซัพพลายเออร์/คลัง/หมวดหมู่ · เพิ่ม/แก้ไข/เปิดปิดใช้งาน · ปุ่มสร้างรหัสอัตโนมัติสำหรับรายการใหม่
  · คลังใช้สาขาจากบัญชีอัตโนมัติ ไม่แสดงกล่องเลือก/แจ้งสาขาใน popup
  · แก้โฟกัสฟอร์ม/บันทึก และอัปเดต detail cache หลังแก้ไขเพื่อเปิดซ้ำได้ค่าล่าสุด
  · ไอคอนปิดใช้งานสอดคล้องกับหน้าสินค้า · คำอธิบาย endpoint แสดงใน Swagger
- ✅ Scope ลูกค้า/รถ/พนักงาน: อ่านและจัดการตาม shard + สาขาจาก JWT ไม่ขยายสิทธิ์ด้วย `IsCustomerDataPrivate`
  · รายการ/รายละเอียด/รูปภาพ/CSV ที่มีในแต่ละโมดูลใช้ scope เดียวกัน
  · พนักงานส่ง BranchId ว่าง/0 ใช้สาขาปัจจุบัน ส่งสาขาอื่นคืน 403 แม้ Admin และไม่ย้ายสาขาผ่าน API นี้
  · ลูกค้า/รถ legacy ไม่มี BranchId ใช้ผู้บันทึก/ความสัมพันธ์เจ้าของรถ/ประวัติงานของสาขา
  · การเห็นลูกค้าไม่ทำให้เห็นรถต่างสาขาทุกคันของลูกค้านั้น
  · **ลูกค้า/รถที่มีประวัติหลายสาขายังเป็นแถวร่วมกัน ไม่ใช่สำเนาแยกสาขา**; ดูข้อขัดแย้งนโยบาย legacy ด้านบน
- ✅ จัดซื้อ/FIFO รุ่นแรก: Web `/purchasing`, `/inventory` + API PR/PO/GRN/ยอดยกมา/เบิก FIFO
  · transaction + ActivityEvent · rowversion · RequestId กันรับ/เบิกซ้ำ · ล็อกแก้ยอดโดยตรงจากหน้าสินค้าหลังเริ่มใช้
  · migration `20260904122540_AddPurchasingFifo` ใช้กับ ServiceDb แล้วในการตรวจครั้งนี้
  · รายละเอียดสิทธิ์/สูตรยอด/ขอบเขตใน [docs/06-purchasing-fifo.md](docs/06-purchasing-fifo.md)
- ✅ ตาราง Web: เรียงหัวคอลัมน์แบบ Jobs และขยายข้อความตามหัวข้อกฎ UI ด้านบน
- ✅ ตารางพนักงาน: แสดงรูปวงกลม 56×56 ข้างชื่อ ผ่าน API รูปที่ตรวจ JWT/สาขาเดิม
  · ไม่มีรูปหรือโหลด/อ่านรูปไม่ได้ใช้ตัวอักษรย่อแทน ไม่แสดงรูปแตก
  · ขอรูปเฉพาะรายการที่มี PictureUrl; เปลี่ยนรูปแล้วโหลดใหม่ตาม LastUpdated และคืน blob URL เมื่อเลิกใช้
  · ปรับเฉพาะ Web ไม่เพิ่มคำสั่งเขียนฐาน legacy
- ✅ **[เพิ่ม 2026-09-09] เมนู Sidebar Web**: รวมกลุ่ม "Purchasing & Stock" (PR/PO/สต็อก FIFO) เข้ากลุ่ม "Workplace"
  เดียวกับ "จ๊อบ" แล้ว (`AppShell.tsx navGroups` เหลือ 3 กลุ่ม: Workplace/Reports/Master Data) · เมนู "จ๊อบ" มีตัวเลข
  จำนวนจ๊อบ **รถในอู่ (JobTypeId=9)** ที่ยังไม่ปิดงาน (ไม่นับ `Completed`/`Cancelled`) ต่อท้ายเหมือนตัวเลข PR/PO เดิม
  — endpoint ใหม่ `GET /api/v1/jobs/count-open?jobTypeId=` (`JobsController`/`JobService.CountOpenAsync`/
  `IJobRepository.CountOpenAsync` นับด้วย EF `CountAsync` กรอง shard/สาขาจาก JWT claim เหมือน `SearchAsync` เดิม
  ไม่ผูกสิทธิ์ purchasing เพราะเป็นเมนูที่ทุก role ใช้) ฝั่งเว็บเรียกผ่าน `countOpenJobs()` ใหม่ใน `api/jobs.ts`
  · หน้ารายการจ๊อบ (`JobsPage.tsx`) ตอนนี้ **ค่าเริ่มต้นกรองเฉพาะรถในอู่** (`typeFilter` เริ่มที่ `9` แทน `0`)
  ก่อนผู้ใช้กดค้นหา/เปลี่ยนตัวกรองเอง — ยังเลือก "ทุกประเภท" จาก dropdown เดิมได้ตามปกติ
  · **[แก้ 2026-09-09 รอบสอง — ผู้ใช้ทักหลังทดสอบ] ตัวกรองเดิมกรองแค่ `JobTypeId` ไม่กรองสถานะ** ทำให้จ๊อบที่ปิดงาน
  (`completed`/`cancelled`) แล้วแต่ยังเป็น `JobTypeId=9` เดิมค้างอยู่ในรายการ "รถในอู่" ค่าเริ่มต้น ไม่ตรงกับตัวเลขในเมนูที่
  หัก terminal status ออกแล้ว — แก้ที่ต้นเหตุ: `JobService.TransitionAsync` เปลี่ยน `JobTypeId`/`JobTypeName` เป็น
  `11`/"ปิดจ๊อบ" ให้เองทันทีที่ `JobStateMachine.IsTerminal(to)` (ดูกฎข้อ 14 ด้านบน) แทนที่จะเพิ่มตัวกรองสถานะซ้อน —
  ตัวกรอง "ประเภท" ฝั่งเว็บเพิ่มตัวเลือก "ปิดจ๊อบ" (`JOB_TYPE_FILTER_OPTIONS`, ใช้เฉพาะ dropdown กรอง ไม่ใช่ตอนเปิดจ๊อบ)
  · ทดสอบแล้ว: `dotnet build`/`dotnet test` ผ่าน 136 (เพิ่ม `CountOpenAsync_counts_only_open_jobs_of_the_current_branch_and_type`,
  `TransitionAsync_reclassifies_job_type_as_closed_once_it_reaches_a_terminal_status`,
  `TransitionAsync_reclassifies_job_type_as_closed_when_cancelled` ใน `JobServiceTests.cs`) · Web `tsc -b`/`vite build` ผ่าน
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (ดูตัวเลขในเมนูตรงกับจำนวนจริง, ปิดงานแล้วจ๊อบหายจากรายการรถในอู่ทันที,
  คลิกเมนูที่รวมกลุ่มแล้วไปหน้าเดิมถูกต้อง)
- ✅ **[เพิ่ม 2026-09-21] จับเวลาการทำงานของช่าง — เฟสเก็บข้อมูล** (รายงานประเมินยังไม่ได้ทำ ดู "ยังไม่ได้ทำ")
  รายละเอียดการออกแบบเต็มที่ [docs/09-technician-time-tracking.md](docs/09-technician-time-tracking.md)
  · **[BIZ] ไม่ผูกเงิน** — ตัดสินใจ 2026-09-21 ว่าใช้เป็นรายงานให้ผู้บริหารประเมินช่าง ไม่ใช่คอมมิชชัน
  ทำให้ตัดตาราง `svc_WorkAdjustment` และ workflow อนุมัติการแก้เวลาออกได้ (แก้ในที่ + เก็บ audit แทน)
  · เอนทิตีใหม่ `WorkInterval` (`Domain/Entities/`) + enum `WorkIntervalKind`/`WorkEndReason` ·
  migration `20260921134231_AddWorkInterval` — **รันกับ ServiceDb จริงแล้ว** (ยืนยันด้วย
  `dotnet ef migrations list` ว่าไม่เหลือ `(Pending)`)

  **กฎใหม่ที่ต้องรู้:**
  19.5 **หนึ่งช่วงเวลาที่เปิดอยู่ต่อ*ช่าง* ไม่ใช่ต่อ*จ๊อบ*** — บังคับที่ฐานข้อมูลด้วย filtered unique index
    `UX_WorkInterval_OneOpenPerTech ... WHERE [EndedAt] IS NULL` ไม่ใช่แค่ที่ service (ต้นแบบ
    `PurchasingConfiguration.cs:80`) ผลคือ**ช่างหลายคนรุมคันเดียวพร้อมกันได้โดยธรรมชาติ** ตามที่ผู้ใช้
    ยืนยัน 2026-09-21 · ถ้าเปิดซ้อนกันได้เมื่อไหร่ ชั่วโมงจะถูกนับซ้ำและทุกเมตริกพังพร้อมกัน
    · **พิสูจน์กับฐานจริงแล้ว** ใน `WorkIntervalSqlTests` (ช่าง 3 คนคันเดียวผ่าน · ช่างคนเดิมเปิดคาบที่สอง
    ถูกปฏิเสธ · ปิด+เปิดใน SaveChanges เดียวผ่าน · `RequestId` ซ้ำถูกปฏิเสธ)
  19.6 **เปิด/ปิด/พักคาบเป็นของ `Technician` เท่านั้น** (`Lead` ไม่ได้ — ยืนยันกับผู้ใช้ว่าหัวหน้าช่างคุม/ตรวจ
    ไม่ลงมือ) · แก้เวลาย้อนหลัง ≤7 วัน + ดูคาบทั้งสาขา = `Lead`/`Manager` · ยกเลิกคาบ = `Manager`
    · **ผลพลอยได้: ไม่ต้องแตะ `JobStateMachine` เลยทั้งงาน** เพราะ `Approved→InProgress` มี
    `Technician` + `EventSource.Mobile` อยู่แล้ว · **ลบคาบจริงไม่ได้** ใช้ `VoidedAt` เท่านั้น ไม่งั้นแยกไม่ออก
    ว่าคาบที่หายคือ "ไม่เคยมี" หรือ "ถูกลบ" แล้วคำนวณความครบของข้อมูลไม่ได้
  19.7 **เปลี่ยนสถานะจ๊อบแล้วคาบต้องปิดตามในคำขอเดียวกัน** — `IWorkIntervalHook` (ห้าม `SaveChanges`
    เอง) ถูกเรียกจาก `JobService.TransitionAsync` และ `AuthService.CloseShiftAsync` · repo แชร์
    `ServiceDbContext` เดียวกันต่อ request จึง commit เป็น transaction เดียวกันจริง
    · **`Qc→Ready` อยู่ในรายการด้วยแม้ docs/09 §6 ไม่ได้ระบุ** ไม่งั้นคาบที่เปิดตอนกลับมาแก้งานค้างเปิด
    ตลอดไปแล้วกลายเป็น `autoCapped` ทุกครั้ง = เมตริกคุณภาพหลักถูกตัดทิ้งอย่างเป็นระบบ

  · **`Qc→InProgress` (ตีกลับ QC) ถูกลบออกจากแอปแล้ว** — เอกสารเคยบันทึกว่า "ไม่มี UI ที่ไหนเรียกเลย"
  ซึ่ง**ผิด**: `mobile/lib/core/job_transitions.dart` ประกาศไว้จริง และเพราะ `primaryFor()` คืนตัวแรก
  ตามลำดับประกาศ ช่างที่เปิดจ๊อบสถานะ `qc` เห็นปุ่ม "ตีกลับให้ช่างแก้ไข" เป็น**ปุ่มหลัก**มาตลอด ขัดกับ
  นโยบาย "QC กดผ่านอย่างเดียว" · ตรวจ `ActivityEvent` จริงแล้ว **0 จาก 34 การเปลี่ยนสถานะ** — ไม่เคยมีใครกด
  จึงลบออก (`JobStateMachine.cs` ฝั่ง backend ยังคง transition ไว้ แต่ไม่มีใครเรียกแล้วจริงๆ) มีเทสต์กันคนใส่กลับ
  · **วัดงานแก้ซ้ำจากเวลาแทน** — คาบที่เปิดตอนจ๊อบอยู่ `qc` ติดธง `IsRework` อัตโนมัติ (ติดตอนเขียน
  ไม่ใช่คำนวณตอนอ่าน เพราะ `ActivityEvent.PayloadJson` ไม่มี index และต้องหา event *แรก* ที่เข้า Qc)
  ⇒ ต้อง**อนุญาต**ให้กดเริ่มงานตอนจ๊อบอยู่ `qc` ได้ โดยไม่ดันสถานะกลับ
  · **`Staff.Id` เข้า JWT แล้ว** (claim `staff` ที่ `JwtTokenIssuer.BaseClaims`) + `ICurrentUser.StaffId`
  เป็น **default interface member** (`=> null`) จึงไม่ต้องแก้ stub ในไฟล์เทสต์ 14 ไฟล์ · `WorkTimeService`
  มี fallback ไป `ILegacyUserReader` จุดเดียวสำหรับ token ที่ออกก่อนมี claim (หน้าต่าง ≤12 ชม.)
  · **ห้ามรับ `staffId` จาก request body เด็ดขาด** ไม่งั้นช่างลงเวลาแทนคนอื่นได้
  · **`autoCapped` ใช้ lazy cap 3 จุดเขียน ไม่มี background worker** (ระบบไม่มี `IHostedService` เลย):
  ตอน `StartAsync` · ตอน `GET /work/current` (เป็น GET ที่เขียนได้โดยตั้งใจ — idempotent, แถวเดียว,
  ของผู้เรียกเอง) · ตอนปิดกะ · **ไม่ cap ตอนอ่านรายงาน** · เพดานมาจาก `Shift.EndTime` โดยหา "การเกิดขึ้น
  ครั้งแรกหลังเวลาเริ่ม" เพราะ**กะข้ามเที่ยงคืนมีจริง** (กะดึก 22:00→08:00) · ไม่มีกะ (เว็บ) ใช้
  `MaxOpenIntervalHours` `[ASSUME] 12`
  · **Atomicity ไม่ต้องมี `AtomicAsync` ของตัวเอง** — ปิดคาบเดิม (UPDATE) + เปิดคาบใหม่ (INSERT) ใน
  `SaveChanges` ครั้งเดียวคือ implicit transaction ของ EF อยู่แล้ว ถ้า INSERT ชน index ทั้งก้อน rollback
  ⇒ "start ล้ม คาบเดิมยังเปิดอยู่" เป็นจริงเอง · **ห้ามเรียก `BeginTransactionAsync` ตรงๆ** เพราะเปิด
  `EnableRetryOnFailure(3)` ไว้ · การชนแยกไม่ได้ที่ระดับ exception (ชนได้ทั้ง `RequestId` และ
  one-open-per-tech) จึง **re-read ด้วย `RequestId` เพื่อตัดสิน — ห้าม parse ชื่อ index จากข้อความ SQL**

  **มือถือ:** แถบจับเวลาติดล่างจอทุกหน้า (`WorkFocusHost` ที่ `MaterialApp.router builder:`)
  · **ต้องอยู่ที่ builder ไม่ใช่ `AppShell`** เพราะหน้าที่ช่างใช้จริงตอนทำงาน (`/jobs/:jobId` + chat/intake/
  qc/payment/handover) push ด้วย `rootNavigatorKey` อยู่ **นอก** `StatefulShellRoute` ทั้งหมด
  · ใช้ `Column` ไม่ใช่ `Stack` — `Stack` จะลอยทับ scrim ของ dialog ตัวเอง และ `Column` ทำให้แถบกินพื้นที่จริง
  ไม่ต้องไล่เติม bottom padding ทุกหน้า · **`Timer` ต้องอยู่ใน State ของ leaf widget ห้ามทำเป็น provider**
  ไม่งั้น subtree ทั้งแอป rebuild ทุกวินาที ทุกหน้า ทุกลิสต์ (`ValueListenableBuilder` ห่อเฉพาะ `Text` ตัวเลข)
  · นาฬิกาเดินด้วย `elapsedAtSync + Stopwatch` โมโนโทนิก ไม่ใช่ `DateTime.now()` ซึ่งกระโดดได้เมื่อผู้ใช้
  แก้เวลาเครื่อง — ชดเชยครึ่งหนึ่งของ round-trip · resync ทุกครั้งที่กลับ foreground + poll safety net 60 วิ
  (คาบถูกปิดโดยที่แอปไม่ได้สั่งได้จริง 3 ทาง: ส่งตรวจ QC · หัวหน้าปิดกะ · Lead แก้/ยกเลิกคาบ)
  · **ปุ่ม "เริ่มงานซ่อม" เดิมของสถานะ `approved` ถูกแทนที่ ไม่ใช่วางคู่กัน** (`WorkActions.hidesTransition`)
  ไม่งั้นช่างกดตัวเก่าแล้วจ๊อบเข้า `inprogress` โดยไม่มีคาบเปิด — เวลาหายทั้งก้อนโดยไม่มีอะไรฟ้อง
  · `requestId` สร้างครั้งเดียวต่อคำขอ และ**ไม่ล้างเมื่อเจอ error** เพราะ `NETWORK_ERROR` แปลว่าไม่รู้ว่า
  server ทำไปแล้วหรือยัง การลองใหม่ต้องใช้ id เดิม · `currentWorkProvider` `watch` `sessionProvider`
  จึงรีเซ็ตเองเมื่อปิดกะ/ออกจากระบบ/สลับบัญชี

  **เว็บ:** `/reports/work-intervals` — **ไม่ใช่รายงานประเมิน** เป็นข้อมูลดิบให้ `Lead`/`Manager` ตรวจและแก้
  คาบที่ผิดระหว่างช่วงเก็บข้อมูล (ถ้าไม่มี คาบที่ลืมกดหยุดจะสะสมโดยไม่มีใครแก้ได้ แล้วข้อมูลทั้งเดือนอาจใช้ไม่ได้)
  · `formatWorkDuration` ใน `web/src/lib/format.ts` (เดิมไม่มี formatter ชั่วโมงเลย)

  · **ทดสอบแล้ว**: `dotnet build` ผ่าน 0 error · `dotnet test` ผ่าน **258 / skipped 5** (เดิม 231/4 —
  เพิ่ม `WorkTimeServiceTests.cs` 22, hook tests ใน `JobServiceTests.cs` 4 และ `AuthServiceTests.cs` 1,
  `WorkIntervalSqlTests.cs` 1 ที่ skip เมื่อไม่มี env var) · **SQL integration test รันกับฐานจริงแล้วผ่าน**
  (802 ms — ตั้ง `GARAGEPRO_WORKINTERVAL_SQL_CONNECTION` ใช้ shard ปลอม `wt-{guid}` แล้วลบทิ้งใน finally)
  · Mobile `dart analyze lib/ test/` สะอาด 0 issue · `flutter test` ผ่าน **54** (เดิม 34 — เพิ่ม
  `work_actions_test.dart` 15 + `work_clock_test.dart` 4) · Web `tsc -b --force`/`vite build` ผ่าน ·
  `node --test tests/*.test.mjs` ผ่าน 10
  · **[ทดสอบกับข้อมูลจริงแล้ว 2026-09-21]** API จริง + ฐาน ServiceDb จริง + บัญชีช่างจริงของสาขา 227
  ผ่านครบ 12 สถานการณ์: เริ่มงานบนจ๊อบ `approved` แล้ว**จ๊อบเด้งเป็น "กำลังซ่อม" เองพร้อมเปิดคาบ** ·
  `RequestId` ซ้ำคืนคาบเดิม · พัก→ทำต่อแยก `work`/`pause` ถูก · สลับคันปิดคันเดิมด้วย `switchedJob` ·
  **ช่าง 3 คนเปิดคาบคันเดียวกันพร้อมกันได้** · ผู้จัดการจับเวลาไม่ได้ (`WORK_FORBIDDEN`) ·
  เปลี่ยนสถานะจ๊อบปิดคาบของช่างทุกคนด้วย `sentToQc` · กดเริ่มงานตอนจ๊อบอยู่ `qc` ติดธง `isRework`
  และ**ไม่ดันสถานะกลับ** · **`Qc→Ready` ปิดคาบ rework ได้จริง** (ยืนยันว่าถ้าไม่เพิ่ม hook นี้
  ชั่วโมงแก้งานจะเป็นศูนย์ตลอดกาล) · ช่างดูคาบทั้งสาขา/แก้เวลาไม่ได้ · ผู้จัดการแก้เวลา + ยกเลิกคาบได้ ·
  ตัวกรอง "เฉพาะที่ต้องตรวจ" คัดถูก
  · **[ทดสอบบนแอปจริงแล้ว]** ผู้ใช้กด "เริ่มงาน (จับเวลา)" บน simulator แล้วจ๊อบเปลี่ยน
  `อนุมัติแล้ว → กำลังซ่อม` พร้อมเปิดคาบจริง (`ActivityEvent` `Source=Mobile` ยืนยัน)
  · **[ทดสอบผ่าน UI จริงบน simulator แล้ว 2026-09-21]** เดินครบทั้ง flow ด้วยบัญชีช่างจริง:
  จ๊อบ `approved` แสดงปุ่ม "เริ่มงาน (จับเวลา)" ปุ่มเดียว (ปุ่ม "เริ่มงานซ่อม" เดิมถูกซ่อนตาม
  `WorkActions.hidesTransition`) · กดแล้วจ๊อบเด้งเป็น "กำลังซ่อม" + แถบขึ้นพร้อมนาฬิกาเดิน ·
  แถบอยู่ครบทุกหน้ารวมหน้าที่ push นอก shell · **จ๊อบที่กำลังจับเวลาถูกปักบนสุดของคิวจริง** ·
  **dialog สลับคันแสดงเวลาแช่แข็ง 00:00:51 ขณะที่แถบข้างหลังเดินต่อเป็น 00:00:56** ·
  snackbar บอก "หยุดเวลา จจ-1112 ที่ 00:01:17 แล้ว" · พักงานเปลี่ยนเป็น "พักอยู่" + ไอคอน pause +
  สีเหลืองอำพัน (สี+ไอคอน+ข้อความครบ) · **คีย์บอร์ดขึ้นแล้วแถบหายไปจริง กดปิดแล้วกลับมาโดยเวลาไม่รีเซ็ต** ·
  แถบรอด hot restart เพราะกู้สถานะจาก `/work/current`
  · **[แก้ 2 บั๊กที่เจอระหว่างทดสอบ UI]** (1) `StickyActionBar` มีที่บอกเหตุผลให้ปุ่มหลักตัวเดียว —
  ปุ่มรองที่กดไม่ได้จึงกลายเป็นปุ่มตายที่ไม่บอกอะไร แก้ให้แสดงเฉพาะตอนกดได้จริง (2) จ๊อบ `waitparts`
  ทำให้ปุ่มน้ำเงินใหญ่เป็น transition ของเว็บที่ช่างกดไม่ได้ ส่วน "พักงาน" ที่กดได้จริงกลายเป็นปุ่มรองเล็ก —
  แก้ให้ **promote การกระทำที่กดได้ขึ้นเป็นปุ่มหลัก** แล้วย้ายเหตุผลของ transition ไปไว้ใน hint
  · เพิ่ม `test/work_focus_host_test.dart` 5 เทสต์ครอบเงื่อนไขการแสดงแถบทั้งหมด (คีย์บอร์ด/บทบาท/
  เซสชัน/มีคาบ) เพราะ `MediaQuery.viewInsets` จำลองด้วย unit test ไม่ได้
  · **ยังไม่ได้ทดสอบด้วยตา**: auto-cap ข้ามคืน · หน้าเว็บ `/reports/work-intervals` แสดงผล
  (ตรวจได้แค่ route + API คืนข้อมูลถูก — ล็อกอินหน้าเว็บเองไม่ได้)
  · **[RISK ที่ยอมรับไว้]** แถบจับเวลาอยู่เหนือ Navigator จึง**ไม่ถูก scrim ของ dialog คลุม** — กดปุ่ม
  "ไปที่งาน" ขณะ dialog ยืนยันเปิดอยู่ได้ ไม่ทำข้อมูลเสีย (การยิง API ยังต้องกดยืนยันใน dialog)
  เป็นผลพ่วงของการวางแถบไว้ที่ `MaterialApp.builder` ซึ่งจำเป็นเพื่อให้เห็นในหน้าที่ push นอก shell

- ✅ **[แก้ไข 2026-09-21] จ๊อบค้างถาวรเมื่อสถานะ "ใบเสนอราคา" กับ "จ๊อบ" เดินคนละจังหวะ** — พบระหว่าง
  ทดสอบจับเวลากับข้อมูลจริง (จ๊อบ `JB2609180227003`): ใบเสนอราคา `approved` + ลูกค้าเซ็นแล้ว
  แต่จ๊อบค้างที่ `waitquote` และ **ไม่มีทางไปต่อจากทั้งแอปและเว็บ** ต้องยิง API ตรงๆ เท่านั้น
  · **ต้นเหตุ**: `sendMutation` (`QuotationEditorModal.tsx`) ยิง `transitionJob(waitapprove)` ทีเดียว
  ซึ่ง**ล้มเงียบ**เมื่อจ๊อบยังอยู่ `waitinspect` (ไม่มีเส้นทางนั้น) ใบกลายเป็น `sent` แต่จ๊อบไม่ขยับ ·
  จากนั้นลูกค้าเซ็นจากมือถือได้ (คิวอนุมัติดูสถานะ "ใบ" ไม่ใช่สถานะ "จ๊อบ") แต่แอปดันจ๊อบต่อไม่ได้เพราะ
  `waitquote → approved` ไม่มีเส้นทาง · **และปุ่มกู้สถานการณ์บนเว็บก็หายไปพร้อมกัน** เพราะมันหาใบที่
  `sent`/`partial` แต่ใบกลายเป็น `approved` ไปแล้ว
  · **แก้ 3 จุด**: (1) `web/src/features/jobs/advanceJobStatus.ts` ใหม่ — helper กลางที่ไต่สถานะทีละขั้น
  จนถึง `waitapprove` ใช้ร่วมกันทั้ง `sendMutation` และปุ่ม "ยืนยันลูกค้าอนุมัติ" (เดิมมี chain อยู่ในปุ่มเดียว)
  (2) `JobCardModal.tsx` — ปุ่มกู้สถานการณ์รับใบสถานะ `approved` ด้วย และ **ข้ามการเซ็นถ้าเซ็นไปแล้ว**
  (`full.approval !== null`) ไม่งั้นเซ็นซ้ำจะถูกปฏิเสธแล้วกู้จ๊อบไม่ได้เลย
  (3) `mobile/lib/features/approval/approval_page.dart` — เมื่อ transition หลังเซ็นล้มด้วย
  `JOB_TRANSITION_NOT_ALLOWED` ให้บอกทางแก้ที่ทำได้จริง (ให้ธุรการกดยืนยันจากเว็บ) แทนข้อความ error ดิบ
  · **ไม่แตะ backend** — `JobStateMachine` ถูกแล้ว ปัญหาอยู่ที่ client ไม่ไต่ขั้น
  · **[ยังไม่ได้แก้]** ต้นตอที่ลึกกว่านั้นคือ **คิวอนุมัติบนมือถือกรองด้วยสถานะใบเสนอราคา ไม่ใช่สถานะจ๊อบ**
  จึงยังเปิดให้เซ็นใบของจ๊อบที่ยังไม่ถึง `waitapprove` ได้ · ตอนนี้กู้คืนได้จากเว็บแล้วจึงไม่ใช่ทางตัน
  แต่ถ้าจะปิดให้สนิทต้องให้ `QuotationsApi.queue` กรองด้วยสถานะจ๊อบด้วย — ต้องยืนยันขอบเขตก่อน

- ✅ **[เพิ่ม 2026-09-21] ย้ายทางเข้าแชทขึ้น AppBar ของการ์ดจ๊อบ พร้อมจุดแดงข้อความใหม่** (มือถือ)
  · **ปัญหา**: การ์ด "แชทของงานนี้" อยู่ล่างสุดของหน้า ต้องปัด 2 ครั้งเต็มผ่านใบเสนอราคาและรูปแนบกว่าจะถึง
  ขณะที่ AppBar ว่างเปล่ามีแค่ชื่อหน้า
  · **แก้**: ย้ายเป็น `IconButton` มุมขวาบน (`_ChatAction` ใน `job_detail_page.dart`) และ**ลบการ์ดเดิมทิ้ง**
  ไม่เก็บไว้สองที่ · **ไม่ใช้ FAB** เพราะล่างจอมี `StickyActionBar` ซ้อนกับแถบจับเวลาอยู่แล้ว FAB จะลอยทับ
  ปุ่มหลักและกลายเป็นปุ่ม "เด่น" สองตัวแย่งกัน · แลกกับการที่มุมขวาบนเอื้อมยากกว่าเมื่อถือมือเดียว
  ซึ่งรับได้เพราะแชทเป็นงานที่เปิดเป็นครั้งคราว ไม่ใช่กดรัวๆ
  · **จุดแดง**: `jobChatUnreadProvider` ใหม่ ดึงข้อความล่าสุด `take: 1` แล้วเทียบกับ `ChatSeenStore.lastSeen`
  (store มีอยู่แล้วตั้งแต่ทำแชท แต่ไม่เคยมีใครอ่านฝั่ง "ยังไม่อ่าน" เลย — หน้าแชทเขียน `markSeen` อย่างเดียว)
  · **[BIZ] บอกแค่ "มี/ไม่มี" ไม่ใช่จำนวน** โดยตั้งใจ — นับจำนวนต้องดึงทั้งช่วงที่ยังไม่อ่านซึ่งแพงกว่ามาก
  และไม่ทำให้ช่างตัดสินใจต่างไป · **ไม่ sync ข้ามเครื่อง** (`lastSeen` อยู่ใน SharedPreferences)
  เครื่องที่ร้านใช้ร่วมกันจึงเห็นจุดแดงตรงกันทั้งกะ ไม่ได้แยกรายคน — เหมือน `JobChatWidget` ของเว็บ
  · **อัปเดตตอนเปิดหน้าจ๊อบ + ตอนกลับจาก background + ตอนกลับจากหน้าแชท** เท่านั้น (คำขอผู้ใช้)
  ไม่ poll ต่อเนื่องเพื่อไม่ให้เปลืองเน็ต/แบตของเครื่องที่เปิดหน้านี้ค้างทั้งวัน — แลกกับการที่ถ้าเปิดหน้าค้างไว้
  จะไม่รู้ว่ามีข้อความใหม่จนกว่าจะออกแล้วเข้ามาใหม่
  · ระหว่างโหลดหรือโหลดไม่ผ่านถือว่า "ไม่มีข้อความใหม่" — จุดแดงหลอกแย่กว่าจุดแดงที่มาช้า
  · **ทดสอบบน simulator จริงแล้ว**: ส่งข้อความจากช่างอีกคน → จุดแดงขึ้น → เปิดอ่าน → กลับมาจุดแดงหาย
  · **[เพิ่มต่อในคำขอเดียวกัน] ป้าย "มีข้อความใหม่ในแชท" บนการ์ดจ๊อบในคิวงานด้วย** — ช่างรู้ว่าคันไหนมีข้อความ
  ตั้งแต่ยังไม่เปิดเข้าไป ไม่ต้องไล่เปิดทีละคัน
  · **ต้องมี endpoint ใหม่ก่อน** เพราะของเดิมถามได้ทีละจ๊อบ (`GET /jobs/{jobId}/chat/messages?take=1`) ซึ่งกับคิว
  25 รายการจะกลายเป็น 25 คำขอทุกครั้งที่เปิดหน้า — เพิ่ม `GET /api/v1/jobs/chat/latest?jobIds=...`
  (`JobChatController.GetLatestPerJob` · `JobChatService.GetLatestPerJobAsync` ·
  `IJobChatRepository.GetLatestPerJobAsync` → record `JobChatLatest(JobId, MessageId, CreatedAt)`)
  คืนเฉพาะ **id ของข้อความล่าสุดต่อจ๊อบ** ไม่ใช่ตัวข้อความ (client ต้องการแค่เอาไปเทียบกับ `lastSeen`)
  · **route เป็น absolute path** (`[HttpGet("/api/v1/jobs/chat/latest")]`) เพราะ controller นี้ผูกกับ
  `api/v1/jobs/{jobId}/chat` — ถ้าใช้ relative route `chat/latest` จะถูกตีความว่า `jobId = "chat"` แล้ว 400 เสมอ
  · SQL เลือกข้อความล่าสุดด้วย **"ไม่มีข้อความไหนใหม่กว่า"** (`!db.JobChatMessages.Any(o => ... newer)`)
  ไม่ใช่ `GroupBy`+`Max` — tie-break `(CreatedAt, Id)` ชุดเดียวกับ `JobRepository.SearchAsync` เพื่อให้ลำดับตรงกับ
  ที่หน้าแชทใช้จริง · สโคป shard/สาขาจาก JWT เหมือนทุก endpoint (ยิง jobId ของสาขาอื่นมาได้แค่ไม่มีผลลัพธ์)
  · จำกัด **100 jobId ต่อคำขอ** (`CHAT_VALIDATION`) — คิวโหลดทีละ 25 จึงเหลือที่เผื่อพอสมควร
  · ฝั่งแอป `jobChatUnreadSetProvider` ยิงคำขอเดียวต่อหน้า · **key ของ family เป็นสตริง jobId ที่เรียงแล้ว
  คั่น comma ไม่ใช่ `List`** — Dart เทียบ `List` ด้วย identity ถ้าใช้ List เป็น key จะได้ provider ตัวใหม่ทุกครั้ง
  ที่หน้า rebuild แล้วยิง API ไม่หยุด (ดัก bug นี้ไว้ใน doc comment ของ provider แล้ว) · การเรียงทำให้ cache ชนกัน
  ได้แม้ลำดับการ์ดสลับ (เช่นตอนปักจ๊อบที่กำลังจับเวลาไว้บนสุด)
  · การ์ดแสดงเป็น **แถวไอคอน + ข้อความ** (`Icons.forum` + "มีข้อความใหม่ในแชท") ไม่ใช่จุดสีเปล่าๆ
  ตามกฎ UI "ห้ามสื่อด้วยสีอย่างเดียว" — มิเรอร์แถว "เกินกำหนด" ที่การ์ดมีอยู่แล้ว
  · **invalidate ตอนกลับจากหน้าจ๊อบ** เพื่อให้ป้ายหายทันทีหลังอ่าน (ไม่ poll ต่อเนื่องเหมือนกัน)
  · ทดสอบจริงบน simulator แล้ว: ส่งข้อความเข้า 2 จ๊อบ → ป้ายขึ้นทั้ง 2 การ์ด (1ขล-7321 และ จจ-1112)
  · `dart analyze` สะอาด · `flutter test` ผ่าน 59 · `dotnet test` ผ่าน 261 / skipped 5
  (เพิ่ม 3 ใน `JobChatServiceTests.cs`: คืนเฉพาะข้อความล่าสุดต่อจ๊อบ, ไม่คืนจ๊อบของสาขาอื่น, เกิน 100 id ถูกปฏิเสธ)

- ✅ Test: บันทึกเดิม 55 ผ่าน (auth · attachment storage · state machine · calculator · role mapper · job service)
  — เป็นผลก่อนงานล่าสุด ไม่ใช่จำนวนรวมปัจจุบัน ดูชุดทดสอบที่รันจริงด้านล่าง
- ✅ **[แก้ไข 2026-09-10] 3 บั๊กที่ผู้ใช้แจ้ง — รูปแนบใน checklist ไม่ขึ้น / อัปโหลดรูปไม่ resize / สร้างใบเสนอราคาเพิ่มไม่ได้:**
  · **รูปแนบไม่แสดง**: ยืนยัน `[RISK]` เดิมที่บันทึกไว้ตอนทำ FTP storage — `attachmentFileUrl()` ต่อ URL ตรงๆ ใช้กับ
  `<img src>` ทั้งที่ `GET /api/v1/attachments/file` ต้อง Bearer token เสมอ เจอจริงในหน้ารายการที่มีปัญหา (checklist
  รับรถ) และอีก 2 จุด (`IntakeReceiptDocument.tsx` ใบรับรถพิมพ์, `JobCardModal.tsx` `IntakeStage` แกลเลอรีเอกสารแนบ) —
  เพิ่ม `web/src/components/AttachmentImage.tsx` (ใหม่) รวม pattern `downloadAttachment` (มี Bearer header) →
  `URL.createObjectURL` → `revokeObjectURL` ตอน unmount ที่ `HandoverDocumentModal.tsx`/`StaffAvatar.tsx` ใช้อยู่แล้ว
  ไว้เป็น component เดียวใช้ซ้ำได้ (จัดการ loading/error state ในตัว ไม่ใช่แค่รูปพัง) แทนที่ทั้ง 3 จุด และลบ
  `attachmentFileUrl()` ที่ deprecated แล้วออกจาก `web/src/api/attachments.ts` เพราะไม่เหลือที่ใช้จริง
  · **อัปโหลดรูปไม่ resize**: เดิมไม่มีการ resize เลยทั้ง client และ server (ตรวจแล้ว) — เพิ่มที่ server เท่านั้น
  (`FtpAttachmentStorage.SaveAsync`/`PrepareUploadStreamAsync` ใหม่ internal) ตาม pattern `StaffImageStorage.cs` เดิม
  (ImageSharp ที่มีอยู่แล้วในโปรเจกต์): รูป (png/jpg/webp) ที่กว้างหรือสูงเกิน 1024px ถูก `AutoOrient()` + resize ด้วย
  `ResizeMode.Max` ให้ด้านที่ยาวที่สุดไม่เกิน 1024px (คงสัดส่วนเดิม) ก่อนอัปโหลดจริง — รูปที่ไม่เกิน 1024px อยู่แล้ว
  คืนต้นฉบับ byte-ต่อ-byte ไม่ re-encode ซ้ำ (กันคุณภาพ/ขนาดไฟล์เปลี่ยนโดยไม่จำเป็น) PDF ไม่ผ่านเส้นทางนี้เลย ทำเป็น
  server-side (ไม่ใช่ client-side) เพราะบังคับใช้ได้แน่นอนไม่ว่าอัปโหลดจากจุดไหน (เว็บ/มือถือ/อนาคต) — ยังไม่ได้เพิ่ม
  resize ฝั่ง client (ยังไม่จำเป็นเพราะ server บังคับผลลัพธ์สุดท้ายอยู่แล้ว ถ้าต้องการประหยัด bandwidth ก่อนอัปโหลดค่อยทำเพิ่ม)
  · **สร้างใบเสนอราคาเพิ่มไม่ได้**: พบ 2 ปัญหาจริงที่ทำให้ปุ่ม "+ สร้างใบเสนอราคา" ใน `JobCardModal.tsx` ค้าง disabled
  ทั้งที่ backend อนุญาตแล้ว — (1) เงื่อนไขเดิม `query.data?.every(q => q.status === 'rejected')` เทียบกับ**ทุกใบ**ใน
  ประวัติของ job แทนที่จะเทียบแค่ "ใบล่าสุด" (version สูงสุด) ตามที่ backend จริงเช็ค (`QuotationService.CreateAsync`
  เช็คแค่ `GetLatestForJobAsync`) — แก้เป็นหา `latestQuotation` จาก `version` สูงสุดแล้วเช็คว่าเป็น `rejected`/`superseded`
  เท่านั้น ตรงกับ semantics ของ backend เป๊ะ (2) **บั๊กจริงที่เจอเพิ่ม**: mutation "ออกฉบับแก้ไข" (`RevisionModal` ใน
  `QuotationEditorModal.tsx`) ไม่เคย invalidate query `['job-quotations', jobId]` เลย (revise สลับ id ในตัว modal
  เดิมไม่ปิด modal จึง `closeEditor` ที่ invalidate query ไม่ทำงาน) ทำให้รายการใบเสนอราคาใน `JobCardModal.tsx` ค้าง
  สถานะเก่าจนกว่าจะปิด editor เองด้วยมือ — เพิ่ม `jobId` prop ให้ `RevisionModal` แล้ว invalidate `job-quotations`/
  `job-detail` ใน `onSuccess` ของ mutation นั้นเลย · ปรับข้อความคำอธิบายปุ่มให้บอกสถานะใบล่าสุดจริงแทนข้อความตายตัวเดิม
  · **[ยังคงเป็นกฎเดิม ไม่ได้เปลี่ยน]** Quotation ยังคง version-first (invariant #1) — สร้างใบใหม่ขนานกับใบที่ยัง
  Draft/Sent/Partial/Approved ไม่ได้ (backend บล็อกด้วย `QUOTE_ALREADY_EXISTS` เสมอ) ต้องปฏิเสธ/ออกฉบับแก้ไขก่อน งานนี้
  แก้แค่ให้ UI ไม่ disable ผิดพลาดตอนที่ backend อนุญาตจริงๆ (ใบล่าสุดถูกปฏิเสธ/ถูกแทนที่แล้ว) — ถ้าต้องการให้สร้าง
  ใบเสนอราคาคู่ขนานกันจริงๆ ระหว่างที่ใบก่อนหน้ายัง active อยู่ (ไม่ใช่แค่ปลดบั๊กนี้) เป็นการเปลี่ยนนโยบายที่ต้องยืนยันแยก
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 140 (เพิ่ม `AttachmentStorageTests.cs`
  5 เทสต์ใหม่ครอบคลุม resize/ไม่ resize/PDF ผ่านไม่แตะ และแก้เทสต์ round-trip เดิมให้ใช้ไฟล์ PNG จริงแทนข้อความเปล่า
  เพราะตอนนี้ decode รูปเสมอ, skip 3 ที่ต้องต่อ SQL/FTP จริงเหมือนเดิม) · Web `tsc -b`/`vite build` ผ่าน (ผ่าน
  `node_modules/.bin/tsc`/`vite` ตรงๆ เพราะ pnpm/corepack ในเครื่องนี้ verify signature ไม่ผ่านเหมือนทุกครั้ง)
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (เปิดดูรูป checklist ที่เคยพังจริง, อัปโหลดรูปใหญ่แล้วตรวจว่า resize
  จริงบน FTP จริง — ยังติด FTP risk เดิม (`Ftp:Username`/`Password` ยังไม่ตั้งในเครื่อง dev นี้), revise แล้วกดสร้างใบใหม่
  ทันทีโดยไม่ปิด modal ก่อน)
- ✅ **[เพิ่ม 2026-09-10 รอบสอง] คลิกรูปดูพรีวิวในหน้าเดิม + อัปโหลด/แก้ไขรูปรถได้จากตอนเปิดจ๊อบและจากการ์ดจ๊อบ:**
  · **พรีวิวรูปในหน้าเดิม**: เดิมคลิกรูปแนบ (checklist/แกลเลอรีเอกสารแนบ) เปิดเป็น `<a target="_blank">` ไปแท็บใหม่ —
  เพิ่ม `web/src/components/ImageLightbox.tsx` (ใหม่, ใช้ `Dialog`/`DialogContent` จาก `ui/dialog.tsx` ตรงๆ ไม่ผ่าน
  `ConfirmModal` เพื่อเลี่ยง header/title chrome ที่ไม่ต้องการสำหรับรูปเปล่าๆ — ได้ overlay/focus-trap/Escape/ปุ่มปิด
  มาฟรีจาก `DialogContent` เดิม) `AttachmentImage.tsx` เปลี่ยนจาก `<a>` เป็น `<button>` (คลาสใหม่
  `.image-preview-trigger` ล้างสไตล์ default ของปุ่ม) ที่เปิด lightbox แทน — พฤติกรรม/prop เดิม (`linkToFullImage`,
  `linkClassName`) ไม่เปลี่ยนชื่อ แค่เปลี่ยนสิ่งที่มันทำตอนคลิก
  · **รูปรถแสดงไม่ได้มาตั้งแต่แรกสำหรับทุกจ๊อบที่สร้างผ่านระบบใหม่ (บั๊กแฝงที่เพิ่งพบ)**: `VehicleImage.tsx` เดิมต่อ
  `job.vehicleImagePath` เข้ากับ `legacyAssetBaseUrl` ตรงๆ เหมือนเป็น path ไฟล์ static ของ legacy — แต่
  `JobService.CreateAsync` (`Application/Jobs/JobService.cs:130`) เก็บค่านี้เป็น `vehicle.ImageUrl` ซึ่งจริงๆ คือ
  string `/api/v1/vehicles/{id}/image` (endpoint ที่ต้อง Bearer token) มาตั้งแต่ต้น (`CustomerVehicleRepository`
  SQL projection สร้าง URL รูปแบบนี้เสมอ) ทำให้รูปรถของทุกจ๊อบใหม่ 401/404 เงียบๆ แล้ว fallback เป็นไอคอนรถตลอด —
  เขียน `VehicleImage.tsx` ใหม่ทั้งหมดให้รับ `vehicleId` (ไม่ใช่ `path`) แล้วดึงผ่าน `apiDownload` + blob URL แบบเดียวกับ
  `AttachmentImage`/`StaffAvatar` — เป็นผลพลอยได้คือรูปรถ**ไม่มีวันค้าง (stale) อีกต่อไป** เพราะดึงจาก endpoint ตรงของ
  รถ (`/api/v1/vehicles/{vehicleId}/image`) สดทุกครั้งแทนสแนปช็อตที่ก็อปไว้ตอนสร้างจ๊อบครั้งเดียว — ไม่ต้องแก้/ลบคอลัมน์
  `Job.VehicleImagePath` เดิม (ยังอยู่ในฐานข้อมูล เผื่ออนาคต แต่ web ไม่อ่านค่านี้มาแสดงผลอีกต่อไป) แก้จุดใช้งานทั้ง 2 จุด
  (`JobsPage.tsx` ตารางจ๊อบ, `JobCardModal.tsx` `IntakeStage`) ให้ส่ง `vehicleId` แทน
  · **อัปโหลด/เปลี่ยนรูปรถจากการ์ดจ๊อบ (ขั้นตอนรับรถ)**: การ์ด "ข้อมูลลูกค้า & รถ" ใน `IntakeStage` เพิ่มปุ่ม "เปลี่ยนรูปรถ"
  ข้างรูป (คลิกดูพรีวิวได้ด้วย `clickToPreview`) เรียก endpoint ใหม่
  · **อัปโหลดรูปรถตอนเปิดจ๊อบ**: `CreateJobModal`/`NewVehicleForm` (`JobsPage.tsx`) เพิ่มช่องเลือกรูป (ไม่บังคับ) ตอน
  สร้างรถใหม่ ส่งพร้อม `createVehicle(input, image)` ที่มีอยู่แล้ว (รองรับรูปอยู่แล้วแต่ไม่เคยถูกเปิดใช้ในหน้านี้) —
  ส่วนตอนเลือก "รถที่มีอยู่แล้ว" เพิ่มปุ่ม "อัปโหลด/เปลี่ยนรูป" ให้แนบรูปได้ทันที (ไม่ต้องรอเปิดจ๊อบสำเร็จก่อน เพราะเป็น
  ข้อมูลของรถ ไม่ใช่ของจ๊อบ) ผ่าน endpoint ใหม่เดียวกัน (`VehiclePhotoQuickUpload` component ใหม่ใน `JobsPage.tsx`)
  · **Endpoint ใหม่ `POST /api/v1/vehicles/{id}/image`** (`VehiclesController.UpdateImage`) — เปลี่ยนเฉพาะรูป ไม่ต้อง
  resend ฟอร์มรถเต็ม (endpoint เดิม `PUT /api/v1/vehicles/{id}` ต้องส่งทุก field รวมทะเบียน/ยี่ห้อ/รุ่นที่หน้าการ์ด
  จ๊อบ/ตอนเลือกรถเดิมไม่มีข้อมูลพร้อมส่งซ้ำ และ `VehicleDetailDto` ก็ไม่มี `CustomerId` ให้สร้าง request ใหม่ได้ครบอยู่ดี)
  `ICustomerVehicleRepository.UpdateVehicleImageAsync` ใหม่ (`CustomerVehicleRepository.cs`) มิเรอร์การจัดการไฟล์ของ
  `UpdateVehicleAsync` เดิมทุกจุด (save ไฟล์ใหม่ก่อน → UPDATE เฉพาะคอลัมน์ `ImageUrl` → ลบไฟล์เก่าหลัง commit สำเร็จ
  → cleanup ไฟล์ใหม่ถ้า UPDATE ไม่โดนแถวไหนเลย) แต่ไม่แตะคอลัมน์อื่นหรือ `CarCustomer` เลย — **ยังคงเป็นคำสั่งเขียน
  Garage legacy (`Car.ImageUrl`) ภายใต้ข้อยกเว้นเดิมที่มีอยู่แล้วสำหรับโมดูล Customer/Vehicle** (ดู `[RISK]` เรื่อง
  นโยบายกับโค้ดจัดการข้อมูลหลักที่ยังไม่ตรงกันด้านบน) ไม่ใช่ข้อยกเว้นใหม่ที่ไม่เคยขอ
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error) — อัปเดต `FakeCustomerVehicleService` ใน
  `JobServiceTests.cs` ให้ implement method ใหม่ (`NotImplementedException` เหมือน method อื่นที่ไม่เกี่ยวกับ test
  เหล่านั้น) `dotnet test` ผ่านทั้ง 140 เท่าเดิม (ไม่ได้เพิ่ม unit test ใหม่สำหรับ `UpdateVehicleImageAsync` เพราะ
  ต้องต่อ SQL จริงเหมือน `BranchScopeSqlTests` ที่ skip อยู่แล้ว — ไม่มี fake/in-memory ของ `CustomerVehicleRepository`
  ในชุดทดสอบเดิมให้ต่อยอด) · Web `tsc -b`/`vite build` ผ่าน
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริงและยังไม่ได้รันกับ SQL จริง** (เปิดจ๊อบพร้อมอัปโหลดรูปรถใหม่, เปลี่ยนรูป
  รถที่มีอยู่แล้วจากหน้าเปิดจ๊อบ, เปลี่ยนรูปรถจากการ์ดจ๊อบแล้วตรวจว่ารูปอัปเดตจริงไม่ค้าง cache, คลิกรูปดู lightbox ทุกจุด)
  ต้องต่อ Garage Pro VPN ก่อนถึงจะทดสอบ endpoint นี้ได้จริง
- ✅ **[เพิ่ม 2026-09-10] JobChat — widget แชทมุมล่างขวาของ Job Card**: ข้อความ/รูป/reply/mention ผูกกับ job
  โดยตรง (ฟีเจอร์ใหม่ทั้งหมด ไม่มีในเอกสาร design เดิม) วางแผนผ่าน plan mode ก่อนเริ่มเขียนโค้ด
  · เอนทิตีใหม่ `JobChatMessage`/`JobChatMention` (`Domain/Entities/JobChatMessage.cs`) — ไม่มีคอลัมน์
  `BranchId`/`ShardKey` เหมือน entity ลูกของ job อื่นทุกตัว (scope ผ่าน `Job` เดียว) `ReplyToMessageId` เป็น
  self-FK `OnDelete(Restrict)` (ลบจริงไม่มี — ใช้ soft delete เท่านั้น) migration
  `20260910045219_AddJobChat` — **ยังไม่ได้รัน `dotnet ef database update` จริง** (sandbox ไม่ได้ต่อ VPN เหมือนทุกครั้ง)
  · **รูปภาพไม่ได้เก็บ path ในเอนทิตีนี้เอง** — reuse ระบบ `Attachment` เดิมทั้งหมด (`Kind="chat"` เพิ่มใน
  `AttachmentService.AllowedKinds`, ผูกด้วย `Attachment.EntityId = JobChatMessage.Id`) client อัปโหลดรูปผ่าน
  `POST /api/v1/attachments` ก่อนแล้วส่ง `attachmentIds` แนบตอนสร้างข้อความ — server เช็ค kind/JobId/ยังไม่ถูกผูก
  ก่อน set `EntityId` ให้ (all-or-nothing บนข้อความเดียว ไฟล์ที่อัปโหลดแล้วไม่เคยส่งเป็น orphan ที่ยอมรับได้
  เหมือน attachment อื่นที่ไม่มี GC job)
  · **Mention ฝังเป็น token ในข้อความเอง** รูปแบบ `@[staffId:ชื่อ]` (client สร้าง/parse เอง ไม่มี rich-text lib
  ในสแตกนี้) server validate แต่ละ `mentionedStaffIds` ผ่าน `IStaffRepository.GetAsync` แบบเดียวกับที่
  `PurchasingService.WithdrawAsync` validate ผู้เบิก (active + สาขาเดียวกันเท่านั้น) เก็บ snapshot ชื่อไว้ใน
  `JobChatMention` กันปัญหาถ้าพนักงานถูกเปลี่ยนชื่อทีหลัง (ตารางนี้ไว้ query/validate เท่านั้น ไม่ใช่แหล่งความจริงของ
  การ render — การ render อ่านจาก token ในข้อความโดยตรง)
  · **ไม่ผูก role เพิ่มจาก `[RequireShiftSession]` เดิม** ตรงกับ `[RISK]` ที่บันทึกไว้แล้วเรื่อง RBAC ของ Job
  endpoint ยังไม่ผูก role — chat ใช้ gate เดียวกัน ไม่ได้เพิ่มช่องโหว่ใหม่
  · API ใหม่ `JobChatController` (`GET/POST/DELETE /api/v1/jobs/{jobId}/chat/messages`) — pagination แบบ
  keyset สองทิศทาง (`beforeAt/beforeId` โหลดเก่ากว่า, `afterAt/afterId` poll ต่อ) ก็อป tie-break logic ตรงจาก
  `JobRepository.SearchAsync` ลบข้อความเป็น soft delete โดยเจ้าของข้อความเท่านั้น (`CHAT_FORBIDDEN`)
  · **Realtime = polling เท่านั้น** (ระบบไม่มี SignalR/WebSocket) — `JobChatPanel.tsx` ใช้ `useInfiniteQuery` +
  `refetchInterval` 5 วิ ตอน panel เปิด (รีเฟรชทุกหน้าที่เคยโหลดแล้ว dedupe ด้วย `Map` ตอน flatten — เรียบง่ายกว่า
  ทำ cursor "ใหม่กว่า" แยกอีกชุด เหมาะกับสเกลข้อความต่อ job ของระบบนี้) ตอน panel ปิด `JobChatWidget.tsx` peek
  แค่ข้อความล่าสุด (`take=1`) ทุก 20 วิ เพื่อโชว์จุดแดง "มีข้อความใหม่" เทียบกับ id ที่จำไว้ใน localStorage ต่อเครื่อง
  (`garagepro.jobchat.last-seen.{jobId}`) — **ไม่ sync ข้าม device/browser** (ตามที่ตกลงไว้ตอนวางแผน)
  · **Mention popover ไม่ได้ทำ caret-tracking แบบ pixel-precise ตามที่คุยไว้ตอนวางแผน** — ใช้ dropdown แบบ
  full-width เหนือกล่องข้อความแทน (`.job-chat-mention-list`, มิเรอร์ `ui-combobox__list` แต่ anchor เหนือ textarea
  ไม่ใช่ที่ตำแหน่ง caret) ตัดสินใจแบบนี้เพราะ caret-mirror-div เป็นเทคนิคที่เปราะบาง (font metrics/wrapping) และ
  widget นี้แคบ ไม่ใช่กล่องข้อความยาวหลายบรรทัดที่ caret-tracking จะมีประโยชน์ชัดเจน — ยัง reuse การพิมพ์
  `@` ตรวจจับคำค้นหา + `getStaffs({keyword})` + คีย์บอร์ด (ลูกศร/Enter/Escape) แบบเดียวกับ `Combobox` เดิม
  · component ใหม่ทั้งหมดอยู่ใต้ `web/src/features/jobs/chat/`: `JobChatWidget.tsx` (ปุ่มลอย + จุดแดง),
  `JobChatPanel.tsx` (รายการข้อความ + reply preview + เรียก `AttachmentImage`/`ImageLightbox` เดิมสำหรับรูป),
  `JobChatComposer.tsx` (กล่องพิมพ์ + แนบรูปหลายไฟล์ + mention popover), `mentionToken.ts` (parse/serialize
  token ล้วน ไม่มี side effect — segment เป็น array ของ `{type:'text'|'mention'}` render ทีละ segment
  **ไม่ใช้ `dangerouslySetInnerHTML` เลย**) mount ใน `JobCardModal.tsx` เป็น sibling ของ `.job-card-page`
  ใน `<ConfirmModal>` (ไม่ใช่ลูกของ `<StageContent>`) จึงอยู่มุมเดิมตลอดแม้สลับ stage — `position: absolute`
  อ้างอิงกับ `.ui-dialog__content` (positioned ancestor, `overflow: hidden`) ไม่ใช่ `.ui-dialog__body` ที่ scroll
  ไม่ต้องแก้ `ConfirmModal.tsx` เลย
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 151 (เพิ่ม
  `JobChatServiceTests.cs` 11 ผ่าน ครอบคลุม branch mismatch, ข้อความว่างไม่มีรูป/มีแต่รูปผ่านได้, attachment
  ผิด job/ถูกผูกไปแล้ว, mention พนักงาน inactive/ต่างสาขา, reply ข้ามจ๊อบ, reply preview ของข้อความที่ถูกลบ,
  soft delete โดยเจ้าของเท่านั้น) · Web `tsc -b`/`vite build` ผ่าน (ผ่าน `node_modules/.bin/tsc`/`vite`
  ตรงๆ เหมือนทุกครั้งที่ pnpm/corepack ในเครื่องนี้ verify signature ไม่ผ่าน)
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (พิมพ์/แนบรูป/reply/mention จริง, เห็นจุดแดงตอนอีกคนส่งข้อความ,
  โหลดข้อความเก่ากว่าด้วย "โหลดข้อความเก่ากว่า", ลบข้อความตัวเอง) และยังไม่ได้รัน migration กับ ServiceDb จริง —
  ต้องทำทั้งสองก่อนใช้งานจริง
- ✅ **[เพิ่ม 2026-09-10 รอบสาม] หน้า PO — ตัวเลือก "ทุกสถานะ" ซ่อนรับครบแล้ว/ยกเลิกเมื่อไม่มีคำค้นหา + ตัวเลขในเมนู PO
  ตรงกัน**: เดิมหน้า `/purchasing/po` (`PurchasingPage.tsx`) ตัวเลือก "ทุกสถานะ" (`status=''`, ค่าเริ่มต้น) แสดงทุก
  เอกสารจริงๆ ทำให้ PO ที่ `complete`/`cancelled` (รับครบแล้ว/ยกเลิก) ค้างปนอยู่ในรายการเสมอ — **ตัดสินใจตามที่ผู้ใช้
  ยืนยันชัดเจน**: ไม่ได้เพิ่ม dropdown option ใหม่แยกต่างหาก แต่ปรับพฤติกรรมของ "ทุกสถานะ" เดิมเอง — เมื่อเลือก
  "ทุกสถานะ" **และไม่มีคำค้นหา** จะไม่ดึง PO ที่ `complete`/`cancelled` มาแสดง (มองเป็นสถานะปิด/จบเหมือนกันทั้งคู่
  ไม่ได้จำกัดแค่ `complete` ตามตัวอักษรคำขอเป๊ะๆ) — พิมพ์คำค้นหา (`q`) ขณะที่ยังเลือก "ทุกสถานะ" อยู่จะดึงมาทุกสถานะจริง
  (รวมรับครบแล้ว/ยกเลิก) เพราะถือว่ากำลังค้นหาเอกสารเจาะจง (มี section-help บอกสถานะการกรองทั้งสองกรณีอยู่เสมอ) —
  ถ้าผู้ใช้เลือกสถานะอื่นเองจาก dropdown (เช่น "รับครบแล้ว"/"ยกเลิก" ตรงๆ) ถือเป็นเจตนาชัดเจน ไม่ถูกซ่อนไม่ว่าจะมีคำ
  ค้นหาหรือไม่ · ปรับเฉพาะ PO — PR ยังคงแสดงทุกสถานะจริงตอนเลือก "ทุกสถานะ" เหมือนเดิมตามที่ผู้ใช้ระบุขอบเขตเฉพาะ
  หน้า PO เท่านั้น
  · Backend: `status="open"` เป็นค่าพิเศษที่ `PurchasingRepository.SearchAsync` ตีความเป็น
  `Status != "complete" && Status != "cancelled"` (ไม่ใช่ enum ค่าจริงของ `PurchaseDocument.Status` — กันชนกับสถานะ
  จริงเพราะไม่มีสถานะไหนชื่อ "open" อยู่แล้ว) เพิ่ม endpoint ใหม่ `GET /api/v1/purchase-orders/count-open`
  (`PurchaseOrdersController`/`PurchasingService.CountOpenAsync`/`IPurchasingRepository.CountOpenAsync`) นับ PO
  ที่ยังไม่ปิด สโคปด้วย shard/สาขาเดิม (ผ่าน `Documents` property ที่กรองอยู่แล้ว) — endpoint นี้ยังผ่าน
  `PurchasingService.Access()` เดิม (เฉพาะ Manager/Office เหมือนทุก endpoint ในโมดูลนี้ ต่างจาก Jobs' `count-open`
  ที่เปิดทุก role) จึงไม่ต้องเพิ่ม guard ใหม่
  · เมนู Sidebar (`AppShell.tsx`): ตัวเลขข้าง "ใบสั่งซื้อ (PO)" เปลี่ยนจากเดิมที่เรียก `purchases('PO','','',1,100)`
  แล้วอ่าน `totalItems` (นับเอกสารทุกสถานะรวมกัน ไม่ตรงกับสิ่งที่ผู้ใช้ต้องดำเนินการต่อจริงๆ) มาเป็นเรียก
  `countOpenPurchaseOrders()` ใหม่ตรงๆ (เบากว่าเดิมด้วย ไม่ต้องดึงเอกสารพร้อม lines 100 รายการมาทิ้งแค่นับ) — คง
  queryKey เป็น `['purchase-count', 'PO', 'open']` (ขึ้นต้นด้วย `'purchase-count'` เดิม) เพื่อให้ `usePurchasingRefresh`
  ที่ invalidate prefix `'purchase-count'` อยู่แล้วครอบคลุมต่อ ไม่ต้องแก้ invalidate list · ตัวเลขเมนู PR ไม่เปลี่ยน
  (ยังนับทุกสถานะเหมือนเดิม ตามขอบเขตที่ผู้ใช้ระบุเฉพาะ PO)
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 152 (เพิ่ม
  `Open_po_filter_and_count_exclude_complete_and_cancelled_documents` ใน `PurchasingServiceTests.cs` ครอบคลุม
  filter `open` คัด PO ที่ `complete`/`cancelled` ออก **แต่ยังคงเห็น PO สถานะ `draft`** (เช่น PO ที่เพิ่งแปลงจาก PR
  ที่อนุมัติแล้วและยังไม่ได้กดส่งขออนุมัติ) เท่ากับ `sent`/`partial`, `CountOpenAsync` นับตรงกับผลกรอง, และ
  "ทุกสถานะ" (`status=null`) ยังคงเห็นครบทุกเอกสารเหมือนเดิม) · Web `tsc -b`/`vite build` ผ่าน (ผ่าน
  `node_modules/.bin/tsc`/`vite` ตรงๆ เพราะ pnpm/corepack ในเครื่องนี้ verify signature ไม่ผ่านเหมือนทุกครั้ง)
  · **[พบระหว่างทดสอบจริง 2026-09-10]** ผู้ใช้แจ้งว่า PO ที่เพิ่งแปลงจาก PR (สถานะ `draft`) ไม่ขึ้นในรายการตอนกรอง
  "ทุกสถานะ" — ตรวจโค้ดซ้ำและเพิ่ม unit test ยืนยันแล้วว่า `draft` **ไม่ได้ถูกกรองออก** โดยเจตนา (ตรรกะ `open` คัดออก
  แค่ `complete`/`cancelled` เท่านั้น) จึงสรุปว่าอาการนี้เกิดจาก **backend API ที่รันอยู่ยังไม่ได้ build/restart ใหม่**
  หลังแก้โค้ดรอบนี้ (`dotnet run` ธรรมดาไม่ hot-reload endpoint/repository) ทำให้ `status=open` ถูกตีความเป็นชื่อ
  สถานะจริงที่ไม่มีอยู่ (คืนผลลัพธ์ว่างทั้งหมด ไม่ใช่แค่ซ่อน `draft`) — ยังไม่ได้รับการยืนยันจากผู้ใช้ว่า build/restart
  แล้วแก้ปัญหาจริงหรือไม่
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (เปิดหน้า PO ดูว่าเริ่มต้นไม่มี PO ที่รับครบแล้ว, ตัวเลขเมนูตรงกับ
  จำนวนแถวที่เห็นจริง, พิมพ์ค้นหาแล้วเจอ PO ที่รับครบแล้วด้วย, เลือกสถานะอื่นเองจาก dropdown แล้วค้นหาไม่ถูกข้ามตัวกรอง)
- ✅ **[เพิ่ม 2026-09-10 รอบสี่] Authentication เข้าได้เฉพาะสาขากลุ่ม "Service" เท่านั้น**: ก่อนหน้านี้
  `LegacyUserReader.GetAccessibleBranchesAsync` กรองแค่ `Branch.Status` — สาขาที่ทำงานเคลม/สีตัวถังล้วน (คนละกลุ่มกับ
  service) เข้าระบบนี้ได้เหมือนกันหมด ทั้งที่เป็น open question B ที่ค้างมาตั้งแต่ `docs/05-legacy-db-mapping.md` §7
  — ผู้ใช้ยืนยันแล้วว่า **`Branch.BranchGroupId = 7` คือกลุ่ม "Service"** จึงเพิ่มเงื่อนไข `AND b.BranchGroupId = 7`
  เข้าไปในทั้งสอง query ของ `GetAccessibleBranchesAsync` (`LegacyUserReader.cs`, ทั้งกรณีผู้ดูแลระบบและพนักงานทั่วไป)
  — SQL ถูกแยกเป็น `internal static BuildAccessibleBranchesSql(bool isAdministrator)` เพื่อให้ทดสอบ SQL จริงได้ตรงกับ
  production query เป๊ะ (`LegacyBranchGroupSqlTests.cs`, gate ด้วย `GARAGEPRO_BRANCH_SQL_CONNECTION` แบบเดียวกับ
  `BranchScopeSqlTests` เดิม — **สร้างตารางชั่วคราวเท่านั้น ไม่แตะข้อมูลจริง**)
  · **ผลกระทบ**: `AuthService.LoginAsync`/`OpenShiftAsync` เรียกฟังก์ชันนี้อยู่แล้ว จึงได้ผลทันทีทั้งสองจุด — พนักงานที่
  `Staff.BranchId` ไม่ใช่กลุ่ม 7 login ไม่ได้อีกต่อไป (ใช้ error เดิม `AUTH_NO_BRANCH` ไม่ได้เพิ่ม error code ใหม่
  เพราะข้อความเดิม "บัญชีนี้ไม่ได้ผูกกับสาขาที่เปิดใช้งาน" ยังตรงกับความจริง) · **ผู้ดูแลระบบก็ถูกจำกัดด้วย** — เดิมเห็น
  ทุกสาขา active ใน shard ตอนนี้เห็นเฉพาะกลุ่ม Service เท่านั้น **[RISK ที่ต้องตรวจก่อนใช้งานจริง]** ถ้ามี admin ที่
  `Staff.BranchId` ของตัวเองอยู่นอกกลุ่ม 7 คนนั้นจะ login เว็บไม่ได้ไปด้วย — เป็นพฤติกรรมที่ตั้งใจตามคำขอ แต่ยังไม่เคย
  ทดสอบจริงกับบัญชี admin จริงเพราะ sandbox นี้ต่อ VPN ไม่ได้
  · แก้ `docs/05-legacy-db-mapping.md` §7 ข้อ B เป็น "ตอบแล้ว" พร้อมอ้างอิงจุดที่บังคับใช้จริงในโค้ด
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 167 (SQL-fact test ใหม่ถูก skip
  เพราะไม่มี `GARAGEPRO_BRANCH_SQL_CONNECTION` เหมือนเทสต์ SQL อื่นทุกตัวในสภาพแวดล้อมนี้) · **ยังไม่ได้รันกับฐานข้อมูล
  จริงเพื่อยืนยันว่า `BranchGroupId` มีค่าอื่นที่ควรนับเป็น "Service" ด้วยหรือไม่** (เช่น สาขาผสม service+เคลม) — ถ้าพบ
  ต้องยืนยันแยกก่อนเปลี่ยนเงื่อนไข
- ✅ **[เพิ่ม 2026-09-10 รอบสี่] เมนู "รายงาน" — 4 รายงานแรกของระบบ**: เดิมเมนู Reports (`AppShell.tsx`) มีลิงก์เดียวไป
  `/reports` ที่ไม่มี route จริงมาตั้งแต่แยกกลุ่มเมนู (กด 404 ตลอด) — ผู้ใช้ขอให้คิดรายงานที่น่าสนใจจาก feature ที่ทำไปแล้ว
  และให้มีหน้าแดชบอร์ดด้วย เลือกทำทั้ง 4 อย่าง: **แดชบอร์ดวันนี้ / รอบเวลาทำงาน (SLA) / ยอดขาย-ต้นทุน-กำไร / สต็อกสินค้า**
  — เป็น endpoint/หน้าใหม่ทั้งหมด ไม่มี entity ใหม่ (อ่านจาก `Job`/`ActivityEvent`/`Quotation`/`Payment`/`Receipt`/
  `StockLot`/`CatalogItem` ที่มีอยู่แล้วทั้งหมด) Backend ใหม่: `IReportsRepository`/`ReportsRepository` (LINQ ตรงกับ
  `ServiceDbContext` — ไม่ใช่ legacy จึงไม่ต้อง Dapper/READUNCOMMITTED) · `ReportsService` (`Application/Reports/`,
  จำกัดเฉพาะ Manager/Office เหมือนโมดูลจัดซื้อ) · `ReportsController` (`GET /api/v1/reports/{dashboard,cycle-time,
  sales-margin,stock}`) · สิทธิ์เห็นต้นทุน/กำไร/กำไร% ใน "ยอดขาย-ต้นทุน-กำไร" strip เป็น `null` ที่ server ตาม
  `user.CanSeeCost` แบบเดียวกับ `QuotationMapper`/`CatalogService` (invariant #7) **ไม่ได้ทำรายงานคอมมิชชันช่าง** เพราะ
  ตรวจโค้ดแล้วคอมมิชชันไม่เคยถูก implement จริงที่ไหนเลย (มีแต่ตัวเลข 8/10/12% ที่ยังเป็น `[ASSUME]` ใน CLAUDE.md) —
  ทำ "ยอดขายค่าแรงต่อช่าง" แทน (ข้อมูลจริงจาก `QuotationLine.AssignedTechnicianName` ที่ snapshot ไว้อยู่แล้ว)
  · **รอบเวลาทำงานต้องรู้ว่าแต่ละช่วงเวลาจ๊อบอยู่สถานะไหน** — เพิ่ม `PayloadJson` (`{"from":...,"to":...}`) ลงใน
  `ActivityEvent` ของ `job.status.changed` ที่ `JobService.TransitionAsync` (คอลัมน์เดิมมีอยู่แล้ว ไม่มี migration)
  แทนการ parse ข้อความไทยใน `DescriptionTh` ที่เปราะบาง — เทสต์ยืนยันว่า payload set ถูกต้อง (`JobServiceTests.cs`)
  ตาราง "จ๊อบที่ค้างนานที่สุด" ไม่ต้องพึ่ง payload นี้เลย (อ่านจาก `Job.Status`/เวลาของ event ล่าสุดตรงๆ) จึงใช้งานได้ทันที
  แม้ข้อมูลเก่าก่อนหน้านี้จะไม่มี payload — **ยังไม่มี SLA เป้าหมาย (เวลา/ชั่วโมง) ที่ยืนยันแล้ว** รายงานแสดงแค่ค่าเฉลี่ย/
  P90 จริงให้ดูเทียบเคียงเท่านั้น ไม่ได้ตัดสินว่าผ่าน/ไม่ผ่าน SLA 90% ที่เป็น `[ASSUME]` เดิม
  · Frontend: `web/src/api/reports.ts` + 4 หน้าใหม่ใน `web/src/features/reports/` (`DashboardReportPage`,
  `CycleTimeReportPage`, `SalesMarginReportPage`, `StockReportPage`) ต่อ route `/reports/{dashboard,cycle-time,
  sales-margin,stock}` (`/reports` redirect ไปแดชบอร์ด) เมนู Reports ใน `AppShell.tsx` ขยายจาก 1 เป็น 4 ลิงก์
  · ไม่ได้เพิ่ม chart library — ทำ `StatTile`/`ReportBar` เป็น component เขียนเองด้วย CSS (สแตกนี้ไม่มี dependency
  กราฟอยู่แล้ว) สีของแท่งกราฟสถานะจ๊อบใช้ค่าเดียวกับ `.job-status-*` ที่มีอยู่แล้วใน `index.css` (ไม่ใช้สีใหม่แยกจาก
  ป้ายสถานะเดิม) ส่วนอายุสต็อกใช้ไล่เฉดเดียว (amber, sequential) ตามหลัก dataviz — ยังไม่ได้ตรวจ contrast/CVD ด้วย
  สคริปต์ validator เพราะเป็นสีที่คัดมาจาก design token ที่อนุมัติแล้วของระบบ ไม่ใช่ palette ใหม่
  · ทดสอบแล้ว: `dotnet build`/`dotnet test` ผ่านทั้ง 167 (เพิ่ม `ReportsServiceTests.cs` 8 ผ่าน ครอบคลุม role gate,
  strip ต้นทุน/กำไรตาม role, คำนวณรอบเวลาต่อสถานะ+จัดอันดับจ๊อบค้างข้ามช่วงวันที่กรอง, bucket อายุสต็อก + เพิ่ม 1 test
  ใน `JobServiceTests.cs` สำหรับ payload) · Web `tsc -b`/`vite build` ผ่าน (ผ่าน `node_modules/.bin/tsc`/`vite`
  ตรงๆ เพราะ pnpm/corepack ในเครื่องนี้ verify signature ไม่ผ่านเหมือนทุกครั้ง)
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริงและยังไม่ได้รันกับ ServiceDb ที่มีข้อมูลจริง** (ตัวเลข/กราฟตรงกับข้อมูล
  จริงหรือไม่, ตัวกรองวันที่ใช้งานได้จริง, หน้าที่ Office เห็นต้นทุนถูกซ่อนจริงในเบราว์เซอร์) — sandbox นี้ไม่ได้ต่อ VPN
- ✅ **[เพิ่ม 2026-09-16] วันเวลานัดหมาย + ปฏิทินจ๊อบ + เทมเพลตใบเสนอราคา** — 3 ฟีเจอร์ตามคำขอผู้ใช้ วางแผนผ่าน
  plan mode ก่อนเริ่มเขียนโค้ด
  · **[แก้บั๊กเดิมทั้งระบบ] DateTimeKind** — ดูหัวข้อ "บทเรียนที่เจ็บมาแล้ว" ด้านบน แก้ที่จุดเดียวด้วย
  `ServiceDbContext.ConfigureConventions` กระทบทุก DateTime ในระบบ (เวลาที่แสดงบนเว็บทุกจุดตรงกับเวลาไทยจริง
  ตั้งแต่ตอนนี้ ไม่ใช่แค่ฟิลด์ใหม่ที่เพิ่มวันนี้) — ไม่มี migration ที่เกี่ยวข้อง (แค่การแปลง Kind ตอนอ่าน ไม่กระทบ
  ชนิดคอลัมน์ในฐานข้อมูล ยืนยันแล้วว่า migration ที่ generate หลังแก้ไม่มี diff ของ column type)
  · **วันเวลานัดหมาย**: `Job.AppointmentAt` (nullable, UTC) ใหม่ — บังคับเฉพาะ `JobTypeId=10` (กฎข้อ 15) แก้ไข/
  เลื่อนนัดได้ภายหลังผ่าน `PUT /api/v1/jobs/{id}/appointment` (เขียน `ActivityEvent job.appointment.changed`
  เสมอ ไม่ใช่ state transition จึงไม่ผ่าน `JobStateMachine`) ล็อกแก้ไม่ได้เมื่อถึงสถานะจบ (`JOB_APPOINTMENT_LOCKED`)
  ไม่ใช่แค่หลัง `waitinspect` เพราะยังไม่มี transition "รถมาถึงแล้ว" ในระบบ · เว็บ: `CreateJobModal` แสดงช่อง
  `datetime-local` (ตัวแรกในโปรเจกต์ — เดิมมีแต่ `type="date"`) เฉพาะตอนเลือกประเภท "รถนัดหมาย", การ์ดจ๊อบมีปุ่ม
  "แก้ไข/เลื่อนนัด" · migration `20260916154819_AddJobAppointmentAt` — บันทึกไว้ตอนแรกว่ายังไม่ได้รัน แต่
  **[แก้ไข 2026-09-17] ยืนยันแล้วว่าถูกรันจริงบน ServiceDb แล้ว** (พบตอนตรวจ `dotnet ef migrations list` ในรอบ
  ถัดมา ซึ่งครั้งนี้ sandbox ต่อ VPN ได้ — ไม่ทราบว่าใครรัน/รันเมื่อไหร่ ระหว่างสองรอบงาน)
  · **ปฏิทินนัดหมาย**: ปุ่มสลับ "รายการ/ปฏิทินนัดหมาย" บนหน้า `/jobs` (ใช้ `Tabs` ที่มีอยู่แล้วเป็น segmented
  control) endpoint ใหม่ `GET /api/v1/jobs/calendar?from=&to=&q=&status=` (คนละ contract กับ `/jobs/search`
  เดิม — คืนทุกแถวในช่วง `[from, to)` ไม่ใช่ keyset cursor, ช่วงสูงสุด 92 วัน เกิน 500 แถวคืน `truncated: true`)
  **กรองด้วย `AppointmentAt != null` ไม่ใช่ `JobTypeId == 10`** (ดูเหตุผลในกฎข้อ 15) เดือนปฏิทินคำนวณเองด้วย
  `calendarMonth.ts` (pure function, ไม่เพิ่ม date/calendar library) ชิปนัดหมายใช้สี/ไอคอน/ข้อความจาก
  `JobStatusChip`/`.job-status-*` เดิม สไตล์ใหม่อยู่ที่ `web/src/features/jobs/jobs.css` (แยกไฟล์ตาม pattern
  `reports.css`/`purchasing.css` เดิม ไม่แตะ `index.css`)
  · **เทมเพลตใบเสนอราคา**: เมนูใหม่ "เทมเพลตใบเสนอราคา" ใต้ Master Data + ปุ่ม "เพิ่มรายการด้วยเทมเพลต" ทั้งใน
  การ์ดจ๊อบ (ขั้นเสนอราคา) และหน้าต่างแก้ไขใบเสนอราคา (พาเนลแคตตาล็อก) — รายละเอียดเต็ม (การแปลงราคา/ต้นทุน,
  กรณีรหัสหาย/ถูกปิดใช้งาน/ซ้ำ, สิทธิ์) ดูที่ [docs/08-quotation-template.md](docs/08-quotation-template.md)
  migration `20260916154843_AddQuotationTemplate` — เช่นเดียวกับข้างบน **[แก้ไข 2026-09-17] ยืนยันแล้วว่าถูกรันจริงแล้ว**
  · **[RISK]** ทั้งปฏิทินนัดหมายและ endpoint เทมเพลตยังอยู่ใต้ `[RequireShiftSession]` เท่านั้น สืบทอดช่องโหว่ RBAC
  เดิมที่บันทึกไว้แล้ว (กฎข้อ 14 ด้านบน) ไม่ได้แก้ในงานนี้
  · ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 210 (เพิ่ม 10 ใน
  `JobServiceTests.cs`, ใหม่ `QuotationTemplateServiceTests.cs` 12 ผ่าน, เพิ่ม 10 ใน `QuotationServiceTests.cs`
  ครอบคลุม apply template ทุกเส้นทาง — ไม่รวม 4 ที่ skip เพราะต้องต่อ SQL/FTP จริงเหมือนเดิม) · Web `tsc -b`
  (`--force` ยืนยันซ้ำ) และ `vite build` ผ่าน (bundle warning เดิม ~940 kB ไม่ใช่ของใหม่) · `node --test
  tests/*.test.mjs` ผ่านทั้ง 10 (3 เดิม + ใหม่ `calendarMonth.test.mjs` 7 ผ่าน) — **ต้องรันด้วย Node 24**
  (เครื่องนี้ default เป็น Node 22.2.0 ซึ่ง `node --test` import `.ts` ตรงๆ ไม่ได้ ติดตั้ง Node 24 ผ่าน `nvm`
  เพิ่มในเครื่องนี้เพื่อรันเทสต์ชุดนี้โดยเฉพาะ ตรงกับที่ CLAUDE.md เตือนไว้แล้วว่า "ตรวจด้วย Node 24")
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (เปิดจ๊อบนัดหมายพร้อมวันเวลาแล้วเห็นในปฏิทินตรงช่อง,
  เลื่อนนัดจากการ์ดจ๊อบ, สร้าง/แก้เทมเพลตแล้ว apply จากทั้ง 2 จุด, ตรวจว่า apply เทมเพลตซ้ำโดน
  `QUOTE_TEMPLATE_DUPLICATE_LINE`, ปิดใช้งานสินค้าที่เทมเพลตอ้างถึงแล้วเห็น badge เตือนก่อน apply) — **[แก้ไข
  2026-09-17] migration ทั้งสองตัวถูกรันกับ ServiceDb จริงแล้ว** (ยืนยันซ้ำในรอบถัดมา ดูหัวข้อถัดไป)
- ✅ **[เพิ่ม 2026-09-17] ปุ่ม "แปลงเป็นรถในอู่" ในการ์ดจ๊อบ + วันนัดหมายห้ามน้อยกว่าวันเวลาปัจจุบัน** ตามคำขอผู้ใช้
  · **แถวประเภทงาน** ใน `IntakeStage` เดิมเป็นข้อความเฉยๆ ตอนนี้แสดงเป็น chip + ปุ่ม "รถเข้าอู่แล้ว → เปลี่ยนเป็น
  รถในอู่" ข้างกัน (แสดงเฉพาะ `jobTypeId === 10`) กดแล้วเปิด `ConvertToInShopModal` ใหม่ ให้ยืนยันวันเวลาที่รถเข้า
  อู่จริง (default = ตอนนี้, แก้ไขได้แต่ห้ามเป็นอนาคต — `max={nowLocalInputValue()}`) เรียก
  `PUT /api/v1/jobs/{id}/convert-to-in-shop` ใหม่ (ดูกฎข้อ 17 ด้านบน) — **ไม่ผูกกับวันนัดหมายที่ตั้งไว้เลย**
  ยืนยันกับผู้ใช้แล้วว่ามาก่อน/หลังนัดก็แปลงได้ ไม่ต้องรอ "ถึงวันนัด" ก่อนปุ่มถึงจะกดได้
  · `Job.ActualArrivalAt` (nullable, UTC) ใหม่ — คนละฟิลด์กับ `CreatedAt`/`AppointmentAt` migration
  `20260917012834_AddJobActualArrivalAt` — **[อัปเดต] รันกับ ServiceDb จริงแล้ว** (sandbox รอบนี้ต่อ VPN ได้จริง
  โดยไม่คาดคิด — ยืนยันด้วย `SELECT 1`/`nc`/`ping` ไป `10.10.4.11` ก่อนรัน และตรวจ `dotnet ef migrations list`
  หลังรันว่าไม่มี `(Pending)` เหลือ — ระหว่างตรวจพบว่า migration ของเมื่อวาน `AddJobAppointmentAt`/
  `AddQuotationTemplate` ก็ถูกรันจริงไปแล้วเช่นกัน ไม่ทราบว่าใครรัน — แก้ข้อความที่บันทึกผิดไว้เมื่อวานแล้ว)
  · **วันนัดหมายห้ามน้อยกว่าวันเวลาปัจจุบัน** (ยืนยันกับผู้ใช้ชัดเจน) — เดิม `ValidateAppointment` มี grace period
  24 ชม.ให้เลือกอดีตได้ (`[ASSUME]` ที่ยังไม่ยืนยัน) ตอนนี้เปลี่ยนเป็น tolerance 5 นาทีเท่านั้น (กันเวลา
  client/server คลาดกันตอนเลือก "ตอนนี้เลย" พอดี ไม่ใช่ grace ให้เลือกอดีตจริงๆ) ใช้กับทั้งตอนเปิดจ๊อบและตอนเลื่อนนัด
  · ฝั่งเว็บ: ช่องเลือกวันนัดหมายทั้ง 2 จุด (`CreateJobModal` ใน `JobsPage.tsx`, `RescheduleAppointmentModal` ใน
  `JobCardModal.tsx`) ใส่ `min={nowLocalInputValue()}` กันเลือกอดีตตั้งแต่ตอนกรอกในเบราว์เซอร์เอง (เพิ่ม
  `nowLocalInputValue()` ใหม่ใน `web/src/lib/format.ts`) — ฝั่ง backend ยังบังคับซ้ำเสมอ (ไม่ได้เชื่อ client ล้วน)
  · **แถวนัดหมายในการ์ดจ๊อบยังแสดงต่อแม้แปลงประเภทไปแล้ว** (เงื่อนไขเปลี่ยนจาก `jobTypeId===10` เป็น
  `jobTypeId===10 || appointmentAt` เพื่อคงประวัติว่าเดิมนัดวันไหน) แต่ปุ่ม "แก้ไข/เลื่อนนัด" ซ่อนไปหลังแปลงแล้ว
  (เลื่อนนัดของรถที่เข้าอู่แล้วไม่มีความหมาย) เพิ่มแถวใหม่ "วันเวลาที่รถเข้าอู่จริง" แสดงเมื่อมี `actualArrivalAt`
  · ทดสอบแล้ว: `dotnet build`/`dotnet test` ผ่านทั้ง 216 (เพิ่ม `ConvertToInShopAsync` 4 test +
  `CreateAsync_fails_when_appointment_is_even_slightly_in_the_past`/`_accepts_an_appointment_at_the_current_moment`
  ใน `JobServiceTests.cs`) · Web `tsc -b --force`/`vite build` ผ่าน
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริงและยังไม่ได้รัน migration กับ ServiceDb จริง**

- ✅ **[เพิ่ม 2026-09-14] แอป Flutter — ยกโมดูลจากเว็บมาลงมือถือทั้งชุด (งานใหญ่ที่สุดของ `mobile/` ตั้งแต่ commit แรก)**
  `mobile/` ไม่เคยถูกแก้เลยตั้งแต่ `2e6fc5b` — มีแค่ 11 ไฟล์ (login → เลือกสาขา/กะ → คิวใบเสนอราคา → อนุมัติ → เซ็น)
  ขณะที่เว็บเพิ่มโมดูลมา 8 รอบ ทำให้ `JobStateMachine.cs` ต้องเปิด `[ASSUME]` ถึง 5 จุดให้เว็บกดยืนยันแทนช่าง/ลูกค้า
  รอบนี้ปิดช่องว่างนั้นตามบทบาทใน design (ใบเสนอราคา/จัดซื้อ/คลัง/ข้อมูลหลัก **ยังคงอยู่บนเว็บ ไม่ได้ยกมา**)
  · **ตัดสินใจไว้ล่วงหน้ากับผู้ใช้**: online-only (ยังไม่ทำ offline queue/Drift/`TMP-` เพราะ OQ#3 ยังไม่มีคำตอบและ
  backend ยังไม่มี `/sync/batch`) · ใช้ `IntakeChecklist` 20 รายการ + `QcChecklist` ที่มีอยู่ **ไม่สร้าง** Inspection
  31 รายการหรือ `RepairTask`/จับเวลา/`PartsRequest` ใหม่ · เปิดสิทธิ์ให้มือถือส่งมอบ+ปิดงานได้ · รับชำระเงินบนมือถือได้
  ตาม role ที่ backend อนุญาตอยู่แล้ว

  **แก้ backend 2 จุด (ทั้งคู่เป็นการเปิดสิทธิ์ ไม่ใช่การผ่อน guard):**
  · `JobStateMachine.cs` `Ready→Completed` เพิ่ม `EventSource.Mobile` — **ไม่ได้ลดการตรวจสอบ** เพราะ guard
  `BalanceSettled|DocumentIssued|VehicleHandedOver` เป็น `isComputable = true` ใน `JobService.ComputeGuardAsync`
  อยู่แล้ว คำนวณจาก `Payment`/`Receipt`/`HandoverRecord` จริงเสมอ และ manual-override ด้วย `reason` ไม่ได้
  ต่างจาก 5 transition `[ASSUME]` เดิม
  · `HandoverService.cs:151` เพิ่ม `UserRole.FrontDesk` — `docs/01-workflow.md` §4 ระบุ "ส่งมอบรถ" เป็นหน้าที่ของ
  `frontdesk` ตรงๆ ที่เดิมจำกัดแค่ 3 role เพราะหน้าส่งมอบมีแต่บนเว็บซึ่งหน้าร้านไม่ได้ใช้ ไม่ใช่เพราะนโยบาย
  · **ไม่แตะ `PosService`** (คงที่ Cashier/Office/Manager) และ **ไม่ตัด `[ASSUME]` Web/Office/Manager ออกจาก 5
  transition เดิม** — ต้องรอให้แอปถึงมือผู้ใช้จริงก่อน ไม่งั้นเว็บใช้งานไม่ได้ทันทีที่ deploy backend ใหม่

  **โครงสร้างฝั่งแอป (P0):**
  · เปิดใช้ `go_router` ที่ประกาศไว้แต่ไม่เคยถูกใช้เลย (`grep GoRouter lib/` เคยได้ 0 ผลลัพธ์) — `lib/app/router.dart`
  ใช้ `StatefulShellRoute.indexedStack` 5 branch + `redirect` ที่อ่าน `sessionProvider`/`pendingLoginProvider`
  แทน `AuthGate` เดิม (ลบแล้ว) · `pendingLoginProvider` ใหม่ถือ token ขั้นแรกระหว่างเลือกสาขา/กะ แทนการส่ง
  `LoginResult` ผ่าน constructor (เดิม `BranchShiftPage` สร้าง `GarageProApi` ของตัวเองขึ้นมาใหม่)
  · แตก `GarageProApi` (god-class 9 เมธอด) เป็น `lib/api/api_client.dart` (Dio + interceptor + `unwrap`/`unwrapBytes`
  + `ApiException`) แล้วให้ `{auth,jobs,job_chat,attachments,staffs,quotations,qc,intake,pos,handover,reports,customers}_api.dart`
  ถือ `ApiClient` ตัวเดียวกัน — **ตรรกะแกะ envelope และข้อความ `NETWORK_ERROR` ภาษาไทยคงเดิมทุกบรรทัด**
  · **`X-Client-Source: mobile` ยกมาครบ** — ค่านี้ไม่ใช่ JWT claim แต่เป็นตัวตัดสิน `EventSource` ใน `JobStateMachine`
  ถ้าหายไปจะถูกนับเป็น Web แล้ว `InProgress→WaitParts` กับ `Qc→InProgress` จะพังด้วย `JOB_TRANSITION_FORBIDDEN_SOURCE`
  · จัดการ `AUTH_REQUIRED`/`AUTH_SHIFT_REQUIRED` แบบรวมศูนย์ที่ `unwrap` (เดิม `isUnauthorized`/`requiresShift`
  เป็น dead code ไม่มีอะไรเรียก) — ล้างเซสชัน + `popUntil` หน้าที่ push แบบ imperative ออกก่อน (ไม่งั้นหน้าเซ็น
  ลายเซ็นจะค้างทับหน้า login) + debounce ไม่ให้ 401 หลายคำขอพร้อมกันสั่งเด้งซ้ำ
  · `lib/core/roles.dart` (`AppRole` + capability ที่สะท้อนกฎ backend) · `lib/core/job_transitions.dart`
  (กระจกเงาของ `JobStateMachine.cs` ใช้ตัดสินแค่ว่าจะ *แสดง* ปุ่มอะไร server ยังเป็นผู้ตัดสินเสมอ)

  **หน้าจอที่เพิ่ม:** คิวงาน keyset infinite scroll + ค้นหา + กรองประเภท/สถานะ · การ์ดจ๊อบ (แถบขั้นตอน 6 ขั้น
  เลื่อนแนวนอน + `StickyActionBar` การกระทำถัดไป + แกลเลอรีไฟล์แนบ) · แชทในจ๊อบ (หน้าเต็ม ไม่ใช่ widget ลอยแบบเว็บ
  เพราะคีย์บอร์ดกินครึ่งจอ) · รับรถ/เปิดจ๊อบ (ค้นหา+สร้างลูกค้า → เลือก/เพิ่มรถ → เปิดจ๊อบ) · เช็คลิสต์สภาพรถ 20 รายการ
  + รูป · QC + ทดลองขับ · ชำระเงิน+ใบเสร็จ · ส่งมอบรถ+ลายเซ็น · รายงาน 4 แท็บ (อ่านอย่างเดียว) · โปรไฟล์/ปิดกะ
  · **แชท poll เฉพาะข้อความใหม่** (cursor `afterAt`/`afterId` ที่เว็บไม่เคยใช้) แทนการดึงทุกหน้าซ้ำทุก 5 วิแบบเว็บ
  และหยุด poll เมื่อแอปเข้า background (`AppLifecycleListener`) — เว็บดึงทั้งประวัติซ้ำทุก 5 วิซึ่งกินเน็ตมือถือฟรีๆ
  · `mention_token.dart` พอร์ตจาก `mentionToken.ts` แบบตรงตัว (render ด้วย `TextSpan` ไม่ใช้ package html/markdown)
  — **สองไฟล์นี้ต้องให้ผลตรงกันเสมอ** ไม่งั้นอีกฝั่งจะเห็นข้อความดิบ `@[41:สมชาย]` มีเทสต์ครอบไว้แล้ว
  · **`AuthImage`** — `/attachments/file` และ `/vehicles/{id}/image` ต้องมี Bearer เสมอ ห้ามใช้ `Image.network`
  (บทเรียนเดียวกับที่เว็บเพิ่งเจ็บ) โหลดเป็นไบต์ + แคช LRU จำกัดด้วย**ขนาดรวม 32 MB ไม่ใช่จำนวนรายการ** +
  `cacheWidth` ตามพื้นที่จริง (รูป 1024px decode เต็มขนาดกิน ~4 MB/ใบ ในกริดจะทำให้แอปตายบนเครื่องเล็ก)
  · **รับชำระเงินกันเก็บซ้ำจริง** — `newRequestId()` (UUID v4) สร้างครั้งเดียวต่อคำขอ ถ้าเจอ `NETWORK_ERROR`
  จะ**เก็บคำขอเดิมไว้พร้อม RequestId เดิม**แล้วให้กด "ลองใหม่" เป็นการยืนยันคำขอเดิม ไม่ใช่เก็บเงินรอบใหม่
  (`docs/01-workflow.md` §5 ข้อ 6: สถานะที่ไม่ทราบผลต้องไม่แสดงว่าล้มเหลว และห้ามจ่ายซ้ำ)

  **สิ่งแวดล้อม/แพ็กเกจที่เพิ่ม:**
  · **`android/app/src/main/AndroidManifest.xml` ไม่มี `INTERNET` เลย** — มีแต่ใน manifest ของ debug/profile
  แปลว่า **build release ทุกตัวจะยิง HTTP ไม่ได้เลยแบบไม่มีอะไรฟ้อง** (แก้แล้ว) + `uses-feature camera required=false`
  · `ios/Runner/Info.plist` เพิ่ม `NSCameraUsageDescription`/`NSPhotoLibraryUsageDescription` (ไม่มีแล้วแอป**crash ทันที**
  ที่เปิดกล้อง) · เพิ่ม `image_picker` (เลือกแทน `camera` เพราะไม่ต้องจัดการ focus/EXIF/lifecycle เอง และไม่ต้องประกาศ
  permission `CAMERA` ซึ่งถ้าประกาศจะกลายเป็นต้องขอ runtime grant ที่ไม่ประกาศแล้วไม่ต้องขอ)
  · **bundle ฟอนต์จริงแล้ว** — `T.fontTh`/`T.fontMono` ชี้ไปฟอนต์ที่ไม่เคยมีใน `pubspec.yaml` มาก่อน จึง fallback เงียบๆ
  ทำให้ `FontFeature.tabularFigures()` ของ `T.money` **ไม่มีผลจริง** (ตัวเลขเงินไม่เรียงหลัก ขัดกฎ UI)
  ตอนนี้มี `assets/fonts/` (NotoSansThai variable + IBMPlexMono 400/600, SIL OFL ลงทะเบียน license ใน `main.dart`)
  · **แก้สี token ที่ web/mobile ไม่ตรงกัน 2 ค่า** โดยยึดค่าฝั่งเว็บ: `pageBg` `#F1F4F9`→`#EEF1F6` ·
  `faint` `#8FA6C4`→`#94A3B8` + เพิ่ม `faintOnDark` สำหรับหน้า login พื้น navy (ค่าเดิมอ่านไม่ออกบนพื้นเข้ม)
  · เพิ่ม `JobStatusStyle` ครบ 10 สถานะจ๊อบ (เดิม `StatusStyle` มีแค่ 7 สถานะใบเสนอราคา) แยกคนละคลาสโดยตั้งใจ
  เพราะ token `approved` มีอยู่ทั้งสองโดเมน ถ้ารวมกันการพิมพ์ผิดจะ resolve ข้ามโดเมนแบบเงียบๆ

  **ทดสอบแล้ว:** `dotnet build` ผ่าน (0 error) · `dotnet test` ผ่าน **168 / skip 4** (SQL/FTP integration ตามเดิม)
  เพิ่ม 5 เทสต์: ปิดงานจากมือถือได้เมื่อ guard ครบ · ยังถูกปฏิเสธด้วย `JOB_GUARD_NOT_SATISFIED` เมื่อยังไม่เซ็นรับรถ ·
  FrontDesk เข้าหน้าส่งมอบได้ · Technician/Lead ยังถูกปฏิเสธ
  · Mobile `dart analyze lib/ test/` **สะอาด 0 issue** · `flutter test` ผ่าน **26** (mention token 11 ·
  job transitions/roles 9 · job status map 4 · money format 1 · smoke test ที่ pump ทั้งแอปจริง 1)
  · **`flutter build ios --no-codesign` ผ่าน** — พิสูจน์ว่า plugin/ฟอนต์/Info.plist ประกอบได้จริง ไม่ใช่แค่ analyze ผ่าน
  · `mobile/test/widget_test.dart` เดิมเป็น template ที่อ้าง `MyApp` ซึ่งไม่มีอยู่จริง — **ทั้ง suite compile ไม่ผ่าน**
  มาตลอด (`flutter test` จึงไม่เคยรันอะไรเลย) แก้เป็น smoke test จริงแล้ว
  · **ยังไม่ได้ทดสอบกับ API จริงเลยสักครั้ง** — sandbox ไม่ได้ต่อ Garage Pro VPN ทุกอย่างเป็นผล analyze/test/build
  เท่านั้น ต้องเดิน 13 ขั้นของ Demo บนเครื่องจริง 2 เครื่องก่อนใช้งานจริง และ **ต้องรัน migration ที่ค้างอยู่**
  (`AddQcChecklist`, `AddPaymentAndHandover`, `AddJobVatIncluded`, `AddJobChat`, `AddStockWithdrawal…`) ที่ยังไม่เคย
  `dotnet ef database update` กับ ServiceDb จริง — มือถือเรียก endpoint เหล่านี้ทั้งหมด

  > **[RISK — พบระหว่างทำงานนี้ ไม่ได้แก้] สีสถานะจ๊อบของเว็บไม่ตรงกับ prototype ที่อนุมัติแล้ว 8 จาก 10 token:**
  > `web/src/index.css:927-936` (ของจริงที่ render) ต่างจากตาราง "Status color map" ใน `docs/01-workflow.md` §8
  > ทุก token ยกเว้น `approved` และ `cancelled` — และที่หนักกว่านั้นคือ `waitapprove` กับ `inprogress` ใช้สีเดียวกันเป๊ะ
  > (`#fdf1e6`/`#c1691f`) ซึ่งขัดกฎ "ทุกสถานะต้องแยกแยะได้" ฝั่งมือถือยึดค่าตาม docs §8 (prototype ที่อนุมัติแล้ว)
  > **ไม่ได้แก้ CSS ของเว็บเพราะเป็นการเปลี่ยนสิ่งที่ผู้ใช้เห็นทุกวันและอยู่นอกขอบเขตที่ตกลง** — ต้องเลือกว่าจะยึดฝั่งไหน
  > แล้วแก้ให้ตรงกันทั้งสองที่ ถ้ายึด docs ให้แก้ `index.css` ถ้ายึดเว็บให้แก้ `tokens.dart` + docs พร้อมกัน
  > นอกจากนี้ `completed` (ขาวบนเขียว `#0E9F8C`) ได้ contrast ~2.9:1 ต่ำกว่า WCAG AA — คงค่าตาม prototype ไว้ก่อน
  >
  > **[RISK] `web/src/lib/tokens.ts` ไม่ถูก import จากที่ไหนเลย** (`grep` ได้ 0 ผลลัพธ์) — ของจริงคือ `:root` ใน
  > `index.css` ทำให้กฎใน CLAUDE.md ที่ว่า "token อยู่ทั้ง `tokens.dart` และ `tokens.ts` ต้องแก้พร้อมกัน" ไม่เป็นจริง
  > ตอนนี้ต้องแก้ **3 ที่** ควรตัดสินใจว่าจะ gen `index.css` จาก `tokens.ts` หรือลบไฟล์ตายนี้ทิ้ง
  >
  > **[RISK] `Job.AssignedTechnicianId` เป็นคอลัมน์ตาย** — มีใน entity แต่ **ไม่เคยถูกเขียนที่ไหนเลย** และไม่อยู่ใน
  > `JobDto` ช่างที่ผูกกับงานจริงอยู่ที่ `QuotationLine.AssignedTechnicianId` ทำให้หน้า "งานของฉัน" (`/jobs/mine`
  > ใน docs §6) **ทำไม่ได้จริง** — แอปจึงให้ช่างใช้คิวของสาขากรองด้วยสถานะแทน และตั้งชื่อแท็บว่า "งานที่ต้องทำ"
  > ไม่ใช่ "งานของฉัน" เพื่อไม่ให้สื่อเกินจริง · ถ้าต้องการของจริงต้องเพิ่มตัวกรอง assignee ใน `GET /jobs/search`
  >
  > **[RISK] `GET /jobs/counts` แยกตามสถานะไม่มี** — มีแค่ `count-open?jobTypeId` ที่คืนเลขเดียว หน้าหลักจึงแสดง
  > ได้แค่ 2 ตัวเลข (รถในอู่/รถนัดหมาย) ไม่ใช่ 5 ตัวตาม docs §6 และเขียนบอกผู้ใช้ตรงๆ แทนการเดาตัวเลข

- ✅ **[เพิ่ม 2026-09-17] บังคับลำดับ "จ่ายเงิน → ออกใบเสร็จ → เซ็นรับรถ → ปิดงาน" + ให้แอปจบงานได้ที่ข้างรถ**
  (กฎข้อ 18-19 ด้านบน) — คำขอผู้ใช้: *"ถ้าออกใบเสร็จแล้ว แอพจะสามารถให้ลูกค้าเซ็นและปิดงานได้"*
  · **ช่องโหว่ที่ปิด**: `HandoverService.SubmitAsync` เดิมเช็คแค่ 4 อย่าง (จ๊อบมีจริง/มีลายเซ็น/ยังไม่ล็อก/ติ๊กครบ)
  **ไม่แตะเรื่องเงินเลยสักบรรทัด** (service ตัวนี้ไม่เคย inject `IPosRepository` มาก่อนด้วยซ้ำ) — เงินถูกตรวจ
  พร้อมกันทั้งสามข้อ *ตอนปิดงาน* เท่านั้น จึงเซ็นรับรถก่อนจ่ายเงินได้จริง แล้วรถออกไปโดยงานค้างปิดไม่ได้
  · **แก้ backend 3 จุด**: `HandoverService` inject `IPosRepository` + ตรวจ `Receipt` ก่อน submit
  (`HANDOVER_RECEIPT_REQUIRED`) · `HandoverDto`/`HandoverMapper` เพิ่ม `ReceiptIssued`/`ReceiptDocumentNo`
  (mapper เปลี่ยน signature เป็น `ToDto(record, receipt)`) · `JobStateMachine` `Ready→Completed` เปิดครบ 6 role
  · **มือถือ**: `AppRole.canHandOverVehicle` เปลี่ยนจาก "Cashier/Office/Manager/FrontDesk" เป็นทุก role ที่รู้จัก
  + เพิ่ม `canCloseJob` · `job_transitions.dart` (กระจกเงาของ state machine) อัปเดต role ของ `ready→completed`
  · `handover_page.dart` มี InfoBanner บอกสถานะใบเสร็จตั้งแต่บนสุด (ไม่ให้ติ๊กครบ 5 ข้อแล้วค่อยรู้ว่ายังเก็บเงินไม่เสร็จ)
  และ `disabledReason` เรียงลำดับตรงกับ `SubmitAsync` โดยเอาใบเสร็จขึ้นก่อน เพราะเป็นข้อเดียวที่คนถือแอปแก้เองไม่ได้
  · **เซ็นเสร็จแล้วถามต่อทันทีว่า "ปิดงานเลยไหม"** (`_offerToCloseJob`) — ไม่ปิดให้เองเงียบๆ และถ้าปิดไม่สำเร็จ
  จะบอกตรงๆ ว่าส่งมอบบันทึกไปแล้วแต่ยังต้องปิดงานอีกที (ไม่มีการ rollback การส่งมอบ)
  · **เว็บ**: `handoverBlockedReason` ใหม่แทนเงื่อนไข disabled เดิมที่กระจายอยู่ใน `title` — แสดงเป็นข้อความจริง
  เหนือปุ่ม ไม่ใช่ tooltip ที่ต้องเอาเมาส์ไปชี้ (กฎ UI "ปุ่มที่ปิดใช้งานต้องบอกเหตุผลเสมอ")
  · **ไม่ได้แตะ `PosService`** — ช่างยังรับเงิน/ออกใบเสร็จเองไม่ได้ · ไม่มี migration (ใช้ตาราง `Receipt` เดิม)
  · ทดสอบแล้ว: `dotnet build` ผ่าน 0 error · `dotnet test` ผ่าน **231 / skipped 4** (เดิม 225 — เพิ่ม 6:
  `HandoverServiceTests` ปฏิเสธเมื่อยังไม่มีใบเสร็จ, รายงานสถานะใบเสร็จใน DTO, ช่าง/หัวหน้าช่างส่งมอบได้เมื่อมีใบเสร็จ
  (Theory 2 เคส) · `JobServiceTests` ทุกบทบาทปิดงานได้เมื่อ guard ครบ (Theory 3 เคส), ช่างยังปิดไม่ได้ถ้ายังไม่เซ็นรับรถ)
  · Mobile `dart analyze lib/ test/` สะอาด 0 issue · `flutter test` ผ่าน **34** · Web `tsc -b --force`/`vite build` ผ่าน
  · **ยังไม่ได้ทดสอบ end-to-end กับข้อมูลจริง** — ยังไม่ได้เดินจริงว่า: กดยืนยันส่งมอบก่อนออกใบเสร็จแล้วโดนปฏิเสธจริง,
  ออกใบเสร็จแล้วปุ่มเปิดใช้งานได้จริง, ช่างล็อกอินบนแอปแล้วเซ็น+ปิดงานได้จริง (ต้องมีบัญชีช่างและจ๊อบที่สถานะ `ready`)
  · restart API แล้วหลังแก้ (`dotnet run` ไม่ hot-reload service/state machine — เคยทำให้เข้าใจผิดว่าโค้ดไม่ทำงานมาแล้ว)
  · **[แก้เพิ่มจากการใช้งานจริง] คิวงานมือถือซ่อนตัวกรองสถานะที่ใช้อยู่**: `_FilterChips` ใน `job_list_page.dart`
  วางชิปประเภทกับชิปสถานะไว้ใน `ListView` แนวนอนอันเดียวกัน ชิปสถานะจึงอยู่พ้นขอบจอไปทางขวาเสมอ — เมื่อหน้าหลัก
  ตั้งตัวกรองสถานะให้ผ่าน `applyFilter` (แตะการ์ด "งานค้างแยกตามสถานะ") ผู้ใช้จะเห็นแค่ชิปประเภทที่ยังเป็น
  "ทุกประเภท" แล้วสรุปว่าไม่ได้กรองอะไร ทั้งที่รายการถูกกรองจริง (ผู้ใช้ถามตรงๆ ว่า "งานทุกประเภท ทำไมมีเท่านี้")
  · **[ปรับหน้าโปรไฟล์ตามคำขอ "ให้สวยๆ เป็นระเบียบ"]** `profile_page.dart` — เดิม `_row` ใช้ `Spacer()` + ค่าชิดขวา
  ทำให้ค่ายาวอย่าง "Service Center Demo" ตัดเป็นสองบรรทัดชิดขวา ขอบซ้ายหยักไม่เท่ากัน · เปลี่ยนเป็นคอลัมน์
  ป้ายกำกับกว้างเท่ากันทุกแถว + ค่าชิดซ้าย (แยกเป็น `_InfoRow` ที่คูณความกว้างตาม `MediaQuery.textScalerOf`
  ไม่ตรึง 76 ไว้เฉยๆ เพราะต้องรองรับตัวอักษร 200%) · การ์ดคั่นแถวด้วย `Divider` แทนที่จะให้แต่ละแถวใส่ padding
  ล่างเอง (ของเดิมแถวสุดท้ายมีช่องว่างเกินก้นการ์ดเสมอ) · ย้าย "บทบาท" จากแถวในตารางขึ้นเป็นชิปใต้ชื่อ (เดิมซ้ำกับ
  หัวการ์ดที่แสดง roleLabelTh อยู่แล้ว) · เพิ่มหัวข้อกลุ่ม "กะที่เปิดอยู่"/"จบการทำงาน" · ปุ่มออกจากระบบเดิมเป็น
  `TextButton` สีเทาลอยๆ ที่ดูไม่ออกว่ากดได้ และคำว่า "ออกจากระบบอย่างเดียว" กำกวม → เป็นปุ่มเต็มความกว้างสีแดง
  พร้อมไอคอน ข้อความเปลี่ยนตามสิทธิ์ ("ออกจากระบบ" / "ออกจากระบบโดยไม่ปิดกะ") · **ไม่ได้เปลี่ยนพฤติกรรมใดๆ**
  (ยังไม่มี dialog ยืนยันก่อนปิดกะ — เป็นการกระทำที่ย้อนไม่ได้จากแอป ควรพิจารณาเพิ่มแยก)
  — เพิ่ม `_ActiveStatusBar` ใต้แถบชิป แสดง `กรองเฉพาะสถานะ "<label>"` + ปุ่ม "ล้าง" เฉพาะตอน `filter.status != null`
  (ข้อความไทยอ่านจาก `jobStatusOptionsProvider` ซึ่งเป็นของ server แหล่งเดียวกับชิป) และ `PreferredSize` เปลี่ยนเป็น
  ความสูงแบบมีเงื่อนไข 108/142 เพราะ `PreferredSize` ไม่ยืดตามลูก · ข้อความ empty state บอกชื่อตัวกรองที่ค้างแทน
  คำว่า "ตัวกรองที่เลือก" ลอยๆ · ทดสอบจริงบน simulator แล้ว: ตั้งตัวกรองจากหน้าหลัก → เห็นแถบ → กด "ล้าง" → งานขึ้นครบ 7 รายการ

### ยังไม่ได้ทำ
- รับรถ **6 ขั้นเต็มรูปแบบ**บนมือถือ (ยืนยันนัดหมาย/รูป 5 มุมบังคับ/QR ติดรถ — มือถือทำได้แล้วแบบย่อ: ค้นหา/สร้าง
  ลูกค้า+รถ → เปิดจ๊อบ → เช็คลิสต์ 20 รายการ + รูป) · ตรวจเช็ค 31 รายการ 8 หมวดของช่าง
  (spec เดิม — คนละอันกับ checklist 20 รายการที่ทำแล้วบนเว็บ) · `RepairTask`/รูปก่อน-หลังบังคับต่อรายการซ่อม
  (guard `AllTasksDoneWithPhotos` ของ `InProgress→Qc` ยังเป็น manual-override เพราะยังไม่มี entity นี้) ·
  เช็คลิสต์ QC ตายตัว 6 ข้อ + ทดลองขับตามระยะทางจริงของ docs/01-workflow.md §3.6 (ตอนนี้ QC เช็คลิสต์อิงบรรทัด
  ที่อนุมัติในใบเสนอราคาแทนตามคำขอผู้ใช้ 2026-09-09 — ดูหัวข้อ Job ด้านบน) · QC ตีกลับส่งช่างแก้ไขจากเว็บ
  (ตัดสินใจแล้วว่า**ไม่ทำ** — ไม่มี process ตีกลับในระบบตามคำขอผู้ใช้ 2026-09-09, `Qc→InProgress` ยังจำกัดแค่
  Technician/Mobile ในโค้ดเหมือนเดิม — **แต่มือถือทำได้แล้ว** เพราะ transition นี้เปิดให้ `EventSource.Mobile` อยู่แล้ว)
- ส่วนต่อยอดคลัง/จัดซื้อ: VAT/ส่วนลด/ค่าขนส่ง/เจ้าหนี้ · พิมพ์ PO · ส่งคำสั่งซื้อไปภายนอก · แบ่ง PR เป็นหลาย PO
  · โอนคลัง/คืนผู้ขาย/ตรวจนับปรับยอด · แนบรูป GRN · จอง/เบิกผูก job หรือ quotation อัตโนมัติ
- เคลียร์ข้อขัดแย้งการเขียน legacy ของโมดูลลูกค้า/รถ/พนักงานให้ตรงนโยบายสถาปัตยกรรม
- การเรียงตารางแบบ server-side ครอบคลุมทุกหน้า (ปัจจุบันเรียงเฉพาะหน้าหรือข้อมูลที่โหลดแล้ว)
- RBAC ของ Job/Intake endpoint (ตอนนี้ gate ด้วย shift session เท่านั้น — ดู `[RISK]` ในหัวข้อกฎที่ห้ามละเมิด)
- Offline queue ของ Flutter (`TMP-` + conflict) · หน้า `/sync` — **งานใหญ่ อย่าประเมินต่ำ** (ตกลงกันแล้วว่ารอบนี้ online-only)
- มือถือ: สแกน QR (`/jobs/by-qr/{code}` ยังไม่มี endpoint) · push notification · พิมพ์เอกสาร A4 (ต้องเพิ่ม `printing`+`pdf`)
- Realtime (SignalR) · refresh token · หน้าตั้งค่า
- สร้าง/แก้นัดหมายด้วยการคลิกช่องว่างในปฏิทิน (ตอนนี้แก้ได้จากการ์ดจ๊อบเท่านั้น) · จัดลำดับบรรทัดในเทมเพลต
  ใบเสนอราคาแบบลาก (v1 = เรียงตามลำดับที่เพิ่ม) · `RowVersion` ที่ header เทมเพลต (ตอนนี้ last-write-wins)
- ปฏิทินนัดหมาย/เทมเพลตใบเสนอราคาบนมือถือ (รอบนี้ทำเฉพาะเว็บ — แอป Flutter ยังไม่มีทั้งสองอย่าง)
- **รายงานประเมินประสิทธิภาพช่าง** — ออกแบบครบแล้วที่ [docs/09](docs/09-technician-time-tracking.md) §5/§8
  แต่ **ยังไม่ได้เขียนโค้ด** (เมตริก 8 ตัว · เว็บ 3 หน้า · หน้า "ผลงานของฉัน" บนมือถือ)
  ตั้งใจเลื่อนไปทำด้วยข้อมูลจริงหลังเก็บเวลาไปสัก 2-4 สัปดาห์ ตามที่ผู้ใช้เลือก — §5.4 อธิบายว่าตั้งเป้า
  ก่อนมีข้อมูลคือตั้งมั่ว และยังไม่รู้ว่าในอู่จริง "ช่างรุมคันเดียว" เกิดบ่อยแค่ไหน ซึ่งเปลี่ยนรูปรายงานทั้งหน้า

### Open questions ที่ยัง block อยู่
ดู [docs/01-workflow.md §10](docs/01-workflow.md) — ที่สำคัญที่สุดคือ **#3 นโยบาย offline conflict**
(ยังไม่ block เพราะยังไม่ทำ offline)

---

## แนวทางการทำงาน

- **อ่าน CLAUDE.md ก่อนเริ่มแก้เสมอ** แล้วอ่านเอกสารที่เกี่ยวข้องตามตารางด้านบน
- **หลังทำงานให้อัปเดต CLAUDE.md** ในหัวข้อที่เกี่ยวข้อง: สิ่งที่ทำจริง กฎที่เปลี่ยน ไฟล์/เอกสารอ้างอิง
  ผลตรวจที่รันจริง และงานค้าง/ข้อจำกัด อย่าเขียนว่าเสร็จทั้งระบบจากผลทดสอบเฉพาะส่วน
- หากพบโค้ดขัดกับนโยบาย ให้บันทึก `[RISK]` และขอข้อยืนยันก่อนเปลี่ยนนโยบาย ไม่เขียนเอกสารย้อนหลังเพื่ออนุญาตเอง
- **ตรวจ schema จริงก่อนเขียน SQL เสมอ** — ชื่อคอลัมน์ใน legacy ไม่ตรงกับที่เดา
  (`Car.CarNumber` = ทะเบียน · `Car.Chassis` = เลขตัวถัง · `Customer`/`Staff` ใช้ `FirstName`+`LastName`)
- เขียน test ให้กฎธุรกิจก่อนต่อ UI — `QuotationCalculatorTests` ล็อกตัวเลขไว้ตรงกับ Demo prototype
  (8,838.20 และ 5,628.20) ถ้า test นี้แดง เอกสารที่ออกจากระบบจะไม่ตรงกับที่ออกแบบ
- comment อธิบาย **ทำไม** ไม่ใช่ **อะไร** · ใส่ `[BIZ]` `[UI]` `[ASSUME]` `[SECURITY]` `[RISK]` ให้ตรงกับเอกสาร
- เจอค่าที่ต้องเดา → ทำเป็น config/override table อย่า hard-code (ดู `svc_UserRoleOverride`)

### ผลตรวจงานล่าสุด — 2026-09-04

- จัดซื้อ: unit/regression ที่เกี่ยวข้อง 19 ผ่าน + SQL workflow integration 1 ผ่าน (เป็นผลของรอบทำโมดูลจัดซื้อ)
- จำกัดสาขา: StaffService/StaffValidator/CustomerVehicleValidator รวม 19 ผ่าน + BranchScopeSqlTests 1 ผ่าน
  · SQL scope test ใช้ตารางชั่วคราวเฉพาะ connection ไม่แก้ข้อมูลลูกค้าหรือพนักงานจริง
  · ตั้ง `GARAGEPRO_BRANCH_SQL_CONNECTION` แล้วรัน `dotnet test backend/AMD.AutoService.GaragePro.Tests --filter FullyQualifiedName~BranchScopeSqlTests`
- ตาราง: `node --test tests/tableSort.test.mjs` ผ่าน 3 tests (ตัวเลข/ภาษาไทย/รหัส, ขึ้นลงและยกเลิก, คงแม่–ลูก)
  · ตรวจคลิก/คีย์บอร์ดและ computed font sizes ใน browser ด้วยข้อมูลสมมติของ component จริงแล้ว
  · หน้า QA ชั่วคราวถูกนำออกหลังทดสอบ ไม่มีการเรียก API หรือบันทึกข้อมูลธุรกิจจาก fixture นี้
- Backend build, Web TypeScript `tsc -b`, Vite production build และ `git diff --check` ผ่านในรอบที่เกี่ยวข้อง
  · ยังมี warning ImageSharp NU1902 และ Vite bundle ใหญ่กว่า 500 kB ไม่ได้แก้ในงานนี้
- ยังไม่ได้ทดสอบหน้าตารางทั้งหมดแบบ end-to-end ด้วยบัญชีที่ล็อกอิน; browser ที่ใช้ตรวจอยู่หน้า login
- จำนวนผลทดสอบข้างต้นเป็นคนละรอบ/อาจมี regression ซ้ำกัน ไม่ใช่ผล full-suite ครั้งเดียว
- เพิ่มรูปในตาราง staffs: ตรวจ TypeScript `tsc -b` ผ่าน; ยังไม่ได้ยืนยันรูปจริงด้วยบัญชีที่ล็อกอิน

### ผลตรวจงานล่าสุด — 2026-09-08

- ปุ่ม "ยืนยันลูกค้าอนุมัติ (ชั่วคราว)": `dotnet build AMD.AutoService.GaragePro.sln` ผ่าน (0 error) ·
  Web `tsc -b` ผ่าน (0 error) — ตรวจแค่ compile ทั้งสองฝั่ง **ยังไม่ได้รัน backend + login จริงในเบราว์เซอร์เพื่อกดปุ่ม
  ทดสอบ flow decide→sign→transition แบบ end-to-end** และยังไม่ได้รัน `dotnet test` ของ suite เดิมซ้ำในรอบนี้
- แก้ปุ่ม "อนุมัติซ่อม" + เพิ่มใบเบิกสินค้าผูก job (คำขอเดียวกัน 2026-09-08 รอบที่สอง):
  `dotnet build AMD.AutoService.GaragePro.sln` ผ่าน (0 error) ·
  `dotnet test backend/AMD.AutoService.GaragePro.Tests --filter FullyQualifiedName~PurchasingServiceTests`
  ผ่านทั้ง 9 (รวม `Withdrawal_issues_multiple_lines_under_one_document_and_requires_known_active_staff` ที่เพิ่มใหม่ —
  ครอบคลุมเบิกหลายบรรทัดในเอกสารเดียว, idempotent replay ด้วย RequestId เดิม, ปฏิเสธผู้เบิกที่ไม่พบ/ไม่ active)
  · Web `tsc -b` ผ่าน (0 error) และ `vite build` ผ่าน (bundle size warning เดิม ไม่เกี่ยวกับงานนี้)
  · **ยังไม่ได้รัน `dotnet ef database update` กับ ServiceDb จริง** (sandbox ไม่ได้ต่อ VPN) — ต้องรันก่อนใช้งานจริง
  · **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์** (สร้างใบเบิกจริงจาก JobCardModal, พิมพ์ใบเบิก, ตรวจสิทธิ์
  Office/Manager เทียบกับ role อื่น) — ตรวจแค่ build/unit test เท่านั้น
  · ไม่ได้รัน `dotnet test` แบบ full suite ซ้ำ (รันเฉพาะ filter PurchasingServiceTests ที่เกี่ยวข้องกับงานนี้)

### ผลตรวจงานล่าสุด — 2026-09-17 (merge `chao` → `main` + deploy production)

รวมงานของทีม (`origin/chao` commit `10cadb4`: ปฏิทินนัดหมาย · เทมเพลตใบเสนอราคา · รายการนอกแคตตาล็อก
`AppointmentAt`/`ActualArrivalAt`) เข้ากับ `main` ที่เพิ่งยกโมดูลลงแอป Flutter (`a4b67df`) — merge commit ปกติ
(ไม่ rebase/squash เพื่อรักษาประวัติของทั้งสองสาย) จุดที่แตกกันคือ `5260959`

- **Conflict ที่ git จับได้มีจุดเดียว: `CLAUDE.md`** (ทั้งสองฝั่งแก้รายการ "ยังไม่ได้ทำ" คนละบรรทัด) แก้โดยรวมทั้งคู่
  แล้วตัด "หน้าส่งมอบรถ" ของฝั่ง chao ออกเพราะ `main` ทำเสร็จไปแล้วทั้งเว็บและมือถือ
- **[BUG] Semantic conflict ที่ git มองไม่เห็น — ต้องแก้เอง**: `main` เพิ่ม
  `IJobRepository.CountOpenByStatusAsync` (ใช้นับจ๊อบแยกสถานะให้หน้าหลักมือถือ) ส่วน chao เพิ่มไฟล์ใหม่
  `QuotationServiceTests.cs` ที่มี `FakeJobRepository` ของตัวเอง — ไฟล์ใหม่ล้วนจึงไม่ conflict แต่ fake ตัวนั้น
  implement interface ไม่ครบ ทำให้ `Build FAILED` (`CS0535`) เติมเมธอดที่ขาดตามสไตล์เดิมของไฟล์นั้น
  (`NotSupportedException`) ให้ตรงลำดับกับ interface — **บทเรียน: merge ที่ auto ผ่านหมดยังต้อง build จริงเสมอ**
  เพราะ interface ใหม่ฝั่งหนึ่ง + implementation ใหม่อีกฝั่งไม่เคยแตะไฟล์เดียวกันเลย
- **DTO ที่ chao แก้เป็น additive ล้วน** (`AppointmentAt`/`ActualArrivalAt` ใน `JobDto`, ฟิลด์ ad-hoc ใน
  `QuotationLine` request ที่มี default) ไม่มีการลบ/เปลี่ยนชื่อฟิลด์ — แอป Flutter ที่ `main` เพิ่งเพิ่มจึงไม่พัง
  (decode ตาม key ข้ามฟิลด์ใหม่ได้) และ merge นี้ **ไม่ได้แตะไฟล์ใน `mobile/` หรือ `deploy/` เลยสักไฟล์**
- **Migration ใหม่ 3 ตัวของ chao เป็น additive ล้วน**: `AddJobAppointmentAt` (คอลัมน์ nullable + filtered index),
  `AddQuotationTemplate` (2 ตารางใหม่), `AddJobActualArrivalAt` (คอลัมน์ nullable) — `DropTable` มีเฉพาะใน `Down()`
  ซึ่ง efbundle ไม่รัน · `main` ไม่ได้เพิ่ม migration เลยในรอบมือถือ จึงต่อท้ายได้สะอาด ไม่มีลำดับซ้อนกัน
- **ผลตรวจที่รันจริงรอบนี้ (หลัง merge)**: `dotnet build` ทั้ง solution ผ่าน 0 error · `dotnet test` ผ่าน **225
  / skipped 4** (SQL/FTP integration ที่ต้องมี environment ตามเดิม) · Web `tsc -b` ผ่าน · `vite build` ผ่าน ·
  `node --test tests/tableSort.test.mjs tests/calendarMonth.test.mjs` ผ่าน 10 (รวม 4 เทสต์ปฏิทินที่ chao เพิ่ม)
  · ไม่ได้รัน `flutter analyze`/`flutter test` ซ้ำเพราะ merge ไม่แตะ `mobile/` แม้แต่ไฟล์เดียว
- **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** — ฟีเจอร์ของ chao (ปฏิทิน, เทมเพลต, รายการนอกแคตตาล็อก)
  ตรวจได้แค่ระดับ build/unit test ในรอบ merge นี้ ไม่ได้เปิดใช้จริงทีละหน้า
