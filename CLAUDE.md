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
| [docs/01-workflow.md](docs/01-workflow.md) | state machine 10 สถานะ · 12 transition · business rules · **9 open questions** |
| [docs/02-domain-model.md](docs/02-domain-model.md) | entity + **15 invariant ที่ต้อง enforce ที่ API** |
| [docs/03-api-contract.md](docs/03-api-contract.md) | endpoint map ทุกหน้าจอ |
| [docs/04-project-plan.md](docs/04-project-plan.md) | แผน 10 phase |
| [docs/05-legacy-db-mapping.md](docs/05-legacy-db-mapping.md) | **สิ่งที่ reuse จาก Garage DB ได้/ไม่ได้ + ความเสี่ยง** |

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
pnpm tsc --noEmit && pnpm build

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

---

## สถาปัตยกรรมที่ต้องเข้าใจก่อนแก้โค้ด

### 1. Hybrid: อ่าน legacy / เขียนฐานใหม่

| | ที่ไหน | ทำอะไร |
|---|---|---|
| `Garage` (10.10.4.11) | legacy | **อ่านอย่างเดียว** — ลูกค้า รถ สาขา พนักงาน งาน (`PJCarPickUp`) ผู้ใช้ (`dbo.User`) |
| `GarageService` (10.10.4.11) | ของเรา | ตาราง `svc_*` ทั้งหมด |

**ห้าม write ลง Garage DB เด็ดขาด** — `PJCarPickUp` มี lock convoy อยู่แล้ว (lock wait 92–96% ของทั้งระบบ,
lock escalation 1.37 ล้านครั้ง, RCSI ปิด) ทุก query ที่ `LegacyReader`/`LegacyUserReader` ต้อง:
1. เลือกเฉพาะคอลัมน์ที่ใช้ — ห้าม `SELECT *` (EF6 เดิมอ่านแถวละ ~18 MB เพราะดึง LOB)
2. ใส่ `WITH (READUNCOMMITTED)`
3. ไม่มีคำสั่งเขียนใดๆ

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
ส่วนลด >10% ต้องผู้จัดการ · margin <15% เตือน · PO >10,000 ต้องผู้จัดการ · SLA 90% · คอมมิชชัน 8/10/12%
ตอนนี้เป็น const ใน `QuotationCalculator` — **ต้องย้ายไป config table ก่อน production**

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

### 🟡 iOS ATS บล็อก HTTP
`mobile/ios/Runner/Info.plist` มี `NSAllowsLocalNetworking` + exception domain สำหรับ `localhost` (dev เท่านั้น)

### 🟡 `vatRate` เป็นสัดส่วน ไม่ใช่เปอร์เซ็นต์
API ส่ง `0.07` — ต้องคูณ 100 ก่อนแสดง ไม่งั้นได้ "ภาษี 0.07%"

---

## ความปลอดภัย — เรื่องที่ต้องรู้

### 🔴 รหัสผ่านใน Garage DB เป็น plaintext
ตรวจแล้ว ~9,500 บัญชี **ไม่มี hash เลย** (ความยาว 3–17 ตัว, 0 รายการขึ้นต้นด้วย `$2`)
`AuthService.PasswordMatches` จึงต้องเทียบแบบ plaintext เพื่อให้ผู้ใช้เดิมเข้าได้ แต่:
- เทียบแบบ **constant-time** (`CryptographicOperations.FixedTimeEquals` บน SHA-256 ของทั้งสองฝั่ง)
- **ห้าม log / ส่งกลับ / ใส่ใน exception** ค่ารหัสผ่านทุกกรณี
- **ต้องวางแผนย้ายไป hash โดยเร็ว** — เป็นความเสี่ยงที่ใหญ่ที่สุดของระบบตอนนี้

### 🔴 ห้าม commit ความลับ
`Jwt:Key`, connection string ทั้งหมด อยู่ใน **dotnet user-secrets** เท่านั้น
`appsettings.Development.json` มีแค่ placeholder

> อ้างอิง: `AMD.GaragePro.Admin/backend/.../appsettings.json` มีรหัส `sa`, รหัส FTP, JWT key เป็น plaintext ใน repo — **อย่าทำตาม pattern นั้น**

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
  Entities/      Quotation · QuotationLine · QuotationApproval · CatalogItem
                 Shift · ShiftSession · UserRoleOverride · Attachment · ActivityEvent
