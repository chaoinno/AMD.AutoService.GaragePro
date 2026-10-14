# 12 — ข้อเสนอ: ServicePro แบบ "ทดลองใช้ด้วยตัวเอง" (Self-serve Trial)

> **สถานะ: ทำแล้วบน branch `feat/sp1-selfserve` (2026-10-14) · ยังไม่ deploy · ยังไม่ได้ทดสอบกับฐานจริง**
> การตัดสินใจของผู้ใช้ (2026-10-13): ลูกค้าที่จ่ายเงิน **ย้ายไปเป็นสาขา GaragePro ใน db2 เฉพาะข้อมูลหลัก** (`spadmin convert`) ·
> อีเมลทำ interface ก่อน (ยังเป็น log) · Google sign-in ทำรอบแรก · ฟีเจอร์ล็อก/ลิมิตตาม §5 · หมดอายุ = อ่านอย่างเดียว 90 วัน ·
> แก้ §9 ข้อ 1–4 ก่อน (ใบเสร็จ · master data แยกสาขา · rate limit · กะ) · ข้อ 5 (secrets/dev DB) เลื่อนไว้ —
> สิ่งที่สร้างจริงและต่างจากข้อเสนอด้านล่าง ดู CLAUDE.md หัวข้อ "ServicePro Self-serve Trial" และ docs/03 §16
> ที่มา: คำขอผู้ใช้ — คนที่เห็น ServicePro ผ่าน social/เว็บ กด signup แล้วเข้ามา "test drive" ได้เอง เปิดอู่ใหม่เอง
> ใช้ฟังก์ชันพื้นฐาน ปิดขั้นสูง จำกัด 30 วัน กันอีเมลซ้ำ/ปลอม และทำ SEO ด้วยคีย์เวิร์ด
> "โปรแกรมสำหรับอู่บริการ / โปรแกรมสำหรับอู่เซอร์วิส"
> เอกสารนี้สรุปจากการสำรวจโค้ด + ฐานข้อมูลจริง (db2, GarageService) + เว็บ production เมื่อ 2026-10-13

---

## 0. สรุปสั้น (อ่านส่วนนี้ก่อน)

**แนวคิดหลักที่เสนอ — "shard ที่สมัครเอง" ไม่ใช่ "ระบบเดโมแยก"**
ระบบตอนนี้แบ่งข้อมูลด้วยคู่ `(LegacyShardKey, BranchId)` อยู่แล้ว (db1 / db2) ข้อเสนอคือเพิ่ม shard ใหม่ชื่อ **`sp1`**
(self-serve) ซึ่งชี้ไปฐานข้อมูลใหม่ที่เป็นของเรา 100% ทุกอู่ที่สมัครเอง = 1 สาขาใน shard นี้ ใช้ **API / เว็บ / แอปมือถือ
ตัวเดียวกับลูกค้าจริง** (คนทดลองได้สัมผัสของจริง รวมแอปช่างบน Play Store ที่มีอยู่แล้ว) ต่างกันแค่
(1) ตัวตนมาจากอีเมล/Google แทนบัญชี Garage เดิม (2) มีแพ็กเกจ · วันหมดอายุ · ฟีเจอร์ที่ล็อก
และเมื่อลูกค้าจ่ายเงิน **ไม่ต้องย้ายข้อมูล** — แค่เปลี่ยนแพ็กเกจ (ดู §2.4)

**คำตอบต่อ 5 ข้อที่ถามมา**

| คำถาม | ข้อเสนอ (รายละเอียดในหัวข้อ) |
|---|---|
| เก็บข้อมูลยังไง / SQLite ชั่วคราว? | **ไม่ใช้ SQLite** (SQL ของระบบเป็น T-SQL เฉพาะทางเกือบทุกจุด — §2.1) → ฐาน SQL Server ใหม่ 1 ฐาน `GarageSelfServe` บน instance เดิม (10.10.4.11) ประกอบด้วย 3 ส่วน: ตาราง "รูปร่างเหมือน Garage" ที่เราเขียนได้ · ตาราง `svc_*` ชุดเดียวกับ production (migration เดิม) · ตาราง `sp_*` ใหม่ (บัญชี/แพ็กเกจ/กันสแปม) — §2 |
| เปิดอู่เองด้วยฟังก์ชันพื้นฐาน | signup **2 หน้าจอ · 4 ช่องบังคับ** (อีเมล+รหัสผ่าน หรือ Google · ชื่ออู่ · เบอร์โทร) → ระบบสร้างสาขา+เจ้าของ+กะ+ข้อมูลตัวอย่าง (เลือกได้) → เช็คลิสต์เริ่มต้น 5 ข้อ — §4 · ฟีเจอร์ล็อกด้วย feature flag ที่ **บังคับที่ API** ไม่ใช่แค่ซ่อนเมนู — §5 |
| จำกัด 30 วัน | ใช้คอลัมน์แพ็กเกจแบบเดียวกับ legacy (`StartPackageDate/PackageDaysAmout/EndPackageDate`) · แถบนับถอยหลังทุกหน้า · เตือนวันที่ 23/29 · หมดอายุ = ล็อกทั้งระบบ + หน้า "ติดต่อ LINE @garagepro / ขอต่ออายุ" · เก็บข้อมูลต่ออีก 90 วันเผื่อสมัครแพ็กเกจ — §6 |
| กันอีเมลซ้ำ/ปลอม (เบื้องต้น) | 8 ชั้น: normalize อีเมล (ตัดจุด/`+tag` ของ Gmail) + unique ตลอดกาล · blocklist โดเมนอีเมลชั่วคราว · ตรวจ MX · OTP ยืนยันอีเมล (Google ยืนยันให้แล้ว) · เบอร์โทรบังคับ · Cloudflare Turnstile · rate limit ต่อ IP · honeypot — §7 |
| SEO + sitemap | ตอนนี้ **ไม่มี** robots/sitemap/canonical และ 2 โดเมนเสิร์ฟเนื้อหาซ้ำกัน · แผน: โดเมนหลักเดียว `service.garage-pro.net` + 301 · prerender หน้าสาธารณะเป็น HTML จริง + code-split ให้หน้าขายไม่โหลดโค้ดทั้งแอป · หน้าใหม่ /features /pricing /signup /privacy /terms · JSON-LD · Search Console — §8 |

**สิ่งที่พบระหว่างสำรวจและต้องแก้ก่อนเปิดให้คนนอกสมัคร (ไม่ว่าจะเลือกทางไหน)** — รายละเอียด §9

1. **เลขใบเสร็จชนกันข้ามสาขา**: counter `RC-yy-NNNN` นับต่อสาขา แต่ index `svc_Receipt.DocumentNo` unique ทั้งระบบ → สาขาที่สองออกใบเสร็จใบแรกได้ 500 (`ServiceDbContext.cs:376` vs `ReceiptNumberGenerator.cs`) — วันนี้ยังไม่เจอเพราะมีสาขาที่ออกใบเสร็จแค่ 227
2. **ซัพพลายเออร์ / หมวดหมู่สินค้า ไม่มี scope สาขา** และรหัสคลัง unique ทั้งระบบ → อู่ทดลองทุกรายจะเห็นของกันและกัน
3. **ยังไม่มี rate limit ที่ `/auth/login`** และ endpoint จ๊อบ/รับรถ gate ด้วย `[RequireShiftSession]` เท่านั้น (RISK เดิม) → ต้อง audit การแยกข้อมูลตาม shard/สาขาของทุก endpoint ก่อนให้คนแปลกหน้าสมัครได้
4. dev DB = production DB (`docs/10` §2.1) และ `appsettings.Development.json` มีรหัส `sa` อยู่ใน git — ต้องเคลียร์ก่อนเปิดช่องทางสาธารณะเพิ่ม

**ขนาดงานคร่าวๆ**: ประมาณ **6–8 สัปดาห์สำหรับนักพัฒนา full-stack 1 คน** (ยังไม่รวมเนื้อหาการตลาด/บทความ SEO และข้อความกฎหมาย) — §11

