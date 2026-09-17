# 08 · เทมเพลตใบเสนอราคา (Quotation Template)

เพิ่ม 2026-09-16 ตามคำขอผู้ใช้: งานซ่อมชุดเดิม (เช่น เช็คระยะ 10,000 กม.) ไม่ต้องคีย์รายการซ้ำทุกครั้ง —
ผู้จัดการเตรียมชุดรายการมาตรฐานไว้ล่วงหน้า แล้วเพิ่มลงใบเสนอราคาได้ในคลิกเดียวจากทั้งการ์ดจ๊อบและหน้าต่างแก้ไขใบเสนอราคา

## การตัดสินใจที่ยืนยันกับผู้ใช้แล้ว (2026-09-16)

1. สร้าง/แก้ไข/เปิดปิดใช้งานเทมเพลต = **ผู้จัดการเท่านั้น** (`CanSeeCost`) — ตาม pattern เดียวกับ `CatalogService.EnsureCanManage`
2. นำเทมเพลตไปใช้กับใบเสนอราคา = **ทุกคนที่แก้ใบเสนอราคาได้** — ตาม pattern เดียวกับ ad-hoc line (docs/07 §3)
3. ปุ่ม "เพิ่มรายการด้วยเทมเพลต" อยู่**ทั้ง 2 จุด**: หัวการ์ด "ใบเสนอราคา & รายการซ่อม" ในการ์ดจ๊อบ และในพาเนลแคตตาล็อกของหน้าต่างแก้ไขใบเสนอราคา

## กติกาใหม่ที่เพิ่มเข้าระบบ

### รูปร่างข้อมูล — 2 ตารางใหม่ (ตารางแรกที่ไม่ได้ reuse sentinel เดิม)

`svc_QuotationTemplate` (header) + `svc_QuotationTemplateLine` (บรรทัด, มิเรอร์ฟิลด์ของ `UpsertLineRequest`
เกือบทั้งหมด) — unique index `UX_svc_QuotationTemplate_Code` บน `(LegacyShardKey, LegacyBranchId, Code)`
(ไม่ทำตาม `UX_svc_Warehouse_Code` ที่ unique ทั้งระบบ ซึ่งขัดกฎ multi-tenant ของ CLAUDE.md §2 อยู่ — `[RISK]` ของเดิม
ไม่ repeat ในตารางใหม่นี้)

`QuotationTemplateLine.CatalogCode == ""` = รายการนอกแคตตาล็อก (sentinel เดียวกับ `QuotationLine`) ·
**ไม่เก็บ `AssignedTechnicianId`** — เทมเพลตเป็นข้อมูลระดับสาขาที่ใช้ซ้ำข้ามจ๊อบ ฝังชื่อช่างไว้จะได้คนที่อาจ
ลาออกไปแล้ว และไปกลบ gate `QUOTE_LINE_NO_TECHNICIAN` ด้วยข้อมูลเก่า — บรรทัดค่าแรงที่ได้จากเทมเพลตจึงยังไม่มีช่าง
จนกว่าจะมีคนระบุเอง แล้ว `QuotationValidator.ValidateForSend` จะบล็อกการส่งถ้ายังไม่ระบุ (ล้มเหลวแบบมองเห็นได้
ดีกว่าค่าผิดแบบเงียบๆ) · ไม่เก็บยอดรวมใดๆ ที่ header (ยอดที่เก็บไว้จะเท็จทันทีที่ราคาแคตตาล็อกเปลี่ยน)

### การแปลงเทมเพลต → บรรทัดใบเสนอราคา (`QuotationService.ApplyTemplateAsync`)

ตรรกะเดียวกับสองสาขาของ `AddLineAsync` (docs/07) เป๊ะ — ไม่มีการคิดสูตรราคาใหม่:

- **บรรทัดแคตตาล็อก**: อ่าน Name/Type/Unit/UnitCost/StandardHours **สดจากแคตตาล็อกเสมอ** (ผ่าน
  `ICatalogRepository.GetByCodesAsync` แบบ batch ครั้งเดียวทั้งชุด) `UnitPrice` = ค่าที่เก็บในเทมเพลตถ้ามี
  (override) หรือราคาสดถ้าไม่มี (`null`) — **ชื่อ/ราคาที่ cache ไว้ในเทมเพลตใช้แสดงผลก่อน apply เท่านั้น
  ห้ามอ่านตอน apply จริง** กันชื่อ/ราคาเก่าค้าง
