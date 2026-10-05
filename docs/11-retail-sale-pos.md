# 11 · ขายสินค้าหน้าร้าน (POS) + โปรโมชัน

> **สถานะ: เริ่มพัฒนาแล้ว — core backend/web MVP เสร็จระดับ build** (2026-09-29) — เอกสารนี้คือแผนที่ผู้ใช้อนุมัติ ถ้าลงมือแล้วต่างจากนี้
> ให้แก้เอกสารนี้ตามของจริง อย่าปล่อยให้แผนกับโค้ดขัดกัน

## ทำไมต้องมี

ตอนนี้ระบบรับเงินได้ทางเดียวคือผ่าน Job (`PosService` + `Payment`/`Receipt`) ซึ่งผูก `JobId` แบบ non-null และ
`Receipt.JobId` เป็น unique (1 job 1 ใบเสร็จ) ขายอะไหล่หรือน้ำมันเครื่องให้ลูกค้าที่เดินเข้ามาโดยไม่มีรถเข้าซ่อมจึงทำไม่ได้

ผู้ใช้ขอเมนูใหม่ในกลุ่ม **Workplace** ที่ขั้นตอนสั้นๆ: ใส่สินค้า → ชำระเงิน → ออกใบเสร็จ หน้าตาคล้าย editor
ใบเสนอราคา และ**ต้องตัดสต็อก FIFO จริง** (ล็อต/ต้นทุน) ให้สอดคล้องกับโมดูลจัดซื้อ ([docs/06-purchasing-fifo.md](06-purchasing-fifo.md))

## การตัดสินใจที่ยืนยันกับผู้ใช้แล้ว (2026-09-28)

1. **ตัดสต็อกตอนปิดการขายเท่านั้น** บิลร่างไม่จองสต็อก การตัดสต็อก การบันทึกรับเงิน และการออกใบเสร็จอยู่ใน
   transaction เดียวกัน ถ้าของไม่พอ ระบบปฏิเสธทั้งก้อนและไม่บันทึกอะไรเลย
2. **ยกเลิกทั้งบิลได้หลังออกใบเสร็จ** (เฉพาะผู้จัดการ และต้องระบุเหตุผล) สต็อกคืนเข้า**ล็อต FIFO เดิมด้วยต้นทุนเดิม**
   ใบเสร็จถูกมาร์คว่ายกเลิกแต่ไม่ลบ
3. **เลขใบเสร็จเป็นชุดแยก `SL-{yy}-{seq:D4}`** นับต่อสาขาต่อปี ไม่ปนกับ `RC-` ของใบเสร็จงานซ่อม
4. **ลูกค้า:** ค้นหาลูกค้าที่มีในระบบด้วยเบอร์โทร (อ่านอย่างเดียว) หรือ walk-in แล้วพิมพ์ชื่อ/เบอร์เอง ไม่บังคับทั้งสองแบบ
5. **ส่วนลด:** ลด % ต่อบรรทัด และส่วนลดท้ายบิล (เลือกเป็น % หรือบาทก็ได้)
6. **โปรโมชัน: ผู้จัดการตั้งเองได้ในเมนูข้อมูลหลัก** ไม่ hard-code แบบ enum `Promotion` 3 แบบของใบเสนอราคา
   โปรแต่ละตัวเป็นแบบลด %/ลดบาท ใช้กับบรรทัดหรือทั้งบิล มีช่วงวันที่ และเปิด/ปิดใช้งานได้

### ค่าเริ่มต้นที่เลือกเอง (ยังไม่ได้ถามผู้ใช้ แก้ได้)

- สิทธิ์ขาย = Cashier/Office/Manager (ตรงกับ `PosService`) · เห็นต้นทุน/กำไร = Manager (`CanSeeCost`)
- VAT 7% แบบบวกเพิ่ม ปิดได้ต่อบิล (เหมือน `Job.VatIncluded`)
- ขายได้เฉพาะ `CatalogItem` ที่เป็น `Part` + `StockManaged` + `IsActive` (เงื่อนไขเดียวกับ `PurchasingService.IssueAsync`)
- ทำเฉพาะเว็บ มือถือยังไม่ทำรอบนี้

## รูปร่างข้อมูล

**แยก aggregate ใหม่ ไม่ generalize `Payment`/`Receipt` ของ Job** เพราะ `Receipt` ไม่มีคอลัมน์ shard/branch
(scope ผ่าน Job) มี unique `JobId` และถูกอ่านโดย guard ของ `JobService` (`Ready→Completed`), `HandoverService`
(กฎข้อ 18) และ `ReportsRepository` ถ้าทำให้ `JobId` เป็น nullable ต้องแก้ทุกจุดนี้

ทุก entity ตั้ง `ValueGeneratedNever()` และมี 3 คอลัมน์ scope `(LegacyShardKey, LegacyBranchId, id)` ตามกฎ