**ต้องตัดสินใจก่อนวางแผนจริง 12 ข้อ** — §12 (ข้อสำคัญสุด: ลูกค้าที่จ่ายเงินแล้ว "อยู่ต่อใน sp1" หรือ "ย้ายไปเป็นสาขา GaragePro ใน db2")

---

## 1. สิ่งที่พบจากระบบปัจจุบัน (ข้อเท็จจริงที่ข้อเสนอนี้ยืนอยู่)

### 1.1 ตัวตนและสิทธิ์ผูกกับฐาน Garage เดิมทั้งสาย
- login = `[User]` (UserName/Password **plaintext**) → `Staff` → `Branch` (ต้อง `Status=1` และ `BranchGroupId=7` "Services") → JWT claims
  `sub`(User.Id) · `staff` · `branch` · `shard` · `role` · `is_admin` · `name_th` (`JwtTokenIssuer.cs:105-125`)
- login ไม่ระบุ shard ได้ — ใช้ fallback `"db2"` เสมอ (`CurrentUser.cs:37`) · **ไม่มี rate limit**
- role มาจาก **ชื่อตำแหน่งภาษาไทย** ใน legacy (`RoleMapper.cs`) โดยเช็ค `svc_UserRoleOverride` ก่อน — ตารางนี้มีอยู่แล้วแต่ไม่มีใครเขียน
- `SessionRevocationMiddleware` อ่าน `[User]/Staff` ซ้ำทุก 60 วิ ต่อ token · fail-open ถ้าฐาน legacy ล่ม
- ไม่มี password hasher · ไม่มี Google/OIDC · **ไม่มีการส่งอีเมล** ใดๆ ในระบบ · มีแต่ LINE push (`LineContactNotifier`) ไปกลุ่มเดียว

### 1.2 ข้อมูลหลัก (ลูกค้า/รถ/พนักงาน/สาขา) อ่าน-เขียนที่ Garage ผ่าน 5 คลาส Dapper
`LegacyUserReader` · `LegacyReader` · `CustomerVehicleRepository` · `StaffRepository` · `BranchPinVerifier`
(`Infrastructure/Legacy/`) แต่ละคลาสเปิด connection เองจาก `LegacyShards:ConnectionStrings[shardKey]`
(`LegacyReader.cs:9-17`) — **ไม่มี factory กลาง แต่มี pattern "shard key → connection string" ที่ใช้ต่อได้ทันที**
ตารางที่ SQL เหล่านี้แตะ (ต้องมีในฐานใหม่): `Branch` `[User]` `Staff` `StaffSectorPosition` `Position` `Sector` `Department`
`StaffSkillLevel` `Gender` `Customer` `Car` `CarCustomer` `PJCarPickUp` `RepairTypeNPoint` `StaffRepairTypeNPoint` `ChangeLog`
และตารางอ้างอิง `province` `amphures` `districts` `zipcodes` `BrandCar` `CarModel` `CarNickName` `[Year]` `CarType`
`PrimaryColor` `ColorMix` `Gear` `Machine` `DriveSystem` `InsuranceType` (dropdown อ่านจาก `DefaultShard` แต่ JOIN ในคิวรีลูกค้า/รถใช้ shard ของ JWT)
- การเขียน legacy ที่มีอยู่แล้ว (INSERT Customer/Car/CarCustomer · INSERT Staff/[User] plaintext) เป็น `[RISK]` ขัดนโยบาย read-only
  — **ในฐาน self-serve ไม่มีปัญหานี้เพราะเป็นฐานของเรา** แต่การสร้าง `[User]` แบบ `Password = Code` plaintext ต้องถูกแทนที่ (§3.3)

### 1.3 ตาราง `svc_*` = 46 ตาราง · 21 migrations · ข้อมูลยังเล็กมาก
- ทุกตารางหลักคีย์ด้วย `(LegacyShardKey, BranchId)` และอ้าง id ของ legacy ตรงๆ (CustomerId, VehicleId, StaffId, UserId …)
  → **ถ้า shard ใหม่มีตารางรูปร่างเดียวกัน id ชนิดเดียวกัน (int/bigint) ทุกอย่างทำงานต่อได้โดยไม่แก้ entity**
- ยกเว้น: `svc_Supplier` / `svc_CatalogCategory` / `svc_CatalogItemSupplier` **ไม่มี scope** · `svc_Warehouse.Code` และ
  `svc_Receipt.DocumentNo` unique ทั้งระบบ (ส่วน `svc_TaxInvoice` ทำถูกแล้ว: unique ต่อ shard+สาขา)
- ไม่มี global query filter — แต่ละ repository กรอง shard/สาขาเอง (จุดที่ต้อง audit)
- จ๊อบจริงในฐาน: `db2/227` 29 จ๊อบ · `db2/105` 4 จ๊อบ (ข้อมูล ณ 2026-10-13) — ย้าย/แยกอะไรตอนนี้ยังถูก

### 1.4 เว็บ/แอปยังไม่มีแนวคิด trial · tenant · signup เลย
- ไม่มี route สมัคร · เมนูไม่ซ่อนตาม role (server ตอบ 403 แล้ว UI ค่อยแสดง) · `canSeeCost` ใช้เป็น "เป็นผู้จัดการ" โดยพฤตินัย
- เว็บเจอ 401 แล้ว **redirect ไป /login เงียบๆ ไม่แสดง `messageTh`** (`api/client.ts:55-60`) — ต้องแก้เพื่อบอก "หมดอายุทดลองใช้"
- แอป: ชี้ `https://gpservice-api.garage-pro.net` คงที่ · login = รหัสพนักงาน/รหัสผ่าน · อ่าน `branchId`/`userId`/`customerId` เป็น **int**
  · เจอ `AUTH_REQUIRED` แล้ว**แสดง `messageTh` ของ server อยู่แล้ว** → อู่ทดลองใช้แอปบน Play Store ได้ทันทีถ้า API รองรับ (§3.2)

### 1.5 สถานะ SEO ของเว็บ production (ตรวจจริง 2026-10-13)
| รายการ | สถานะ |
|---|---|
| `<title>` | "ServicePro" เฉยๆ ไม่มีคีย์เวิร์ด · meta description มี · `lang="th"` มี · og:* มีแต่ og:image 256px |
| canonical / og:url / twitter:card / JSON-LD / manifest | ไม่มี |
| `robots.txt` · `sitemap.xml` | **ไม่มี** — `try_files … /index.html` ทำให้ URL ไหนก็ตอบ 200 เป็นหน้า SPA (soft-404) |
| โดเมน | `gpservice.garage-pro.net` และ `service.garage-pro.net` เสิร์ฟเนื้อหา**เหมือนกันทั้งคู่ ไม่มี 301 ไม่มี canonical** (duplicate content) · `service.*` ยังใช้ใบรับรองของ `gpservice` (RISK 526 ถ้า Cloudflare เป็น strict) |
| ประสิทธิภาพ | SPA ไฟล์เดียว ~1.1 MB JS + CSS 200 KB โหลดทั้งแอปแม้เปิดหน้าขาย · ฟอนต์ Google render-blocking · nginx ไม่มี gzip/brotli · ไม่มี no-cache ให้ index.html |
| เนื้อหา | หน้าเดียว (hero/ฟีเจอร์ 9/ขั้นตอน 7/แอป/เกี่ยวกับ/ติดต่อ) เมนูเลื่อนภายในหน้า ไม่เปลี่ยน URL · `docs/04` เคยบันทึกว่า "ไม่ต้องทำ SEO" (ล้าสมัยแล้ว) |
| เว็บเดิม `gp.ipongs.com` | เป็นเว็บขาย GaragePro (ตัวถัง/เคลม) คนละผลิตภัณฑ์ มี testimonial/FAQ/"เริ่มได้ใน 3 ขั้นตอน" — ใช้เป็นต้นแบบโครงเนื้อหาได้ |

