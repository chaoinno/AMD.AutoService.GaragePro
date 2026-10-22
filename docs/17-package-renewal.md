# 17 · แจ้งชำระเงินและต่ออายุแพ็กเกจ (Package Renewal)

> สถานะ: **พัฒนาเสร็จบน branch · รอรีวิว · ยังไม่ merge/deploy** (2026-10-22 · แบ่ง PR ตามหัวข้อ 10 · ผลตรวจหัวข้อ 12) · หัวข้อ 5 และ 8.6 เป็น **สัญญา API** ที่ทุก PR ยึด
> ที่มา: เอกสารสรุป flow ต่ออายุของ GaragePro Enterprise (`Package-Renewal-Flow*.md` ที่เพื่อนในทีมส่งมา ไม่ได้ commit เข้า repo)
> อ่านคู่กับ [docs/12](12-self-serve-trial-proposal.md) (self-serve) และ [docs/03 §16](03-api-contract.md)

## 0. สรุปสั้น

อู่ ServicePro ที่สมัครเอง (shard `sp1`) แจ้งชำระเงินพร้อมสลิปได้เองจากเว็บและแอป ทีมงานตรวจแล้วกดอนุมัติ
ระบบต่อวันหมดอายุให้ทันทีในคำสั่งเดียว และหน้า `/branches` ของ GaragePro Admin แสดงอายุและสถานะของทุกอู่ ServicePro

ใช้ขั้นตอนงานแบบเดียวกับ GaragePro Enterprise (แจ้งชำระ → ทีมงานตรวจสลิป → ขยายวัน) แต่ไม่ใช้ตาราง API
หรือหลังบ้านของระบบเดิม เพราะระบบเดิมมีบั๊กที่ยืนยันจากโค้ดแล้วหลายข้อ (หัวข้อ 9) และมองไม่เห็นฐาน `GarageSelfServe`

### การตัดสินใจ (ผู้ใช้ยืนยัน 2026-10-21: "เอาตามคำแนะนำทั้งหมด" + แอปมือถือด้วย)

| # | เรื่อง | ตัดสินใจ |
|---|---|---|
| D1 | ขอบเขต | **อู่ sp1 เท่านั้น** · สาขา GaragePro ใน db2 (รวมกลุ่ม Service) ใช้กระบวนการเดิมของ GaragePro ต่อ เพราะการอนุมัติต้องเขียน `Garage.Branch` ซึ่งขัดนโยบายอ่านอย่างเดียว และ ServicePro ไม่บังคับวันหมดอายุกับ db2 (docs/12 ข้อ 12) |
| D2 | ที่เก็บข้อมูล | **ตารางใหม่ของเรา** `sp_PackagePayment` ในฐาน `GarageSelfServe` ไม่เขียน `BranchPaymentHistory` เดิม · รหัสสถานะเทียบกับของเดิมได้ (หัวข้อ 3) |
| D3 | ทีมงานอนุมัติที่ไหน | **ระยะแรก `spadmin`** + ข้อความ LINE ที่มีรูปสลิป · ระยะถัดไปอนุมัติจากหน้า GaragePro Admin (หัวข้อ 8 ระยะ B) |
| D4 | สูตรวัน | ต่อด้วย `AddMonths(N)` จากวันหมดเดิมที่ปัดขึ้นเป็นเที่ยงคืนไทย **ไม่บวก 1 วัน** · จ่ายหลังหมดอายุ นับจากวันอนุมัติ · คำนวณตอนอนุมัติ ไม่ใช่ตอนแจ้ง (หัวข้อ 4) |
| D5 | ราคาและบัญชีรับเงิน | อยู่ใน config (`SelfServe:Package`) ยังไม่มีค่าจริง ต้องได้จากฝ่ายขายก่อนเปิดใช้ (หัวข้อ 11) · trial ที่จ่ายแล้วเปลี่ยนเป็น `paid` |
| D6 | ช่องทางแจ้งชำระ | **เว็บและแอปมือถือ** |
| D8 | session เมื่อแพ็กเกจเปลี่ยน (ผู้ใช้เลือก 2026-10-21) | **ไม่บังคับ login ใหม่เมื่อสิทธิ์เพิ่มขึ้น** (trial→paid · อ่านอย่างเดียว→เขียนได้ · วันหมดเลื่อนออก) · ยังบังคับเมื่อสิทธิ์ลดลง (ล็อก · ตัดวันจนอ่านอย่างเดียว · paid→trial) (หัวข้อ 4.1) |
| D7 | GaragePro Admin `/branches` (คำขอเพิ่ม) | แสดงอายุและสถานะของทุก site · แท็บเดิมเพิ่มสถานะแพ็กเกจ + สลิป Enterprise ที่รอตรวจ · แท็บใหม่ "ServicePro" รวมสาขากลุ่ม Service ใน db2 กับอู่ sp1 ข้อมูลจาก API ภายในของ ServicePro (หัวข้อ 8) |

---

## 1. ข้อเท็จจริงจากระบบ (ตรวจ 2026-10-21)

### 1.1 ระบบเดิม (GaragePro Enterprise)

- ตาราง `BranchPaymentHistory` มีอยู่ใน Garage ของ db2 (zone 2 ของ WebApi2024 = db2) · db1 มีตารางแต่ว่าง
- รายการทั้งหมด 145 รายการ (ก.ค. 2025 ถึง ต.ค. 2026) เป็นของกลุ่ม Enterprise 142 และ Basic 3 · **กลุ่ม Service (7) ไม่เคยมีรายการเลย**
- ลูกค้าส่วนใหญ่ต่อทีละ 1 เดือน · วันหมดใหม่ = วันหมดเดิมบวกจำนวนเดือน (`2026-10-03 00:00` → `2026-11-03 00:00`) ไม่มีวันขาดหรือซ้อน
- `EndPackageDate` เกือบทั้งหมดเป็นเวลา 00:00 (เวลาไทย ไม่มีโซน) ตรงกับที่เราตีความว่าเป็น "เที่ยงคืนเริ่มวันที่หมดสิทธิ์"
  (`SelfServeAdminService.UntilToEndsAtUtc`) · หน้าจอเดิมแสดง "วันเริ่มต่อ = End + 1" แต่ข้อมูลจริงไม่ได้ใช้สูตรนั้น
- มี `PayDate` ที่ปีเป็น 2569 อย่างน้อย 1 รายการ (ผู้ใช้กรอกปี พ.ศ.) → ฟอร์มของเราต้องเก็บ ค.ศ. เสมอ (หัวข้อ 6)

### 1.2 ระบบเรา

| ส่วน | มีแล้ว | ยังขาด |
|---|---|---|
| วันหมดอายุ | `PlanPolicy` อ่านอย่างเดียวตั้งแต่ `EndPackageDate` · ล็อกที่ +90 วัน (`Trial:ReadOnlyDays`) | อู่ `paid` ไม่มีแถบเตือนเลย (`trialTone` คืน `none`) และงานรายวันข้าม paid ทั้งหมด (ไม่มีอีเมลเตือนก่อนหมด) |
| ลูกค้าขอต่อ | ปุ่ม "ขอต่ออายุ" ส่งข้อความเข้า LINE (`SelfServeStatusService.RequestExtensionAsync`) | สลิป · ยอดเงิน · จำนวนเดือน · ประวัติ |
| ทีมงานต่อวัน | `spadmin extend` / `set-plan` → `SelfServeAdminRepository.ApplyPackageChangeAsync` เขียน Branch + `sp_Workspace` + `sp_WorkspaceAudit` ใน transaction เดียว | ผูกการต่อวันกับรายการชำระ · ปฏิเสธพร้อมเหตุผล |
| ไฟล์ | `IAttachmentStorage` (FTP · ตรวจชนิด/ขนาด 15 MB · resize · SHA-256) | `SaveAsync` บังคับ `jobId` → ต้องเพิ่มทางเก็บไฟล์ที่ไม่ผูกจ๊อบ |
| production | sp1 มีแค่อู่ทดสอบที่ purge แล้ว (SP1001) | ยังไม่มีลูกค้าจริง จึงเปิดฟีเจอร์นี้ได้โดยไม่ต้องย้ายข้อมูล |

---

## 2. กฎธุรกิจ (บังคับที่ API)

1. **ผู้แจ้งชำระ = Manager ของอู่นั้น** (เจ้าของอู่ได้ Manager ผ่าน `svc_UserRoleOverride`) · ผู้แจ้งอ่านจาก JWT ห้ามรับจาก body
   · role อื่นเห็นสถานะและประวัติได้ แต่ปุ่มแจ้งชำระปิดพร้อมเหตุผล "เฉพาะผู้จัดการอู่" `[ASSUME]`
2. **รอตรวจได้ครั้งละ 1 รายการต่ออู่** · บังคับด้วย filtered unique index ไม่ใช่แค่ใน service (บทเรียนจาก `WorkInterval`)
   · Manager ของอู่ยกเลิกรายการรอตรวจได้ (แทนการ "ยกเลิกอัตโนมัติ" `-1` ของระบบเดิมที่เคยยกเลิกรายการที่อนุมัติแล้วไปด้วย)
   · role อื่นเห็นสถานะ แต่ไม่เห็นยอดเงิน หมายเหตุ และสลิป (ค่าเป็น null ที่ server) `[ASSUME]`
