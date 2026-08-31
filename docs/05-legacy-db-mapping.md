# 05 · Legacy Garage DB — สิ่งที่ reuse ได้ / ไม่ได้

> **แหล่งข้อมูล:** EF scaffolded entities 189 ตัวใน `AMD.GaragePro.Admin/backend/.../Entities/Scaffolded/` + `docs/api-pjquotation.md` + `appsettings.json`
> **สถานะ:** ✅ ตรวจกับ DB จริงแล้ว (ผ่าน Garage Pro VPN · read-only ทุก query) — ตัวเลขในเอกสารนี้เป็นของจริง ณ 2026-08-19

---

## 1. ⚠️ ข้อค้นพบที่กระทบสถาปัตยกรรมมากที่สุด: **GaragePro เป็น multi-tenant SaaS แบ่ง shard**

> **แก้จากที่เข้าใจตอนแรก:** ไม่ใช่ "1 สาขา = 1 database" — `Branch` เป็น **row ในตาราง** (147 / 325 แถว) แต่ข้อมูลถูก**แบ่งไว้คนละ instance** และ `Branch.Id` **ไม่ unique ข้าม shard**

| | `db1` · 10.10.4.14 | `db2` · 10.10.4.11 | `maindb` · 10.10.4.16 |
|---|---|---|---|
| อู่ (Branch) | **147** | **325** | 325 |
| งานสะสม (PjcarPickUp) | 209,971 | — | 388,484 |
| งานปี 2026 | 16,331 | 69,848 | 69,848 |
| งานแรกสุด | 2018-05-15 | 2018-05-15 | — |
| งานล่าสุด | 2026-08-18 17:40 | 2026-08-18 19:54 | 2026-08-18 19:54 |

- **`db2` กับ `maindb` เลขตรงกันทุกตัว = ฐานเดียวกัน** (replica / AG listener คนละ host) — ไม่ใช่ shard ที่สาม
- **`db1` เป็นคนละ shard จริง** — ทั้งคู่ยัง live (มีงานเข้าวันเดียวกัน)
- **`Branch.Id` ชนกัน:** Id 30 → db1 = "บริษัท เอ็มเอ็มเอ็ม ออโต้ กรุ๊ป" · db2 = "บริษัท อู่ พี.ซี.ซี" (Id 13/27/29/32/50/55 ก็ซ้ำแต่เป็นคนละอู่บางตัว)
- นี่คือเหตุผลที่ API เดิมต้องมี `ScanSearchOrder: ["db1","db2"]` — ค้นทีละ shard จนเจอ

```jsonc
"ConnectionStrings": { "db1": "Server=10.10.4.14;Database=Garage",
                       "db2": "Server=10.10.4.11;Database=Garage" },
"Databases": { "Default":"db1", "Allowed":["db1","db2","db1-dev","db2-dev"],
               "ScanSearchOrder":["db1","db2"] }
// เลือก shard ด้วย header X-Database-Id ต่อ request
```

### กระทบอะไรบ้าง
| เรื่อง | ผล |
|---|---|
| Primary key ไม่ unique ข้าม shard | ทุก reference ต้องเป็นคู่ **(shardKey, id)** ไม่ใช่ id เดี่ยว |
| ตารางใหม่ของเรา | `svc_Job` เก็บ `LegacyShardKey` + `BranchId` + `CustomerId` + `VehicleId` (อ้างอิงอย่างเดียว ไม่ใช่ FK ข้าม DB) — ตารางอื่นที่ผูกกับจ๊อบ (`Quotation`/`IntakeChecklist`/`Attachment`/`ActivityEvent`) ใช้ `JobId` (Guid) เป็น FK ตรงไปที่ `svc_Job` แทน composite เดิม (เปลี่ยนเมื่อ 2026-08-31 ดู §5) |
| หน้า "เลือกสาขา" / dashboard ข้ามอู่ | fan-out query ทุก shard แล้ว merge ที่ application |
| Connection/transaction | ทุก service ต้องรับ shardKey จาก session context |

**→ `IShardResolver` ต้องเป็นชิ้นแรกๆ ของ P0** ใส่ทีหลังคือ rewrite

---

## 2. Legacy DB เป็นระบบ **อู่สีตัวถัง + งานเคลม** ไม่ใช่ service ทั่วไป