### 1.6 โครงสร้างพื้นฐานที่เกี่ยว
- ไม่มี background job/`IHostedService` เลย (ต้องมีสำหรับเตือนวันหมดอายุ/ล้างข้อมูล) · ไม่มีอีเมล · secrets ผ่าน user-secrets/env file
- ฐาน dev = production (`10.10.4.11/GarageService`) — migration จากเครื่อง dev ลง production ทันที
- Rate limiter ของ ASP.NET Core ตั้งไว้แล้วสำหรับ `/api/v1/public/*` (ต่อยอดได้) · `UseForwardedHeaders` ได้ IP จริงจาก Nginx แล้ว

### 1.7 ข้อมูลจากระบบเดิมที่นำมาใช้ได้ (อ่านจาก db2 จริง)
| ข้อมูล | ที่พบ | ใช้ทำอะไร |
|---|---|---|
| แนวคิดแพ็กเกจมีอยู่แล้วใน legacy | `Branch.StartPackageDate / PackageDaysAmout / EndPackageDate` · กลุ่ม `BranchGroup` = Express 177 · Enterprise 112 · **Services 13 (active 5, แพ็กเกจยังไม่หมด 3)** · Basic 12 … | ใช้คอลัมน์ชุดเดียวกันนับ 30 วัน → ภายหลังบังคับแพ็กเกจกับสาขา legacy ได้ด้วยกติกาเดียว (ตอนนี้ ServicePro **ไม่ได้เช็ค** EndPackageDate ของ legacy เลย) |
| ตารางอ้างอิงรถ/ที่อยู่ | `BrandCar` 130 · `CarModel` 1,222 · `CarType` 23 · `PrimaryColor` 17 · `province` 79 · `amphures` 1,006 · `districts` 8,928 | แชร์ให้ sp1 อ่านโดยไม่คัดลอก (synonym ข้ามฐานบน instance เดียวกัน) |
| ตารางอ้างอิงพนักงาน | `Position` 10 แถว (ผู้บริหาร/ผู้จัดการ/หัวหน้าแผนก/พนักงาน…) · `Department` · `Sector` · `StaffSkillLevel` · `Gender` | คัดลอกครั้งเดียว (เล็กมาก) — ฟอร์มพนักงานอ่านตาม shard ของ JWT |
| รายการเช็คลิสต์/กฎธุรกิจ | อยู่ในโค้ดแล้ว: เช็คลิสต์รับรถ 20 ข้อ (`IntakeChecklistTemplate.cs`) · ของในรถ 5 ข้อ · QC จากบรรทัดอนุมัติ · กะ 3 กะสร้างให้เองครั้งแรก · state machine ทั้งหมด | **อู่ใหม่ได้ฟรีทันที** ไม่ต้องตั้งค่า |
| แคตตาล็อกตัวอย่าง | สาขาเดโม 227 มี 36 รายการ (ของทดสอบ) | ใช้เป็นต้นร่าง "ชุดเริ่มต้น" แล้วคัดใหม่ให้เป็นรายการอู่บริการจริง (§4.3) |
| ข้อมูลที่ **ไม่** นำมา | ลูกค้า/รถ/พนักงานจริงของสาขาใดๆ (PDPA) · `Gpscode` 19,823 รหัส (อะไหล่ตัวถัง) · `PJCarPickUp` | — |

---

## 2. การเก็บข้อมูล (ตอบเรื่อง SQLite)

### 2.1 ทำไมไม่ใช่ SQLite
ระบบนี้ไม่ได้ใช้ EF แบบ provider-agnostic — มี T-SQL เฉพาะทางกระจายอยู่ทุกชั้น: `MERGE … WITH (HOLDLOCK) … OUTPUT` (เลขจ๊อบ/ใบเสร็จ/ใบกำกับ/บิล POS),
`sp_getapplock` + `THROW` (ธุรกรรมสต็อก), `WITH (READUNCOMMITTED)` + `#temp` ในคิวรี Dapper ของลูกค้า/รถ/พนักงาน, `rowversion` 7 ตาราง,
filtered unique index ที่บังคับกฎธุรกิจ (`UX_WorkInterval_OneOpenPerTech`), migration 21 ตัวเขียนสำหรับ SQL Server
→ SQLite = ต้องเขียน SQL ชั้น Infrastructure ใหม่เกือบทั้งหมดและ maintain สองชุดตลอดไป "ชั่วคราว" จะกลายเป็นถาวร
ถ้าประเด็นคือ**ค่าใช้จ่าย**: SQL Server Express ฟรี (10 GB/ฐาน) รันใน Docker บนเครื่อง API ได้ — แต่ข้อ 2.2 แนะนำให้ใช้ instance เดิมก่อน

### 2.2 ทางเลือกที่พิจารณา

| | A. SQLite | B. ฐานใหม่เฉพาะส่วน "รูปร่างเหมือน Garage" · `svc_*` ใช้ GarageService ร่วม (shard `sp1`) | **C. ฐานใหม่ 1 ฐานครบทุกส่วน (แนะนำ)** | D. เขียน implementation ใหม่ด้วยตาราง `trial_*` ออกแบบเอง |
|---|---|---|---|---|
| แก้โค้ดหลัก | รื้อ Infrastructure | น้อยมาก (เพิ่ม connection string + logic สมัคร) | น้อย (B + resolve connection ของ `ServiceDbContext` ตาม shard ~20 บรรทัด) | มาก: 5 คลาส ~40 เมธอด + DTO ที่ UI คาดหวัง |
| แยกข้อมูลทดลองออกจาก production | ✅ | ❌ แถวทดลองปนใน `svc_*` ของจริง · ล้างข้อมูล = DELETE 46 ตารางบน production | ✅ ล้างได้ระดับฐาน · สแปมไม่กระทบตาราง production | ✅ |
| "คล้ายของจริง" | ❌ | ✅ โค้ดเดียวกัน | ✅ โค้ดเดียวกัน | ⚠️ ฟอร์มลูกค้า/รถ/พนักงานต้องทำสองแบบ |
| แปลงเป็นลูกค้าจ่ายเงิน | — | เปลี่ยนแพ็กเกจ | เปลี่ยนแพ็กเกจ | เปลี่ยนแพ็กเกจ |
| ความเสี่ยง | สูง | ปานกลาง (ปน) | ต่ำ | ตารางเวลา |

**แนะนำ C** — ฐาน `GarageSelfServe` หนึ่งฐานบน instance เดิม (มี backup อยู่แล้ว · ops คุ้นเคย · synonym ไปตารางอ้างอิงของ `Garage` ได้โดยไม่คัดลอก)
ถ้าภายหลังโหลดโต ย้ายฐานนี้ไป server อื่นได้ทั้งก้อน (ตารางอ้างอิงเปลี่ยนจาก synonym เป็นสำเนา) · B เป็นทางลัดถ้าไม่อยากแตะ `ServiceDbContext` เลย

### 2.3 โครงฐาน `GarageSelfServe` (shard key `sp1`)