3. **จำนวนเดือน 1, 2, 3, 6 หรือ 12** (ตามตัวเลือกของระบบเดิม) `[ASSUME]`
4. **วันเวลาที่โอน**: ห้ามเป็นอนาคต (tolerance 5 นาทีเหมือนวันนัดหมาย) · ย้อนหลังได้ไม่เกิน 60 วัน `[ASSUME]` · เก็บเป็น UTC
5. **ยอดเงิน**: ผู้แจ้งกรอกยอดที่โอนจริง (> 0 ทศนิยม 2 ตำแหน่ง) · ระบบแสดงยอดตามราคามาตรฐานให้เทียบ (ถ้าตั้งราคาไว้)
   · ยอดไม่ตรงราคา **อนุมัติได้** (ส่วนลด/โปรโมชัน) แต่ทีมงานต้องใส่หมายเหตุ
6. **สลิปบังคับ 1 ไฟล์** png/jpeg/webp/pdf ตาม `AttachmentOptions` เดิม · เก็บ SHA-256 ไว้ เจอไฟล์ซ้ำกับรายการเดิม **เตือนทีมงาน** (ไม่ปฏิเสธเอง)
7. **อนุมัติได้เฉพาะรายการ `Pending`** · UPDATE แบบมีเงื่อนไข `WHERE Status = 'Pending'` อยู่ใน transaction เดียวกับการต่อวัน
   (ระบบเดิมแยกเป็น 2 call แล้ววันที่ต่อแล้วถูกเขียนทับกลับ) · ได้ 0 แถว = `PACKAGE_PAYMENT_NOT_PENDING`
   · UPDATE Branch มีเงื่อนไขว่าวันหมดยังเท่ากับค่าที่อ่านมา (กัน `spadmin extend` ที่รันพร้อมกัน) ไม่ตรง = ยกเลิกทั้ง transaction ให้รันใหม่
   · อู่ที่ถูกระงับ (suspended) อนุมัติไม่ได้ ต้องยกเลิกการระงับก่อน
8. **ปฏิเสธต้องเลือกเหตุผล** 1 ใน 5: สลิปไม่ชัด · ยอดไม่ตรง · ไม่พบเงินเข้าบัญชี · สลิปซ้ำ · อื่นๆ (อื่นๆ ต้องมีข้อความ)
9. **ทุกการเปลี่ยนสถานะเขียน `sp_WorkspaceAudit`** พร้อมผู้กระทำ (`spadmin --by` หรือชื่อผู้ใช้จาก JWT)
10. **ไม่ให้แจ้งชำระ** เมื่ออู่เป็น `converted` หรือ `paid` ที่ไม่มีวันหมดอายุ (`PACKAGE_NOT_RENEWABLE`) · `suspended` เข้าระบบไม่ได้อยู่แล้ว
11. **แจ้งชำระได้ทั้งตอนใช้งานปกติและตอนอ่านอย่างเดียว** (route อยู่ใต้ `/api/v1/selfserve` ที่ `TrialReadOnlyFilter` ยกเว้นอยู่แล้ว)
    · พ้นช่วงอ่านอย่างเดียว (ล็อก) เข้าระบบไม่ได้ ต้องติดต่อ LINE แล้วทีมงานใช้ `spadmin extend`
12. **ลบอู่ (purge)**: มีรายการรอตรวจ = **ไม่ลบ** (ทั้งงานรายวันและ `spadmin purge` ที่ไม่ใส่ `--allow-paid`) เพราะวันล็อกกับวันลบเป็นวันเดียวกัน
    ลูกค้าที่แจ้งชำระวันที่ 89 จะถูกลบก่อนทีมงานตรวจ · ลบไฟล์สลิปก่อนแถว (ลำดับเดียวกับ `WorkspacePurger` เดิม) ·
    ตอน tombstone รายการรอตรวจเป็น `Cancelled` และล้าง `Note` (เก็บคอลัมน์การเงินไว้) · อู่ที่อนุมัติแล้วเป็น `paid` ไม่ถูกลบอัตโนมัติอยู่แล้ว
13. **งานรายวัน**: อู่ที่มีรายการรอตรวจไม่ได้อีเมลเตือน/เชิญกลับ · อู่ paid ได้อีเมลเตือนก่อนหมด (ค่าเริ่มต้น 14 และ 3 วัน) ด้วย notice kind
    `paid-reminder-{d}:{yyyyMMdd ของวันหมด}` เพราะ `sp_WorkspaceNotice` unique ต่อ (workspace, kind) ตลอดกาล ต่ออายุแล้วต้องเตือนรอบใหม่ได้ ·
    ข้อความสรุปรายวันต่อท้าย " · แจ้งชำระรอตรวจ N"

---

## 3. ข้อมูล: `sp_PackagePayment` (SelfServeDbContext · migration ชุด SelfServe)

| คอลัมน์ | ชนิด | หมายเหตุ |
|---|---|---|
| `Id` | uniqueidentifier | PK · `ValueGeneratedNever()` |
| `WorkspaceId` | uniqueidentifier | FK → `sp_Workspace` |
| `RequestId` | nvarchar(100) | unique ร่วมกับ `WorkspaceId` · กดซ้ำ/เน็ตหลุดได้แถวเดิม |
| `Status` | nvarchar(20) | `Pending` · `Approved` · `Rejected` · `Cancelled` (เก็บเป็นข้อความแบบ `sp_Workspace.Plan`) |
| `MonthCount` | int | 1/2/3/6/12 |
| `Amount` | decimal(18,2) | ยอดที่ลูกค้าแจ้ง |
| `ExpectedAmount` | decimal(18,2) null | ราคามาตรฐาน ณ ตอนแจ้ง (สำเนา) · null = ยังไม่ตั้งราคา |
| `PaidAt` | datetime2 | UTC |
| `SlipPath` | nvarchar(400) | relative path บน FTP |
| `SlipSha256` | char(64) | ไว้เตือนสลิปซ้ำ |
| `SlipContentType` / `SlipSizeBytes` | | |
| `Note` | nvarchar(500) null | จากลูกค้า |
| `SubmittedByUserId` / `SubmittedByUserName` / `SubmittedAt` | bigint / nvarchar(100) / datetime2 | จาก JWT |
| `PrevEndsAt` | datetime2 null | วันหมดเดิม ณ ตอนอนุมัติ (UTC) |
| `PeriodStartsAt` / `PeriodEndsAt` | datetime2 null | ช่วงที่ต่อให้จริง เติมตอนอนุมัติ |
| `ReviewedBy` / `ReviewedAt` | | ผู้อนุมัติ/ปฏิเสธ |
| `RejectReason` / `ReviewNote` | nvarchar(20) / nvarchar(1000) | `ReviewNote` แสดงให้ลูกค้าเห็นเฉพาะตอนปฏิเสธ (หมายเหตุตอนอนุมัติเป็นของทีมงาน) |
| `CancelledAt` / `CancelledBy` | datetime2 null / nvarchar(200) | ยกเลิกโดย Manager หรือโดย purge |

Index: `UX_sp_PackagePayment_OnePending (WorkspaceId) WHERE Status = N'Pending'` · `UX_sp_PackagePayment_Workspace_Request (WorkspaceId, RequestId)` ·
`IX_sp_PackagePayment_Status_SubmittedAt` · `IX_sp_PackagePayment_SlipSha256` · check constraint `Amount > 0` และ `MonthCount > 0`

`SlipSha256` เป็น hash ของ**ไฟล์ต้นฉบับที่อัปโหลด** (ก่อนแปลงเป็น JPEG) ใช้เตือนสลิปซ้ำ

เทียบกับระบบเดิม: `Pending`=1 · `Approved`=2 · `Cancelled`=0 · `Rejected` เป็นสถานะใหม่ (ระบบเดิมไม่มี) · ไม่มี `-1`

**ทำไมไม่ใช้ `svc_*`**: การต่อวันเขียน `Branch` และ `sp_Workspace` ผ่าน `SelfServeDbContext` อยู่แล้ว ตารางนี้อยู่ context เดียวกัน
จึงอนุมัติ + ต่อวัน + audit ได้ใน transaction เดียวโดยไม่ต้องข้าม context

---

## 4. สูตรวัน

`EndPackageDate` = เที่ยงคืน (เวลาไทย) ของวันแรกที่หมดสิทธิ์ · วันสุดท้ายที่ใช้ได้ = `End − 1 tick` (แสดงผลแบบนี้ทุกจุด เหมือน `PlanAccess`)