Application/     service + DTO + abstraction (interface ทั้งหมดอยู่ที่นี่)
Infrastructure/  EF Core (write) · Dapper (อ่าน legacy) · JWT · file storage
API/             controller บางๆ — logic อยู่ที่ Application
```

**หลักการ:** กฎ lifecycle อยู่ที่ `JobStateMachine` ที่เดียว ห้ามกระจายไปอยู่ใน controller หรือ service

### Auth flow
```
POST /auth/login          → token ขั้นแรก (pre-session) + รายการสาขา
GET  /auth/branches/{id}/shifts
POST /auth/shift-sessions → token ที่ใช้เรียก API งานได้จริง (มี branch/shift/session ใน claim)
```
`[RequireShiftSession]` บล็อก token ขั้นแรกไม่ให้เรียก endpoint งาน —
ถ้าไม่มี attribute นี้ `BranchId` จะเป็น 0 แล้วอ่าน/เขียนผิดสาขา

### Envelope
```jsonc
{ "success": true,  "data": {...}, "error": null, "traceId": "..." }
{ "success": false, "data": null,  "error": { "code": "...", "messageTh": "..." }, "traceId": "..." }
```
`messageTh` เป็นภาษาไทยพร้อมแสดงผล — **client ห้ามแปลหรือแต่งใหม่**

---

## กฎ UI (ทั้ง Flutter และ React)

- ทุกสถานะสื่อด้วย **สี + ไอคอน + ข้อความ** — ห้ามใช้สีอย่างเดียว
- ทุก state (`loading` `empty` `error` `forbidden` `stale`) ต้องมี **สาเหตุ + ปุ่มถัดไป + traceId**
- **ปุ่มที่ปิดใช้งานต้องบอกเหตุผลเสมอ** — ห้าม disable เฉยๆ
- ตัวเลขเงิน: `IBM Plex Mono` + `tabular-nums` + format `1,234.56` เสมอ
- Mobile: touch ≥48px · CTA 54–56px · ฟอร์มยาวใช้ sticky bar + บันทึกร่าง
- Web: desktop 1440 หลัก · 1024 ย่อ sidebar · **< 1024px ไม่รองรับ**
- ข้อความ UI เป็นภาษาไทยทั้งหมด · โค้ดและตัวแปรเป็นอังกฤษ

Design token อยู่ที่ `mobile/lib/core/tokens.dart` และ `web/src/lib/tokens.ts` —
**ค่ามาจาก prototype ที่อนุมัติแล้ว ห้ามแก้ข้างเดียว ต้องแก้ให้ตรงกันทั้งสองที่**

---

## สถานะปัจจุบัน

### เสร็จแล้ว
- ✅ Auth: login ด้วยบัญชีเดิม · เลือกสาขา/กะ · JWT · role mapping จากโครงองค์กรเดิม
- ✅ Quotation: สร้าง · แก้บรรทัด · validate · ส่ง · **ออกฉบับแก้ไข** · อนุมัติรายบรรทัด · เซ็น
- ✅ Attachment: อัปโหลด · เปิดไฟล์ · ลายเซ็นจากมือถือขึ้น server จริง
- ✅ Mobile: login · เลือกสาขา/กะ · คิว (pull-to-refresh) · อนุมัติ · ลายเซ็น · โปรไฟล์/ปิดกะ
- ✅ Web: คิว · editor 3 พาเนล · เอกสาร A4 พร้อมพิมพ์
- ✅ Test: 49 ผ่าน (state machine · calculator · role mapper)

### ยังไม่ได้ทำ
- รับรถ 6 ขั้น · ตรวจเช็ค 31 รายการ · ซ่อม+QC · คลัง/จัดซื้อ · POS · รายงาน
- Offline queue ของ Flutter (`TMP-` + conflict) — **งานใหญ่ อย่าประเมินต่ำ**
- Realtime (SignalR) · refresh token · หน้าตั้งค่า · หน้าส่งมอบรถ

### Open questions ที่ยัง block อยู่
ดู [docs/01-workflow.md §10](docs/01-workflow.md) — ที่สำคัญที่สุดคือ **#3 นโยบาย offline conflict**
(ยังไม่ block เพราะยังไม่ทำ offline)

---

## แนวทางการทำงาน

- **ตรวจ schema จริงก่อนเขียน SQL เสมอ** — ชื่อคอลัมน์ใน legacy ไม่ตรงกับที่เดา
  (`Car.CarNumber` = ทะเบียน · `Car.Chassis` = เลขตัวถัง · `Customer`/`Staff` ใช้ `FirstName`+`LastName`)
- เขียน test ให้กฎธุรกิจก่อนต่อ UI — `QuotationCalculatorTests` ล็อกตัวเลขไว้ตรงกับ Demo prototype
  (8,838.20 และ 5,628.20) ถ้า test นี้แดง เอกสารที่ออกจากระบบจะไม่ตรงกับที่ออกแบบ
- comment อธิบาย **ทำไม** ไม่ใช่ **อะไร** · ใส่ `[BIZ]` `[UI]` `[ASSUME]` `[SECURITY]` `[RISK]` ให้ตรงกับเอกสาร
- เจอค่าที่ต้องเดา → ทำเป็น config/override table อย่า hard-code (ดู `svc_UserRoleOverride`)