```
GarageSelfServe
├─ dbo.*  ตาราง "รูปร่างเหมือน Garage" เฉพาะที่ SQL ของเราแตะ (~16 ตาราง, สร้างจาก DDL ที่ generate จาก schema จริง)
│    Branch · [User] · Staff · StaffSectorPosition · Position · Sector · Department · StaffSkillLevel · Gender
│    Customer · Car · CarCustomer · PJCarPickUp(ว่าง) · RepairTypeNPoint/StaffRepairTypeNPoint(ว่าง) · ChangeLog
│    + synonym → Garage.dbo.{province, amphures, districts, zipcodes, BrandCar, CarModel, CarNickName, Year,
│                              CarType, PrimaryColor, ColorMix, Gear, Machine, DriveSystem, InsuranceType}
├─ svc_*  46 ตารางชุดเดียวกับ production — ใช้ migration bundle เดิม รันเพิ่มอีก 1 target ตอน deploy
└─ sp_*   ใหม่ (DbContext แยก + migration แยก ไม่ปนกับ production)
     sp_Account         บัญชีเข้าระบบของ shard นี้: NormalizedEmail(unique ตลอดกาล) · Email · Provider(google|email) · GoogleSubject ·
                        PasswordHash(PBKDF2/Argon2id) · EmailVerifiedAt · UserId(→[User]) · Status · CreatedAt/IP/UserAgent
     sp_Workspace       1 แถว/อู่: BranchId(→Branch) · Slug/รหัสอู่ (`SP1042`) · Plan(trial|paid|suspended) · TrialStartsAt/EndsAt (mirror ไป Branch package)
                        · Limits(json) · SampleDataLoadedAt · Source/UTM · OwnerAccountId · ConvertedAt · PurgeAfter
     sp_SignupAttempt   log ทุกความพยายามสมัคร (IP · email domain · ผลลัพธ์ · เหตุผลที่ปฏิเสธ) — ไว้ดูสแปม/ปรับกฎ
     sp_EmailOtp        รหัส 6 หลัก · หมดอายุ 10 นาที · นับครั้งผิด
     sp_BlockedDomain   โดเมนอีเมลชั่วคราว/ต้องห้าม (นำเข้าจาก list สาธารณะ + เพิ่มเอง)
```
- **id ชนิดเดิม** (Branch int · Staff/User/Customer/Car bigint identity) → entity `svc_*` และแอปมือถือ (`as int`) ไม่ต้องแก้
- `Branch.BranchGroupId = 7` และ `Status = 1` ตั้งให้ทุกอู่ที่สมัคร → ผ่านตัวกรอง login เดิม · `PinCode` สุ่มให้ (จัดซื้อถูกล็อกอยู่แล้ว)
- วันหมดอายุเก็บที่ `Branch.EndPackageDate` (แบบ legacy) และสำเนาใน `sp_Workspace` — ตัวเช็คอ่านจาก Branch ที่เดียว

### 2.4 เมื่อลูกค้าตัดสินใจจ่าย — สองทางที่ต้องเลือก (คำถาม §12 ข้อ 1)
- **ทาง B (แนะนำ): อยู่ต่อใน `sp1` ตลอดไป** — เปลี่ยน `Plan=paid`, ตั้ง `EndPackageDate` ตามรอบบิล ข้อมูลที่ลองไว้ใช้ต่อทันที ("ข้อมูลไม่หาย" เป็นจุดขายตอนปิดการขาย)
  `sp1` จะกลายเป็น "บ้านของลูกค้า ServicePro ที่ไม่ได้มาจากระบบ GaragePro เดิม" — ต้องมีหน้า/CLI admin เล็กๆ สำหรับทีมขาย (ต่ออายุ · เปลี่ยนแพ็กเกจ · ระงับ)
- **ทาง A: ย้ายไปเป็นสาขา GaragePro จริงใน db2** (สร้างผ่าน GaragePro Admin กลุ่ม 7) — ต้อง INSERT ลูกค้า/รถ/พนักงานลง Garage (ขัดนโยบาย read-only อีกครั้ง)
  และ re-key แถว `svc_*` ทั้ง 46 ตาราง + remap id — ทำได้แต่เป็นสคริปต์ที่ต้องเขียนและทดสอบ แนะนำเก็บไว้เป็นทางเลือกเฉพาะรายที่ต้องใช้ฟีเจอร์ legacy (E-claim ฯลฯ)

---

## 3. ตัวตนและการเข้าสู่ระบบ

### 3.1 สมัคร (public endpoints ใหม่ ใต้ `/api/v1/public/signup/*` — ใช้ rate limiter กลุ่ม public เดิม)
| วิธี | ขั้นตอน | สิ่งที่ต้องเพิ่ม |
|---|---|---|
| **Google** (แนะนำให้เด่นสุด) | ปุ่ม "สมัครด้วย Google" (Google Identity Services) → ID token → server ตรวจกับ Google (`Google.Apis.Auth`) → ได้ email + `email_verified` + `sub` → ไม่ต้องตั้งรหัสผ่าน ไม่ต้องส่ง OTP | Google Cloud project + OAuth client · หน้า privacy/terms (บังคับใน consent screen) |
| **อีเมล + รหัสผ่าน** | 2 ช่อง → ส่ง OTP 6 หลักทางอีเมล → ยืนยัน → ต่อขั้นตั้งค่าอู่ | ผู้ให้บริการอีเมล (ยังไม่มีในระบบ — Brevo/Resend/Amazon SES ผ่าน `IEmailSender` ใหม่ · dev = log ลง console) + SPF/DKIM บน `garage-pro.net` |

### 3.2 เข้าสู่ระบบ — ใช้ `POST /auth/login` เดิม แอปมือถือไม่ต้องแก้
- เพิ่มขั้น "หา shard": ถ้า username มี `@` **หรือ** พบใน `sp_Account`/รูปแบบรหัสพนักงาน self-serve → shard `sp1` · ไม่งั้น `db2` ตามเดิม
- เพิ่ม `POST /auth/google` (ID token → token ระบบ) สำหรับเว็บ · ผู้สมัครด้วย Google ที่จะใช้แอป ให้ตั้ง "รหัสผ่านสำหรับแอป" ในหน้าโปรไฟล์ครั้งเดียว
  (หรือเพิ่ม Google sign-in ในแอปทีหลัง)
- JWT เหมือนเดิมทุก claim (`sub` = `[User].Id` ของ sp1 ซึ่ง parse เป็น long ได้ · `branch` มี → ผ่าน `[RequireShiftSession]`) **เพิ่ม** `plan` (`trial|paid`) และ `plan_exp`
- role: ตอนสร้างพนักงานทุกคนใน sp1 **เขียน `svc_UserRoleOverride`** (ตารางที่มีอยู่แล้ว) → ไม่พึ่งการจับคู่ชื่อตำแหน่งไทยของ `RoleMapper`
- `SessionRevocationMiddleware` ใช้ต่อได้ (อ่าน `[User]/Staff` ของ sp1) **เพิ่ม** เช็คแพ็กเกจหมดอายุ → 401 `AUTH_REQUIRED` + `messageTh`
  "ทดลองใช้ครบ 30 วันแล้ว …" (แอปแสดงเองอยู่แล้ว · เว็บต้องแก้ให้แสดง) และ `LoginAsync` ตอบ `TRIAL_EXPIRED` เพื่อไม่ให้วนลูป login→401
- `GET /auth/me` เพิ่มข้อมูลแพ็กเกจ (วันหมดอายุ · เหลือกี่วัน · ฟีเจอร์ที่เปิด) ให้แถบนับถอยหลัง
- เพิ่ม rate limit `/auth/login` (ต่อ IP + ต่อ username) — ควรทำแม้ไม่มีโปรเจกต์นี้

### 3.3 รหัสผ่าน — โอกาสแก้ความเสี่ยงอันดับหนึ่งของระบบไปพร้อมกัน
- `AuthService.PasswordMatches` ให้รู้จัก **รูปแบบ hash** (เช่น `$pbkdf2$…`/Argon2id) ถ้าค่าในคอลัมน์เป็น hash → verify แบบ hash · ถ้าไม่ใช่ → เทียบ plaintext แบบเดิม
  บัญชี sp1 เป็น hash ตั้งแต่แรก · บัญชี legacy ยังเข้าได้ · และเปิดทางให้ **ค่อยๆ ย้าย legacy ไป hash** (แผนที่ CLAUDE.md บอกว่าต้องทำโดยเร็ว)
- พนักงานของอู่ทดลอง: เจ้าของกด "เพิ่มช่าง" ใส่แค่ **ชื่อ + บทบาท** → ระบบสร้าง username `SP1042.T01` (รหัสอู่.รหัสพนักงาน — ไม่ซ้ำทั้ง shard)
  + รหัสผ่านสุ่ม แสดง/คัดลอกครั้งเดียวเป็นการ์ด "ส่งให้ช่างเข้าแอป" · เปลี่ยนรหัสได้จากหน้าพนักงาน
  (`StaffRepository.CreateAsync` เดิมตั้ง `UserName = Password = Code` plaintext — ใน sp1 ต้องใช้ทางนี้แทน)

---

## 4. ประสบการณ์สมัครและเริ่มใช้งาน (เน้น "เริ่มง่ายๆ")

### 4.1 หน้าจอสมัคร — 2 หน้าจอ · บังคับ 4 ช่อง · ใช้ได้บนมือถือ