```text
base       = max(ปัดขึ้นเป็นเที่ยงคืนไทย(EndPackageDate), เที่ยงคืนไทยของวันที่อนุมัติ)
newEnd     = base.AddMonths(MonthCount)            // DateOnly ของไทย: 31 ม.ค. + 1 เดือน = 28/29 ก.พ.
Start      = ไม่เปลี่ยน (เหมือนระบบเดิม) · PackageDaysAmout = DATEDIFF(day, Start, newEnd) ตาม UpdateBranchEndSql เดิม
Plan       = Trial → Paid · Status Expired → Active · PurgeAfter = null
```

- **ปัดขึ้นเป็นเที่ยงคืน**: วันหมดของ trial คือเวลาที่สมัคร + 30 วัน (เช่น 13 พ.ย. 10:00) ถ้าไม่ปัด วันหมดของรอบที่จ่ายเงินจะเป็น 10:00 ตลอดไป
  · 13 พ.ย. 10:00 → 14 พ.ย. 00:00 · ค่าที่เป็นเที่ยงคืนพอดีอยู่แล้วไม่เปลี่ยน
- ต่อก่อนหมด: ไม่เสียวันที่เหลือ (base = End เดิม)
- ต่อหลังหมด: เริ่มนับจากวันอนุมัติ เพราะอู่ sp1 บันทึกงานไม่ได้ตั้งแต่วันหมด (ต่างจากระบบเดิมที่ไม่ล็อกจริง)
- ทีมงานแก้วันหมดใหม่ได้ตอนอนุมัติ (`--until`) เช่นแถมวัน · ต้อง**หลัง** `base` และไม่เกิน `MaxExtendDays` ของ spadmin
- คำนวณตอนอนุมัติ ไม่ใช่ตอนแจ้ง → รายการที่อนุมัติทีหลังต่อจากวันหมดล่าสุดเสมอ (ระบบเดิมคำนวณตอนแจ้ง ทำให้ต่อซ้อนกันไม่ได้)
- หน้าแจ้งชำระแสดง "ถ้าอนุมัติวันนี้ จะใช้งานได้ถึง …" เป็นพรีวิว (server คำนวณ)

---

### 4.1 session เมื่อแพ็กเกจเปลี่ยน (D8)

`PlanAccess.RevocationReason` เดิมตอบ "แพ็กเกจของอู่เปลี่ยนแล้ว กรุณาเข้าสู่ระบบใหม่" ทุกครั้งที่ claim `plan`/`plan_exp` ใน token ไม่ตรงกับนโยบาย
ผลคือ อนุมัติ trial→paid เตะทุกคนในอู่ภายใน 60 วิ · ต่ออายุหลังหมดอายุก็เตะ · ต่อก่อนหมดอายุจะเตะทุกคนอีกครั้งเมื่อถึงวันหมดเดิม
(claim ค้างวันเก่า และ token มือถืออายุ 10 ปี) ช่างที่จำรหัสสุ่มไม่ได้จะเข้าระบบไม่ได้ทุกรอบที่ต่ออายุ

กติกาใหม่: **เตะเฉพาะเมื่อนโยบายเข้มกว่า claim**

| กรณี | เตะ? |
|---|---|
| ถูกล็อก (เลยช่วงอ่านอย่างเดียว / ระงับ) | เตะ (เหมือนเดิม) |
| นโยบายเป็นอ่านอย่างเดียวแล้ว แต่ claim ยังเขียนได้ (ทีมงานตัดวัน) | เตะ (เหมือนเดิม) |
| paid → trial | เตะ |
| trial → paid | **ไม่เตะ** |
| claim เป็นอ่านอย่างเดียว แต่นโยบายเขียนได้แล้ว (ต่ออายุหลังหมด) | **ไม่เตะ** |
| วันหมดใน claim เก่ากว่านโยบาย (ต่อก่อนหมด) | **ไม่เตะ** แม้เลยวันหมดเดิมแล้ว |
| หมดอายุตามธรรมชาติ · token legacy | ไม่เตะ (เหมือนเดิม) |

claim ใช้แค่จุดนี้กับ fast path ใน `TrialReadOnlyFilter` · การล็อกฟีเจอร์/ลิมิตถาม `IWorkspacePolicy` เสมอ (cache 60 วิ) ·
เว็บและแอปดึงแพ็กเกจใหม่จาก `/auth/me` ทุก 5 นาที หน้าจอจึงเปลี่ยนเองโดยไม่ต้อง login ใหม่

---

## 5. API (ServicePro): สัญญา

ทั่วไป
- envelope เดิม `{ success, data, error: { code, messageTh, field? }, traceId }`
- JSON camelCase · **ฟิลด์ที่เป็น null ไม่อยู่ใน JSON** (`WhenWritingNull`) client ต้องเช็คแบบ truthy
- เวลาเป็น ISO 8601 UTC ลงท้าย `Z` · ฟิลด์ `...Date` เป็น `"yyyy-MM-dd"` ตามปฏิทินไทย (ห้ามส่งเข้า formatter ที่ตีความเป็นเวลา UTC)
- เงินเป็น number ทศนิยม 2 ตำแหน่ง · รหัสสถานะ/เหตุผลเป็นตัวพิมพ์เล็ก snake_case

### 5.1 ลูกค้า (token ของอู่ sp1)

shard อื่นได้ 404 `SELFSERVE_NOT_APPLICABLE` · route อยู่ใต้ `/api/v1/selfserve` จึงเรียกได้แม้อู่อยู่ช่วงอ่านอย่างเดียว

| Method | Path | ใคร | ผล |
|---|---|---|---|
| GET | `/api/v1/selfserve/package` | ทุก role | `PackageOverview` |
| GET | `/api/v1/selfserve/package/payments?take=20` | ทุก role | `PackagePayment[]` ใหม่สุดก่อน (take ≤ 50) |
| POST | `/api/v1/selfserve/package/payments` | Manager | multipart → `PackagePayment` |
| POST | `/api/v1/selfserve/package/payments/{id}/cancel` | Manager | `PackagePayment` |
| GET | `/api/v1/selfserve/package/payments/{id}/slip` | Manager | ไฟล์ `image/jpeg` หรือ `application/pdf` (ต้องมี Bearer token) |

`PackageOverview`
```jsonc
{
  "workspaceNo": "SP1042",
  "plan": "trial",                    // trial | paid | suspended | converted
  "status": "active",                 // provisioning | active | expired | purged
  "endsAt": "2026-11-13T03:00:00Z",   // EndPackageDate · ไม่มี = ไม่มีกำหนด
  "lastUsableDate": "2026-11-13",     // วันสุดท้ายที่ใช้ได้
  "daysLeft": 23,                     // ค่าเดียวกับ /auth/me
  "readOnly": false,
  "vatRate": 0.07,                    // ไม่อยู่ = ราคาไม่คิด VAT แยก
  "monthOptions": [                   // ตาม SelfServe:Package:MonthOptions (ค่าเริ่มต้น 1,2,3,6,12)
    { "months": 1,
      "subtotal": 3999.00,            // ราคาก่อน VAT
      "vat": 279.93,                  // VAT ปัดทศนิยม 2 ตำแหน่ง (ปัดครึ่งขึ้น)
      "price": 4278.93,               // ยอดที่ต้องโอน (รวม VAT) = expectedAmount · ไม่อยู่ทั้งสามช่อง = ยังไม่ตั้งราคา
      "previewEndsAt": "2026-12-13T17:00:00Z", "previewLastUsableDate": "2026-12-13" }  // ถ้าอนุมัติวันนี้
  ],
  "paymentAccount": { "bankName": "…", "accountNo": "…", "accountName": "…", "promptPayId": "…" }, // ไม่อยู่ = ยังไม่ตั้ง
  "pending": { /* PackagePayment */ },  // ไม่อยู่ = ไม่มีรายการรอตรวจ
  "canSubmit": false,
  "cannotSubmitCode": "pending_exists", // not_manager | pending_exists | not_renewable | unavailable
  "cannotSubmitReasonTh": "มีรายการแจ้งชำระรอตรวจอยู่แล้ว ทีมงานจะตรวจภายใน 1 วันทำการ",
  "paidAtMaxPastDays": 60
}
```

`PackagePayment`
```jsonc
{
  "id": "3f2a9c1b-…", "shortId": "3f2a9c1b",
  "status": "pending",                // pending | approved | rejected | cancelled
  "monthCount": 3,
  "amount": 12836.79, "expectedAmount": 12836.79,  // ไม่ใช่ Manager = ไม่อยู่ใน JSON · expectedAmount ไม่อยู่ = ยังไม่ตั้งราคา
  "paidAt": "2026-10-21T07:30:00Z",
  "submittedAt": "2026-10-21T07:41:12Z", "submittedBy": "SP1042.M01",
  "note": "โอนจากบัญชีบริษัท",          // ไม่ใช่ Manager = ไม่อยู่
  "slip": { "contentType": "image/jpeg", "sizeBytes": 183422 },  // ไม่ใช่ Manager = ไม่อยู่
  "periodStartsAt": "2026-11-13T17:00:00Z", "periodEndsAt": "2027-02-13T17:00:00Z",
  "periodLastUsableDate": "2027-02-13",                              // เฉพาะ approved
  "reviewedAt": "2026-10-21T09:00:00Z",
  "rejectReason": "slip_unreadable", "rejectReasonTh": "สลิปไม่ชัด อ่านไม่ออก", "rejectNote": "…", // เฉพาะ rejected
  "cancelledAt": "…"
}
```

