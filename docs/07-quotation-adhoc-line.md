# 07 · รายการนอกแคตตาล็อก (Ad-hoc Line) ในใบเสนอราคา

เพิ่ม 2026-09-15 ตามคำขอผู้ใช้: หน้าร้าน/ธุรการต้องเพิ่มรายการที่ไม่มีในแคตตาล็อกลงใบเสนอราคาได้สะดวก
โดยคีย์แค่ฟิลด์ที่จำเป็น ไม่ต้องสร้างสินค้าในแคตตาล็อกก่อนเสมอไป

## ปัญหาเดิม

ทุกบรรทัดในใบเสนอราคา **ต้อง** มีรหัสสินค้าที่มีอยู่จริงใน `svc_CatalogItem` ของสาขานั้น —
`QuotationService.AddLineAsync` (`backend/AMD.AutoService.GaragePro.Application/Quotations/QuotationService.cs:89-93`)
ยิง `CATALOG_ITEM_NOT_FOUND` ถ้าหาไม่เจอ และ `Name`/`Type`/`Unit`/`UnitCost`/`StandardHours`
ถูกก็อปมาจากแคตตาล็อกทั้งหมด (`UpsertLineRequest` ไม่มีฟิลด์ `Name` ให้ส่งด้วยซ้ำ)

ทางออกเดียวที่มีตอนนี้คือปุ่ม "เพิ่มรายการใหม่" ใน `CatalogPanel.tsx:154-163` ซึ่ง:
- โผล่**เฉพาะตอนค้นหาแล้วไม่เจอ**เท่านั้น
- เปิดฟอร์มสินค้าเต็มรูป (`CatalogFormModal`) ที่บังคับ **รหัสสินค้าที่ต้องคิดเอง** (ไม่มีปุ่มสร้างรหัสอัตโนมัติ
  ต่างจากคลัง/หมวดหมู่/ซัพพลายเออร์), หน่วยนับ, ต้นทุน, ราคา, และ `standardHours` ถ้าเป็นค่าแรง
- gate ด้วย `canSeeCost` (Manager)
- ปิดแล้ว**ไม่เพิ่มบรรทัดให้** ต้องกลับไปค้นหาใหม่เอง (ไม่ได้ส่ง `onSuccess`)
- `initialName` ที่ส่งเข้าไปไม่เคยถูกใช้จริง (`CatalogPage.tsx:219-230` reset เป็น `emptyForm` เสมอ) — บั๊กแฝง

## การตัดสินใจที่ยืนยันกับผู้ใช้แล้ว (2026-09-15)

1. เก็บเป็น**บรรทัดเฉพาะกิจ (ad-hoc)** เป็นค่าเริ่มต้น + มี checkbox เลือกบันทึกเข้าแคตตาล็อกได้
2. ฟิลด์บังคับขั้นต่ำ = **ประเภท + ชื่อ + ราคา/หน่วย** (จำนวน default 1, หน่วย default ตามประเภท, ต้นทุนไม่บังคับ)
3. **ทุกคนที่แก้ใบเสนอราคาได้** เพิ่มรายการนอกแคตตาล็อกได้ (ไม่ผูก `canSeeCost`)

## กติกาใหม่ที่เพิ่มเข้าระบบ

- **ad-hoc line = บรรทัดที่ `CatalogCode` เป็นค่าว่าง** (sentinel) — ไม่เพิ่มคอลัมน์ใหม่ **จึงไม่ต้องมี migration**
  (`CatalogCode` เป็น `IsRequired()` = NOT NULL เท่านั้น `""` เก็บได้ปกติ) และ entity เดิมไม่มี FK ไป `CatalogItem`
  อยู่แล้ว (เป็น snapshot ตามหลัก version-first) จึงไม่ขัดหลักการเดิม
- **ต้นทุนยังคงเป็นค่าที่ server ควบคุม** ตามกฎข้อ 7 ของ CLAUDE.md — รับ `unitCost` จาก client เฉพาะเมื่อผู้เรียกมี
  `CanSeeCost` ไม่งั้นบังคับเป็น 0