```
หน้า 1  /signup                                 หน้า 2  ตั้งค่าอู่ (หลังยืนยันอีเมล/Google)
┌──────────────────────────────────┐            ┌──────────────────────────────────┐
│  ทดลองใช้ ServicePro ฟรี 30 วัน  │            │  บอกเราเกี่ยวกับอู่ของคุณ         │
│  ไม่ต้องใส่บัตรเครดิต             │            │  ชื่ออู่ *        [ อู่ช่างเอก   ] │
│                                  │            │  เบอร์โทร *      [ 08x-xxx-xxxx] │
│  [ G  สมัครด้วย Google ]         │            │  จังหวัด         [ เลือก ▾ ]     │
│  ───────── หรือ ─────────         │            │  ขนาดอู่  (1-3 ช่าง)(4-10)(10+)   │
│  อีเมล *        [            ]   │            │  ☑ ใส่ข้อมูลตัวอย่างให้ (แนะนำ)  │
│  รหัสผ่าน *     [            ]   │            │                                  │
│  ☑ ยอมรับข้อกำหนด/นโยบายข้อมูล  │            │     [ เริ่มใช้งาน ServicePro → ] │
│  [ เริ่มทดลองใช้ → ]              │            └──────────────────────────────────┘
└──────────────────────────────────┘            ชื่อเจ้าของดึงจาก Google/อีเมล แก้ทีหลังได้
```
ไม่ถามตอนสมัคร (ให้กรอกทีหลังเมื่อจำเป็น เช่นตอนพิมพ์เอกสาร): ที่อยู่ · เลขผู้เสียภาษี · โลโก้ · เวลาทำการ

### 4.2 ระบบสร้างให้อัตโนมัติ
Branch (กลุ่ม 7 · แพ็กเกจ 30 วัน) → Staff เจ้าของ + `[User]` + `svc_UserRoleOverride = Manager` → `sp_Account`/`sp_Workspace` →
กะ 3 กะ (ของเดิมสร้างให้เองครั้งแรก) → **ชุดเริ่มต้น** (ถ้าติ๊ก): แคตตาล็อกอู่บริการ ~40 รายการ (บริการ: เปลี่ยนถ่ายน้ำมันเครื่อง · เช็คระยะ · เบรก · ยาง · แบตเตอรี่ · แอร์ …
อะไหล่: น้ำมันเครื่อง · ไส้กรอง · ผ้าเบรก …) + เทมเพลตใบเสนอราคา 2 ชุด ("เช็คระยะ 10,000 กม." / "เปลี่ยนผ้าเบรกหน้า") + ช่าง 2 คน + ลูกค้า/รถตัวอย่าง 2 คัน
+ จ๊อบตัวอย่าง 2 งาน (รอเสนอราคา / กำลังซ่อม) — สร้างผ่าน service ชั้น Application เดียวกับของจริง (แบบ `tools/devseed` แต่ in-process) ให้ผ่าน validation จริง
· ข้อมูลตัวอย่างติดป้าย "ตัวอย่าง" และมีปุ่ม "ล้างข้อมูลตัวอย่าง" เมื่อพร้อมใช้ของจริง → เนื้อหาชุดเริ่มต้นต้องให้ผู้ใช้รีวิว (คำถาม §12 ข้อ 5)

### 4.3 หลังเข้าระบบครั้งแรก — การ์ดเช็คลิสต์เริ่มต้น (ปิดได้ · อยู่บนหน้าจ๊อบ)
1. เพิ่มช่างคนแรก (ชื่อ+บทบาท → ได้รหัสเข้าแอป) 2. เพิ่ม/ตรวจรายการบริการ 3. เปิดจ๊อบแรกและรับรถ 4. ส่งใบเสนอราคาและ "ดำเนินการแทนลูกค้า"
5. ติดตั้งแอปช่าง (QR Play Store) แล้วลองกด "เริ่มงาน (จับเวลา)" → เห็นสถานะเด้งบนเว็บ
แต่ละข้อมีปุ่มพาไปทำ + ติ๊กให้เองเมื่อเกิดขึ้นจริง (อ่านจากข้อมูล ไม่ต้องให้ผู้ใช้ติ๊ก)

### 4.4 สิ่งที่เห็นต่างจากลูกค้าจริงระหว่างทดลอง
- แถบบนสุด (เว็บ) / แบนเนอร์ (แอป): "ทดลองใช้ · เหลือ 23 วัน · [สมัครแพ็กเกจ]" — สี+ไอคอน+ข้อความ ตามกฎ UI
- เอกสารพิมพ์ทุกใบมีท้ายกระดาษ "เอกสารจากระบบทดลองใช้ ServicePro — service.garage-pro.net" (กันเอาไปใช้ฟรีจริง + โฆษณาตัวเอง)
- เมนูที่ล็อกยังเห็นอยู่พร้อมไอคอนกุญแจ → กดแล้วเจอหน้าอธิบายฟีเจอร์ (ภาพ+ข้อความ) + ปุ่ม "สนใจ ติดต่อเรา" (ส่งเข้า LINE กลุ่มขายพร้อมชื่ออู่)

---

## 5. ขอบเขตฟีเจอร์: พื้นฐาน (เปิด) vs ขั้นสูง (ล็อก) — ข้อเสนอเริ่มต้น ปรับได้

| กลุ่ม | ฟีเจอร์ | ทดลองใช้ | หมายเหตุ |
|---|---|---|---|
| งานหลัก | จ๊อบ (รายการ/ปฏิทิน/บอร์ด) · รับรถ+เช็คลิสต์ 20 ข้อ+รูป · ใบรับรถ · เลขไมล์ | ✅ | หัวใจของ test drive |
| | ใบเสนอราคา (เทมเพลต · รายการนอกแคตตาล็อก · ฉบับแก้ไข · ดำเนินการแทนลูกค้า · หลายใบต่อจ๊อบ) | ✅ | |
| | QC · ชำระเงิน · ใบแจ้งยอด/ใบเสร็จ · ส่งมอบ+ลายเซ็น+นัดครั้งถัดไป | ✅ | เอกสารมีลายน้ำทดลองใช้ |
| | แชทในจ๊อบ · แจ้งเตือน (เว็บ+แอป) | ✅ | |
| แอปช่าง | คิวงาน · จับเวลา · อนุมัติ/เซ็นบนมือถือ · QC · ส่งมอบ | ✅ | จุดขายที่คู่แข่งส่วนใหญ่ไม่มี |
| ข้อมูลหลัก | ลูกค้า/รถ · พนักงาน · สินค้า/บริการ · หมวดหมู่ · เทมเพลตใบเสนอราคา | ✅ จำกัดจำนวน | ดูลิมิตด้านล่าง |
| รายงาน | แดชบอร์ดวันนี้ · ประวัติรถ · รถใกล้ครบรอบบริการ | ✅ | ให้เห็นคุณค่าทันที |
| | ยอดขาย-ต้นทุน-กำไร · รอบเวลา (SLA) · เวลาทำงานช่าง · ขายหน้าร้าน · สต็อก · ส่งออก Excel | 🔒 | "ขั้นสูง" ไว้คุยขาย (เปิดยอดขาย-กำไรได้ถ้าอยากโชว์) |
| จัดซื้อ/สต็อก | PR/PO/GRN · สต็อก FIFO · ใบเบิกสินค้า · ซัพพลายเออร์ · คลัง | 🔒 | ต้องแก้ scope ซัพพลายเออร์/หมวดหมู่ก่อนอยู่ดี (§9) |
| POS | ขายสินค้าหน้าร้าน · โปรโมชัน · ใบกำกับภาษีเต็มรูป | 🔒 | ใบกำกับต้องมีเลขผู้เสียภาษีสาขา — ยังไม่ครบในแบบฟอร์มสมัคร |
| หลายสาขา | สลับสาขา · ผู้ดูแลระบบ | 🔒 | 1 อู่ = 1 สาขา |

