# 03 · API Surface — map จากหน้าจอ → endpoint

Base: `/api/v1` · Auth: JWT Bearer · ทุก response ห่อด้วย envelope เดียวกัน
ทุก mutation รับ header `X-Client-Source: mobile|web` → เขียนลง `ActivityEvent.source`

```jsonc
// envelope
{ "success": true, "data": {}, "error": null, "traceId": "..." }
{ "success": false, "data": null,
  "error": { "code": "QUOTE_VERSION_STALE", "message": "...(ไทย)", "field": null },
  "traceId": "..." }   // ⚠️ [UI] ทุก error state ต้องมี สาเหตุ + ปุ่มถัดไป + รหัสอ้างอิง(traceId)
```

---

## 1 · Auth & Session

| Method | Endpoint | ใช้ที่ | หมายเหตุ |
|---|---|---|---|
| POST | `/auth/login` | `/auth/login` (M), `/login` (W) | username + password → JWT ที่มี `Staff.BranchId`; Web ใช้งานต่อได้ทันที |
| POST | `/auth/refresh` | ทั้งสอง | |
| GET | `/branches` | `/auth/shift` (M) | คืน technicianOnShift, pendingJobs, overdueJobs ต่อสาขา |
| GET | `/branches/{id}/shifts` | `/auth/shift` | + supervisor |
| POST | `/shift-sessions` | `/auth/shift` | เปิดกะ → กำหนด branch+shift context ทั้งวัน |
| POST | `/shift-sessions/{id}/close` | `/profile` | ❌ role `technician` (403 + StateBlock "ไม่มีสิทธิ์") |
| GET | `/me` | `/profile`, AppShell | role, branch, shift, permissions[], pendingSyncCount |

---

## 2 · Master data (ขึ้นกับ Open Question #1)

| Method | Endpoint | ใช้ที่ |
|---|---|---|
| GET | `/customers/search?q=` | intake ขั้น 1 · ค้นด้วยชื่อ/เบอร์/ทะเบียน |
| GET | `/customers/{id}` | intake · คืน vehicles[] |
| GET | `/vehicles/search?q=` | intake, job search |
| POST | `/customers/provisional` | intake — สร้างชั่วคราว + คืน `REQ-xxxx` **[ASSUME]** |
| GET | `/catalog?q=&type=part\|labor` | quotation editor · คืน stock/reserved/onorder/eta/cost*/price (`cost` ตาม role) |

---

## 3 · Jobs

| Method | Endpoint | ใช้ที่ |
|---|---|---|
| GET | `/jobs?status=&branchId=&assignedTo=&overdue=&page=` | `/jobs/queue`(M), `/jobs`(W), `/jobs/board`(W) |
| GET | `/jobs/mine` | `/jobs/mine`(M) — เรียง urgency → promiseAt |
| GET | `/jobs/search?q=&pjTypeId=&pjStatusId=` | `/jobs/search`(M) — คืน `matchedOn` ("ตรงที่ทะเบียน"); เว็บใช้ `pjTypeId`/`pjStatusId` กรองตารางหน้าจ๊อบ (`/jobs`(W)) — ยังกรองฝั่ง server ด้วย legacy id ตรงๆ ไม่ผ่าน `PJTypeStatus` |
| GET | `/jobs/status-options` | `/jobs`(W) ตัวกรองสถานะ — คืนสถานะ (PJStatus) ที่มีจ๊อบใช้งานจริงในสาขาเท่านั้น |
| GET | `/jobs/form-options` | `/jobs`(W) — ยี่ห้อ รุ่น โฉม และสี |
| GET | `/jobs/by-qr/{code}` | `/scan`(M) |
| GET | `/jobs/{id}` | `/jobs/:id`(W) — full aggregate สำหรับ 8 แท็บ |
| GET | `/jobs/{id}/timeline` | แท็บกิจกรรม — ActivityEvent + source |
| POST | `/jobs` | `/jobs`(W) modal เปิดจ๊อบ — เลือกประเภทได้ (`pjTypeId` 9=รถในอู่/10=รถนัดหมาย, whitelist ที่ API) + สถานะยัง fix เป็น `รอตรวจสอบ` (dropdown สถานะที่ผูกกับ `PJTypeStatus` ยังไม่ทำ — รอตรวจ schema จริงจาก legacy DB); transaction ลง `Customer`/`Car`/`CarCustomer`/`PJCarPickUp`; intake mobile ในอนาคตต้องเพิ่ม idempotency |
| POST | `/jobs/{id}/cancel` | **ต้องมี** reason + approvedBy · แจ้งอะไหล่ที่สั่งไปแล้ว |
| GET | `/jobs/counts` | `/home`(M) 5 ตัวเลข · `/dashboard`(W) 9 KPI |
| GET | `/jobs/board` | กระดานโรงซ่อม 7 คอลัมน์ |
| GET | `/saved-views` · POST · DELETE | `SavedViewChips`(W) |