| ตาราง | ฟิลด์หลัก |
|---|---|
| `svc_Sale` | `Status` (`Draft`/`Completed`/`Voided`/`Cancelled`) · `WarehouseId` · `LegacyCustomerId?` + snapshot `CustomerName?`/`CustomerPhone?` · `VatIncluded` · ส่วนลดท้ายบิล `BillDiscountType` (`None`/`Percent`/`Amount`) + `BillDiscountValue` · `BillPromotionId?` + snapshot (ชื่อ/ชนิด/ค่า/เพดาน) · ยอด snapshot (`GrossAmount`, `LineDiscountAmount`, `LinePromotionAmount`, `SubtotalAmount`, `BillDiscountAmount`, `BillPromotionAmount`, `NetAmount`, `VatRate`, `VatAmount`, `TotalAmount`, `CostTotal`) · `ReceiptNo?` (unique filtered) · `CheckoutRequestId?`/`CheckoutRequestHash?` (unique filtered) · `CompletedAt/By` · `VoidedAt/ByName/Reason` · `VoidRequestId?` · `CreatedAt/ByName` · `RowVersion` |
| `svc_SaleLine` | `SaleId` · `CatalogItemId` · snapshot `Code/Name/Unit` · `Quantity` · `UnitPrice` · `DiscountPercent` · `PromotionId?` + snapshot · `DiscountAmount` · `PromotionAmount` · `NetAmount` · `CostAmount?` (เติมตอนปิดการขาย) |
| `svc_SalePayment` | `SaleId` · `Method` (reuse enum `PaymentMethod` + token helper ของ `PosMapper`) · `Amount` · `Reference?` |
| `svc_SaleReceiptNumberCounter` | key `(Shard, Branch, Year)` + `LastSequence` |
| `svc_Promotion` | `Code` (unique **ต่อสาขา** ไม่ทำตาม `UX_svc_Warehouse_Code` ที่ unique ทั้งระบบ) · `Name` · `Kind` (`Percent`/`Amount`) · `Value` · `MaxAmount?` (เพดานของแบบ % เช่น ลด 10% ไม่เกิน 300) · `Scope` (`Line`/`Bill`) · `MinSubtotal?` (เฉพาะ Bill) · `StartsAt?`/`EndsAt?` · `IsActive` · `RowVersion` · audit |
| `svc_StockMovement` | **เพิ่มคอลัมน์ `SaleId?`** + filtered index `(Shard, Branch, SaleId) WHERE SaleId IS NOT NULL` |

- **โปรโมชันถูก snapshot ลงบิลเสมอ** ผู้จัดการแก้หรือปิดโปรทีหลังได้โดยยอดของบิลที่ปิดไปแล้วไม่เปลี่ยน
- **`CostAmount` คิดจากล็อตจริงที่ถูกตัด** (Σ qty × `lot.UnitCost`) ห้ามทำแบบ `BuildWithdrawalDto` ที่ใช้
  `group.First().UnitCost` (ดู `[RISK]` ท้ายเอกสาร)
- **Type ของ movement ใหม่:** `"sale"` (qty ติดลบ) และ `"sale-void"` (qty บวก) ไม่ใช้ `"issue"` เพราะ
  `MovementsByJobAsync` และ `WithdrawalDetailAsync` กรอง `Type == "issue"` ถ้าใช้ `"issue"` การขายจะไปปนในประวัติเบิกของ job
- Migration เดียวชื่อ `AddRetailSaleAndPromotion` เพิ่มของใหม่อย่างเดียว ไม่แก้ของเดิม (ใช้ `dotnet ef migrations add` ห้ามใช้ `EnsureCreated`)

## ลำดับคำนวณเงิน (`Domain/Common/SaleCalculator.cs`)

ปัดเศษ 2 ตำแหน่งแบบ `MidpointRounding.AwayFromZero` เหมือน `QuotationCalculator` และคำนวณตามลำดับตายตัวนี้

```
บรรทัด:  gross     = UnitPrice × Quantity
         discount  = gross × DiscountPercent / 100
         promo     = Percent → (gross − discount) × Value / 100, cap ที่ MaxAmount
                     Amount  → Value
         net       = max(0, gross − discount − promo)

บิล:     subtotal  = Σ line.net
         billDisc  = Percent → subtotal × Value / 100   ·   Amount → Value
         billPromo = คิดจาก (subtotal − billDisc) แบบเดียวกับโปรบรรทัด · ต้องถึง MinSubtotal
         net       = max(0, subtotal − billDisc − billPromo)
         vat       = VatIncluded ? net × VatRate : 0
         total     = net + vat
```

- บรรทัดหนึ่งใช้โปรได้ไม่เกิน 1 ตัว บิลหนึ่งใช้โปรท้ายบิลได้ไม่เกิน 1 ตัว
- ส่วนลดกับโปรใช้ซ้อนกันได้ แต่ยอดต้องไม่ติดลบ
- **ยอดเงินคิดฝั่ง server เสมอ** client แสดงผลตามที่ server ส่งมาเท่านั้น (pattern เดียวกับใบเสนอราคา)

## สิทธิ์และกฎ (บังคับที่ service ไม่ใช่แค่ `[RequireShiftSession]`)

| การกระทำ | ใครทำได้ | เงื่อนไข / error |
|---|---|---|
| สร้างร่าง · แก้ header · เพิ่ม/แก้/ลบบรรทัด · ยกเลิกร่าง · ชำระเงิน | Cashier/Office/Manager | อื่นๆ → `SALE_FORBIDDEN` · บิลไม่ใช่ Draft → `SALE_LOCKED` |
| ส่วนลด % ต่อบรรทัด ≥ `QuotationCalculator.DiscountApprovalThresholdPercent` (10%) | Manager | `SALE_DISCOUNT_NEEDS_MANAGER` — เป็น `[ASSUME]` ค่าเดียวกับใบเสนอราคา |
| ส่วนลดท้ายบิลเกิน 10% ของ subtotal (ถ้าใส่เป็นบาท ให้เทียบเป็น %) | Manager | `SALE_DISCOUNT_NEEDS_MANAGER` |
| เลือกโปรโมชัน | ผู้ขายทุกคน | โปรผ่านการอนุมัติตั้งแต่ตอนผู้จัดการสร้างแล้ว จึงไม่ติดเกณฑ์ 10% · ต้อง active + อยู่ในช่วงวัน + `Scope` ตรง + ถึง `MinSubtotal` ไม่งั้น `SALE_PROMOTION_INVALID` |
| ยกเลิกบิลที่ปิดแล้ว (void) | **Manager** | ต้องมีเหตุผล (`SALE_VOID_REASON_REQUIRED`) |
| สร้าง/แก้/เปิดปิดโปรโมชัน | **Manager** | `MASTER_DATA_MANAGE_FORBIDDEN` (pattern เดียวกับเทมเพลตใบเสนอราคา) |
| ดูต้นทุน/กำไรของบิล | Manager (`CanSeeCost`) | strip เป็น `null` ที่ mapper (กฎข้อ 7) · **ใบเสร็จห้ามแสดงต้นทุนเลย** (กฎข้อ 10) |