**กลไก**: `PlanFeatures` (bitmask ต่อ workspace, ใส่ใน JWT) + attribute `[RequireFeature(Feature.Purchasing)]` บน controller/action → 403 `PLAN_FEATURE_LOCKED` + `messageTh`
· สาขา legacy (db2) = เปิดครบทุกอย่าง (พฤติกรรมเดิมไม่เปลี่ยน) · เว็บ: เมนูมีไอคอนกุญแจ + หน้า `LockedFeaturePage` · แอป: ซ่อนแท็บรายงานขั้นสูง
**ลิมิต (config `SelfServe:Trial:*` — ค่าเริ่มต้นเสนอ)**: พนักงาน 5 · สินค้า/บริการ 200 · ลูกค้า 200 · จ๊อบ 100 · ไฟล์แนบรวม 200 MB (FTP) · 1 อู่ต่ออีเมล
— ลิมิตมีไว้กันต้นทุน/สแปม ไม่ใช่กันการใช้งานปกติใน 30 วัน

---

## 6. กลไก 30 วัน

```
วันที่ 0        สมัคร → EndPackageDate = +30 วัน (config `SelfServe:Trial:Days`) → LINE แจ้งทีมขาย "สมัครใหม่: อู่ X · โทร Y · จังหวัด Z"
วันที่ 1–22     แถบ "ทดลองใช้ · เหลือ N วัน" (เทา)
วันที่ 23       แถบเปลี่ยนเป็นสีเตือน + อีเมล "เหลือ 7 วัน" + LINE ทีมขาย (โอกาสโทรติดตาม)
วันที่ 29       อีเมล "พรุ่งนี้หมดอายุ"
วันที่ 30       หมดอายุ: ทุก request ตอบ AUTH_REQUIRED/TRIAL_EXPIRED → หน้า "ทดลองใช้ครบแล้ว" (QR LINE @garagepro · โทร · ปุ่ม "ขอต่ออายุ/ขอใบเสนอราคา" → LINE ทีมขาย)
                ข้อมูลยังอยู่ครบ · เจ้าของยังดาวน์โหลด/พิมพ์เอกสารเก่าได้ (อ่านอย่างเดียว) — เสนอให้เปิดโหมดอ่านอย่างเดียว ไม่ใช่ปิดสนิท
วันที่ 30–120   ทีมขายต่ออายุ/เปลี่ยนเป็น paid ได้ (admin CLI/หน้าเล็กๆ) · อีเมลซ้ำ 1 ครั้งที่วัน 45
วันที่ 120      job รายวันลบข้อมูลอู่ (svc_* + dbo.* ของสาขานั้น + ไฟล์แนบบน FTP) — **คง `sp_Account.NormalizedEmail` ไว้ตลอด** กันสมัครซ้ำ
```
- การเช็คหมดอายุอยู่ที่ `SessionRevocationMiddleware` (มี cache 60 วิอยู่แล้ว) + `LoginAsync` — จุดเดียวกับ legacy → ภายหลังเปิดใช้กับสาขา legacy ได้ (คำถาม §12 ข้อ 12)
- ต้องเพิ่ม `IHostedService` รายวัน (ระบบไม่เคยมี): เตือน · ล้างข้อมูล · รีเฟรช blocklist · สรุปยอดสมัคร/หมดอายุส่ง LINE ทีมขาย
- ตัวเลขทุกตัว (30/7/90) เป็น config ไม่ hard-code

---

## 7. กันอีเมลซ้ำ/ปลอม (เบื้องต้น — ไม่ต้องสมบูรณ์แบบ แต่ต้องกันคนทั่วไปกลับมาสมัครใหม่ได้ง่ายๆ)

| ชั้น | กันอะไร | วิธี |
|---|---|---|
| 1. Normalize + unique ตลอดกาล | `A.b+1@gmail.com` = `ab@gmail.com` | lowercase · trim · Gmail/Googlemail ตัดจุดและ `+tag` · unique index บน `sp_Account.NormalizedEmail` และ**ไม่ลบแถวแม้ล้างข้อมูลอู่แล้ว** (tombstone) |
| 2. โดเมนอีเมลชั่วคราว | mailinator, 10minutemail, … | ตาราง `sp_BlockedDomain` นำเข้าจาก list สาธารณะ (หลายพันโดเมน) รีเฟรชรายเดือน + เพิ่มเอง · ปฏิเสธพร้อมข้อความแนะนำให้ใช้อีเมลจริง |
| 3. โดเมนไม่มีจริง | พิมพ์มั่ว | DNS lookup MX/A ของโดเมน (lib `DnsClient`) · cache ผล |
| 4. ยืนยันอีเมล | อีเมลที่ไม่ใช่ของตัวเอง | OTP 6 หลัก 10 นาที · ผิดเกิน 5 ครั้ง = ขอใหม่ · Google = ยืนยันแล้ว (`email_verified`) |
| 5. เบอร์โทรบังคับ | สมัครหลายรอบด้วยอีเมลต่างกัน | normalize เป็น E.164 · **ไม่บล็อกถ้าซ้ำ** แต่ติดธง "เคยสมัครแล้ว" ให้ทีมขายเห็น · (v2: SMS OTP = กันซ้ำได้ดีที่สุดในตลาดไทย แต่มีค่าใช้จ่าย) |
| 6. บอท | สคริปต์ยิงสมัคร | Cloudflare Turnstile (ฟรี · เว็บอยู่หลัง Cloudflare แล้ว) + honeypot field (แบบฟอร์มติดต่อใช้อยู่แล้ว) |
| 7. Rate limit | สมัครรัว | ต่อ IP 3 ครั้ง/ชม. · 10/วัน · เพดานรวม/วัน (ต่อยอด policy `public-contact` เดิม) · OTP 3 ครั้ง/อีเมล/ชม. |
| 8. บันทึกร่องรอย | ดูย้อนหลัง/ปรับกฎ | `sp_SignupAttempt`: IP (จาก `X-Forwarded-For` ที่ตั้งไว้แล้ว) · `CF-IPCountry` · user agent · ผลลัพธ์ → IP เดียวสร้างเกิน 2 อู่/30 วัน = ติดธงรีวิว |

ไม่ทำใน v1: device fingerprint (เปราะ + ประเด็นความเป็นส่วนตัว) · ยืนยันตัวตนด้วยบัตรเครดิต · ตรวจชื่ออู่ซ้ำ (อู่ชื่อซ้ำกันมีจริง)
PDPA: ต้องมีหน้า "นโยบายความเป็นส่วนตัว" + "ข้อกำหนดการใช้งาน" และช่องยินยอม (ใช้อีเมล/โทรติดต่อเพื่อการขาย) — ข้อความต้องมีเจ้าของ (คำถาม §12 ข้อ 10)

---

## 8. SEO · sitemap · เว็บไซต์

### 8.1 คีย์เวิร์ดและ intent
- หลัก (ตามคำขอ): **โปรแกรมสำหรับอู่บริการ** · **โปรแกรมสำหรับอู่เซอร์วิส** · เพิ่ม: โปรแกรมบริหารอู่ซ่อมรถ · ระบบจัดการอู่ · ซอฟต์แวร์อู่ซ่อมรถ · โปรแกรมอู่ซ่อมรถ ฟรี/ทดลองใช้ · แอปช่าง · ใบเสนอราคาอู่
- ระวัง: คำว่า "อู่เซอร์วิส"/"อู่บริการ" เดี่ยวๆ คนค้นหา**หาอู่ไปซ่อมรถ** (ผลค้นหาเป็นสมุดหน้าเหลือง/รายชื่ออู่) ไม่ใช่หาซอฟต์แวร์ → ทุกหน้าต้องผูกคำว่า โปรแกรม/ระบบ/ซอฟต์แวร์ เสมอ
- long-tail ที่เขียนเป็นบทความได้ (ไทยยังแข่งน้อย): "วิธีออกใบเสนอราคาอู่ซ่อมรถ" · "เช็คลิสต์รับรถเข้าอู่" · "แจ้งเตือนลูกค้าเช็คระยะ" · "จับเวลาช่าง คิดค่าแรง" · "เปิดอู่ซ่อมรถต้องใช้โปรแกรมอะไร"