### Intake (draft ทุกขั้น — **[UI]** บังคับ)
| Method | Endpoint |
|---|---|
| POST | `/jobs/intake/drafts` (สร้าง) |
| PUT | `/jobs/intake/drafts/{draftId}` (บันทึกทุกขั้น 1–5) |
| GET | `/jobs/intake/drafts/{draftId}` (กลับมาต่อ) |
| DELETE | `/jobs/intake/drafts/{draftId}` (ทิ้งร่าง) |
| POST | `/jobs/intake/drafts/{draftId}/photos` (5 มุม) |
| POST | `/jobs/intake/drafts/{draftId}/signature` |
| POST | `/jobs/intake/drafts/{draftId}/submit` → สร้าง Job + QR → `waitinspect` |

---

## 4 · Inspection

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| GET | `/inspections/template` | 8 หมวด 31 รายการ + flag cust/optional (ให้ client cache) |
| GET | `/jobs/{id}/inspection` | resume ร่าง |
| PUT | `/jobs/{id}/inspection/items/{itemCode}` | บันทึกทีละรายการ (offline-friendly) |
| POST | `/jobs/{id}/inspection/items/{itemCode}/photos` | |
| POST | `/jobs/{id}/inspection/submit` | ✅ validate: ครบทุกรายการบังคับ + `na` มี reason → `waitquote` + lock |

---

## 5 · Quotation

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| GET | `/quotations?status=&branchId=` | `/quotations`(W) คิวจัดลำดับ |
| GET | `/jobs/{id}/findings` | ดึงผลตรวจ → เสนอเป็น line (แยก cust/tech + `add[]` catalog codes) |
| POST | `/jobs/{id}/quotations` | สร้าง draft (หรือ revision ถ้ามีอยู่แล้ว) |
| GET | `/quotations/{id}` | + version history |
| PUT | `/quotations/{id}/lines/{lineId}` | qty/price/discount/promo/tech/note |
| POST | `/quotations/{id}/lines` · DELETE | |
| POST | `/quotations/{id}/lock` · `/unlock` | **[UI]** ขอสิทธิ์แก้คนเดียว |
| GET | `/quotations/{id}/warnings` | ของไม่พอ / ซ้ำ / margin ต่ำ / หมดอายุ |
| POST | `/quotations/{id}/send` | ✅ validate ทุก line มี price+tech, ไม่ซ้ำ → `waitapprove` |
| POST | `/quotations/{id}/revise` | ✅ เวอร์ชันใหม่ + เหตุผลต่อบรรทัด → เดิม `superseded`, approval เดิมโมฆะ |
| GET | `/quotations/{id}/approval` | `/quotations/:id/approval`(W) ผลรายบรรทัด + ลายเซ็น |
| POST | `/quotations/{id}/discount-approval` | **[ASSUME]** > 10% ต้องผู้จัดการ |

### Customer approval (มือถือ — บนเครื่องพนักงาน)
| Method | Endpoint |
|---|---|
| GET | `/quotations/{id}/for-approval` | คืนแยก cust/tech + ยอดสด (ไม่คืน `cost`) |
| PUT | `/quotations/{id}/lines/{lineId}/decision` | approve / reject + rejectReason (5 ตัวเลือก) |
| POST | `/quotations/{id}/sign` | signature + deviceInfo + witnessEmployeeId + **version** → `approved` |

---

## 6 · Repair

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| GET | `/jobs/{id}/repair-tasks` | สร้างจาก approved lines เท่านั้น |
| POST | `/jobs/{id}/repair/start` | → `inprogress` + เริ่มจับเวลา |
| POST | `/repair-tasks/{id}/status` | doing / pause / done |
| POST | `/repair-tasks/{id}/photos?kind=before\|after` | ✅ บังคับก่อน done |
| POST | `/repair-tasks/{id}/parts-request` | part + qty + reason(4) + eta(3) + `canContinueOthers` → job `waitparts`, ล็อกเฉพาะ line |
| POST | `/jobs/{id}/extra-findings` | งานเพิ่ม → task `pending` + ต้องออก quotation revision |
| POST | `/jobs/{id}/repair/submit-qc` | ✅ ทุก approved task done + before/after ครบ → `qc` |