- กฎเดิมที่ยังบังคับเหมือนเดิมทุกข้อ: ค่าแรงต้องระบุช่างก่อนส่ง (`QUOTE_LINE_NO_TECHNICIAN`),
  ราคาต้อง > 0, ห้ามรายการซ้ำ, ส่วนลด >10% ต้องผู้จัดการ

## Backend

### 1. `UpsertLineRequest` — `QuotationDtos.cs:150-158`

เพิ่มฟิลด์ optional ท้าย record (ไม่กระทบ caller เดิมที่ส่งเฉพาะ catalog path):
`string? Name`, `LineType? Type`, `string? Unit`, `decimal? UnitCost`, `decimal? StandardHours`
`CatalogCode` คงชนิดเดิมแต่ยอมรับค่าว่างได้

### 2. `AddLineAsync` — `QuotationService.cs:83-118`

แตกเป็น 2 ทาง โดย **ทาง catalog เดิมไม่เปลี่ยนพฤติกรรมเลย**:

- `CatalogCode` ไม่ว่าง → เส้นทางเดิมทุกประการ (รวม `CATALOG_ITEM_NOT_FOUND`)
- `CatalogCode` ว่าง → ad-hoc:
  - `Name` ว่าง → `QUOTE_LINE_NAME_REQUIRED` · เกิน 300 ตัว → `QUOTE_LINE_NAME_TOO_LONG`
  - `Type` เป็น null → `QUOTE_LINE_TYPE_REQUIRED`
  - `UnitPrice` ไม่มีค่า/≤ 0 → `QUOTE_LINE_PRICE_REQUIRED`
    (ต่างจาก catalog path ที่ fallback ไปราคาแคตตาล็อกได้)
  - `Unit` = ค่าที่ส่งมา (trim, ≤40) หรือ default `"ชิ้น"` (part) / `"งาน"` (labor)
  - `UnitCost` = `user.CanSeeCost ? (request.UnitCost ?? 0) : 0`
  - `StandardHours` = `request.StandardHours` เฉพาะ labor (**ไม่บังคับ** ต่างจาก `CatalogValidator` ที่บังคับ)
  - `CatalogCode = ""`
  - ActivityEvent เขียนเหมือนเดิมผ่าน `PersistAsync` แต่ข้อความระบุว่าเป็นรายการนอกแคตตาล็อก

### 3. `UpdateLineAsync` — `QuotationService.cs:120-141`

เพิ่ม: **ถ้าบรรทัดนั้นเป็น ad-hoc เท่านั้น** ให้แก้ `Name`/`Unit`/`UnitCost` ได้เมื่อส่งค่ามา
(cost เฉพาะ `CanSeeCost`) — บรรทัดที่มาจากแคตตาล็อกยัง**ห้าม**แก้ชื่อ/หน่วย/ต้นทุนเหมือนเดิม
จำเป็นเพราะฝั่งเว็บ PUT ทั้ง object ทุกครั้งที่แก้ช่องใดช่องหนึ่ง ถ้าไม่ทำ ชื่อจะถูกเขียนทับหาย

### 4. `QuotationValidator.ValidateForSend` — `QuotationValidator.cs:41-49`

ตอนนี้ `GroupBy(l => l.CatalogCode)` จะมอง ad-hoc **ทุกบรรทัด**เป็นรายการซ้ำกันหมด (code ว่างเท่ากัน) — ต้องแยก:
- บรรทัดที่มี code → group by code (ข้อความเดิม)
- บรรทัดที่ code ว่าง → group by `Name.Trim()` แบบ case-insensitive →
  `QUOTE_DUPLICATE_LINE` ข้อความใหม่ "รายการนอกแคตตาล็อก “{ชื่อ}” ซ้ำกัน N บรรทัด — ต้องรวมเป็นบรรทัดเดียว"

### 5. `QuotationMapper` — `QuotationMapper.cs:75`

เพิ่ม `IsAdHoc` (derived จาก `string.IsNullOrWhiteSpace(l.CatalogCode)`) ใน `QuotationLineDto`
(`QuotationDtos.cs:35` โซนเดียวกับ `CatalogCode`) เพื่อให้ client ไม่ต้องเดาจาก string ว่าง

### ไม่ต้องแก้