### 8.2 โครงเว็บสาธารณะ (ทุกหน้า prerender เป็น HTML จริง · title/description/canonical/og ของตัวเอง)
| URL | หน้า | H1 (ร่าง) |
|---|---|---|
| `/` | หน้าแรก | โปรแกรมสำหรับอู่บริการ ครบตั้งแต่รับรถจนส่งมอบ — ทดลองใช้ฟรี 30 วัน |
| `/features` | ฟีเจอร์ (ขยายจาก 9 การ์ด + ภาพจริง) | ฟีเจอร์โปรแกรมอู่เซอร์วิส ServicePro |
| `/mobile-app` | แอปช่าง + ลิงก์ Play Store (ASO) | แอปสำหรับช่างในอู่ |
| `/pricing` | แพ็กเกจ (แม้เป็น "ติดต่อสอบถาม" ก็ต้องมีหน้าเพราะคนค้นคำว่า "ราคา") | ราคาและแพ็กเกจ |
| `/signup` | สมัครทดลองใช้ (§4) | — |
| `/about` · `/contact` | เดิม แยกเป็นหน้า | |
| `/privacy` · `/terms` | กฎหมาย (จำเป็นต่อ Google consent screen และ PDPA) | |
| `/blog/*` (เฟสถัดไป) | บทความ long-tail | |
title หน้าแรกเสนอ: `ServicePro | โปรแกรมสำหรับอู่บริการ ทดลองใช้ฟรี 30 วัน` (≤60 ตัวอักษร) · เมนูเปลี่ยนเป็นลิงก์จริง (URL เปลี่ยน) แทน scroll ภายในหน้า

### 8.3 เทคนิค
1. **โดเมนหลักเดียว** `service.garage-pro.net` → `gpservice.garage-pro.net` 301 ไปหาทุก path (API โดเมนเดิมไม่แตะ) + ออกใบรับรองจริงให้ `service.*` (ปิด RISK 526) + `<link rel=canonical>` ทุกหน้า
2. **Prerender + code-split** (เฟส 1 — ไม่เพิ่ม framework): สคริปต์หลัง `vite build` เปิดหน้าสาธารณะด้วย Playwright แล้วบันทึกเป็น `dist/<route>/index.html`
   · `React.lazy` แยก route ของแอป (จ๊อบ/จัดซื้อ/รายงาน/exceljs) ออกจาก bundle หน้าขาย → หน้าแรกเหลือ JS หลักสิบ KB
   · ทางเลือกเฟส 2 ถ้าจะทำบทความจริงจัง: แยกเว็บขายเป็น static site (Astro) container ใหม่ `web-marketing` — เนื้อหาเป็น markdown · sitemap อัตโนมัติ · JS ≈ 0
3. **`robots.txt` + `sitemap.xml`** เป็นไฟล์จริงใน `web/public/` (generate ตอน build ให้มี `lastmod`) · robots: allow หน้าสาธารณะ disallow `/jobs`,`/api`,`/customers`… · nginx เสิร์ฟไฟล์ก่อน fallback (ลำดับ `try_files` ถูกแล้ว แค่ไฟล์ยังไม่มี)
4. **Metadata**: og:url · og:locale `th_TH` · og:image 1200×630 (ภาพหน้าจอจริง) · twitter:card · JSON-LD `SoftwareApplication` (`offers` ทดลองใช้ฟรี) + `Organization` (โทร/LINE) + `FAQPage` ในหน้าแรก · `lang=th` · favicon.ico + manifest
5. **ประสิทธิภาพ (Core Web Vitals)**: `preconnect` + `font-display: swap` (หรือ self-host Noto Sans Thai) · gzip/brotli ใน nginx · `Cache-Control: no-cache` ให้ `index.html` · รูป mockup เป็น webp/lazy · `exceljs` โหลดเฉพาะหน้าส่งออก
6. **วัดผล**: Google Search Console (ยืนยันผ่าน DNS) · Bing Webmaster · analytics ที่ไม่ใช้คุกกี้ (Plausible/Umami) หรือ GA4 + cookie banner · event "signup สำเร็จ" · Google Business Profile ของ Armadillo Tech
7. **นอกเว็บ**: LINE OA rich menu "ทดลองใช้ฟรี" → `/signup` · โพสต์ FB/IG ชี้ URL canonical · คำอธิบายแอปใน Play Store ใส่คีย์เวิร์ดเดียวกัน + ลิงก์เว็บ (ตอนนี้ pubspec description ยัง "A new Flutter project.")

---

## 9. ต้องแก้ก่อนเปิดให้คนนอกสมัคร (gate — ไม่ขึ้นกับทางเลือกการเก็บข้อมูล)

| # | เรื่อง | ทำไมต้องก่อน | ขนาด |
|---|---|---|---|
| 1 | `svc_Receipt.DocumentNo` unique ทั้งระบบ แต่ counter ต่อสาขา | อู่ที่สองออกใบเสร็จใบแรกได้ 500 ทันที (กระทบสาขา 105 วันนี้ด้วยถ้าออกใบเสร็จ) → เปลี่ยน index เป็น (shard, สาขา, เลข) แบบ `svc_TaxInvoice` + migration | S |
| 2 | `svc_Supplier` / `svc_CatalogCategory` / `svc_CatalogItemSupplier` ไม่มี scope · `svc_Warehouse.Code` unique ทั้งระบบ | ข้อมูลรั่วข้ามอู่ · รหัสชนกัน → เพิ่ม shard/สาขา + unique ต่อสาขา + migration ย้ายของเดิมให้ 227/105 | M |
| 3 | audit การแยกข้อมูลตาม (shard, สาขา) ทุก endpoint + RBAC ของ Job/Intake (`[RequireShiftSession]` อย่างเดียว) | วันนี้ผู้ใช้ทุกคนเป็นพนักงานของเราที่รู้จัก พรุ่งนี้เป็นคนแปลกหน้า · ทดสอบด้วย 2 อู่ทดลองยิง id ของกันและกัน | M |
| 4 | rate limit `/auth/login` + nginx `limit_req` | เปิดช่องเดาสุ่มรหัสผ่านของบัญชี legacy (plaintext) | S |
| 5 | แยกฐาน dev ออกจาก production + ลบรหัส `sa` ออกจาก `appsettings.Development.json` + login ของ API ไม่ใช่ `sa` | การเพิ่ม DbContext/migration รอบนี้จะเสี่ยงมากถ้ายังรันลง production จากเครื่อง dev | S–M |
| 6 | เว็บแสดง `messageTh` เมื่อ 401 (ตอนนี้ redirect เงียบ) | ไม่งั้นบอก "หมดอายุ/ถูกระงับ" ไม่ได้ | S |
| 7 | exception จาก FTP/อื่นๆ ที่หลุดเป็น 500 ดิบ (RISK เดิม) | ผู้ทดลองเจอ 500 = เลิกใช้ | S |

---

## 10. ภาพรวมสถาปัตยกรรมที่เสนอ

```
ผู้ใช้ใหม่ ──► service.garage-pro.net (/signup, หน้าขาย prerender) ──► POST /api/v1/public/signup/{google|email,verify,workspace}
                                                                            │  Turnstile · rate limit · blocklist · MX · OTP
                                                                            ▼
                                   ┌──────────────── API เดียว (gpservice-api) ────────────────┐
  JWT shard=db2 (ลูกค้าเดิม) ─────►│  Garage (legacy, อ่าน) + GarageService (svc_*)            │ ◄── แอปมือถือตัวเดียวกัน
  JWT shard=sp1 (สมัครเอง) ──────►│  GarageSelfServe = dbo.* รูปร่าง Garage (เขียนได้)           │     (login เดิม · เลือก shard จาก username)
       plan=trial|paid             │                   + svc_* (migration เดิม) + sp_* (ใหม่)    │
                                   │  [RequireFeature] · แพ็กเกจหมดอายุที่ revocation middleware │
                                   │  IHostedService รายวัน: เตือน · ล้าง · สรุปส่ง LINE ทีมขาย    │
                                   └────────────────────────────────────────────────────────────┘
```
โค้ดที่คาดว่าจะแตะ (คร่าวๆ): `DependencyInjection.cs` (connection ตาม shard) · `AuthService`/`JwtTokenIssuer`/`CurrentUser`/`SessionRevocationMiddleware` ·
`StaffRepository` (ทางสร้างบัญชี sp1) · `Application/SelfServe/*` ใหม่ (signup · workspace · plan · starter data) · `Infrastructure/SelfServe/*` (DbContext · email · Google · DNS · Turnstile) ·
`API/Public/SignupController` · attribute `RequireFeature` · เว็บ: `features/signup/*` · `features/landing/*` แยกหน้า · `LockedFeaturePage` · แถบทดลองใช้ · `api/client.ts` (401) ·
แอป: แบนเนอร์ + ซ่อนแท็บ (ไม่บังคับใน v1) · `deploy/`: ฐานใหม่ · migration 2 target · nginx 301/gzip · prerender ใน Dockerfile.web