---

## 7 · QC

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| GET | `/qc/checklist-template` | 6 ข้อ |
| GET | `/jobs/{id}/qc` | |
| PUT | `/jobs/{id}/qc/items/{itemCode}` | passed + note |
| POST | `/jobs/{id}/qc/test-drive` | km + note + photos |
| POST | `/jobs/{id}/qc/pass` | ✅ ครบ 6/6 + test drive → `ready` + **ล็อก RepairTask ทั้งหมด** |
| POST | `/jobs/{id}/qc/fail` | issues[] + priority(high/medium/low) → `inprogress` + สร้าง task `fix` |
| POST | `/jobs/{id}/qc/revoke` | ยกเลิกผล QC (เปิดงานใหม่) — destructive, ต้องมี reason |

---

## 8 · Inventory

| Method | Endpoint |
|---|---|
| GET | `/inventory/dashboard?branchId=` |
| GET | `/inventory/items?q=&category=&lowStock=` |
| GET | `/inventory/items/{code}` (+ suppliers, compat, reservedJobs, poRef) |
| GET | `/inventory/items/{code}/ledger` |
| POST | `/inventory/items/{code}/adjust` | type(4) + reason + evidencePhotos + approvedBy |
| POST | `/inventory/reservations` · DELETE | จองจาก approved quotation line |
| POST | `/inventory/issues` | เบิกให้งาน → `ISS-xxxx` |
| GET | `/inventory/counts` · POST | ตรวจนับ `CNT-xxxx` |

---

## 9 · Purchasing

| Method | Endpoint |
|---|---|
| GET | `/purchasing/reorder-suggestions?branchId=` | **[BIZ]** คำแนะนำเท่านั้น |
| GET | `/purchase-orders?status=` |
| POST | `/purchase-orders` (จาก suggestion ที่เลือก) |
| GET | `/purchase-orders/{id}` |
| POST | `/purchase-orders/{id}/submit` → `pending` |
| POST | `/purchase-orders/{id}/approve` | ✅ **[ASSUME]** > 10,000 ต้อง manager |
| POST | `/purchase-orders/{id}/send` → `sent` |
| POST | `/purchase-orders/{id}/cancel` | destructive + reason |
| POST | `/purchase-orders/{id}/receipts` | GRN รายบรรทัด: qtyGood/qtyDamaged/issueType(4)/photos/costVariance → `partial` หรือ `complete` |
| GET | `/suppliers` |

---

## 10 · POS

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| GET | `/pos/queue?branchId=` | คิวรอชำระ (`ready`) |
| GET | `/jobs/{id}/reconciliation` | 3 คอลัมน์: approved/actual/issued + status(5) |
| PUT | `/jobs/{id}/reconciliation/lines/{lineId}/decision` | decision + reason |
| POST | `/jobs/{id}/reconciliation/complete` | ✅ ทุก line ≠ match ต้องมี decision → ปลดล็อก checkout |
| GET | `/jobs/{id}/checkout` | bill lines + deposit + balance |
| POST | `/jobs/{id}/payments` | method(4) + amount + reference/slip · **ต้องมี `Idempotency-Key`** |
| GET | `/payments/{id}/status` | polling สำหรับ `checking` (QR หมดเวลา / card pending) |
| POST | `/jobs/{id}/accounts-receivable` | ✅ ต้อง manager approve |
| POST | `/jobs/{id}/documents` | type(receipt\|taxinvoice) + recipient → `RC-24-xxxx` / `IV-24-xxxx` |
| GET | `/documents/{id}/preview` · `/pdf` | |
| POST | `/documents/{id}/reprint` | reason + approvedBy · **ไม่สร้างเลขใหม่** + ตี "สำเนา/REPRINT" |
| POST | `/documents/{id}/void` · `/refund` | ✅ manager only + reason → ใบลดหนี้อ้างเอกสารเดิม |
| GET | `/pos/audit-log?branchId=&from=&to=` | ประวัติพิมพ์ซ้ำ/ยกเลิก/คืนเงิน |
| POST | `/jobs/{id}/complete` | ✅ balance = 0 หรือ AR approved + มีเอกสาร + handover → `completed` |

---

