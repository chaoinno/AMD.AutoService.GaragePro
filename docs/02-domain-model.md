# 02 · Domain Model — สกัดจาก Design

Entity + field ที่ prototype ใช้จริง (ไม่ใช่การเดา) พร้อมชี้จุดที่ต้องตัดสินใจ

---

## Aggregate Map

```
Branch ──┬── Shift
         ├── Employee (role)
         └── StockItem ── StockLedgerEntry

Customer ── Vehicle ──┐
                      │
                      ▼
                  Job (JB-xxxx)  ← aggregate root
                      ├── IntakeRecord (สภาพรถ + 5 รูป + ลายเซ็นรับรถ)
                      ├── Inspection (8 หมวด 31 รายการ)
                      │     └── InspectionItem ── Photo[]
                      ├── Quotation (QT-xxxx-NN)  ← versioned
                      │     ├── QuotationLine
                      │     └── ApprovalRecord (ลายเซ็น + version binding)
                      ├── RepairTask
                      │     ├── Photo[] (before/after)
                      │     ├── TimeLog[] (start/pause/resume)
                      │     └── PartsRequest → PurchaseOrder
                      ├── QcInspection (6 ข้อ + test drive)
                      │     └── QcFailItem (issue + priority)
                      ├── Reconciliation ── ReconLine
                      ├── Payment[] (split payment)
                      ├── FiscalDocument (RC / IV / credit note)
                      ├── HandoverRecord  ⚠️ ยังไม่ออกแบบ
                      └── ActivityEvent[] (audit — ทุก event)

Supplier ── PurchaseOrder (PO-xxxx)
                ├── PurchaseOrderLine
                └── GoodsReceipt (GRN-xxxx) ── GoodsReceiptLine
```

---

## Enums (ใช้ string token ตรงกันทั้ง API/Flutter/React)

```csharp
JobStatus       = waitinspect | waitquote | waitapprove | approved
                | inprogress | waitparts | qc | ready | completed | cancelled

QuotationStatus = draft | sent | partial | approved | rejected | superseded | expired

InspectionResult = ok | watch | fix | na
Severity         = minor | moderate | severe          // เล็กน้อย/ปานกลาง/รุนแรง
Urgency          = now | within1month | within6months  // ทำครั้งนี้/1 เดือน/6 เดือน
LineSource       = customer | technician               // ลูกค้าขอ / ช่างแนะนำ

RepairTaskStatus = todo | doing | done | blocked | pending | fix

QcPriority       = high | medium | low

PoStatus         = draft | pending | approved | sent | partial | complete | cancelled

ReconStatus      = match | over | notdone | removed | extra

PaymentMethod    = cash | card | transfer | qr
PaymentStatus    = ok | pending | checking                 // ⚠️ checking ≠ failed
DocumentType     = receipt | taxinvoice | creditnote

StockAdjustType  = damaged | loss | found | return
StockDocType     = issue | reserve | grn | adjust | count  // ISS/—/GRN/ADJ/CNT

SyncState        = local | syncing | synced | failed | conflict
EventSource      = mobile | web | system

Role             = frontdesk | technician | office | cashier | manager | lead
```

---

## Entities

### Customer / Vehicle (master data — ดู Open Question #1)
```
Customer:  id(CUS-xxxxxx), name, phone, customerSince, visitCount, lastVisitAt
           + isProvisional, linkRequestNo(REQ-xxxx)   ← เคสสร้างชั่วคราวจากหน้าร้าน
Vehicle:   id, customerId, registrationNo, model, year, color, vin,
           mileage, lastServiceAt, lastServiceNote, jobCount
```

### Job
```
Job: id(JB-xxxx), branchId, customerId, vehicleId,
     status(JobStatus), promiseAt, assignedTechnicianId,
     mileageAtIntake, createdBy, createdAt, source(EventSource),
     tempNo(TMP-xxxx, nullable)   ← ออฟไลน์
     isOverdue (derived: now > promiseAt && status ∉ {completed,cancelled})
     overdueReason (text — บังคับแสดงเป็นข้อความ ไม่ใช่แค่สีแดง)
```