ยืนยันจากข้อมูลจริงใน `Pjstatus` (61 สถานะ):
```
รื้อ · เคาะ · โป๊ว · เตรียมพื้น · ผสมสี · พ่นสี · ขัดยา · ประกอบ · ล้าง
รอส่งอีเคลม · รอประกันอนุมัติ · รออะไหล่ ประกันจัด · รออะไหล่ อู่จัด
ลูกหนี้ไม่เกิน 15/30/45 วัน · ลูกหนี้ 60 วันขึ้นไป
```
`PjquotationStatus` มีแค่ 3 ค่า: **รอส่ง E-Claim · ส่ง E-Claim แล้ว · ปิดราคา**

`CodeOffer` / `CodeRepair` (รหัสประเมินความเสียหาย) — ทั้งหมดเป็นภาษาของงานเคาะ-พ่นสี:
```
R ขีดข่วน · B1 เบา(0-10%) · B2 กลาง(10-20%) · B3 หนัก(20-30%) · B4–B10 บุบ 30-100%
SB3 เคาะแทนเปลี่ยน · P ขัดสี · C1 แท้ทำสี · C3 เทียมทำสี · C01 แท้ไม่ทำสี
W2 รอประกันอนุมัติ · B11/B12 ส่งไปทำข้างนอก
```

`Gpscode` (19,823 รหัส) = **แคตตาล็อกอะไหล่ตัวถัง** — จัดกลุ่มตาม `Gpstype`:
```
แค๊ปหลัง-กระบะท้าย 1,675 · ช่วงล่าง 1,533 · ประตูหน้า 934 · แผงเหล็กหลัง 853
กันชนหน้า 718 · บังโคลนหน้า 710 · เครื่องยนต์ 688 · เสา 542 · กันชนหลัง 534
ตัวอย่าง: "พลาสติกปิดใต้กันชนหน้า" · "คิ้วโครเมี่ยมคาดหน้ากระจัง" · "นอตยึดกันชนหน้า"
```

`Pjquotation` มี field ชุด **SO / IV / RE / H2 / HS** (เลขเอกสารต่อบริษัทประกัน), `SendInsuranceDate`, `DealPayDate`, `WithdrawalDate`, `MarketInComeValue/OutComeValue` — flow **เคลมประกัน + วางบิลบริษัทประกัน** ล้วน

**Design ใหม่เป็น general service** (เช็คระยะ 40,000 กม. · ผ้าเบรก · กรองแอร์ · น้ำมันเครื่อง · แบตเตอรี่) — **ไม่มีในโครงเดิมเลย** ทั้งสถานะ รหัสงาน และแคตตาล็อกอะไหล่
> ข้อยกเว้น: `Gpstype` "ช่วงล่าง" (1,970 รหัส) และ "เครื่องยนต์" (688) พอมี overlap — ใช้อ้างอิงได้บางส่วน แต่ไม่มีผ้าเบรก/กรอง/น้ำมันเครื่อง

---

## 3. Mapping — ตารางต่อตาราง

### ✅ Reuse ได้เกือบทั้งก้อน (master data) — ตัวเลขจริงจาก `db1`
| Legacy | แถว (db1) | ใช้แทน | หมายเหตุ |
|---|---|---|---|
| `Customer`, `CarCustomer` | **151,394** | Customer | **แก้ OQ#1 ได้เลย** — master data มีอยู่แล้ว ไม่ต้องสร้างใหม่ |
| `Car`, `BrandCar`, `CarModel`, `CarNickName`, `CarType`, `Color`, `Year`, `Gear`, `DriveSystem` | **164,732** | Vehicle | ครบกว่าที่ design ต้องการ |
| `Staff`, `User`, `Position`, `Department`, `Sector`, `StaffSkillLevel` | **4,847** | Employee + Role | `StaffSkillLevel` ใช้ทำระดับคอมมิชชัน 8/10/12% ได้ |
| `Branch`, `BranchGroup`, `BranchType` | **147** (db2: 325) | Branch | ⚠️ Id ไม่ unique ข้าม shard (ข้อ 1) |
| `Province`, `Amphure`, `District`, `Zipcode`, `Geography` | — | ที่อยู่ | |
| `Supply`, `SuppliesType`, `SuppliesGroup`, `Manufacturer`, `Unit` | — | Supplier / Part master | |
| `Gpscode`, `Gpstype`, `Gpsposition`, `CatalogPart`, `CheckPartsPrice` | **19,823** | Catalog | ⚠️ เป็นอะไหล่**ตัวถัง** — service part ต้องสร้างแคตตาล็อกใหม่ |
| `Gpsinventory` | **1,704,840** | สต็อก/รับของ | ผูกกับ `PjquotationDetailId` (จองต่อบรรทัด) |