อื่นๆ
- เพิ่มสินค้าที่มีในบิลแล้ว → **บวกจำนวนเข้าบรรทัดเดิม** ไม่สร้างบรรทัดซ้ำ
- สินค้าที่ไม่ใช่ Part / ไม่ StockManaged / ถูกปิดใช้ → `SALE_ITEM_NOT_STOCKED`
- ราคาเริ่มต้นดึงจาก `CatalogItem.Price` และแก้ได้ในบรรทัด
- ลูกค้าในระบบตรวจด้วย `ICustomerVehicleService.GetCustomerAsync` (ใช้ scope สาขาเดิม) แล้ว snapshot ชื่อ/เบอร์ลงบิล
  **ไม่มีการเขียน legacy `Customer`** · ค้นหาด้วย `getCustomers({ keyword })` ได้เลย เพราะ keyword ค้นใน
  `PhoneNumber1/2` อยู่แล้ว

## Flow ปิดการขาย (`SaleService.CheckoutAsync`)

ทำทั้งหมดใน `AtomicAsync` ครั้งเดียว ใช้ **lock เดียวกับโมดูลจัดซื้อ** (`sp_getapplock garagepro:purchasing:{shard}:{branch}`)
เพื่อไม่ให้การขายกับการเบิกชิ้นสุดท้ายชนกัน

1. **กันส่งซ้ำ:** `RequestId` ที่เคยใช้แล้วและ hash ตรงกัน → คืนผลเดิม · hash ต่างกัน → `SALE_CONFLICT`
2. บิลต้องเป็น Draft และมีอย่างน้อย 1 บรรทัด (`SALE_EMPTY`)
3. **ตรวจโปรทุกตัวซ้ำอีกรอบ:** ร่างที่ค้างข้ามวันอาจมีโปรหมดอายุ ถูกปิด หรือยอดไม่ถึงขั้นต่ำแล้ว → `SALE_PROMOTION_INVALID`
   พร้อมชื่อโปร ให้ผู้ขายเอาออกเอง **ไม่ตัดโปรออกเงียบๆ**
4. คำนวณยอดใหม่ฝั่ง server
5. **ยอดรวมทุกช่องทางชำระต้องเท่ากับ `TotalAmount` พอดี** (`SALE_PAYMENT_MISMATCH`) เงินทอนของเงินสดคำนวณบนหน้าจอเท่านั้น
   ไม่บันทึกยอดที่เกินมา
6. ทำทีละบรรทัด:
   - ตรวจ `qty <= item.Available` และ Σ `lot.RemainingQuantity` ในคลังของบิลต้อง ≥ qty ไม่งั้นได้ `STOCK_INSUFFICIENT`
     **ต้องตรวจก่อนเรียก `PurchasingRules.Allocate`** เพราะถ้าของไม่พอมันจะโยน `InvalidOperationException` ซึ่งกลายเป็น 500
   - allocate ตามลำดับ FIFO แล้วเขียน movement `"sale"` ล็อตละ 1 แถว (`OperationId` = RequestId, `SaleId`, `UnitCost` ของล็อต)
   - `lot.RemainingQuantity -= q`, `item.OnHand -= q` แล้วเติม `SaleLine.CostAmount`
7. เพิ่ม `SalePayment` · ออกเลข `SL-` (`MERGE ... WITH (HOLDLOCK)` + `.ToListAsync()` ห้ามใช้ `.SingleAsync()` ตามบทเรียนของ
   `ReceiptNumberGenerator`) · ตั้ง `Status=Completed` · เขียน ActivityEvent `sale.completed` (`JobId=null`, `EntityType="Sale"`, `Source`)

## Flow ยกเลิกบิล (`SaleService.VoidAsync`)

- บิลต้องเป็น Completed · ใช้ `RequestId` เดิมซ้ำ → คืนผลเดิม · บิล Voided อยู่แล้ว → `SALE_LOCKED`
- ทำกับทุก movement `"sale"` ของบิล:
  - `lot.RemainingQuantity += q` ในล็อตเดิมด้วยต้นทุนเดิม (check constraint `Remaining <= Received` กันไว้อยู่แล้ว)
  - `item.OnHand += q`
  - เขียน movement `"sale-void"` qty บวก ด้วย **`OperationId` ใหม่** เพราะ unique index `(OperationId, StockLotId)`
    ไม่ยอมให้ใช้ id เดิม
- **คืนล็อตเดิม ไม่สร้างล็อตใหม่** ถ้าสร้างใหม่ ของที่คืนมาจะไปต่อท้ายคิว FIFO และได้ต้นทุนผิด
- ActivityEvent `sale.voided`
- **คืนเงินลูกค้าทำนอกระบบ** `SalePayment` ยังเก็บไว้เป็นประวัติ ส่วนรายงานไม่นับบิลที่ Voided

## ส่วนที่แตะโค้ดเดิม

- **แยก body ของ `PurchasingRepository.AtomicAsync`** (`Infrastructure/Persistence/PurchasingRepository.cs:17-43` ซึ่งทำงานตามลำดับ:
  execution strategy → `ChangeTracker.Clear()` → transaction → applock → save/commit → แปลง error 2601/2627/51001/concurrency)
  ออกมาเป็น helper `BranchStockTransaction` ให้ `PurchasingRepository` เรียกต่อ พฤติกรรมเดิมไม่เปลี่ยน และ `SaleRepository`
  ใช้ตัวเดียวกัน · **ห้ามเรียก `BeginTransactionAsync` นอก execution strategy** เพราะเปิด `EnableRetryOnFailure(3)` ไว้