### IntakeRecord
```
IntakeRecord: jobId, symptoms[](8 ตัวเลือก + free text), warningLights[](7),
              fuelLevel(E,1/8..F — 8 ระดับ), documentsInCar[](5), accessories[](5),
              keyCount, spareTireOk, valuablesNote, conditionNote,
              photos[5 มุมบังคับ: front|rear|left|right|dash],
              customerSignature, signedAt
```

### Inspection
```
Inspection:     id, jobId, technicianId, submittedAt, isLocked(true หลังส่ง)
InspectionItem: id, inspectionId, categoryKey(8), itemCode(c1i1..c8i4),
                result(InspectionResult),
                naReason        ← บังคับเมื่อ result=na
                severity, note, workSuggested, urgency,
                isCustomerConcern(bool), isOptional(bool),
                photos[], videos[]
```
หมวด: `engine(5) brake(5) susp(4) tyre(3) elec(4) ac(3) body(3) drive(4)` = **31 รายการ**

### Quotation (versioned — สำคัญที่สุด)
```
Quotation: id(QT-xxxx-NN), jobId, version, status(QuotationStatus),
           subtotal, discountAmount, promotionCode, vatAmount, netAmount,
           validUntil, createdBy, sentAt,
           supersededByQuotationId, supersedesQuotationId,
           lockedByUserId, lockedAt      ← concurrent edit
QuotationLine: id, quotationId, catalogCode, type(part|labor), name,
           source(LineSource), inspectionItemId(nullable),
           qty, unit, unitPrice, unitCost, discount, promotion,
           assignedTechnicianId, note, lineTotal,
           approvalStatus(pending|approved|rejected), rejectReason,
           grossMarginPct (derived — เตือนเมื่อ < 15% [ASSUME])
ApprovalRecord: id, quotationId, quotationVersion,   ← binding บังคับ
           signatureImage, signedAt, deviceInfo,
           witnessEmployeeId, consentText, ipAddress
```
> **[BIZ]** ออกเวอร์ชันใหม่ → เวอร์ชันเดิม `superseded`, ApprovalRecord เดิม **ใช้ไม่ได้**, ทุก line กลับเป็น `pending`

### RepairTask
```
RepairTask: id, jobId, quotationLineId, label, status(RepairTaskStatus),
            technicianId, partsNote,
            requiresBeforePhoto(true), requiresAfterPhoto(true),
            beforePhotoId, afterPhotoId,
            isLocked (true หลัง QC ผ่าน),
            blockedReason, blockedEtaOption, blockedPartCode
TimeLog:    id, repairTaskId, technicianId, startedAt, endedAt, kind(work|pause)
```

### QC
```
QcInspection: id, jobId, inspectorId(หัวหน้ากะ), performedAt,
              result(pass|fail), testDriveKm, note, photos[]
QcCheckItem:  id, qcInspectionId, itemCode(q1..q6), passed(bool), note
QcFailItem:   id, qcInspectionId, issue, priority(QcPriority),
              createdRepairTaskId   ← สร้าง task fix ให้ช่าง
```

### Inventory
```
StockItem: code(P-xxx-xxxx), barcode, name, brand, category, unit,
           onHand, reserved, onOrder, damaged,
           reorderPoint(rop), purchaseQty(pq), lastCost, sellPrice,
           monthlyUsage, compatibility[], branchId,
           available = onHand − reserved     ← [BIZ] computed, ไม่รวม onOrder
           isDuplicateBarcode, isNewCode     ← เคสพิเศษที่ design จับไว้
StockLedgerEntry: id, stockItemCode, branchId, occurredAt,
           docNo, docType(StockDocType), qty(+/-), balanceBefore, balanceAfter,
           reason, performedBy, relatedJobId, attachments[]
StockAdjustment: id, stockItemCode, type(StockAdjustType), qty, reason,
           evidencePhotos[], performedBy, approvedBy  ← [BIZ] บังคับ
StockReservation: id, stockItemCode, jobId, quotationLineId, qty, releasedAt
```