### 🟡 Reuse ได้แต่ต้อง extend (โครงตรง แต่ field ไม่พอ)
| Legacy | Design ต้องการเพิ่ม |
|---|---|
| `PjcarPickUp` (= Job) | JobStatus 10 ตัวใหม่ · `promiseAt` · mileage · 5 รูปรับรถ · ลายเซ็นรับรถ · ระดับน้ำมัน · เอกสาร/อุปกรณ์ในรถ |
| `Pjquotation` | **`version` + `supersededBy`** (สำคัญที่สุด — เดิมมีแค่ `EditTimes`) · `validUntil` · promotion · lock/concurrent-edit · **ApprovalRecord + ลายเซ็นผูก version** |
| `PjquotationDetail` | `source` (ลูกค้าขอ/ช่างแนะนำ) · `inspectionItemId` · `rejectReason` · margin · `assignedTechnicianId` |
| `Gpsinventory`, `GpsoutStock`, `Storsge` | **`reserved` แยกจาก `onHand`** · `damaged`/quarantine · `rop`/`pq` · ledger ที่มี before/after ทุกแถว |
| `Prsupply`, `PrsuppliesDetail`, `PurchaseStatus`, `SuppliesRequisition` | PO 7 สถานะ · GRN รับบางส่วน/ของชำรุด · costVariance + ผู้อนุมัติ |
| `Pjqc`, `Pjqctype` | เช็กลิสต์ 6 ข้อรายข้อ · ทดลองขับ (km) · ตีกลับ + priority · รูป |
| `PjtaxReceipt`, `PjtaxReceiptType` | แยก RC/IV ตาม design · reprint/void/credit note + เหตุผล+ผู้อนุมัติ |
| `Pjcomment`, `PjcarPickUpLog`, `EntityLog`, `ChangeLog` | ActivityEvent ที่มี **`source` (มือถือ/เว็บ/ระบบ)** |

### ✅ มีของดีที่ design ไม่ได้คิดถึง — ควรเอามาใช้
| Legacy | ทำไมมีค่า |
|---|---|
| `PjquotationDetail.IsUseEstimate/IsPending/IsApproved/IsCanceled` + `*Status` | **line-level approval มีอยู่แล้ว** — ตรงกับหัวใจของ design ใหม่พอดี |
| `IsPrcreated → IsPrapproved → IsPocreated → IsPoapproved → IsDoreceived` | **PR→PO→DO flow ครบ** ต่อ line ของใบเสนอราคา — ดีกว่าที่ design วาดไว้ |
| `EndWagePrice`, `EndPartsPrice`, `EndPriceStatus` | ≈ reconciliation (ราคาปิดงาน vs ที่เสนอ) ของ POS |
| `Point`, `RepairTypeNpoint`, `StaffRepairTypeNpoint`, `ReportStaffPerformanceRecord` | **ระบบคะแนน/คอมมิชชันช่างมีอยู่แล้ว** — ตอบ OQ#8 ได้ |
| `PjquotationDetailLog` | audit ระดับบรรทัดของใบเสนอราคา |
| `TimeAttendance` | กะ/เวลาเข้างาน — ต่อกับ ShiftSession ได้ |
| `CustomerLineAccount`, `CustomerLineLog`, `LineOasendNotificationResponse` | ช่องทางแจ้งลูกค้าผ่าน LINE มีอยู่แล้ว |

### ❌ ไม่มีในเดิม — ต้องสร้างใหม่ทั้งหมด
| ต้องสร้าง | เหตุผล |
|---|---|
| **Inspection 8 หมวด 31 รายการ** (ok/watch/fix/na + severity + urgency + naReason) | เดิมเป็น damage-based (`PjdamagedItemType`, `DamageFix`) คนละแนวคิด |
| **IntakeRecord** (อาการ, ไฟเตือน, ระดับน้ำมัน, เอกสาร/อุปกรณ์ในรถ, 5 มุมบังคับ, ลายเซ็นรับรถ) | เดิมเก็บรูปความเสียหาย ไม่ใช่สภาพรถขณะรับ |
| **RepairTask + TimeLog + รูปก่อน/หลังต่อรายการ** | `Repair`/`RepairDetail` เป็น lookup ประเภทงาน ไม่ใช่ task ที่จับเวลาได้ |
| **Sync queue / offline** (TMP-, conflict, 5 สถานะ) | ไม่มีร่องรอยเลย |
| **POS รับชำระ 4 ช่องทาง + split + idempotency + `checking`** | เดิมเป็น `ReceiveMoney*` แบบเคลมประกัน ไม่มีหน้าร้านรับเงินสด/QR |
| **HandoverRecord** | design ก็ยังไม่ได้ออกแบบ (GAP สูง) |
| **Quotation version / superseded / ลายเซ็นผูก version** | เดิมมีแค่ `EditTimes` นับจำนวนครั้งที่แก้ |

