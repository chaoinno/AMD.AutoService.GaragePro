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
| [docs/06-customer-vehicle-management.md](docs/06-customer-vehicle-management.md) | ลูกค้า/รถ · SQL scope ตามสาขา · ข้อจำกัดข้อมูล legacy ร่วมกัน |
| [docs/staff-api.md](docs/staff-api.md) | API พนักงาน · อ่านคู่กับข้อจำกัดสาขาล่าสุดด้านล่าง (Admin ไม่ได้สิทธิ์ข้ามสาขา) |
| [docs/06-purchasing-fifo.md](docs/06-purchasing-fifo.md) | PR/PO/GRN · Stock FIFO · สิทธิ์ · transaction/idempotency · ขอบเขตที่ยังไม่รวม |

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
14. **`JobTypeId` ต้องเป็น 9 (รถในอู่) หรือ 10 (รถนัดหมาย) เท่านั้น** (`JOB_VALIDATION`) · เปลี่ยนสถานะแบบ manual (ไม่มี guard อัตโนมัติรองรับ) ต้องระบุเหตุผลเสมอ (`JOB_TRANSITION_NEEDS_REASON`)

> **[RISK]** `JobsController`/`IntakeChecklistController` ตอนนี้ gate ด้วย `[RequireShiftSession]` เท่านั้น
> ยังไม่ผูก role ตาม `docs/01-workflow.md §4` (เช่น ใครก็ตามที่ login แล้วมีกะเปิดอยู่สร้าง/เปลี่ยนสถานะ job ได้หมด) — ต้องปิดช่องนี้ก่อน production

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

### 🟡 EF Core ห้ามใช้ `.SingleAsync()`/`.FirstAsync()` ต่อท้าย raw SQL ที่มี `OUTPUT`
`JobNumberGenerator` ออกเลขจ๊อบด้วย `MERGE ... WITH (HOLDLOCK)` + `OUTPUT inserted.LastSequence` ผ่าน `SqlQueryRaw`
(กันชนเลขซ้ำเวลาออกพร้อมกันหลาย request ต่อ branch/วันเดียวกัน) — ถ้าต่อท้ายด้วย `.SingleAsync()`/`.FirstAsync()`
EF จะห่อ query เป็น subquery ทำให้ `MERGE`/`OUTPUT` composable ไม่ได้ (SQL error)
→ ต้องใช้ `.ToListAsync()` แล้วดึงตัวแรกเอง

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
Web:    POST /auth/login → token ใช้งานได้ทันที (มี branch จาก Staff.BranchId ใน claim)
Mobile: POST /auth/login → เลือกสาขา/กะต่อผ่าน /auth/branches/{id}/shifts
        POST /auth/shift-sessions → token ที่มี branch/shift/session ใน claim
```
`[RequireShiftSession]` ยังใช้ตรวจว่ามี branch context ก่อนเรียก endpoint งาน;
Web ไม่ต้องมี ShiftSession ส่วน Mobile compatibility ยังเปิด/ปิดกะได้ตามเดิม

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
- ✅ Attachment: อัปโหลด · เปิดไฟล์ · ลายเซ็นจากมือถือขึ้น server จริง
- ✅ Job (ใหม่, เว็บเท่านั้น): เปิดจ๊อบลง `svc_Job` · เลขจ๊อบ `JB{yyMMdd}{BranchId:D4}{seq:D3}` ต่อสาขา/วัน
  (กันชนด้วย `MERGE ... WITH (HOLDLOCK)`) · การ์ดจ๊อบ 6 stage ขับเคลื่อนด้วย `Job.Status` ผ่าน `JobCardModal`
  (แทน `JobDetailModal` เดิมที่ลบไปแล้ว) — stage ซ่อม/QC/ชำระเงินยังเป็น placeholder
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
- ✅ Test: บันทึกเดิม 55 ผ่าน (auth · attachment storage · state machine · calculator · role mapper · job service)
  — เป็นผลก่อนงานล่าสุด ไม่ใช่จำนวนรวมปัจจุบัน ดูชุดทดสอบที่รันจริงด้านล่าง

### ยังไม่ได้ทำ
- รับรถ 6 ขั้นเต็มรูปแบบบนมือถือ (ค้นหา/ยืนยันนัดหมาย/รูป 5 มุม/QR) · ตรวจเช็ค 31 รายการ 8 หมวดของช่าง
  (spec เดิม — คนละอันกับ checklist 20 รายการที่ทำแล้วบนเว็บ) · ซ่อม+QC (placeholder ใน `JobCardModal`) ·
  POS · รายงาน
- ส่วนต่อยอดคลัง/จัดซื้อ: VAT/ส่วนลด/ค่าขนส่ง/เจ้าหนี้ · พิมพ์ PO · ส่งคำสั่งซื้อไปภายนอก · แบ่ง PR เป็นหลาย PO
  · โอนคลัง/คืนผู้ขาย/ตรวจนับปรับยอด · แนบรูป GRN · จอง/เบิกผูก job หรือ quotation อัตโนมัติ
- เคลียร์ข้อขัดแย้งการเขียน legacy ของโมดูลลูกค้า/รถ/พนักงานให้ตรงนโยบายสถาปัตยกรรม
- การเรียงตารางแบบ server-side ครอบคลุมทุกหน้า (ปัจจุบันเรียงเฉพาะหน้าหรือข้อมูลที่โหลดแล้ว)
- RBAC ของ Job/Intake endpoint (ตอนนี้ gate ด้วย shift session เท่านั้น — ดู `[RISK]` ในหัวข้อกฎที่ห้ามละเมิด)
- Offline queue ของ Flutter (`TMP-` + conflict) — **งานใหญ่ อย่าประเมินต่ำ**
- Realtime (SignalR) · refresh token · หน้าตั้งค่า · หน้าส่งมอบรถ

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