POST multipart (ชื่อฟิลด์ไม่สนตัวพิมพ์)

| ฟิลด์ | ค่า |
|---|---|
| `requestId` | ≤ 100 ตัว · client สร้างครั้งเดียวต่อคำขอ · ส่งซ้ำได้แถวเดิม (ไม่อัปโหลดซ้ำ ไม่ส่ง LINE ซ้ำ) · ห้ามสร้างใหม่เมื่อเจอ `NETWORK_ERROR` |
| `paidAt` | ISO 8601 มี offset เช่น `2026-10-21T14:30:00+07:00` · ไม่มี offset = เวลาไทย · ปี ≥ 2400 ได้ข้อความ "กรุณาใช้ปี ค.ศ." |
| `amount` | `"1500.00"` (มี `,` ได้) · > 0 · ทศนิยม ≤ 2 · ≤ 999,999.99 |
| `monthCount` | หนึ่งใน `monthOptions` |
| `note` | ไม่บังคับ ≤ 500 |
| `slip` | 1 ไฟล์ png/jpeg/webp/pdf ≤ 15 MB |

เหตุผลปฏิเสธ (ตายตัว)

| รหัส | ข้อความ |
|---|---|
| `slip_unreadable` | สลิปไม่ชัด อ่านไม่ออก |
| `amount_mismatch` | ยอดเงินไม่ตรงกับแพ็กเกจ |
| `not_received` | ไม่พบยอดเงินเข้าบัญชี |
| `duplicate` | สลิปซ้ำกับที่เคยแจ้ง |
| `other` | อื่นๆ (ต้องมีหมายเหตุ) |

Error

| code | HTTP | เมื่อ |
|---|---|---|
| `SELFSERVE_NOT_APPLICABLE` | 404 | ไม่ใช่อู่ sp1 |
| `PACKAGE_PAYMENT_FORBIDDEN` | 403 | ไม่ใช่ Manager |
| `PACKAGE_PAYMENT_UNAVAILABLE` | 503 | ยังไม่ตั้งบัญชีรับเงิน |
| `PACKAGE_PAYMENT_VALIDATION` | 422 | มี `field`: `requestId` · `paidAt` · `amount` · `monthCount` · `note` · `slip` · `until` · `reason` |
| `PACKAGE_PAYMENT_PENDING_EXISTS` | 409 | มีรายการรอตรวจอยู่แล้ว |
| `PACKAGE_NOT_RENEWABLE` | 409 | converted · paid ไม่มีวันหมด · suspended |
| `PACKAGE_PAYMENT_NOT_FOUND` | 404 | ไม่พบ หรือเป็นของอู่อื่น |
| `PACKAGE_PAYMENT_NOT_PENDING` | 409 | ยกเลิก/อนุมัติ/ปฏิเสธรายการที่ไม่ได้รอตรวจ |
| `PACKAGE_APPROVAL_CONFLICT` | 409 | วันหมดอายุเปลี่ยนระหว่างอนุมัติ (ให้ลองใหม่) |
| `PACKAGE_SLIP_UPLOAD_FAILED` | 503 | เก็บไฟล์ไม่สำเร็จ (ไม่มีแถวถูกบันทึก) |
| `ATTACHMENT_EMPTY` · `ATTACHMENT_TYPE_INVALID` · `ATTACHMENT_TOO_LARGE` | 422 · 422 · 413 | ไฟล์สลิป |
| `RATE_LIMITED` | 429 | แจ้งชำระเกิน 10 ครั้ง/ชม. ต่ออู่ |

### 5.2 ไฟล์สลิป

- `IPackageSlipStorage` แยกจาก `IAttachmentStorage` (ของเดิมผูกจ๊อบ) · implement ใน `FtpAttachmentStorage` · ไม่บันทึกลง `svc_Attachment`
- path `{shard}/{branch}/package-slip/{paymentId:N}/{fileId:N}.jpg|.pdf` ผ่าน `SafeSegment` / `IsSafeRelativePath` เดิม และผ่าน prefix guard ของ purge
- รูปทุกไฟล์ (เล็กหรือใหญ่) ถูกแปลง: ตรวจชนิดจากเนื้อไฟล์ (PNG/JPEG/WebP เท่านั้น) · ≤ 40 ล้านพิกเซล · AutoOrient · ย่อ ≤ 1600px ·
  ลบ metadata (EXIF) · พื้นขาว · JPEG ≤ 1 MB (quality 85 แล้วลด 70 ถ้ายังเกิน) เพื่อให้ LINE แสดงได้
- PDF (`%PDF-`) เก็บตามเดิม · ไฟล์ที่ไม่ใช่รูป/PDF จริง = `ATTACHMENT_TYPE_INVALID`

### 5.3 LINE และลิงก์สลิป

- แจ้งชำระสำเร็จ → push เข้ากลุ่มขาย (`ISelfServeSalesNotifier`) ครั้งเดียวหลายข้อความ: ข้อความ + รูปสลิป (retry key `DeterministicGuid("package-payment:{id}")`)
  · ข้อความมี: อู่ · จำนวนเดือน · ยอดเทียบราคา · เวลาโอน · "ถ้าอนุมัติวันนี้ ใช้ได้ถึง …" · เตือนสลิปซ้ำ · คำสั่ง `spadmin approve-payment --id <shortId> --by <ชื่อ>`
  · สลิป PDF ส่งเป็นลิงก์ในข้อความ · ส่งไม่สำเร็จไม่ทำให้การแจ้งล้ม
- ลิงก์ที่ LINE ใช้ดึงรูป: `GET /api/v1/public/package-slips/{id}?exp={unix}&sig={base64url(HMAC-SHA256(key, "{id:N}.{exp}"))}`
  · base = `SelfServe:Package:PublicApiBaseUrl` (โดเมน API ไม่ใช่โดเมนเว็บ) · อายุ `SlipLinkDays` (7) · ผิดทุกกรณีตอบ 404 · ตรวจลายเซ็นก่อนแตะฐาน/FTP
  · rate limit bucket ของตัวเอง `public-package-slip` 300/ชม. (ไม่แย่ง bucket 60/ชม. ของฟอร์มติดต่อ)
  · header `Cache-Control: private, max-age=3600` · `X-Content-Type-Options: nosniff` · `Referrer-Policy: no-referrer` · `X-Robots-Tag: noindex`
  · `[SECURITY]` เปิดดูสลิปได้โดยไม่ล็อกอินจนลิงก์หมดอายุ กลุ่ม LINE ต้องเป็นทีมงานเท่านั้น
- ไม่ตั้ง `SlipLinkKey` (หรือสั้นกว่า 32 ตัว) = ไม่ส่งรูป ส่งข้อความอย่างเดียว

### 5.4 หลังอนุมัติ/ปฏิเสธ

- แจ้งเตือนในระบบถึง Manager: kind `package.payment_approved` / `package.payment_rejected` · `entityType: "PackagePayment"` · `linkHint: "package"`
  → เว็บและแอปพาไปหน้าแพ็กเกจ
- อีเมลถึงเจ้าของอู่ (แม่แบบ `package-approved` / `package-rejected`) · ทั้งสองอย่าง best effort หลัง commit (คนละ DbContext)
- อีเมลเตือนอู่ paid ก่อนหมด (แม่แบบ `paid-reminder-{n}`)

### 5.5 ค่าตั้ง (`SelfServe:Package`)

| คีย์ | ค่าเริ่มต้น | หมายเหตุ |
|---|---|---|
| `MonthOptions` | ว่าง → 1, 2, 3, 6, 12 | array ต้องเริ่มว่าง (binder ต่อท้าย array) |
| `MonthlyPrice` | ว่าง (production ตั้งใน `appsettings.json` = 3999) | ราคาต่อเดือน**ก่อน VAT** · ราคาของ N เดือน = N × MonthlyPrice |
| `VatRate` | ว่าง (production = 0.07) | VAT บวกเพิ่มจากราคาก่อน VAT · ว่าง = ไม่แยก VAT (price = subtotal) |
| `Prices:{เดือน}` | ว่าง | ราคาก่อน VAT ของจำนวนเดือนนั้น ใช้แทน N × MonthlyPrice (ไว้ทำส่วนลด) เช่น `Prices:12=43990` |
| `PaymentAccount:{BankName,AccountNo,AccountName,PromptPayId}` | ว่าง (production ตั้งใน `appsettings.json`: ธนาคารกสิกรไทย · 068-8-63750-3 · บจก. อาร์มาดิลโล เทค จำกัด) | ต้องมี AccountName + (AccountNo หรือ PromptPayId) ไม่งั้นแจ้งชำระไม่ได้ · ไม่ใช่ความลับ (แสดงให้ลูกค้าทุกคน) |
| `SlipLinkKey` | ว่าง | `[SECURITY]` user-secrets/env file เท่านั้น ≥ 32 ตัว |
| `SlipLinkDays` | 7 | |
| `PublicApiBaseUrl` | `https://gpservice-api.garage-pro.net` | |
| `PaidReminderDays` | ว่าง → 14, 3 | |
| `PaidAtMaxPastDays` | 60 | |