- `ReportsRepository`: ยอด "เก็บเงินได้" บนแดชบอร์ดบวก Σ `SalePayment` ของบิล Completed ในช่วงวันเข้าไปด้วย
  (รายงาน sales-margin ไม่แตะในรอบนี้)
- `InventoryPage.tsx`: เพิ่มชื่อประเภท movement `sale` = "ขายหน้าร้าน" และ `sale-void` = "คืนสต็อก (ยกเลิกขาย)"

## API

`api/v1/sales` — `[Authorize, RequireShiftSession]` ส่วนสิทธิ์ตาม role ตรวจใน service

| Method | Path | ทำอะไร |
|---|---|---|
| GET | `/sales?status=&from=&to=&q=` | รายการบิล |
| GET | `/sales/count-drafts` | จำนวนบิลร่างที่ค้าง (ใช้เป็น badge บนเมนู) |
| POST | `/sales` | สร้างบิลร่าง |
| GET / PUT | `/sales/{id}` | อ่านบิล / แก้ header |
| POST / PUT / DELETE | `/sales/{id}/lines[/{lineId}]` | จัดการบรรทัด |
| POST | `/sales/{id}/checkout` | ชำระเงิน ตัดสต็อก และออกใบเสร็จ |
| POST | `/sales/{id}/void` | ยกเลิกบิลที่ปิดแล้ว |
| POST | `/sales/{id}/cancel` | ยกเลิกบิลร่าง |
| GET | `/reports/retail-sales?fromDate=&toDate=` | รายงานขายหน้าร้าน (Manager/Office · ต้นทุน strip ตาม role) — เพิ่ม 2026-09-29 |

บรรทัดใน `SaleDto` มี `availableInWarehouse` (คำนวณจากล็อตของคลังในบิล) ไว้ให้หน้าจอเตือนก่อนกดชำระเงิน

`api/v1/promotions` — CRUD + เปิด/ปิด (Manager) · `GET /promotions/applicable?scope=line|bill` (ผู้ขายทุกคน
คืนเฉพาะโปรที่ active และอยู่ในช่วงวัน)

HTTP status ใช้ pattern `Render` เดิม: `*_FORBIDDEN` = 403 · `*_NOT_FOUND` = 404 · `SALE_CONFLICT`/`SALE_LOCKED`/`STOCK_INSUFFICIENT` = 409 · อื่นๆ = 422

## Web

- **เมนู:** กลุ่ม Workplace เพิ่ม "ขายสินค้า (POS)" → `/sales` พร้อม badge จำนวนบิลร่างค้าง และกลุ่ม Master Data เพิ่ม "โปรโมชัน"
- **`/sales`:** ใช้ `ManagementTable` คอลัมน์ เลขที่ · วันเวลา · ลูกค้า · จำนวนรายการ · ยอดรวม · สถานะ (สี+ไอคอน+ข้อความ)
  ตัวกรองสถานะ/วันที่ เริ่มต้นที่วันนี้ และปุ่ม "ขายใหม่"
- **`/sales/:id`:** เต็มหน้า 3 พาเนลแบบ `editor-grid` ของ `QuotationEditorModal`
  - **01 สินค้า:** ค้นด้วย `searchCatalog` การ์ดแสดงราคา + คงเหลือ **พิมพ์รหัสตรงเป๊ะแล้วกด Enter = เพิ่มทันที**
    (รองรับเครื่องสแกนบาร์โค้ดที่พิมพ์ตัวอักษรเหมือนคีย์บอร์ด) · ไม่ reuse `CatalogPanel` ตรงๆ เพราะผูกกับ `quotationId`/เทมเพลต
  - **02 รายการขาย:** จำนวน / ราคา / ส่วนลด % / โปร / ลบ · แก้แล้ว debounce 400ms และใช้ยอดที่ server คืนมา ·
    เตือนเมื่อของในคลังไม่พอ
  - **03 สรุป:** ลูกค้า (ค้นด้วยเบอร์หรือพิมพ์ walk-in) · คลัง · ส่วนลดท้ายบิล (สลับ %/บาท) · โปรท้ายบิล · VAT ·
    สรุปยอด: ยอดก่อนลด → ส่วนลดรายการ → โปรรายการ → รวม → ส่วนลดท้ายบิล → โปรท้ายบิล → ก่อน VAT → VAT → สุทธิ ·
    ปุ่ม "ชำระเงิน" ที่กดไม่ได้ต้องบอกเหตุผลเสมอ
- **Modal ชำระเงิน:** แบ่งจ่ายได้หลายช่องทาง ช่องเงินสดมี "รับเงินมา" และ "เงินทอน" · ถ้าคำขอค้างอยู่ (5xx หรือเน็ตหลุด)
  ให้ส่งซ้ำด้วย `requestId` เดิม ใช้ `pendingPaymentCommand`/`paymentCommand` จาก `api/pos.ts`
- **ใบเสร็จ `SL-`:** มิเรอร์ `PaymentReceiptDocument/Modal` แสดงตารางสินค้า ชื่อโปร ส่วนลดท้ายบิล ช่องทางชำระ และ net/VAT/total ·
  บิลที่ถูกยกเลิกมีลายน้ำ "ยกเลิกแล้ว" · ใช้ body class `printing-sale-receipt` และเก็บ CSS ไว้ใน `features/sales/sales.css`
- **หน้าโปรโมชัน:** `ManagementTable` + ฟอร์ม ป้ายสถานะมี "ใช้งานอยู่ / ยังไม่เริ่ม / หมดอายุ / ปิดใช้งาน" ·
  role อื่นเห็นรายการได้ แต่ปุ่มแก้ไขกดไม่ได้และต้องบอกเหตุผล

## นอกขอบเขตรอบนี้