### Purchasing
```
Supplier: id, name, creditTerms, leadTimeDays, onTimeRatePct, priceNote
PurchaseOrder: id(PO-xxxx), supplierId, branchId, status(PoStatus),
           terms, deliveryLocation, notes, totalValue,
           expectedDate, createdBy, approvedBy, sentAt,
           requiresManagerApproval (totalValue > 10000 [ASSUME])
PurchaseOrderLine: id, poId, stockItemCode, qty, unitCost, lineTotal,
           qtyReceived, qtyDamaged, qtyOutstanding
GoodsReceipt: id(GRN-xxxx), poId, receivedAt, receivedBy, supplierDeliveryNo
GoodsReceiptLine: id, grnId, poLineId, qtyGood, qtyDamaged,
           issueType(qty|cost|wrong|damage), issueNote, photos[],
           costVariance, varianceApprovedBy
ReorderSuggestion: stockItemCode, available, rop, onOrder, suggestedQty,
           preferredSupplierId, reason  ← [BIZ] คำแนะนำเท่านั้น ไม่ auto-create
```

### POS
```
Reconciliation: id, jobId, performedBy, completedAt, isResolved
ReconLine: id, reconciliationId, quotationLineId, name, source,
           qtyApproved, qtyActual, qtyIssued, amount,
           status(ReconStatus), decision, decisionReason, decidedBy
           ⚠️ [BIZ] ทุก line ที่ status ≠ match ต้องมี decision ก่อนเปิด checkout

Payment: id, jobId, method(PaymentMethod), amount, status(PaymentStatus),
         reference, slipImage, edcTerminal, qrExpiresAt,
         receivedBy, receivedAt, idempotencyKey  ← กันเก็บซ้ำ
Deposit: id, jobId, amount, receivedAt   ← มัดจำตอนรับรถ นำมาหักตอนจ่าย

FiscalDocument: id(RC-24-xxxx | IV-24-xxxx), jobId, type(DocumentType),
         recipientName, taxId, address, subtotal, vat, total,
         issuedBy, issuedAt, isVoided, voidReason, voidApprovedBy,
         originalDocumentId  ← ใบลดหนี้/พิมพ์ซ้ำอ้างอิงเอกสารเดิม
DocumentPrintLog: id, documentId, action(print|reprint|void|refund),
         reason, performedBy, approvedBy, occurredAt
         ⚠️ [BIZ] reprint ต้องมีคำว่า "สำเนา/REPRINT" และไม่สร้างเลขใหม่

AccountsReceivable: id, jobId, amount, approvedBy, dueDate, settledAt
```

### Audit / Realtime / Offline
```
ActivityEvent: id, jobId, occurredAt, eventType, description,
         performedByEmployeeId, performedByName,
         source(EventSource)   ← [BIZ] บังคับทุก event
         payloadJson

SyncQueueItem (client-side + server ack): localId(TMP-xxxx), entityType,
         payload, attachmentCount, attachmentBytes,
         state(SyncState), attempts, lastError,
         serverId (หลังซิงก์สำเร็จ → แสดงการแทนที่ TMP → เลขจริง),
         conflictServerVersion, conflictResolution(mine|theirs)

Notification: id, employeeId, jobId, kind(warn|late|ok), title, body,
         createdAt, readAt, deepLink
```

### JobChat ([เพิ่ม 2026-09-10] ฟีเจอร์ใหม่ — ไม่มีในเอกสาร design ต้นแบบ)
```
JobChatMessage: id, jobId, body(nullable — อนุญาตข้อความมีแต่รูป), replyToMessageId(self-FK, Restrict),
         isDeleted, deletedAt(soft delete โดยเจ้าของข้อความเท่านั้น),
         createdByUserId, createdByUserName, createdAt
         ⚠️ ไม่มี BranchId/ShardKey แยก — scope ผ่าน Job เหมือน entity ลูกของ job อื่นทุกตัว
         ⚠️ ไม่มีคอลัมน์ path รูป — รูปภาพเป็น Attachment ปกติ (Kind="chat", EntityId = JobChatMessage.Id)
         ⚠️ mention ฝังเป็น token ในข้อความเอง "@[staffId:ชื่อ]" ไม่ parse จาก plain text ฝั่ง server

JobChatMention: id, messageId, staffId, staffName(snapshot ตอนส่ง)
         ← เก็บไว้ validate/query เท่านั้น (เช่น "ข้อความที่ถูกกล่าวถึง" ในอนาคต) ไม่ใช่แหล่งความจริงของการ render
```