## 11 · Handover ⚠️ **[GAP · สูง]** ยังไม่มี design

| Method | Endpoint (เสนอ) |
|---|---|
| GET | `/jobs/{id}/handover/checklist` |
| POST | `/jobs/{id}/handover/photos` |
| POST | `/jobs/{id}/handover/signature` → ปิดงาน |

---

## 12 · Reports

| Method | Endpoint |
|---|---|
| GET | `/reports/sales?from=&to=&branchId=` |
| GET | `/reports/operations` (turnaround, SLA, QC fail rate, comeback) |
| GET | `/reports/funnel` |
| GET | `/reports/stock` |
| GET | `/reports/purchasing` (fill rate, price variance) |
| GET | `/reports/technicians` (⚠️ role `lead` = ไม่มีตัวเงิน) |
| POST | `/reports/exports` → GET `/reports/exports/{id}` (สถานะไฟล์) |
| GET | `/reports/{key}/drilldown?...` | ทุกตัวเลขต้องเจาะได้ |

---

## 13 · Notifications & Realtime

| Method | Endpoint |
|---|---|
| GET | `/notifications?unreadOnly=` |
| POST | `/notifications/{id}/read` · `/read-all` |
| POST | `/devices` (FCM/APNs token) |

**Realtime (Open Question #2)** — แนะนำ **SignalR** เพราะทีมใช้ .NET อยู่แล้ว
```
Hub: /hubs/ops
Groups: branch:{branchId} · job:{jobId} · role:{role}

Events → client:
  job.status.changed      { jobId, from, to, at, by, source }
  job.created             { jobId, branchId }
  quotation.sent          { jobId, quotationId }
  quotation.approved      { jobId, quotationId, approvedCount, rejectedCount }
  inspection.submitted    { jobId }
  parts.requested         { jobId, itemCode }
  parts.received          { jobId, grnNo }
  qc.result               { jobId, result }
  payment.completed       { jobId, documentNo }
  stock.low               { itemCode, available, rop }
  po.status.changed       { poNo, status }
```
พฤติกรรม UI ที่ต้องได้: toast มุมจอ · badge เมนู · แถวตารางไฮไลต์ · board อัปเดตสด
**Fallback บังคับ:** polling `GET /jobs?updatedSince=` ทุก 30 วิ (เว็บใช้เมื่อ WS หลุด · มือถือใช้เป็นหลักตอนสัญญาณแย่)

---

## 14 · Sync (Flutter offline-first)

| Method | Endpoint | หมายเหตุ |
|---|---|---|
| POST | `/sync/batch` | ส่ง queue รวม — ทุก item มี `clientLocalId`(TMP-xxxx) + `baseVersion` |
| | | คืนต่อ item: `{ clientLocalId, serverId, state, conflict? }` |
| POST | `/sync/conflicts/{id}/resolve` | `mine` \| `theirs` **[ASSUME]** ดู Open Question #3 |
| POST | `/attachments` | multipart · resumable/chunked สำหรับรูป (~25–30 ไฟล์/งาน) |
| GET | `/sync/status` | เทียบกับ local queue |
| GET | `/reference-data?since=` | template ตรวจเช็ค, QC, catalog, สาขา, เหตุผล dropdown ทั้งหมด → cache ในเครื่อง |

**สัญญา offline บังคับ:** ทุก mutation ที่มือถือทำได้ต้อง **idempotent ด้วย `clientLocalId`** — ส่งซ้ำต้องไม่สร้างซ้ำ

---

## 15 · Cross-cutting

| เรื่อง | วิธี |
|---|---|
| Permission | policy-based ต่อ endpoint จากตาราง Roles (§4 ของ doc 01) · 403 ต้องคืน `StateBlock` payload (สาเหตุ + ปุ่มถัดไป) |
| Field-level security | `cost`, `grossMargin`, `commission` → strip ตาม role ที่ serializer ไม่ใช่ที่ client |
| Destructive action | action filter บังคับ `reason` (+ `approvedBy` ตาม policy) ก่อนถึง handler |
| Activity log | interceptor เขียน `ActivityEvent` ทุก state change พร้อม `source` จาก header |
| Idempotency | header `Idempotency-Key` (payments) + `clientLocalId` (sync) |
| Concurrency | `rowVersion` ทุก aggregate root · 409 + `QUOTE_VERSION_STALE` |
| Error codes | ทุก business rule ที่ fail มี code ตายตัว → client แปลเป็นข้อความไทย + ปุ่มถัดไป |