`ReviseAsync` (`QuotationService.cs:239-264`) ก็อป snapshot ครบทุกฟิลด์อยู่แล้ว ad-hoc จึงรอดข้ามเวอร์ชันเอง ·
`QcChecklistService` อ่านจาก snapshot ล้วน ไม่แตะ `CatalogItem` ·
`PosService`/รายงาน/เอกสารพิมพ์ ใช้ snapshot ทั้งหมด · **ไม่มี migration**

## Frontend

### 6. ฟอร์มเพิ่มรายการเอง — `CatalogPanel.tsx`

- ปุ่ม **"＋ เพิ่มรายการเอง (ไม่มีในแคตตาล็อก)"** อยู่ใต้ช่องค้นหา **ตลอดเวลา** ไม่ใช่เฉพาะตอนค้นไม่เจอ
  (empty state ก็ยังโชว์ซ้ำอีกจุดเพราะเป็นจังหวะที่ต้องใช้พอดี)
- กดแล้วกางเป็นการ์ดฟอร์มในพาเนลเดิม (ไม่เปิด modal ซ้อน modal) **prefill ชื่อจากคำค้นที่พิมพ์ค้างไว้**
- ช่องกรอก: ประเภท (ปุ่มสลับ อะไหล่/ค่าแรง, default อะไหล่) · ชื่อรายการ\* · ราคา/หน่วย\* (`MoneyInput`) ·
  จำนวน (default 1) · หน่วย (default ตามประเภท สลับให้อัตโนมัติถ้าผู้ใช้ยังไม่แก้เอง) ·
  ต้นทุน/หน่วย (แสดงเฉพาะ `canSeeCost`) · ชั่วโมงมาตรฐาน (แสดงเฉพาะค่าแรง ไม่บังคับ)
- ปิดท้ายด้วยปุ่มคู่ **ลูกค้าขอ / ช่างแนะนำ** แบบเดียวกับการ์ดแคตตาล็อก (เป็นตัวกำหนด `source`)
- checkbox **"บันทึกเข้าแคตตาล็อกเพื่อใช้ครั้งต่อไป"** — แสดงเฉพาะ `canSeeCost`
  (เพราะ `CatalogService.EnsureCanManage` บังคับสิทธิ์นี้อยู่แล้ว จึงไม่ต้องแก้สิทธิ์ backend)
  ติ๊กแล้วโผล่ช่องรหัสพร้อมค่าที่สร้างให้อัตโนมัติ `AD-{yyMMdd}-{HHmmss}` + ปุ่ม "สร้างรหัส" (`WandSparkles`
  ตาม pattern `WarehousePage.tsx:60`) แก้เองได้
- ปุ่มที่กดไม่ได้ต้องบอกเหตุผลเสมอ (`title`) ตามกฎ UI — เช่น "กรอกชื่อรายการก่อน" / "กรอกราคาก่อน"

**Flow เมื่อกดเพิ่ม**
- ไม่ติ๊ก → `addQuotationLine(id, { catalogCode: '', name, type, unit, unitPrice, unitCost, quantity, ... })`
- ติ๊ก → `createCatalogItem(...)` ก่อน (ค่าที่ไม่ได้ถาม ส่ง default: `compatibility` ว่าง, stock ทั้ง 4 = 0,
  ไม่ผูกหมวด/คลัง) แล้วค่อย `addQuotationLine` ด้วยรหัสจริง → ได้บรรทัดแบบแคตตาล็อกปกติ ไม่ใช่ ad-hoc
  · รหัสซ้ำให้โชว์ error จาก backend ตรงๆ พร้อมให้แก้รหัสในฟอร์มเดิม

### 7. แก้บรรทัด ad-hoc ได้ในที่ — `LineEditor.tsx`

- หัวบรรทัด: ad-hoc แสดง `Badge` **"นอกแคตตาล็อก"** แทนรหัส (สี + ไอคอน + ข้อความ ตามกฎ UI)
- ชื่อรายการ + หน่วย กลายเป็น `Input` แก้ได้ในที่ (เฉพาะ ad-hoc) · ต้นทุน/หน่วย แก้ได้เมื่อ `canSeeCost`
  ทุกช่องใช้ `onPatch` เดิม (optimistic + debounce 400 ms) ไม่ต้องทำกลไกใหม่