---

## 6. หน้าจอลูกค้า

### 6.1 เว็บ
- หน้าใหม่ `/package` "แพ็กเกจและการชำระเงิน" (เมนูใต้ชื่ออู่/ตั้งค่า): การ์ดแพ็กเกจปัจจุบัน · บัญชีรับเงิน + ราคา · ฟอร์มแจ้งชำระ · ประวัติ
- สถานะในประวัติ: สี + ไอคอน + ข้อความ (รอตรวจ · อนุมัติแล้ว ใช้งานได้ถึง … · ปฏิเสธ พร้อมเหตุผล · ยกเลิก)
- วันที่โอนใช้ `type="date"` + `type="time"` ของเบราว์เซอร์ (ค่าเป็น ค.ศ. เสมอ) · ห้ามช่องพิมพ์ปีเอง
- แถบทดลองใช้ (`TrialBanner`) เพิ่มปุ่ม "แจ้งชำระเงิน" ไปหน้า `/package` · ปุ่ม "ขอต่ออายุ" เดิมคงไว้สำหรับคนที่ยังไม่พร้อมจ่าย
- **อู่ paid**: เพิ่มแถบเตือนเมื่อเหลือ ≤ 14 วัน `[ASSUME]` และเมื่ออ่านอย่างเดียว (วันนี้ `trialTone` คืน `none` ให้ paid ทุกกรณี
  อู่ที่จ่ายแล้วจึงกลายเป็นอ่านอย่างเดียวโดยไม่มีคำอธิบาย)
- ปุ่มที่กดไม่ได้บอกเหตุผลเป็นข้อความ (ใช้ `cannotSubmitReasonTh` ของ server ไม่แต่งข้อความเอง)
- เมนูผู้ใช้มี "แพ็กเกจและการชำระเงิน" เฉพาะอู่ self-serve · แจ้งเตือน `entityType: "PackagePayment"` คลิกแล้วไปหน้า `/package`
- รูปสลิปดาวน์โหลดผ่าน `apiDownload` (ต้องมี Bearer token ห้ามใช้ `<img src>` ตรง)

### 6.3 แนวทางออกแบบ (ผู้ใช้ขอ "สวยๆ เท่ๆ" 2026-10-22)

- ใช้สี ฟอนต์ และ token เดิมของระบบเท่านั้น (ห้ามแก้ token ข้างเดียว) · ความ "เท่" มาจากลำดับชั้นของข้อมูล ขนาด ระยะห่าง และการ์ด ไม่ใช่สีใหม่
- โครงหน้า: การ์ดหัวสีกรมท่า (แพ็กเกจปัจจุบัน + วันใช้งานได้ถึง + แถบเวลาคงเหลือ) → ต่ออายุ 3 ขั้น (เลือกระยะเวลา · โอนเงิน · แนบสลิป) → ประวัติแบบ timeline
- การ์ดเลือกระยะเวลาแสดงยอดรวม VAT ตัวใหญ่ + ราคาก่อน VAT/VAT ตัวเล็ก + "ใช้ได้ถึง …" · การ์ดบัญชีธนาคาร (ข้อความ "ธนาคารกสิกรไทย" ไม่ใช้โลโก้ธนาคาร) เลขบัญชีและยอดโอนตัวใหญ่ mono พร้อมปุ่มคัดลอก
- สรุปยอด (ก่อน VAT · VAT 7% · รวม · ใช้ได้ถึง) อยู่ข้างฟอร์มบนเว็บ และอยู่ใน sticky bar บนแอป · ยอดที่โอนกรอกให้ล่วงหน้าเป็นยอดรวม VAT แก้ได้
- ไม่แต่งป้าย "คุ้มสุด/ยอดนิยม" ที่ไม่มีข้อมูลรองรับ (ราคาตอนนี้คิดตามจำนวนเดือนตรงๆ ไม่มีส่วนลด)

### 6.2 แอปมือถือ
- หน้าใหม่ "แพ็กเกจและการชำระเงิน" เข้าจากโปรไฟล์ และจากการแตะแบนเนอร์ทดลองใช้ (`widgets/trial_banner.dart`)
- เลือกสลิปจากคลังรูป (สลิปจากแอปธนาคารอยู่ในคลังรูปอยู่แล้ว) หรือถ่ายใหม่ ผ่าน `image_picker` ที่มีอยู่
- วันเวลาที่โอนใช้ `date_time_wheel_sheet.dart` ที่มีอยู่ (ป้ายไทย ค่า ค.ศ.) · จำนวนเดือนเป็นชิป · `requestId` สร้างครั้งเดียวต่อคำขอ ไม่ล้างเมื่อ `NETWORK_ERROR`
- **แนบได้เฉพาะรูป** (แอปไม่มีตัวเลือกไฟล์ PDF) · สลิป PDF ให้แจ้งผ่านเว็บ
- วงล้อนาทีปัดขึ้นทีละ 5 นาที เวลาโอนที่เลือกอาจเกินเวลาปัจจุบันเล็กน้อย จึงพึ่ง tolerance 5 นาทีของ server
- แบนเนอร์แสดงกรณีอู่ paid ด้วยเหมือนเว็บ (เหลือ ≤ 14 วัน หรืออ่านอย่างเดียว) · แจ้งเตือน `PackagePayment` แตะแล้วไปหน้าแพ็กเกจ
- แอปต้องออกรุ่นใหม่ `1.2.0+4` (ต่อจาก `1.1.0+3` ที่ยังไม่ปล่อย) · backend ต้องขึ้นก่อนแอป

---

## 7. เครื่องมือทีมงาน: `spadmin` (ระยะแรก)

parser ของ spadmin รับคำสั่งคำเดียวและไม่มีอาร์กิวเมนต์ตามตำแหน่ง จึงใช้ชื่อคำสั่งแบบแบน

```text
payments [--status pending|approved|rejected|cancelled|all] [--workspace SP1042]   # ค่าเริ่มต้น pending
payment --id <guid หรือ 8 ตัวอักษรแรก>                                              # รายละเอียด + สลิปซ้ำ + วันหมดใหม่ + ลิงก์สลิป
approve-payment --id <...> --by <ชื่อ> [--until yyyy-MM-dd] [--note ...]
reject-payment  --id <...> --by <ชื่อ> --reason slip_unreadable|amount_mismatch|not_received|duplicate|other [--note ...]
```

- `--by` **บังคับ** สำหรับ approve/reject (ใน container `$USER` ไม่มีความหมาย และเป็นการอนุมัติเรื่องเงิน)
- `--id` สั้นกว่า 8 ตัวหรือตรงหลายรายการ = ปฏิเสธพร้อมรายการที่ตรง
- `approve-payment` ใช้ `ApprovePaymentAsync` (กฎข้อ 7) · ยอดไม่ตรงราคาต้องมี `--note` · `--until` ต้องหลัง `base` (หัวข้อ 4)
- พิมพ์วันหมดก่อน → หลัง และผลการส่งอีเมล/แจ้งเตือนในระบบ
- `payments` / `payment` อ่านอย่างเดียว · รันบน production ด้วยวิธีเดิมใน CLAUDE.md (`docker compose run ... SpAdmin.dll`)


---

## 8. GaragePro Admin หน้า `/branches`

คำขอผู้ใช้ 2026-10-21: แสดงอายุและสถานะของแต่ละ site ในหน้า `/branches` ของ GaragePro Admin

### 8.1 ของเดิม (สำรวจ repo `AMD.GaragePro.Admin` ที่ commit `6a3325c`)

- Next.js 14 + Tailwind (ไม่มี component library) · .NET 8 API ใช้ EF ผ่าน `GarageDbContextFactory` ไม่มี service layer
- เลือกฐานต่อคำขอด้วย header `X-Database-Id` (db1 = numchai · db2 = enterprise) สลับที่ sidebar แล้วรีโหลด · `/branches` เห็นเฉพาะฐานที่เลือก
- `/branches` ดึงทุกแถวครั้งเดียวจาก `GET /api/v1/branches` แล้วกรอง เรียง แบ่งหน้า 50 แถวที่เบราว์เซอร์
  · คอลัมน์: ID · ชื่อ · ประเภท · กลุ่ม · วันหมด Package · สถานะ · โทร · อีเมล · จัดการ
- **มีวันหมดอายุอยู่แล้ว**: คอลัมน์วันหมดเป็นสีแดง "หมดแล้ว X วัน" หรือสีอำพัน "เหลือ X วัน" (≤ 30 วัน) คำนวณที่เบราว์เซอร์
- แก้วันแพ็กเกจได้ทางเดียวคือฟอร์มแก้สาขา (`BranchModal` แท็บ package → `PUT /branches/{id}`) ไม่มีประวัติและไม่บันทึกผู้แก้
  · นี่คือวิธีที่ทีม GaragePro ต่ออายุสาขากลุ่ม Service ใน db2 ตามการตัดสินใจ D1