- คืนสินค้าบางรายการ / ใบลดหนี้ · ใบกำกับภาษีเต็มรูป
- โปรอัตโนมัติตามเงื่อนไข (ซื้อ X แถม Y / ซื้อครบแล้วระบบลดให้เอง)
- ใช้โปรชุดใหม่นี้กับใบเสนอราคา: ใบเสนอราคายังใช้ enum 3 แบบเดิม ถ้าจะรวมเป็นระบบเดียวต้องยืนยันแยก
- ขายสินค้านอกแคตตาล็อก หรือสินค้าที่ไม่ StockManaged · จองสต็อกตั้งแต่ตอนร่างบิล
- POS บนมือถือ (รายงานขายหน้าร้านแยกหน้า **ย้ายเข้าขอบเขตแล้ว 2026-09-29** — ดูหัวข้อ "รายงานขายหน้าร้าน + widget แดชบอร์ด")

## `[RISK]` ที่พบระหว่างวางแผน (ไม่แก้ในงานนี้)

- `PurchasingService.BuildWithdrawalDto` (`Application/Purchasing/PurchasingService.cs:384-390`) ใช้ `group.First().UnitCost`
  เป็นต้นทุนของทั้งบรรทัด เมื่อบรรทัดเดียวตัดจากหลายล็อตที่ต้นทุนต่างกัน **ใบเบิกจะแสดงต้นทุนต่อหน่วยและ `TotalCost` ผิด**
  (ยอดในฐานข้อมูลถูกต้อง เพราะ movement เก็บต้นทุนล็อตละแถว ที่ผิดคือ DTO ที่ใช้แสดงผลเท่านั้น)
- `PurchasingRules.Allocate` โยน `InvalidOperationException` เมื่อของไม่พอ ผู้เรียกทุกคนจึงต้องตรวจยอดรวมก่อนเรียกเอง

## แผนทดสอบ

1. `dotnet build` ต้องได้ 0 error
2. `SaleServiceTests` (fake repo แบบ `PurchasingServiceTests` ที่ snapshot/rollback ใน `AtomicAsync`):
   - ตัด FIFO ข้าม 2 ล็อตที่ต้นทุนต่างกันแล้ว `CostAmount` ถูก
   - บรรทัดหลังของไม่พอ → ไม่มีอะไรถูกบันทึกเลย
   - replay `RequestId` ได้ผลเดิม · hash ต่าง → `SALE_CONFLICT`
   - ยอดชำระไม่เท่ายอดบิล → ปฏิเสธ
   - role gate: Technician ถูกปฏิเสธ · void ต้องเป็น Manager
   - ต้นทุนถูก strip เมื่อผู้เรียกไม่ใช่ Manager
   - void แล้ว `RemainingQuantity`/`OnHand` กลับมาเท่าก่อนขายเป๊ะ · void ซ้ำไม่คืนสต็อกสองรอบ
   - แก้บิลที่ไม่ใช่ Draft → `SALE_LOCKED`
   - ส่วนลดเกินเกณฑ์ · เพิ่มสินค้าซ้ำแล้วบวกจำนวน · ปิด VAT
   - โปรหมดอายุ / ถูกปิด / scope ผิด / ไม่ถึงขั้นต่ำ → ปฏิเสธทั้งตอนเลือกและตอน checkout
   - แก้โปรหลังปิดบิลแล้ว ยอดของบิลเดิมไม่เปลี่ยน
3. `SaleCalculatorTests`: ล็อกตัวเลขทุกลำดับ (ส่วนลด % + โปร % ที่มีเพดาน + โปรบาท + ส่วนลดท้ายบิล %/บาท + โปรท้ายบิล +
   VAT เปิด/ปิด) และยอดต้องไม่ติดลบ
4. `PromotionServiceTests`: สร้าง/แก้ได้เฉพาะ Manager · รหัสซ้ำในสาขาถูกปฏิเสธ · `applicable` คืนเฉพาะโปรที่ใช้ได้วันนี้
5. รัน `PurchasingServiceTests` เดิมซ้ำ เพื่อยืนยันว่าการแยก lock ออกเป็น helper ไม่ทำอะไรเสีย
6. `SaleSqlTests` (ใช้ env var เหมือน `PurchasingSqlFact`): checkout พร้อมกับ `IssueAsync` บนชิ้นสุดท้าย → สำเร็จ 1 ครั้ง ได้ `STOCK_INSUFFICIENT` 1 ครั้ง
7. รัน `dotnet test` ทั้ง suite · `dotnet ef database update` แล้วดู `migrations list` ว่าไม่มี `(Pending)` เหลือ (ต้องต่อ VPN)
8. Web: `pnpm exec tsc -b` และ `pnpm build`
9. E2E ในเบราว์เซอร์ด้วยบัญชี Cashier และ Manager:
   - Manager สร้างโปรบรรทัดและโปรท้ายบิล
   - สร้างบิล ค้นลูกค้าด้วยเบอร์ เพิ่มสินค้า (รวมการกด Enter ด้วยรหัส)
   - ใส่ส่วนลดและโปรครบทุกแบบ แล้วเทียบยอดกับที่คำนวณมือ
   - แบ่งจ่ายเงินสด + โอน แล้วพิมพ์ใบเสร็จ `SL-`
   - ที่ `/inventory` ตรวจว่าล็อตถูกตัดตาม FIFO และประวัติขึ้น "ขายหน้าร้าน"
   - Manager ยกเลิกบิล แล้วตรวจว่าสต็อกกลับเข้าล็อตเดิม
   - Cashier ไม่เห็นต้นทุน และใบเสร็จไม่มีต้นทุน

## ผลการลงมือรอบ 2026-09-29