- ต้องส่ง `canSeeCost` เข้ามา (prop จาก `QuotationEditorModal` หรือ `useSession` ตรงในไฟล์)

### 8. ท่อ API ฝั่งเว็บ

- `UpsertLine` ใน `types.ts:331-340`: `catalogCode` ยอมรับ `''`,
  เพิ่ม `name?`/`type?`/`unit?`/`unitCost?`/`standardHours?`
- `QuotationLine` read model: เพิ่ม `isAdHoc: boolean`
- **สำคัญ**: `toUpsertLine` (`QuotationEditorModal.tsx:64-75`)
  ต้องแนบ `name`/`unit`/`unitCost` ไปด้วยสำหรับ ad-hoc ไม่งั้น PUT ที่เกิดจากการแก้ "จำนวน" จะลบชื่อทิ้ง
- `addMutation` รับ payload ได้ทั้งแบบ catalog item และแบบ ad-hoc

### 9. จุดที่แสดงรหัสต้องกันช่องว่าง

- `QuotationDocumentModal.tsx:407` (ใบเสนอราคา A4) → แสดง `—`
- `JobCardModal.tsx:802` (ตาราง QC checklist) → แสดง `—`

### 10. ใบเบิกสินค้าต้องไม่เงียบ — `StockWithdrawalModal.tsx:68-86`

ตอน prefill จากใบเสนอราคาที่อนุมัติ ให้ **ข้าม ad-hoc part line อย่างชัดเจน** (ไม่ต้องยิงค้นหาให้เปลือง)
แล้วแสดง `section-help` ว่า "มี N รายการนอกแคตตาล็อก เบิกจากสต็อกไม่ได้ (เป็นของซื้อนอก) — เพิ่มเองได้จากช่องค้นหา"
ปิดช่องที่ของหายเงียบๆ ตามกฎ UI เรื่อง state ต้องมีสาเหตุ

## Tests

`backend/AMD.AutoService.GaragePro.Tests/QuotationServiceTests.cs` (+ `QuotationValidatorTests` ถ้ามีไฟล์แยก):
1. เพิ่ม ad-hoc line สำเร็จด้วย ประเภท+ชื่อ+ราคา · `isAdHoc = true` · unit ได้ default ตามประเภท
2. ชื่อว่าง → `QUOTE_LINE_NAME_REQUIRED` · ไม่ส่ง type → `QUOTE_LINE_TYPE_REQUIRED` · ราคา ≤ 0 → `QUOTE_LINE_PRICE_REQUIRED`
3. role ที่ไม่มี `CanSeeCost` ส่ง `unitCost` มา → เก็บเป็น 0 (กฎข้อ 7)
4. update ad-hoc แก้ชื่อได้จริง · update บรรทัดจากแคตตาล็อกส่ง `name` มาก็ไม่เปลี่ยนชื่อ
5. ad-hoc 2 บรรทัด **ชื่อต่างกัน** ส่งใบได้ (regression ของบั๊ก duplicate ที่จะเกิดถ้าไม่แก้ validator) ·
   **ชื่อเดียวกัน** → `QUOTE_DUPLICATE_LINE`
6. ad-hoc ที่เป็นค่าแรงยังติด `QUOTE_LINE_NO_TECHNICIAN` ถ้าไม่ระบุช่าง
7. `ReviseAsync` ก็อป ad-hoc ไปฉบับใหม่ครบ (ชื่อ/หน่วย/ต้นทุน)

`QuotationCalculatorTests` เดิม (ตัวเลข 8,838.20 / 5,628.20) ต้องยังเขียวเหมือนเดิม

## Verification

```bash
dotnet build AMD.AutoService.GaragePro.sln
dotnet test backend/AMD.AutoService.GaragePro.Tests          # เดิม 167 ผ่าน → ต้องผ่านทั้งหมด + ที่เพิ่มใหม่
cd web && node_modules/.bin/tsc -b && node_modules/.bin/vite build
```
(ใช้ `node_modules/.bin/` ตรงๆ เพราะ pnpm/corepack เครื่องนี้ verify signature ไม่ผ่าน)