---

## 4. ผลต่อแผน — flow แรก "เสนอราคา + แสดงเอกสาร"

ตามที่เลือกไว้ P1 = quotation ไม่ใช่ intake นั่นแปลว่า:

**ต้องมีข้อมูลตั้งต้นก่อน** — quotation ผูกกับ `PjcarPickUp` (งาน) ที่ต้องมีอยู่แล้ว
→ P1 อ่านงาน + ลูกค้า + รถ + catalog อะไหล่ จาก legacy DB (read-only) แล้วเขียน quotation ลง **ตารางใหม่ของเรา**

### 3 ทางเลือกที่ต้องเลือก (ดูข้อ 5)
| | อ่าน legacy | เขียน quotation |
|---|---|---|
| **A · ต่อยอดตารางเดิม** | legacy | `Pjquotation` + `PjquotationDetail` เดิม (เพิ่ม column) |
| **B · Hybrid** ⭐ | legacy (read-only) | ตารางใหม่ใน DB เดียวกัน (`svc_*` prefix) |
| **C · DB ใหม่แยก** | sync/mirror มา | DB ใหม่ทั้งหมด |

---

## 5. ⚠️ ความเสี่ยงที่ต้องรู้ก่อนแตะ legacy DB

จาก [`garage-db-remediation-plan.md`](../../garage-db-remediation-plan.md) (ประเมิน 28 ก.ค. 2569):

| ปัญหา | ตัวเลข |
|---|---|
| **Lock convoy บน `PJCarPickUp`** | lock wait = **92–96% ของ wait ทั้งระบบ** · UPDATE ถูก timeout ~410 ครั้งใน 45 นาที |
| Lock escalation เป็น table lock | สำเร็จ **1,375,006 ครั้ง** |
| EF6 อ่านเกิน (ดึง LOB มาด้วย) | อ่านแถวเดียว = **2,300 logical reads (~18 MB)** |
| Index ซ้ำซ้อน | **15 nonclustered index** บนตารางเดียว |
| Compatibility level | **100 (SQL 2008)** — เสีย Adaptive QP / Batch Mode |
| Read Committed Snapshot | **OFF** — reader ถือ S lock ชนกับ writer ตรงๆ |
| ขนาด DB / buffer pool | 206 GB / 23 GB (cache ได้ 11%) |
| Login ที่แอปใช้ | **`sa`** |

**ข้อสรุปเดิม:** `PjcarPickUp` เป็นตารางที่ร้อนที่สุดและเปราะที่สุดในระบบ — หลีกเลี่ยง write ใหม่โดยทั่วไป
→ ชั่งน้ำหนักไปทาง **ตัวเลือก B (Hybrid)**: อ่าน legacy อย่างเดียว (`WITH (NOLOCK)` หรือ Dapper read-only + snapshot) เขียนลงตารางใหม่ของเรา

**ข้อยกเว้น 2026-08-26 (ถูกยกเลิก 2026-08-31):** เดิมผู้ใช้อนุมัติให้หน้า `/jobs` เปิดจ๊อบตาม
`ProjectAdd.aspx` ลง legacy โดยตรงผ่าน writer เฉพาะทางและ transaction สั้นเท่านั้น
**ตัดสินใจใหม่แล้ว: ยกเลิกข้อยกเว้นนี้ทั้งหมด** — จ๊อบไม่เขียนกลับ legacy อีกต่อไป
`svc_Job` เป็นแหล่งข้อมูลจ๊อบเพียงแหล่งเดียว เก็บ `CustomerId`/`VehicleId`/`BranchId` เป็นเพียง
id อ้างอิงที่อ่าน (ไม่เขียน) จาก legacy ผ่าน `CustomerVehicleService` ที่มีอยู่แล้ว (คนละ flow กับ
`LegacyJobWriter`/`ProjectAdd` ที่ถูกลบทั้งหมด) `Quotation`/`IntakeChecklist`/`Attachment`/
`ActivityEvent` ผูกกับ `Job.Id` (Guid) โดยตรงเป็น FK ธรรมดา แทน composite
`(LegacyShardKey, LegacyBranchId, LegacyJobId)` เดิม — legacy DB กลับสู่สถานะ read-only ล้วน
ตามข้อ 5 เดิมทุกโมดูล ไม่มีข้อยกเว้นอีก