- เพิ่ม Domain aggregate `Sale`/`SaleLine`/`SalePayment`/`Promotion` และ `SaleCalculator`
- เพิ่ม Application service/repository contracts, FIFO checkout, payment exact-match, void คืนล็อตเดิม และ promotion CRUD
- เพิ่ม API `/api/v1/sales` และ `/api/v1/promotions` พร้อม role/error mapping
- เพิ่ม migration `AddRetailSaleAndPromotion` แต่ **ยังไม่ได้ apply กับ ServiceDb จริง** เพราะยังไม่ได้ยืนยัน VPN/ฐานข้อมูลในรอบนี้
- เพิ่ม Web routes `/sales` และ `/promotions`, draft editor, catalog search, checkout เงินสดแบบ MVP และ movement labels
- ตรวจแล้ว: backend build ผ่าน, backend tests `276 passed / 5 skipped`, `SaleCalculatorTests` 4 ผ่าน, web `tsc -b` และ Vite build ผ่าน
- ยังเหลือก่อน production: split-payment modal, customer/warehouse editor ครบ, `SaleServiceTests`/`PromotionServiceTests`/`SaleSqlTests`, apply migration, และ E2E จริง
- ความต่างจากแผนรอบนี้: UI checkout รองรับเงินสดช่องทางเดียวใน MVP ชั่วคราว; ใบเสร็จ retail มีเอกสารพิมพ์พื้นฐานแล้ว แต่ยังต้องเก็บรายละเอียด CSS/สถานะ void ให้ครบตามต้นแบบ

## ผลการลงมือรอบ 2026-09-29 (รอบสอง — ทำหน้าเว็บใหม่ตามแผน)

หน้าเว็บ MVP รอบแรกใช้ class ที่ไม่มี CSS อยู่จริง (`management-heading`, `catalog-picker`, `sale-line`) และมีบั๊กใช้งาน
จึงเขียน `web/src/features/sales/` ใหม่ทั้งชุดตามหัวข้อ "Web" ด้านบน

**บั๊กที่แก้ (รอบแรกมีจริง):**
- ปุ่มชำระเงินกดได้เฉพาะตอน "รับเงินสด" เท่ายอดพอดี → **ทอนเงินไม่ได้เลย** ทั้งที่มีบรรทัด "เงินทอน" แสดงอยู่
- `requestId` สร้างใหม่ทุกครั้งที่กด (`crypto.randomUUID()` ใน `mutationFn`) ทั้งที่หน้าเขียนว่า "ใช้ requestId เดิมเมื่อ retry"
  — ตอนนี้ใช้ `saleCheckoutCommand` ที่มีอยู่แล้ว + ถือคำขอเดิมไว้ใน state เมื่อผลไม่ทราบ (เน็ตหลุด/5xx)
- dropdown คลังเป็น `onChange={() => undefined}` เปลี่ยนไม่ได้ · ไม่มีช่องลูกค้า/ส่วนลดท้ายบิล/โปร/VAT เลย
- จำนวน/ส่วนลดยิง API ทุกตัวอักษรที่พิมพ์ (ไม่มี debounce) · ไม่มี error state/traceId ทุกจุด
- `saleCheckoutCommand` เรียก `sessionStorage.setItem` ตรงๆ → private window ชำระเงินไม่ได้ (ครอบ try แล้ว)

**Backend (เล็ก เฉพาะที่หน้าจอต้องใช้):**
- `availableInWarehouse` เป็น 0 เสมอ เพราะ `SaleMapper.ToDto` ไม่เคยได้รับ dictionary — เพิ่ม
  `ISaleRepository.AvailabilityAsync` = min(`OnHand − Reserved`, Σ ล็อตคงเหลือในคลังของบิล) ใช้กับบิลร่างเท่านั้น
- `BillPromotionCode`/`BillPromotionName` ไม่เคยถูก snapshot (มีคอลัมน์แต่ไม่มีใครเซ็ต) — เซ็ตใน `RecalculateAsync` แล้ว
- `SaleDto` เพิ่ม `BillPromotionId`/`BillPromotionName` (additive) ให้หน้าจอแสดงโปรที่เลือกได้
- ตัวกรองสถานะใน `SaleRepository.SearchAsync` ใช้ `Status.ToString().ToLower()` ใน LINQ → เปลี่ยนเป็น parse enum ก่อน

**หน้าจอ:** `/sales` ตัวกรองสถานะ/ช่วงวัน (เริ่มที่วันนี้) + แถบเตือนบิลร่างค้างนอกช่วง · `/sales/:id` บิลร่าง = 3 พาเนล
(สแกน/Enter รหัสตรงตัว · stepper จำนวน · ส่วนลด % · โปรรายบรรทัด · เตือนของในคลังไม่พอ · ลูกค้าทั่วไป/ในระบบ · คลัง ·
ส่วนลดท้ายบิล %/บาท · โปรท้ายบิล + เตือนยอดไม่ถึงขั้นต่ำ · VAT · สรุปยอดครบลำดับ · ปุ่มชำระเงินบอกเหตุผลเสมอ) ·
modal ชำระเงินแบ่งจ่ายหลายช่องทาง + รับเงินมา/เงินทอน + ปุ่มลัดธนบัตร · บิลที่ปิดแล้ว = หน้าใบเสร็จ (พิมพ์ · เงินทอน ·
ต้นทุน/กำไรเฉพาะผู้จัดการ ไม่พิมพ์ลงใบเสร็จ · ยกเลิกบิลเฉพาะผู้จัดการพร้อมเหตุผล · ลายน้ำ "ยกเลิกแล้ว") · badge บิลร่างบนเมนู

**ตรวจแล้ว:** `dotnet build` 0 error · `dotnet test` 276 ผ่าน / 5 skipped · web `tsc -b` + `vite build` ผ่าน ·
เปิดในเบราว์เซอร์จริง (Chromium headless) **โดยใช้ข้อมูลจำลองแทน API ทั้งหมด** — ไม่ได้ต่อ backend จริงเพราะ ServiceDb
ของ dev คือฐาน production (docs/10) ยืนยัน layout 1440px, debounce (กด + สองครั้ง = PUT ครั้งเดียว), เหตุผลปุ่มปิด,
modal ชำระเงิน/เงินทอน, หน้าใบเสร็จ และ print preview · ไม่มี console error