### Org / Auth
```
Branch: id, name, technicianOnShiftCount, pendingJobCount, overdueJobCount
Shift: id, branchId, name, startTime, endTime, supervisorEmployeeId
Employee: id(employeeCode), name, role(Role), branchId, level(สำหรับคอมมิชชัน 8/10/12%)
ShiftSession: id, employeeId, branchId, shiftId, openedAt, closedAt
         ⚠️ ค่าที่เลือกกำหนดคิวงานและคลังทั้งวัน · ช่างไม่มีสิทธิ์ปิดกะ
```

---

## จุดที่ Design บังคับไว้ชัด — ต้อง enforce ที่ API layer

| # | Invariant | เช็คที่ |
|---|---|---|
| 1 | `available = onHand − reserved` (ไม่รวม onOrder, ไม่รวม damaged) | StockService |
| 2 | Quotation version ใหม่ → เวอร์ชันเดิม superseded + ApprovalRecord เดิมเป็นโมฆะ + ทุก line → pending | QuotationService |
| 3 | ApprovalRecord ผูก `quotationVersion` — validate ก่อนให้เริ่มซ่อม | RepairService |
| 4 | เฉพาะ QuotationLine ที่ `approved` เท่านั้นสร้าง RepairTask ได้ และเข้า POS ได้ | RepairService / PosService |
| 5 | `inprogress → qc` ต้องมี before+after photo ครบทุก task ที่ approved | RepairService |
| 6 | `qc pass` → ล็อก RepairTask ทั้งหมด (`isLocked = true`) | QcService |
| 7 | `ready → completed` ต้อง `balance = 0` หรือมี AR ที่ approved | PosService |
| 8 | Payment ต้องมี `idempotencyKey` — กันเก็บซ้ำ | PosService |
| 9 | `PaymentStatus.checking` ≠ failed — ห้าม UI แสดงล้มเหลว, ห้ามเปิดให้จ่ายซ้ำ | PosService + client |
| 10 | Recon ทุก line ที่ ≠ match ต้องมี decision ก่อน checkout | PosService |
| 11 | GRN รับบางส่วน → PO ยังเปิด · รับเกินต้องอนุมัติ · damaged ไม่เข้า onHand | PurchasingService |
| 12 | Destructive action (cancel job / cancel PO / refund / reprint / stock adjust) ต้องมี reason + performedBy (+ approvedBy บางเคส) | Cross-cutting filter |
| 13 | ทุก state change เขียน ActivityEvent พร้อม `source` | Cross-cutting interceptor |
| 14 | InspectionItem `result = na` ต้องมี `naReason` | InspectionService |
| 15 | Inspection ที่ submit แล้ว = read-only | InspectionService |

---

## หมายเหตุเรื่องฐานข้อมูล

- ทีมมี pattern เดิมอยู่แล้วที่ `AMD.GaragePro.Admin` — **.NET 8 + EF Core 8 + Dapper + MSSQL** (คนละ domain: EMCS/ประกัน)
- **Open Question #1** กระทบ schema โดยตรง: ถ้า Customer/Vehicle เป็น master data จาก GaragePro เดิม → ต้องออกแบบเป็น read-through + `isProvisional` + `REQ-xxxx` linking table ไม่ใช่ owned table
- ทุก money field: `decimal(18,2)` · ทุก timestamp: `datetime2` + เก็บ UTC + timezone ที่ client
- Photo/signature: เก็บ blob แยก (FTP/S3/ไฟล์เซิร์ฟเวอร์) เก็บแค่ path ใน DB — ปริมาณเยอะมาก (5 รูปรับรถ + 7–12 รูปตรวจ + 8 รูปก่อน/หลัง + QC + ลายเซ็น 2 ใบ = **~25–30 ไฟล์ต่องาน**)