- **บรรทัดนอกแคตตาล็อก**: ใช้ค่าที่เก็บในเทมเพลตตรงๆ ทั้งหมด (เหมือน ad-hoc line ปกติ)
- **ต้นทุนบรรทัดนอกแคตตาล็อก** เก็บ**ค่าจริงลงฐานเสมอ** แม้ผู้กด apply จะไม่มีสิทธิ์เห็นต้นทุน — **ต่างจาก
  ad-hoc line ปกติที่บังคับเป็น 0** เพราะที่นั่นต้นทุนมาจาก client ที่เชื่อไม่ได้ ส่วนนี่มาจากแถวที่ผู้จัดการเขียนไว้
  ฝั่ง server ถ้าบังคับเป็น 0 จะทำให้ `MarginAmount`/รายงานกำไรพังทุกครั้งที่แคชเชียร์เป็นคนกด apply —
  **strip ที่ `QuotationTemplateMapper`/`QuotationMapper` ตาม role ตอนอ่านแทน** (invariant #7 เป็นกฎ
  serializer ไม่ใช่กฎการเก็บ) — **`[RISK]` เป็นความไม่สมมาตรที่ตั้งใจ อย่า "แก้กลับ" ให้บังคับ 0 เหมือน ad-hoc**

**รหัสหาย vs ถูกปิดใช้งาน — คนละเรื่องกัน:**
- **หาไม่เจอ (`QUOTE_TEMPLATE_ITEM_MISSING`)** → หยุดทั้งหมด **ไม่เพิ่มสักบรรทัด** (all-or-nothing) เพราะไม่มี
  endpoint ลบใบเสนอราคา การเพิ่มครึ่งๆ จะทำให้ผู้ใช้ไม่รู้ว่าอะไรเข้าไปแล้วบ้าง
- **ถูกปิดใช้งาน** → ไม่บล็อก (ทางเพิ่มบรรทัดปกติก็ไม่บล็อกอยู่แล้ว — `GetByCodesAsync` ไม่กรอง `IsActive`)
  แต่แสดง badge เตือนในหน้าต่างเลือกเทมเพลตก่อนกด apply

**รายการซ้ำ (`QUOTE_TEMPLATE_DUPLICATE_LINE`)** → บล็อก 409 ไม่เพิ่มสักบรรทัด ไม่ auto-merge จำนวน
ไม่ข้ามเงียบๆ — ใช้ **`QuotationValidator.FindDuplicateGroups`** (สกัดออกมาจาก `ValidateForSend` เดิม) ตรวจ
ทั้งบรรทัดในเทมเพลตเองตอนบันทึก และบรรทัดในเทมเพลตเทียบกับบรรทัดที่มีอยู่แล้วในใบตอน apply — เพื่อไม่ให้นิยาม
"ซ้ำ" เพี้ยนกันระหว่างสามจุด (บันทึกเทมเพลต / apply เทมเพลต / ส่งใบเสนอราคา)

**apply ทั้งชุดใน `PersistAsync` เดียว** — คำนวณยอดครั้งเดียว, save ครั้งเดียว, `ActivityEvent` ใบเดียว
(`quotation.lines.from_template`, ข้อความมีแค่รหัส/ชื่อเทมเพลต/จำนวนบรรทัด **ไม่มีต้นทุน/กำไร** เพราะ timeline
แสดงทุก role) — ถ้าให้ client ยิงทีละบรรทัดจะรก timeline และค้างครึ่งทางได้เมื่อ mid-sequence ล้มเหลว

`source` ใน request เป็น `null` ได้ (ใช้ค่า `Source` ที่เก็บในแต่ละบรรทัดของเทมเพลต — เทมเพลตผสม
ลูกค้าขอ/ช่างแนะนำ ได้) หรือระบุ override ทุกบรรทัดพร้อมกัน