**ยังไม่ได้ทำ / ข้อจำกัดที่พบ:**
- ยังไม่ได้ทดสอบกับ API จริง (ต้อง restart API ให้ได้โค้ด backend ใหม่ก่อน) และยังไม่ได้ชำระเงินจริงสักบิล
- `[RISK]` เกณฑ์ "ส่วนลด ≥10% ต้องผู้จัดการ" (`SALE_DISCOUNT_NEEDS_MANAGER`) **ยังไม่ได้บังคับที่ service** — `CheckDiscount`
  ตรวจแค่ 0–100 ต้องตัดสินใจก่อนว่าผู้จัดการ "อนุมัติ" ส่วนลดอย่างไร (ถ้าเช็คตาม role ตอน checkout แคชเชียร์จะปิดบิลที่ผู้จัดการใส่ส่วนลดไว้ไม่ได้)
- แก้ราคาต่อหน่วยในบรรทัดยังไม่ได้ (`UpdateSaleLineRequest` ไม่มี `UnitPrice`) ทั้งที่แผนเขียนว่า "แก้ได้ในบรรทัด"
- ลูกค้าในระบบยังไม่ถูกตรวจด้วย `ICustomerVehicleService.GetCustomerAsync` ที่ `UpdateAsync` (เก็บ id ตามที่ client ส่ง)
- `SaleServiceTests`/`PromotionServiceTests`/`SaleSqlTests` ตามแผนทดสอบยังไม่มี

## รายงานขายหน้าร้าน + widget แดชบอร์ด (เพิ่ม 2026-09-29 ตามคำขอผู้ใช้)

เดิมอยู่ใน "นอกขอบเขตรอบนี้" — ผู้ใช้ขอให้ทำ จึงย้ายเข้าขอบเขต · อ่านอย่างเดียว ไม่มีตาราง/migration ใหม่

**กฎการนับ (ตัดสินใจเอง ยังไม่ได้ถามผู้ใช้ แก้ได้):**
- นับเฉพาะบิล `Completed` ตาม **วันที่ชำระเงิน** (`CompletedAt`) ไม่ใช่วันเปิดบิล
- บิลที่ชำระในช่วงแล้วถูก `Voided` ภายหลัง **ไม่นับในยอด** แต่แสดงแยก (จำนวน/มูลค่า/เหตุผล/ผู้ยกเลิก) — ผูกกับช่วงของ
  วันที่ชำระเดิม ไม่ใช่วันที่ยกเลิก เพื่อให้ยอดของช่วงเดียวกันไม่ขยับไปมาตามวันที่เปิดรายงาน
- `fromDate`/`toDate` เป็น **วันตามปฏิทินไทย** (`DateOnly` yyyy-MM-dd รวมทั้งสองวัน) server แปลงเป็นขอบ UTC เอง
  (`ReportsService.ThaiDateStartUtc`) — ไม่ทำแบบ `sales-margin` ที่ client ส่ง `T23:59:59` ไม่มีโซนเวลา ซึ่งทำให้บิล
  00:00–07:00 ไทยตกวันผิด · ช่วงยาวสุด 366 วัน (`REPORTS_VALIDATION`) · ค่าเริ่มต้น = ต้นเดือนถึงวันนี้
- ยอดต่อสินค้า = **ยอดตามบรรทัด** (หลังส่วนลด/โปรรายบรรทัด ก่อนส่วนลดท้ายบิลและ VAT) — ส่วนลดท้ายบิลไม่ได้เฉลี่ยลงบรรทัด
  จึงบอกบนหน้าจอชัดๆ · กำไรขั้นต้นของบิล = `NetAmount` (ก่อน VAT หลังส่วนลดทั้งหมด) − `CostTotal` (ล็อต FIFO ที่ถูกตัดจริง)
- สิทธิ์ = Manager/Office (เหมือนรายงานอื่น) · ต้นทุน/กำไร = `CanSeeCost` (ผู้จัดการ) strip เป็น null ที่ server
- "ผู้รับเงิน" = `Sale.CompletedByName` (ผู้กดปิดการขาย)

**Backend:** `ReportsService.GetRetailSalesAsync` · `GET /api/v1/reports/retail-sales` · `IReportsRepository`
เพิ่ม `GetRetailSalesCompletedInRangeAsync`/`CountRetailDraftsAsync` · `DashboardReportDto` เพิ่ม `RetailToday`
(`BillCount`/`TotalAmount`/`ItemQuantity`/`DraftCount` — additive, optional) · ยอด "รับชำระวันนี้" เดิมรวมขายหน้าร้าน
อยู่แล้ว (หัวข้อ "ส่วนที่แตะโค้ดเดิม") แค่แก้คำอธิบายบนการ์ดให้บอกว่ารวม

**Web:** `/reports/retail-sales` (เมนู Reports → "ขายหน้าร้าน") — ตัวเลือกช่วง วันนี้/7 วัน/เดือนนี้/เดือนก่อน + เลือกวันเอง ·
การ์ดสรุป 5 ใบ (ยอดขาย · บิล/เฉลี่ยต่อบิล · ส่วนลด+โปร · กำไรขั้นต้น · บิลยกเลิก) · กราฟยอดรายวัน (`RetailDailyChart.tsx`
คอลัมน์ซีรีส์เดียวสี `--blue-600` · tooltip ตอน hover/focus · ตัวเลขเฉพาะวันสูงสุด · ทุกวันในช่วงรวมวันที่ไม่มีบิล ·
ปุ่ม "ดูเป็นตาราง") · ช่องทางชำระเงิน/ผู้รับเงินเป็นแถบสัดส่วน · สินค้าขายดี 20 อันดับ · โปรโมชันที่ใช้ · บิลที่ยกเลิก (ลิงก์ไปใบเสร็จ)
· แดชบอร์ด `/reports/dashboard` มีการ์ด "ขายหน้าร้านวันนี้" (ยอด · บิล · ชิ้น · บิลร่างค้าง) + ลิงก์ไปหน้าขาย/รายงาน