- `BranchPaymentHistory` map ใน EF แล้วแต่ไม่มีโค้ดอ่านหรือเขียน · ไม่มี LINE/อีเมลในฝั่ง API · มี FTP (`FileManagerController`)
- **ไม่เห็นฐาน `GarageSelfServe`** อู่ที่สมัครเองจึงไม่อยู่ในระบบนี้เลย
- login ด้วย `User.IsAdministrator` ของฐานที่เลือก · JWT มี `sub` (User.Id) และ `name` ใช้บันทึกผู้อนุมัติได้
- กฎ "หมดอายุ" ใน Admin เองมี 3 แบบไม่ตรงกัน: `/branches` เตือนที่ ≤ 30 วัน · `SaleMarketingController` ถือว่าหมดเมื่อ `End.Date < Today` ·
  `AdminsController` ผ่อนผัน 15 วัน (`End.AddDays(15)`)

### 8.2 สิ่งที่จะเพิ่ม: หน้า `/branches` มี 2 แท็บ

**แท็บ "สาขา GaragePro"** (ตารางเดิม ตามฐานที่เลือก)
- เพิ่มคอลัมน์วันเริ่ม และป้ายสถานะแพ็กเกจ (สี + ไอคอน + ข้อความ): ไม่มีแพ็กเกจ · ใช้งาน · ใกล้หมด (≤ 30 วัน) · เลยกำหนด อยู่ในช่วงผ่อนผัน 15 วัน · หมดอายุ
  · คำนวณที่ server เป็นวันไทยด้วยฟังก์ชันเดียว แทนที่จะคำนวณแยก 3 แบบ
- ป้าย "รอตรวจสลิป N" จาก `BranchPaymentHistory.Status = 1` + วันแจ้งล่าสุด (มีจริงใน db2: 6 รายการรอตรวจ ณ วันที่สำรวจ)
- ตัวกรองเพิ่ม: ใกล้หมด/หมดแล้ว · มีสลิปรอตรวจ
- แถวกลุ่ม Service (7) มีป้าย "ServicePro" ให้รู้ว่าเป็นลูกค้า ServicePro บนฐาน GaragePro
- ยังอ่านอย่างเดียว (การอนุมัติสลิป Enterprise จากหน้านี้อยู่นอกขอบเขต ดู 8.4)

**แท็บ "ServicePro"** (ใหม่) รวมทุก site ของ ServicePro ไว้ในตารางเดียว ข้อมูลมาจาก API ภายในของ ServicePro
- แหล่ง: (ก) สาขากลุ่ม Service ใน db2 (ข) อู่สมัครเอง sp1 · คีย์ของแถวคือ (shard, branchId) เพราะ id ชนกันข้ามฐาน
- คอลัมน์: แหล่ง · รหัส (`SPxxxx` หรือ id สาขา) · ชื่อ · แพ็กเกจ (ทดลองใช้ · จ่ายแล้ว · ระงับ · ย้ายแล้ว · GaragePro) · วันเริ่ม ·
  ใช้ได้ถึง (วันสุดท้าย) · เหลือ/เลยกี่วัน · สถานะการใช้งาน (ปกติ · อ่านอย่างเดียว · ล็อก · กำลังเปิดอู่ · ลบแล้ว) ·
  สลิปรอตรวจ · ขอต่ออายุล่าสุด · จำนวนจ๊อบ 30 วันล่าสุด และวันที่เปิดจ๊อบล่าสุด · วันสมัคร
- สถานะคำนวณที่ ServicePro ด้วย `PlanPolicy` (แหล่งความจริงเดียว) Admin แสดงตามที่ได้ ไม่คำนวณกฎเอง
- สาขา db2: ServicePro ไม่บังคับวันหมด จึงแสดงวันตาม Branch พร้อมป้าย "ServicePro ไม่ล็อกตามวันหมด"
- แถว sp1 **ห้ามเปิด `BranchModal`** (แก้ Branch ของ sp1 ตรงๆ จะทำให้ไม่ตรงกับ `sp_Workspace`) · แถว db2 เปิดได้ตามเดิม

### 8.3 ทางเชื่อม Admin → ServicePro

- ServicePro เพิ่ม `GET /api/v1/internal/sites` (ระยะ B เพิ่ม `GET /internal/package-payments` · `GET .../{id}/slip` · `POST .../{id}/approve|reject`)
  · ข้อมูล sp1 ต่อยอดจาก `SelfServeAdminService.ListAsync` ที่ `spadmin list` ใช้อยู่ · สาขา db2 อ่านผ่าน legacy reader (เลือกคอลัมน์ + `READUNCOMMITTED`)
- auth: header `X-Internal-Key` เทียบแบบ constant-time (คีย์อยู่ใน user-secrets/env file ทั้งสองฝั่ง ห้ามอยู่ใน Git) · ไม่ใช้ JWT ของผู้ใช้
  · ระยะ B Admin ส่งชื่อและ id ของ admin ที่กดมาใน body เพื่อบันทึก audit (Admin backend ตรวจ `[Authorize(Roles="Admin")]` แล้ว)
- เครือข่าย: Nginx ของ ServicePro **ปฏิเสธ `/api/v1/internal/` จากภายนอกทั้งหมด** · Admin เรียก container API ของ ServicePro ผ่าน docker network
  · ตรวจแล้ว 2026-10-21: `garagepro_admin_api` กับ `garagepro_service_api` รันบน `garage_amd` เครื่องเดียวกัน
  (network `amdgarageproadmin_internal` และ `garagepro-service_internal`) → ให้ container Admin API เข้า network ของ ServicePro เพิ่ม
  แล้วเรียก `http://garagepro_service_api:8080` ไม่ต้องผ่านอินเทอร์เน็ต
- ทำไมไม่ให้ Admin อ่าน `GarageSelfServe` ตรง: กฎอ่านอย่างเดียว/ล็อกอยู่ใน `PlanPolicy` ถ้า Admin คำนวณเองจะเพี้ยนเมื่อกฎเปลี่ยน
  (กฎหมดอายุใน Admin เองยังไม่ตรงกัน 3 แบบ) และการอนุมัติต้องผ่าน transaction เดียวกับ `spadmin`

### 8.4 ระยะ

- **A** แท็บ GaragePro เพิ่มคอลัมน์/ป้าย/ตัวกรอง + แท็บ ServicePro อ่านอย่างเดียว
- **B** ในแท็บ ServicePro ดูสลิปและอนุมัติ/ปฏิเสธได้ (เรียก API ภายใน) ทีมงานไม่ต้องใช้ `spadmin` อีก · ผู้อนุมัติ = admin ที่ล็อกอิน
- **นอกขอบเขต** (เจ้าของระบบ Enterprise ต้องตัดสินใจ): อนุมัติสลิป Enterprise (`BranchPaymentHistory`) จาก Admin แทน Branch.aspx
  · Admin เป็นระบบที่เขียน Garage อยู่แล้วตามบทบาท และแก้บั๊กเขียนวันทับได้ในคำสั่งเดียว แต่ต้องอยู่ร่วมกับปุ่มยืนยันใน LINE เดิม
  (UPDATE แบบมีเงื่อนไข `WHERE Status = 1` กันยืนยันซ้ำ)

### 8.5 จุดที่ต้องแก้ใน Admin

Backend
- `Models/Branches/BranchListDto.cs` และ projection ใน `BranchesController.GetBranches`: `PackageDaysAmout` · `PackageState` · `DaysLeft` ·
  `PendingPaymentCount` (`b.BranchPaymentHistories.Count(p => p.Status == 1)`) · `LastPaymentAt`
- ฟังก์ชันสถานะแพ็กเกจแบบ pure (ไว้ให้ SaleMarketing/Admins ใช้ร่วมทีหลัง) + xUnit ในโปรเจกต์เทสต์เดิม (ยังไม่มีเทสต์ของสาขาเลย)
- `ServiceProSitesController` ใหม่ (`[Authorize(Roles="Admin")]`) + typed HttpClient · config `ServicePro:BaseUrl` / `ServicePro:InternalKey`

Frontend
- `types/index.ts` (`BranchListItem` + type ใหม่ `ServiceProSite`) · `lib/api.ts` เพิ่ม `getServiceProSites()` (ไม่ขึ้นกับฐานที่เลือก)
- `branches/page.tsx`: แท็บ · คอลัมน์ · ตัวกรอง · ป้ายสถานะ/สลิป (ตอนนี้ทุกหน้าเขียน badge ของตัวเอง ทำเป็น component กลางใน `components/`)
  · จุดที่ฮาร์ดโค้ดจำนวนคอลัมน์ไว้: skeleton 10 ช่อง · `colSpan={10}` ของแถวว่าง · CSV · หน้าพิมพ์