## Backend

- `Domain/Entities/QuotationTemplate.cs` — `QuotationTemplate` + `QuotationTemplateLine`
- `Infrastructure/Persistence/Configurations/QuotationTemplateConfiguration.cs` — auto-register ผ่าน
  `ApplyConfigurationsFromAssembly` · migration `20260916154843_AddQuotationTemplate`
- `Application/Abstractions/IQuotationTemplateRepository.cs` + `Infrastructure/Persistence/QuotationTemplateRepository.cs`
  (แปลง `SqlException 2601/2627` → `MasterDataConflictException` แบบเดียวกับ `MasterDataRepository`/
  `PurchasingRepository` — คงรูปแบบ duplicate try/catch ต่อ repo ตาม convention เดิมของโปรเจกต์ ไม่ได้สกัดเป็น
  helper กลางเพราะ `MasterDataRepository`/`PurchasingRepository` ก็ไม่ได้ใช้ร่วมกันอยู่แล้ว)
- `Application/Dtos/QuotationTemplateDtos.cs` · `Application/QuotationTemplates/{QuotationTemplateMapper,QuotationTemplateService}.cs`
- `API/Controllers/QuotationTemplateController.cs` — `GET/POST /api/v1/quotation-templates`,
  `GET/PUT /api/v1/quotation-templates/{id}`, `PATCH /api/v1/quotation-templates/{id}/status`
  (อ่านเปิดทุก role, เขียนเช็ค `EnsureManager` ที่ service)
- `QuotationsController.cs` — เพิ่ม `POST /api/v1/quotations/{id}/lines/from-template`
  (`{ templateId, source? }` → `QuotationDto` เดิม)
- `QuotationService.cs` — เพิ่ม `ApplyTemplateAsync`; ctor เพิ่ม dependency `IQuotationTemplateRepository`
- `Domain/Common/QuotationValidator.cs` — เพิ่ม `FindDuplicateGroups` (static, reuse ได้), `ValidateForSend`
  เดิมถูก refactor ให้เรียกใช้ตัวนี้แทนที่จะมี logic ซ้ำ (พฤติกรรม/ข้อความเดิมไม่เปลี่ยน — มีเทสต์ regression คุม)
- `MasterDataSupport.ConflictCode` — เพิ่ม `"UX_svc_QuotationTemplate_Code" => "QUOTE_TEMPLATE_CODE_DUPLICATE"`
- `Infrastructure/DependencyInjection.cs` — ลงทะเบียน repo/service ใหม่

### error code ใหม่

`QUOTE_TEMPLATE_NOT_FOUND` (404) · `QUOTE_TEMPLATE_CODE_DUPLICATE` (409) · `QUOTE_TEMPLATE_NO_LINES` ·
`QUOTE_TEMPLATE_LINE_NAME_REQUIRED` / `_TYPE_REQUIRED` / `_PRICE_REQUIRED` / `_QTY_INVALID` ·
`QUOTE_TEMPLATE_DUPLICATE_LINE` (409 ตอน apply) · `QUOTE_TEMPLATE_ITEM_MISSING` · `QUOTE_TEMPLATE_INACTIVE` ·
`QUOTE_TEMPLATE_EMPTY` · `MASTER_DATA_MANAGE_FORBIDDEN` (สิทธิ์เขียนเดิม)

## Frontend

- `web/src/api/types.ts` — `QuotationTemplate`/`QuotationTemplateLine`/`QuotationTemplateInput`/
  `QuotationTemplateLineInput`/`ApplyTemplateInput`
- `web/src/api/masterData.ts` — CRUD 5 ฟังก์ชัน (pattern เดียวกับ warehouse/supplier)
- `web/src/api/quotations.ts` — `applyQuotationTemplate`
- `web/src/features/master-data/QuotationTemplatePage.tsx` — หน้าข้อมูลหลัก (โครงเดียวกับ `WarehousePage.tsx`)
  พร้อมตารางกรอกบรรทัดในฟอร์ม (ค้นหาสินค้าด้วย `Combobox` ที่มีอยู่แล้ว + ปุ่มเพิ่มรายการนอกแคตตาล็อก + toggle
  "ใช้ราคาแคตตาล็อก" ต่อบรรทัด) · เส้นทาง `/quotation-templates` + เมนู Master Data ใน `AppShell.tsx`