### 🔐 หมายเหตุความปลอดภัย
`AMD.GaragePro.Admin/backend/AMD.GaragePro.Admin.API/appsettings.json` มี **รหัส `sa` ของ SQL, รหัส FTP, JWT signing key และ API key เป็น plaintext** อยู่ใน repo — ถ้า repo นี้ push ขึ้น remote ควรถอดออกเป็น user-secrets / env var และหมุนรหัสใหม่ ระบบใหม่ไม่ควรทำตาม pattern นี้

---

## 6. ข้อสรุปสำหรับ flow แรก (เสนอราคา + เอกสาร)

**ตัดสินใจแล้ว: ตัวเลือก B · Hybrid** — หลักฐานสนับสนุนครบ

| | ทำอะไร |
|---|---|
| **อ่านจาก legacy (read-only)** | `Customer` · `Car` + brand/model/color · `Branch` · `Staff` · `PjcarPickUp` (งาน) |
| **เขียนลงตารางใหม่** | `svc_Quotation` · `svc_QuotationLine` · `svc_QuotationApproval` · `svc_CatalogItem` · `svc_ActivityEvent` |
| **แคตตาล็อก service** | สร้างใหม่ทั้งหมด (`svc_CatalogItem`) — `Gpscode` เป็นอะไหล่ตัวถัง ใช้แทนไม่ได้ |
| **ผูกกลับ legacy** | ทุกตารางใหม่เก็บ `LegacyShardKey` + `LegacyBranchId` + `LegacyJobId` เป็น composite |

**เหตุผลที่ไม่ต่อยอด `Pjquotation` เดิม**
1. `PjquotationStatus` มีแค่ 3 ค่า (E-Claim) — รองรับ 7 สถานะของ design ไม่ได้ และแก้แล้วกระทบระบบเดิมที่ยัง live
2. `PjquotationDetail` บังคับ `GpscodeId` + `CodeOfferId` + `CodeRepairId` + `DamageLevelId` — service line ไม่มีค่าพวกนี้
3. 4.26 ล้านแถวใน `PjquotationDetail` + lock convoy บน `PjcarPickUp` — เพิ่ม write ลงไปคือเพิ่มความเสี่ยงให้ระบบที่มีปัญหาอยู่แล้ว
4. ทั้ง 2 shard ยัง live (มีงานเข้าทุกวัน) — DDL บนตารางใหญ่ต้องมี downtime window

**สิ่งที่ยัง reuse ได้เต็มที่:** แนวคิด line-level approval (`IsPending/IsApproved/IsCanceled`), PR→PO→DO, และระบบคะแนนช่าง (`Point`) — เอา*แนวคิด*มาใช้ในตารางใหม่ ไม่ใช่เขียนทับตารางเดิม

---

## 7. คำถามที่ยังต้องตอบ (จากข้อมูลจริงที่เพิ่งเห็น)

| # | คำถาม | ทำไมสำคัญ |
|---|---|---|
| A | ระบบใหม่นี้ทำให้ **อู่ไหน / shard ไหน** | `db1` (147 อู่) หรือ `db2` (325 อู่) หรือทั้งคู่ — กำหนดว่าต้องทำ multi-shard ตั้งแต่ P1 ไหม |
| B | อู่ที่จะใช้ระบบใหม่ ทำงาน service ด้วยหรือทำแต่งานเคลม | ถ้าทำแต่เคลม แคตตาล็อก service ต้องสร้างจากศูนย์และไม่มีข้อมูลตั้งต้น |
| C | ตารางใหม่ `svc_*` วางใน `Garage` DB เดิม หรือ DB ใหม่บน instance เดียวกัน | DB เดิม 206 GB · แยก DB ทำ backup/restore ง่ายกว่า แต่ join ข้าม DB ต้องใช้ 3-part name |
| D | `db2` กับ `maindb` — ตัวไหนคือ primary ที่ควรเขียน | เลขตรงกันเป๊ะ ถ้าเป็น AG replica การเขียนผิดตัวจะ fail |