Docs: `docs/api-branches.md` ฟิลด์ใหม่ของรายการสาขา

ข้อควรระวัง: ทำงานบน branch ใหม่ใน repo Admin และไม่แตะไฟล์ที่เจ้าของแก้ค้างไว้ (เช่น `GarageDbContext.cs`) · ไม่ต้องมี migration เพราะไม่เพิ่มตารางในฐาน Garage

### 8.6 สัญญา API ภายใน (ServicePro) และ proxy (Admin)

**ServicePro `/api/v1/internal/*`**
- auth: header `X-Internal-Key` เทียบแบบ constant-time กับ `Internal:ApiKey` (user-secrets/env file)
  · server ไม่ได้ตั้งคีย์ = **404 ทุก route** (เหมือนไม่มี endpoint) · คำขอไม่มีหรือผิดคีย์ = 401 `INTERNAL_AUTH_REQUIRED`
- ไม่ใช้ JWT ผู้ใช้ · Nginx ของโดเมน API ตอบ 404 ให้ `/api/v1/internal` จากภายนอกเสมอ (marker `gp-api-internal-deny`)
- Admin เรียกผ่าน docker network ที่ใช้ร่วมกัน `http://garagepro_service_api:8080` (ห้ามใช้ชื่อ service `api` เพราะทั้งสองระบบชื่อ `api`)

`GET /api/v1/internal/sites`
```jsonc
{
  "generatedAt": "2026-10-21T09:00:00Z",
  "sites": [{
    "source": "selfserve",            // selfserve (sp1) | garagepro (db2 กลุ่ม Service 7)
    "shardKey": "sp1", "branchId": 12, // คีย์ของแถว = (shardKey, branchId) เพราะ id ชนกันข้ามฐาน
    "code": "SP1042",                  // garagepro = เลข branchId เป็นข้อความ
    "name": "อู่ช่างเอก", "phone": "0812345678",
    "plan": "trial",                   // trial | paid | suspended | converted | legacy
    "workspaceStatus": "active",       // เฉพาะ selfserve: provisioning | active | expired | purged
    "branchActive": true,              // Branch.Status = 1
    "startsAt": "…Z", "endsAt": "…Z", "lastUsableDate": "2026-11-13", "daysLeft": 23,
    "usage": "normal",                 // normal | read_only | locked | provisioning | purged | not_enforced (garagepro)
    "pendingPayments": 1, "latestPaymentAt": "…Z", "extensionRequestedAt": "…Z",
    "jobsLast30Days": 14, "lastJobAt": "…Z", "createdAt": "…Z"
  }]
}
```
- selfserve: ข้อมูลจาก `SelfServeAdminService.ListAsync` + `PlanPolicy` (ไม่รวม purged ที่ไม่มี Branch แล้ว ให้ส่งได้แต่ `usage: "purged"`)
- garagepro: Branch กลุ่ม 7 ใน db2 อ่านแบบเลือกคอลัมน์ + `WITH (READUNCOMMITTED)` · `plan: "legacy"` · `usage: "not_enforced"`
- จ๊อบ: `svc_Job` ของ shard/สาขานั้น (GarageService สำหรับ db2 · GarageSelfServe สำหรับ sp1)
- `daysLeft` = วันสุดท้ายที่ใช้ได้ (`EndPackageDate` − 1 tick ตามปฏิทินไทย) − วันนี้ตามปฏิทินไทย (`PlanAccess.DaysLeft` ค่าเดียวกับ `/auth/me`) · 0 = วันนี้วันสุดท้าย · ติดลบ = เลยมาแล้ว · ไม่มีวันหมด = ไม่อยู่ใน JSON
- **สถานะ (PR 4 · `feat/package-renewal-internal` 2026-10-22)**: ทำแล้ว ยังไม่ deploy · รายละเอียดที่สัญญาไม่ได้ระบุ:
  อู่ที่ยังไม่มี `BranchId` (ค้างกลาง signup) ไม่ส่ง เพราะไม่มีคีย์ของแถว · อู่ purged ส่งพร้อม `BranchId` เดิมและไม่มี `branchActive` ·
  `pendingPayments`/`latestPaymentAt`/`extensionRequestedAt`/`workspaceStatus` มีเฉพาะ selfserve (`latestPaymentAt` = เวลาแจ้งล่าสุดทุกสถานะ) ·
  `jobsLast30Days` นับย้อน 30×24 ชม. จากเวลาที่เรียก · `lastJobAt` = จ๊อบล่าสุดตลอดกาล · `phone` ของ selfserve แปลง +66 เป็น 0 ·
  อ่านฐานไม่สำเร็จ = 503 `INTERNAL_SOURCE_UNAVAILABLE` · Nginx ตอบ 404 พร้อม `X-GP-Internal-Deny: nginx` (ใช้แยกจาก 404 ของ API)

ระยะ B
| Method | Path | body | ผล |
|---|---|---|---|
| GET | `/api/v1/internal/package-payments?status=pending&take=50` | | `[ PackagePayment (ฟิลด์ครบ ไม่ซ่อน) + { workspaceNo, workspaceName, branchId, duplicates: [{ paymentId, workspaceNo, status, submittedAt }], preview: { baseAt, endsAt, lastUsableDate } } ]` |
| GET | `/api/v1/internal/package-payments/{id}/slip` | | ไฟล์ |
| POST | `/api/v1/internal/package-payments/{id}/approve` | `{ actorName, actorId, until?, note? }` | `{ payment, endsFrom?, endsTo, emailSent, inAppSaved }` |
| POST | `/api/v1/internal/package-payments/{id}/reject` | `{ actorName, actorId, reason, note? }` | `{ payment, emailSent, inAppSaved }` |

- ผู้กระทำบันทึกเป็น `admin:{actorName}#{actorId}` · เรียก `PackagePaymentAdminService` ตัวเดียวกับ spadmin · error code ชุดเดียวกับ 5.1

**Admin `/api/v1/servicepro/*`** (`[Authorize(Roles="Admin")]` · ไม่ขึ้นกับ `X-Database-Id`)
| Method | Path | ผล |
|---|---|---|
| GET | `/api/v1/servicepro/sites` | `{ configured: bool, reachable: bool, generatedAt?, sites: [...] }` · ไม่ตั้ง `ServicePro:BaseUrl`/`InternalKey` = `configured:false` · เรียกไม่ได้ = `reachable:false` + ข้อความ |
| GET | `/api/v1/servicepro/payments?status=pending` | ระยะ B ส่งต่อจาก ServicePro |
| GET | `/api/v1/servicepro/payments/{id}/slip` | ระยะ B stream ไฟล์ |
| POST | `/api/v1/servicepro/payments/{id}/approve` · `/reject` | ระยะ B · client ส่ง `{ until?, note? }` / `{ reason, note? }` · Admin เติม `actorName`/`actorId` จาก claim `name`/`sub` |

---

## 9. ปัญหาของระบบเดิมที่แผนนี้ไม่รับมา

| ระบบเดิม | แผนนี้ |
|---|---|
| ยืนยันกับต่อวันเป็น 2 call แยก และ call ที่สองเขียนวันเดิมทับ | ต่อวัน + ปิดรายการ + audit ใน transaction เดียว (กฎ 7) |
| คำนวณวันใหม่ตอนแจ้ง (รายการค้างหลายรายการต่อซ้อนกันไม่ได้) | คำนวณตอนอนุมัติจากวันหมดล่าสุด (หัวข้อ 4) |
| แจ้งใหม่วันเดียวกันยกเลิกทุกรายการเดิม รวมรายการที่อนุมัติแล้ว | รอตรวจได้ครั้งละ 1 รายการ ยกเลิกเองได้ ไม่แตะรายการอื่น (กฎ 2) |
| ไม่มีปฏิเสธบนหน้าเว็บ | ปฏิเสธพร้อมเหตุผลตายตัว (กฎ 8) |
| ผู้แจ้ง/ผู้ยืนยันส่งมาจาก client · API ไม่ต้องล็อกอิน | ผู้แจ้งจาก JWT · ผู้อนุมัติจาก `--by` / บัญชี Admin |
| ไม่ตรวจไฟล์ · เก็บลงดิสก์เครื่อง | `IAttachmentStorage` เดิม (ชนิด/ขนาด/path traversal/SHA-256) |
| `PackageDaysAmout` ไม่อัปเดตตอนยืนยัน | อัปเดตใน SQL เดียวกับวันหมด (`UpdateBranchEndSql` เดิม) |
| ยอดเงิน/หมายเหตุอยู่ใน JSON ของ `EntityLog` | คอลัมน์จริง |
| ข้อความเตือนกับเงื่อนไขจริงไม่ตรงกัน (เตือนหลังหมดอายุ) | ใช้ `PlanPolicy` เดิม + เพิ่มแถบเตือนให้ paid (หัวข้อ 6.1) |
| เวลาหน่วง 3 วินาทีในทุกคำขอแจ้งชำระ | ส่ง LINE หลังบันทึกโดยไม่บล็อก |