- `web/src/features/quotations/QuotationTemplatePickerModal.tsx` — หน้าต่างเลือกเทมเพลต **ใช้ร่วมกันทั้ง 2 จุด**
  แสดง preview รายการที่จะถูกเพิ่มก่อนกด (จำเป็นเพราะไม่มี undo) พร้อม badge เตือนรหัสหาย/สินค้าปิดใช้งาน
- `web/src/features/quotations/CatalogPanel.tsx` — ปุ่ม "เพิ่มรายการด้วยเทมเพลต" ข้างปุ่ม "เพิ่มรายการเอง" เดิม
  (props ใหม่ `quotationId`/`onApplied`)
- `web/src/features/jobs/JobCardModal.tsx` `QuoteStage` — ปุ่มเดียวกันในหัวการ์ด พร้อม create-then-apply chain
  (ถ้ามีใบร่างอยู่แล้ว apply ลงใบนั้น ถ้ายังไม่มีใบหรือใบล่าสุดถูกปฏิเสธ/แทนที่แล้ว สร้างใบใหม่ก่อนแล้ว apply ต่อ) —
  **กรณีสร้างใบสำเร็จแต่ apply ล้มเหลว ไม่กลบ**: toast error ของจริง + ข้อความเพิ่มบอกว่าสร้างใบร่างไว้แล้ว
  ให้เปิดจากรายการด้านล่างเพื่อเพิ่มรายการเอง (ไม่มี endpoint ลบใบเสนอราคาให้ rollback)

## Tests

- `backend/AMD.AutoService.GaragePro.Tests/QuotationTemplateServiceTests.cs` (ใหม่ 12 ผ่าน) — สิทธิ์ผู้จัดการ,
  strip ต้นทุนตาม role, เทมเพลตว่าง/รายการซ้ำ/ad-hoc ไม่ครบ/รหัสไม่มีในแคตตาล็อก, update แทนที่บรรทัดทั้งชุด, รหัสซ้ำ
- `backend/AMD.AutoService.GaragePro.Tests/QuotationServiceTests.cs` (เพิ่ม 10 ผ่าน) — apply เพิ่ม event เดียว,
  ราคาสดจากแคตตาล็อกแม้ชื่อในเทมเพลตเก่า, override ราคา, ต้นทุนจริงในฐานแต่ strip ใน response, รหัสหาย/ซ้ำ →
  ไม่เพิ่มสักบรรทัด, ใบไม่ editable, เทมเพลตปิดใช้งาน/ว่าง, ช่างไม่ถูกกำหนดจนกว่าจะระบุเอง, source override
- ทดสอบแล้ว: `dotnet build` ทั้ง solution ผ่าน (0 error), `dotnet test` ผ่านทั้ง 210 (ไม่รวม 4 ที่ skip เพราะต้องต่อ
  SQL/FTP จริง) · Web `tsc -b`/`vite build` ผ่าน
- **ยังไม่ได้ทดสอบ end-to-end ในเบราว์เซอร์จริงและยังไม่ได้รัน migration กับ ServiceDb จริง** (sandbox ไม่ได้ต่อ
  Garage Pro VPN) — ต้องทำทั้งสองก่อนใช้งานจริง

## ขอบเขตที่ยังไม่ทำ

- ไม่มีการจัดลำดับบรรทัดในเทมเพลตแบบลาก (v1 = ตามลำดับที่เพิ่ม)
- ไม่มี `RowVersion` ที่ header เทมเพลต (last-write-wins เหมือนข้อมูลหลักตัวอื่น — ผู้จัดการ 2 คนแก้พร้อมกันจะเสียการแก้ไขของคนแรกอย่างเงียบๆ)
- ไม่มีการสร้าง/แก้ไขนัดหมายจากปฏิทิน หรือการเพิ่มบรรทัดเทมเพลตแบบลากเข้าไปในตารางในฟอร์ม