**ตรวจแล้ว:** `dotnet test` 283 ผ่าน / 5 skipped (เพิ่ม 7 ใน `ReportsServiceTests`: role gate · ไม่นับบิลยกเลิกแต่แสดงแยก ·
ขอบวันตามเวลาไทย (01:00 ไทย = 18:00 UTC วันก่อน) + เติมวันที่ไม่มีบิล · strip ต้นทุนสำหรับ Office · ช่วงวันกลับหัว/ยาวเกิน ·
widget วันนี้ไม่นับบิลยกเลิก) · web `tsc -b` + `vite build` ผ่าน · render ในเบราว์เซอร์ด้วยข้อมูลจำลองที่ 1440px แล้ว ไม่มี console error
· **ยังไม่ได้เปิดกับ API จริง** (ต้อง restart API อีกครั้งให้ได้ endpoint ใหม่)

**ยังไม่ได้ทำ:** ส่งออก Excel · เทียบกับช่วงก่อนหน้า (เช่น เดือนนี้ vs เดือนก่อน) · รายงานนี้บนมือถือ ·
เฉลี่ยส่วนลดท้ายบิลลงสินค้าแต่ละตัว (ถ้าต้องการกำไรต่อสินค้าที่แม่นขึ้น)

## ใบเสร็จกระดาษม้วน (เครื่องพิมพ์ความร้อน) — เพิ่ม 2026-09-29 ตามคำขอผู้ใช้

หน้าใบเสร็จ `/sales/:id` มีตัวเลือก **รูปแบบใบเสร็จ: A4 · ม้วน 80 มม. · ม้วน 58 มม.** เหนือปุ่มพิมพ์ · ฝั่งเว็บล้วน ไม่แตะ backend/ไม่มี migration

- `web/src/features/sales/SaleThermalReceipt.tsx` — ข้อมูลชุดเดียวกับ `SaleReceiptDocument` (A4) จัดเป็นคอลัมน์เดียว ขาวดำล้วน
  (หัวพิมพ์ความร้อนไม่มีเทา) ตัวเลขเงิน IBM Plex Mono + tabular-nums · บิลยกเลิกแสดงกรอบ "*** ยกเลิกแล้ว ***" + เวลา/เหตุผลแทนลายน้ำ
  · **ไม่มีต้นทุน/กำไร** (กฎข้อ 10) แม้ผู้พิมพ์เป็นผู้จัดการ
- **ความยาวกระดาษ = ความสูงใบเสร็จจริง** — วัดจากตัวอย่างบนจอ (render กว้างเท่ากระดาษเป็น mm) ด้วย `ResizeObserver`
  แล้วใส่ `<style>@page { size: 80mm <สูง>mm; margin: 0 }</style>` ซึ่งประกาศทีหลัง `@page A4` ของ `index.css` จึงชนะเฉพาะตอนเลือกม้วน
  (กด Ctrl+P เองก็ได้ขนาดถูก) · ไม่ใช้ 297 มม. ตายตัว ไม่งั้นบิลสั้นป้อนกระดาษเปล่ายาว บิลยาวถูกตัดขึ้นหน้าใหม่กลางรายการ
- เนื้อหาเว้นขอบในตัว (80 มม. → 4 มม./ข้าง ≈ พื้นที่พิมพ์ 72 มม. · 58 มม. → 5 มม./ข้าง ≈ 48 มม.) · ตอนพิมพ์ล้าง padding ของ layout ด้วย
  `body:has(.sale-thermal)` ไม่งั้นใบเสร็จเยื้องหลุดขอบ
- รูปแบบที่เลือกจำไว้ต่อเครื่องใน `localStorage` (`garagepro.sales.receipt-format`, ครอบ try — ใช้ไม่ได้ก็กลับเป็น A4)
  เพราะเครื่องพิมพ์ผูกกับเคาน์เตอร์ ไม่ใช่กับผู้ใช้
- เงินทอนพิมพ์ได้เฉพาะตอนพิมพ์ทันทีหลังชำระเงิน (มาจาก navigation state — ไม่ได้เก็บในฐานข้อมูล) · พิมพ์ซ้ำภายหลังจะไม่มีบรรทัดเงินทอน

**ตรวจแล้ว:** `tsc -b` + `vite build` ผ่าน · Chromium headless + ข้อมูลจำลอง (ไม่ต่อ API): ตัวอย่างบนจอ 80/58 มม., PDF ที่ได้ขนาด
80×145 มม. และ 58×135 มม. หน้าเดียว, บิลยกเลิก 58 มม. ยาวขึ้นตามเนื้อหา, สลับกลับ A4 แล้ว `@page` ของม้วนหายและ PDF เป็น A4 ตามเดิม,
รีโหลดแล้วจำรูปแบบไว้ · **ยังไม่ได้ทดสอบกับเครื่องพิมพ์ความร้อนจริง** — ไดรเวอร์บางรุ่นต้องตั้งขนาดกระดาษ/ขอบ "ไม่มี"/สเกล 100% เองในหน้าต่างพิมพ์
และบางรุ่นตัดกระดาษเองตามความยาวหน้า บางรุ่นไม่ตัด · **ยังไม่รองรับ** ส่งคำสั่ง ESC/POS ตรง (ไม่ผ่านหน้าต่างพิมพ์ของเบราว์เซอร์), เปิดลิ้นชักเก็บเงิน,
โลโก้/ที่อยู่/เลขผู้เสียภาษีของร้าน (ยังไม่มีข้อมูลในระบบ)