> เอกสารต้นทางมีรายละเอียดช่องโหว่ของระบบ Enterprise ที่ยังเปิดอยู่ ไม่คัดลอกมาที่นี่เพราะ repo นี้เป็น public
> และไม่ควร commit ไฟล์ต้นทางเข้า repo

---

## 10. แบ่งงาน

| PR | repo · branch | ขอบเขต | ขึ้นกับ |
|---|---|---|---|
| A | ServicePro · `fix/plan-no-relogin-on-upgrade` (จาก main) | หัวข้อ 4.1 (`PlanAccess.RevocationReason`) | ไม่มี |
| 1 | ServicePro · `feat/package-renewal-api` | ตาราง + migration SelfServe · กฎ/สูตรวัน · เก็บสลิป · LINE + ลิงก์สลิป · endpoint ลูกค้า · `spadmin` 4 คำสั่ง · อีเมล/แจ้งเตือน · งานรายวัน · purge · rate limit · secrets/env.example · docs/03 §16 | ฐาน `feat/package-renewal` (เอกสารนี้) |
| 2 | ServicePro · `feat/package-renewal-web` | เว็บ `/package` · ปุ่มบนแถบทดลองใช้ · แถบเตือน paid · แจ้งเตือนพาไปหน้าแพ็กเกจ | สัญญาหัวข้อ 5 (ทำคู่ขนานกับ PR 1) |
| 3 | ServicePro · `feat/package-renewal-app` | แอปหน้าแพ็กเกจ + แจ้งชำระ (รูปเท่านั้น) · แบนเนอร์แตะได้ · แอป `1.2.0+4` | สัญญาหัวข้อ 5 (ทำคู่ขนานกับ PR 1) |
| 4 | ServicePro · `feat/package-renewal-internal` | API ภายใน `GET /internal/sites` + `X-Internal-Key` + Nginx ปิด `/internal` + docker network ร่วม | PR 1 |
| 5 | Admin · `feat/branches-package-status` | `/branches` แท็บ GaragePro (สถานะแพ็กเกจ + สลิป Enterprise) + แท็บ ServicePro | สัญญาหัวข้อ 8.6 (ทำคู่ขนาน) |
| 6 | ServicePro · `feat/package-renewal-internal-review` + Admin · `feat/servicepro-payment-review` | ระยะ B: ดูสลิป/อนุมัติ/ปฏิเสธจาก Admin | PR 4, 5 |

ลำดับ merge ที่แนะนำ: A → ฐานเอกสาร → 1 → 2, 3, 4 → 6 (ServicePro) · Admin 5 → 6 · deploy backend ก่อนแอปถึงมือผู้ใช้

**บันทึกจาก PR 1 (backend + spadmin)** สัญญาหัวข้อ 5/8.6 ไม่เปลี่ยน · รายละเอียดที่สัญญาไม่ได้ระบุ:
- `cannotSubmitCode` เลือกเหตุผลแรกตามลำดับ not_renewable → unavailable → not_manager → pending_exists
- สลิปที่ประกาศ content-type ว่างหรือ `application/octet-stream` รับได้ (ตัดสินจากเนื้อไฟล์) · รูป JPEG ที่ยังเกิน 1 MB ที่ quality 70 ถูกย่อต่อ (1280/1024/800px)
- ถัง `public-package-slip` นับต่อ IP · `field: "id"` ใช้กับ `--id` ของ spadmin ที่สั้นเกิน/ตรงหลายรายการ
- อีเมลเตือนอู่ paid นับวันถึง "วันสุดท้ายที่ใช้ได้" (กติกาเดียวกับ `daysLeft`) · tag อีเมลเตือนคือ `paid-reminder-{รอบ}`
- ชน unique ตอน INSERT ตัดสินด้วยการอ่านซ้ำ (requestId เดิม → แถวเดิม · ไม่งั้นรอตรวจซ้อน) ไม่ parse ชื่อ index
- ผลอนุมัติ/ปฏิเสธฝั่ง Application คืน `PackagePaymentAdminItem` (รายการเต็ม + ข้อมูลอู่ + สลิปซ้ำ + พรีวิว) ให้ API ภายในระยะ B แปลงเป็น JSON ตาม 8.6
- ราคา (2026-10-22) คำนวณที่ `PackagePricing` ที่เดียว: ไม่แยก VAT ได้ `vat: 0.00` และ `price = subtotal` (ไม่มี `vatRate`) · `VatRate` = 0 ถือว่าไม่แยก VAT · `VatRate` ผิดช่วง (เช่น 7) = ไม่แสดงราคาเลย และ `spadmin payment` บอกเหตุผล · `expectedAmount` เก็บยอดรวม VAT ณ ตอนแจ้ง · ข้อความ LINE มีบรรทัด "ราคามาตรฐานรวม VAT 7%: ก่อน VAT + VAT = รวม"

### การทดสอบที่ต้องมี
- unit: สูตรวัน (ต่อก่อน/หลังหมด · ปัดเที่ยงคืน · 31 ม.ค. + 1 เดือน · `--until` ไม่หลัง base) · กฎ 1–13 · ราคาไม่ตั้ง · role ที่ไม่ใช่ Manager · truth table หัวข้อ 4.1
- SQL integration (`GarageSelfServe_Dev`): รอตรวจซ้อนถูก index ปฏิเสธ · requestId ซ้ำได้แถวเดิม · อนุมัติพร้อมกัน 2 ครั้งได้ผลครั้งเดียว ·
  วันหมดเปลี่ยนระหว่างอนุมัติ = ยกเลิกทั้งหมด · ต่อวัน + audit + รายการอยู่ transaction เดียว · purge
- E2E: แจ้งจากเว็บและแอป → LINE เห็นรูป → `spadmin approve-payment` → อู่ที่อ่านอย่างเดียวกลับมาเขียนได้ภายใน 60 วิ (cache นโยบาย) **โดยไม่ถูก logout** ·
  ปฏิเสธ → เจ้าของเห็นเหตุผล · มีรายการรอตรวจแล้วงานรายวันไม่ purge · purge ลบไฟล์สลิป
- API ภายใน: server ไม่ตั้งคีย์ 404 · ไม่มี/ผิดคีย์ 401 · เรียกผ่านโดเมนสาธารณะถูก Nginx ปฏิเสธ · id สาขาที่ชนกันระหว่าง db2 กับ sp1 แยกแถวถูก
- Admin: xUnit ของฟังก์ชันสถานะแพ็กเกจ (ขอบวันไทย · ช่วงผ่อนผัน 15 วัน · ไม่มีวันหมด) · `npm run type-check` / `lint` / `build`


---

## 11. ยังต้องได้จากฝ่ายขาย/เจ้าของระบบ

| เรื่อง | ใช้ที่ไหน | ถ้ายังไม่มี |
|---|---|---|
| ~~ราคาต่อจำนวนเดือน~~ **ได้แล้ว 2026-10-22**: 3,999 บาท/เดือน ก่อน VAT + VAT 7% | ยอดที่ควรโอน · เทียบตอนตรวจ | |
| ~~บัญชีรับเงิน~~ **ได้แล้ว 2026-10-22**: กสิกรไทย 068-8-63750-3 บจก. อาร์มาดิลโล เทค จำกัด | หน้าแจ้งชำระ | |
| ใบเสร็จ/ใบกำกับภาษีค่าบริการให้ลูกค้า | หลังอนุมัติ | นอกขอบเขต ทีมบัญชีออกเองนอกระบบ |
| คนในทีมที่อนุมัติได้ | `--by` / สิทธิ์ใน Admin | ใครที่รัน spadmin ได้ |
| แจ้งเตือนอู่ paid ล่วงหน้ากี่วัน | แถบเตือน + อีเมล | 14 และ 3 วัน `[ASSUME]` |

---

## 12. สถานะ (2026-10-22)

- โค้ดครบทุก PR ในหัวข้อ 10 · รวมทดสอบใน branch `integration/package-renewal` (ไม่ push) ผ่านทั้ง backend เว็บ แอป และ Admin
- migration `AddPackagePayment` รันกับ `GarageSelfServe_Dev` แล้ว · production รันตอน deploy (`efbundle-selfserve`)
- E2E กับ API local + ฐาน dev + Admin local ผ่านครบเส้นทางหลัก (แจ้ง · อนุมัติ · ปฏิเสธ · ยกเลิก · อ่านอย่างเดียวแล้วต่ออายุ · ไม่ถูก logout · API ภายใน · Admin) รายละเอียดใน CLAUDE.md
- ยังไม่ได้ทดสอบ: LINE จริง · ปุ่มแจ้งชำระบนเว็บกับไฟล์จริง · แจ้งชำระจากแอปด้วยบัญชีผู้จัดการบน simulator · Android · หน้าเว็บ Admin กับ ServicePro จริง · Nginx/docker network บน production
- ก่อนเปิดใช้: ต้องได้บัญชีรับเงินจากฝ่ายขาย (หัวข้อ 11) และตั้ง secrets ตามหัวข้อ 5.5 / 8.6