**End-to-end ในเบราว์เซอร์** (ต้องต่อ Garage Pro VPN + รัน API + `cd web && pnpm dev`):
1. เปิดใบเสนอราคา draft → กด "เพิ่มรายการเอง" → คีย์ ค่าแรง / "ถอดล้างเทอร์โบ" / 1,500 → "ลูกค้าขอ" →
   บรรทัดขึ้นพร้อม badge "นอกแคตตาล็อก"
2. แก้ชื่อ/จำนวน/ราคาในบรรทัดนั้น → reload → ค่าที่แก้ยังอยู่ครบ (พิสูจน์ว่า PUT ไม่ลบชื่อ)
3. เพิ่ม ad-hoc อีกบรรทัดชื่อต่างกัน → กด "ส่งให้ลูกค้าอนุมัติ" → **ต้องไม่ติด** รายการซ้ำ ·
   แล้วลองเพิ่มบรรทัดชื่อซ้ำ → ต้องติด `QUOTE_DUPLICATE_LINE`
4. ค่าแรง ad-hoc ที่ไม่ระบุช่าง → ส่งไม่ได้ พร้อมข้อความเดิม
5. ติ๊ก "บันทึกเข้าแคตตาล็อก" (ล็อกอินด้วย Manager) → สินค้าโผล่ในหน้า `/catalog` และค้นเจอในพาเนลรอบถัดไป
6. พิมพ์ใบเสนอราคา A4 → ช่องรหัสเป็น `—` ไม่ใช่ช่องว่างเปล่า
7. อนุมัติ → ขั้นเบิกสินค้า: ad-hoc part ไม่เข้าตะกร้า แต่มีข้อความบอกจำนวนที่ข้าม ·
   ขั้น QC: ad-hoc โผล่เป็นรายการให้ติ๊กผ่านได้ปกติ
8. ออกฉบับแก้ไข → ad-hoc ตามไปฉบับใหม่ครบ

**หมายเหตุข้อจำกัด**: ไม่มี migration ในงานนี้ · ยังไม่ได้ทำ RBAC ของ quotation endpoint เพิ่ม
(ยังเป็นสิทธิ์เดิมตาม `[RISK]` ที่บันทึกไว้แล้วใน CLAUDE.md)

## สถานะ

✅ ทำเสร็จแล้ว 2026-09-15 — Backend (`UpsertLineRequest`/`AddLineAsync`/`UpdateLineAsync`/`QuotationValidator`/
`QuotationMapper`) + Frontend (`CatalogPanel.tsx` ฟอร์มเพิ่มรายการเอง, `LineEditor.tsx` แก้ในที่,
`QuotationDocumentModal.tsx`/`JobCardModal.tsx` กันช่องว่าง, `StockWithdrawalModal.tsx` ข้าม ad-hoc พร้อมแจ้งเตือน)
ตามแผนด้านบนครบทุกข้อ ไม่มี migration (ไม่ต้องมีตามที่ออกแบบไว้)

**ทดสอบแล้ว**: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 183 (เพิ่ม
`QuotationServiceTests.cs` ใหม่ 16 ผ่าน ครอบคลุมฟิลด์บังคับ/ต้นทุนตาม role/แก้ในที่/รายการซ้ำแยกจากแคตตาล็อก/
ออกฉบับแก้ไข) · Web `tsc -b` และ `vite build` ผ่าน (ผ่าน `node_modules/.bin/tsc`/`vite` ตรงๆ เพราะ pnpm/corepack
เครื่องนี้ verify signature ไม่ผ่านเหมือนทุกครั้ง)

**ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริง** (เพิ่มรายการเองจริง, แก้ชื่อ/ราคาแล้ว reload ไม่หาย, ส่งใบที่มี ad-hoc
2 บรรทัดชื่อต่างกันได้ ชื่อซ้ำติด error, ติ๊กบันทึกเข้าแคตตาล็อกแล้วเจอในหน้า `/catalog` จริง, พิมพ์เอกสาร A4, ขั้นเบิก
สินค้า/QC กับ ad-hoc line) — ต้องต่อ Garage Pro VPN ก่อนถึงจะทดสอบได้ครบ