---

## 11. แผนงานคร่าวๆ (ลำดับ + ขนาด)

| เฟส | เนื้อหา | ขนาด | ขึ้นกับ |
|---|---|---|---|
| P0 | รีวิวเอกสารนี้ · ตอบ §12 · ตั้ง Google Cloud/ผู้ให้บริการอีเมล/Turnstile/LINE กลุ่มขาย | — | ผู้ใช้ |
| P1 | gate §9 ข้อ 1,2,4,5,6 | M | — |
| P2 | ฐาน `GarageSelfServe` (DDL shadow + synonym + migration svc_*/sp_*) · connection ตาม shard · login เลือก shard · hash รหัสผ่าน · claims/แพ็กเกจ/`RequireFeature` | L | P1 |
| P3 | signup API (Google/OTP/anti-abuse) · สร้าง workspace · ชุดเริ่มต้น · LINE แจ้งทีมขาย · `IHostedService` · admin CLI | L | P2 |
| P4 | เว็บ: หน้าสมัคร 2 จอ · เช็คลิสต์เริ่มต้น · แถบนับถอยหลัง · หน้าฟีเจอร์ล็อก · หน้าหมดอายุ · ลายน้ำเอกสาร · การ์ดรหัสช่าง | M–L | P3 |
| P5 | SEO/เว็บขาย: แยกหน้า · prerender + code-split · robots/sitemap · canonical/301 · metadata/JSON-LD · gzip · Search Console | M | ทำขนานได้ |
| P6 | audit isolation (§9 ข้อ 3) · ทดสอบ 2 อู่ทดลอง + 1 สาขา legacy พร้อมกัน บนเว็บ+แอปจริง · runbook ทีมขาย | M | P4 |
| P7 | แอป: แบนเนอร์ทดลองใช้ · ซ่อนแท็บล็อก · (ทางเลือก) Google sign-in | S | ปล่อยพร้อม build ถัดไป |

รวมประมาณ **6–8 สัปดาห์ / 1 คน full-stack** (ไม่รวมเนื้อหาการตลาด บทความ ภาพหน้าจอ และข้อความกฎหมาย) — เป็นการประเมินก่อนวางแผนละเอียด

---

## 12. คำถามที่ต้องตัดสินใจก่อนวางแผนจริง (พร้อมคำแนะนำ)

| # | คำถาม | แนะนำ |
|---|---|---|
| 1 | ลูกค้าที่จ่ายเงินแล้ว **อยู่ต่อใน sp1** หรือ **ย้ายเป็นสาขา GaragePro ใน db2**? (กำหนดว่า sp1 คือ "ฐานทดลอง" หรือ "ฐานลูกค้า ServicePro ยุคใหม่") | อยู่ต่อใน sp1 (§2.4 ทาง B) |
| 2 | ฐานข้อมูล: ทางเลือก C (ฐานใหม่ครบทุกส่วน) หรือ B (svc_* ปนใน GarageService)? · วางบน 10.10.4.11 หรือ SQL Express บนเครื่อง API? | C บน 10.10.4.11 |
| 3 | วิธีสมัคร: Google + อีเมล ทั้งคู่? ผู้ให้บริการอีเมลเจ้าไหน (Brevo ฟรี 300/วัน · Resend · SES)? ใครเป็นเจ้าของ Google Cloud project? | ทั้งคู่ · Brevo หรือ SES |
| 4 | ฟีเจอร์ที่ล็อก/ลิมิตตามตาราง §5 ใช่ไหม? เปิดรายงานยอดขาย-กำไรด้วยหรือไม่? | ตาม §5 · ล็อกรายงานขั้นสูง |
| 5 | ข้อมูลตัวอย่างเริ่มต้น: ติ๊กเป็นค่าเริ่มต้น? รายการแคตตาล็อกเริ่มต้น ~40 รายการให้ใครคัด? | ติ๊กไว้ · ผมร่างให้ ผู้ใช้รีวิว |
| 6 | 30 วัน แล้วหมดอายุ = ปิดสนิท หรือ อ่านอย่างเดียว? เก็บข้อมูลหลังหมดอายุกี่วันก่อนลบ (เสนอ 90)? | อ่านอย่างเดียว · 90 วัน |
| 7 | แอปมือถือเปิดให้อู่ทดลองใช้ตั้งแต่วันแรก? (ใช้ build บน store ได้เลย ไม่ต้องแก้แอป) | ใช่ — เป็นจุดขายหลัก |
| 8 | เอกสารพิมพ์ระหว่างทดลองมีลายน้ำ "ทดลองใช้"? | ใช่ |
| 9 | โดเมนหลัก `service.garage-pro.net` + 301 จาก `gpservice`? | ใช่ |
| 10 | ใครเขียน/รับผิดชอบข้อความนโยบายความเป็นส่วนตัว + ข้อกำหนด (PDPA)? ใครรับ lead ใน LINE กลุ่มขายและ SLA ตอบ? | ต้องระบุ |
| 11 | ลำดับ: ทำ gate §9 ก่อนทั้งหมด หรือทำขนานกับ P2? (ข้อ 1,2 มี migration ลง production) | gate ก่อน |
| 12 | ให้ระบบเช็ค `EndPackageDate` ของสาขา legacy ด้วยกติกาเดียวกันเลยหรือไม่ (วันนี้ไม่เช็ค)? | แยกตัดสินใจทีหลัง — ค่าเริ่มต้น: ไม่ |

---

## 13. ความเสี่ยง/ข้อจำกัดที่รู้ตั้งแต่ตอนนี้
- **เปิดช่องทางสาธารณะบน API ที่ออกแบบมาเป็นระบบภายใน** — ความเสี่ยงหลักไม่ใช่ตัว trial แต่คือช่องโหว่เดิม (RBAC/isolation/login ไม่มี rate limit/secrets ใน git) จึงต้องทำ §9 ก่อน
- ฐาน self-serve อยู่บน instance เดียวกับ legacy ที่มี lock convoy อยู่แล้ว — โหลดจาก trial เล็กมาก แต่ถ้าโดนสแปมหนัก rate limit คือด่านแรก และย้ายฐานออกได้ทั้งก้อน
- ตารางรูปร่างเหมือน Garage = ผูกกับ SQL เดิมของ 5 คลาส ถ้าวันหน้า SQL เหล่านั้นแตะตาราง/คอลัมน์เพิ่ม ต้องเพิ่มใน DDL ของ sp1 ด้วย (มีเทสต์ smoke ต่อ shard ช่วยจับได้)
- Google OAuth ต้องมีหน้า privacy/terms จริงก่อนจึงจะเปิดใช้ (consent screen) · การส่งอีเมลใหม่ๆ จากโดเมนที่ไม่เคยส่งอาจตกสแปมช่วงแรก (warm-up)
- SEO เห็นผลช้า (สัปดาห์–เดือน) และขึ้นกับเนื้อหาต่อเนื่อง — โครงสร้างทางเทคนิคใน §8 เป็นแค่พื้นฐานที่ "ไม่ขวาง" Google
- ยังไม่ได้ประเมิน: ต้นทุน SMS OTP (ถ้าจะใช้ v2) · ค่าใช้จ่ายอีเมล · ค่า Cloudflare (Turnstile ฟรี) · แผนราคาที่จะขาย (หน้า /pricing ต้องมีเนื้อหา)
