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
| GET | `/auth/switchable-branches` | AppShell (W) | **[เพิ่ม 2026-10-12]** ผู้ดูแลระบบเท่านั้น (อ่าน `User.IsAdministrator` จาก Garage DB ใหม่ทุกครั้ง ไม่เชื่อ claim — อื่นได้ 403 `AUTH_SWITCH_FORBIDDEN`) · สาขา active กลุ่ม Service ที่มีบัญชี**ผู้ดูแลระบบ** active ซึ่ง `Staff.FirstName` ตรงกับพนักงานของผู้เรียก · `{branchId,name,address,isCurrent}` ไม่ส่ง UserId ปลายทาง |
| POST | `/auth/switch-branch` | AppShell (W) | `{branchId}` → `LoginResult` ในนามบัญชีของสาขานั้น (ไม่ถามรหัสผ่าน) · server หาบัญชีปลายทางใหม่เองเสมอ · ไม่มีบัญชีที่ตรง/บัญชีถูกปิดหรือย้ายสาขา → 403 `AUTH_BRANCH_FORBIDDEN` · เขียน `ActivityEvent` `auth.branch_switched` ในนามบัญชีเดิม |

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
| POST | `/jobs` | `/jobs`(W) modal เปิดจ๊อบ — เลือกประเภทได้ (`pjTypeId` 9=รถในอู่/10=รถนัดหมาย, whitelist ที่ API) + สถานะยัง fix เป็น `รอตรวจสอบ` (dropdown สถานะที่ผูกกับ `PJTypeStatus` ยังไม่ทำ — รอตรวจ schema จริงจาก legacy DB); transaction ลง `Customer`/`Car`/`CarCustomer`/`PJCarPickUp`; intake mobile ในอนาคตต้องเพิ่ม idempotency **[เพิ่ม 2026-09-16] ของจริงที่ implement: `POST /api/v1/jobs` รับ `appointmentAt` เพิ่ม — บังคับเมื่อ `jobTypeId=10` ห้ามส่งเมื่อ 9** |
| PUT | `/api/v1/jobs/{id}/appointment` | **[เพิ่ม 2026-09-16]** เลื่อน/แก้วันเวลานัดหมาย — เฉพาะ `jobTypeId=10` และยังไม่ถึงสถานะจบ · เขียน `ActivityEvent` `job.appointment.changed` เสมอ |
| GET | `/api/v1/jobs/calendar?from=&to=&q=&status=` | **[เพิ่ม 2026-09-16]** มุมมองปฏิทินนัดหมายของหน้า `/jobs`(W) — คืนทุกจ๊อบที่มี `appointmentAt` ในช่วง (ไม่ใช่ keyset cursor แบบ `/jobs/search`) ช่วงสูงสุด 92 วัน คืน `{ items, truncated, limit }` |
| GET | `/api/v1/jobs/board?q=` | **[เพิ่ม 2026-10-10]** บอร์ดสถานะรถในอู่ของหน้า `/jobs?view=board`(W) — **ดูอย่างเดียว** จ๊อบ `jobTypeId=9` ที่ยังไม่ปิดทุกงาน (ไม่ใช่ keyset) เพดาน 300 แถว · คืน `{ columns[8 สถานะเปิด เรียงตาม lifecycle แม้ว่าง]{ status, statusLabel, cards[]{ job, statusSince, activeWorkers[]{technicianName, kind work/pause, startedAt}, awaitingCustomerQuotationCodes[], latestChatMessageId? } }, total, truncated, limit, generatedAt }` · การ์ดเรียงอยู่ในสถานะนานสุดก่อน · ไม่มีตัวเงิน (ทุก role เปิดได้) · `statusSince` = event `job.opened`/`job.status.changed` ล่าสุด หรือ `createdAt` |
| PUT | `/api/v1/jobs/{id}/convert-to-in-shop` | **[เพิ่ม 2026-09-17]** แปลงงานนัดหมาย (`jobTypeId=10`) เป็นรถในอู่ (`jobTypeId=9`) พร้อม `{ actualArrivalAt }` — ไม่ผูกกับวันนัดที่ตั้งไว้ (มาก่อน/หลังนัดก็แปลงได้) `AppointmentAt` เดิมไม่ถูกล้าง เขียน `ActivityEvent` `job.converted_to_in_shop` |
| PUT | `/api/v1/jobs/{id}/mileage` | **[เพิ่ม 2026-10-08]** บันทึก/แก้เลขไมล์ขณะรับรถ `{ mileageAtIntake }` (0..9,999,999) — ล็อกเมื่อส่งมอบรถแล้ว/จ๊อบปิด (`JOB_MILEAGE_LOCKED` 409) · เขียน `job.mileage.changed` payload `{from,to}` เมื่อค่าเปลี่ยนจริง · `POST /jobs` รับ `mileageAtIntake` (บังคับเมื่อ `jobTypeId=9`) · `convert-to-in-shop` รับ `mileageAtIntake` (บังคับถ้าจ๊อบยังไม่มีค่า) · `POST .../intake-checklist/submit` ปฏิเสธ `INTAKE_MILEAGE_REQUIRED` ถ้ายังไม่มี |
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
| PUT | `/quotations/{id}/lines/{lineId}` | qty/price/discount/promo/tech/note (+ name/unit/unitCost/standardHours ถ้าเป็น ad-hoc) |
| POST | `/quotations/{id}/lines` · DELETE | `catalogCode` ว่าง = รายการนอกแคตตาล็อก (ad-hoc) — ต้องส่ง `name`+`type`+`unitPrice` แทน (2026-09-15, [docs/07-quotation-adhoc-line.md](07-quotation-adhoc-line.md)) |
| POST | `/api/v1/quotations/{id}/lines/from-template` | **[เพิ่ม 2026-09-16]** `{ templateId, source? }` เพิ่มหลายบรรทัดจากเทมเพลตในครั้งเดียว — [docs/08-quotation-template.md](08-quotation-template.md) |
| GET/POST | `/api/v1/quotation-templates` · GET/PUT `/{id}` · PATCH `/{id}/status` | **[เพิ่ม 2026-09-16]** ข้อมูลหลักเทมเพลตใบเสนอราคา — อ่านเปิดทุก role, เขียนเฉพาะผู้จัดการ |
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

ของจริงที่ implement: `GET /api/v1/jobs/{id}/handover` · `PUT .../handover/items/{itemId}` · `PUT .../handover/submit`
· **[เพิ่ม 2026-10-08]** `PUT /api/v1/jobs/{id}/handover/service-info` `{ mileageAtHandover, nextServiceMileage, nextServiceMonths }`
— ไมล์ส่งมอบ ≥ ไมล์รับรถ · ไมล์นัด > ไมล์ส่งมอบ · เดือน 1–24 (`HANDOVER_VALIDATION`) · จ๊อบยังไม่มีไมล์รับรถ → `HANDOVER_INTAKE_MILEAGE_REQUIRED`
· `submit` ปฏิเสธ `HANDOVER_SERVICE_INFO_REQUIRED` ถ้ายังไม่บันทึก · `HandoverDto` เพิ่ม `mileageAtIntake`/`mileageAtHandover`/
`nextServiceMileage`/`nextServiceMonths`/`nextServiceDueOn` (yyyy-MM-dd ตามปฏิทินไทย = วันส่งมอบจริง + N เดือน — ก่อนเซ็นเป็นพรีวิว)

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

ของจริงเพิ่ม 2026-10-08:
| GET | `/api/v1/reports/vehicle-history/search?q=` | ค้นรถจากทะเบียน/เบอร์โทร (ตัดช่องว่าง/ขีด ≥3 ตัว) ใน snapshot ของ `svc_Job` สาขาปัจจุบัน — **ทุกบทบาท** · คืน `{ items, truncated }` สูงสุด 50 คัน |
| GET | `/api/v1/reports/vehicle-history/{vehicleId}` | timeline งานของรถ (ใหม่สุดก่อน): ไมล์รับ/ส่ง · บรรทัดที่ลูกค้าอนุมัติของทุกใบที่ไม่ Superseded · ใบเสร็จ · นัดครั้งถัดไป — **ทุกบทบาท แต่ Technician/Lead ได้ยอดเงินเป็น null** (`showAmounts=false`) · ไม่มีงาน → 404 `VEHICLE_HISTORY_NOT_FOUND` |
| GET | `/api/v1/reports/service-due?fromDate=&toDate=` | รถใกล้ครบรอบบริการ (Manager/Office) — ใบส่งมอบล่าสุดของรถแต่ละคันที่วันนัดอยู่ในช่วง ตัดคันที่กลับมาเปิดจ๊อบใหม่ (ไม่นับที่ยกเลิก) หรือยังมีจ๊อบอื่นที่ยังไม่ปิด · ค่าเริ่มต้น −30..+30 วัน ช่วงสูงสุด 366 วัน |

---

## 13 · Notifications & Realtime

| Method | Endpoint | สถานะ |
|---|---|---|
| GET | `/notifications?unreadOnly=&beforeAt=&beforeId=&take=` | **ทำแล้ว 2026-10-07** · keyset ใหม่→เก่า ย้อนหลัง 30 วัน · คืน `{items, hasMore}` |
| GET | `/notifications/unread-count` | **ทำแล้ว** · คืน `{unread}` (ไม่นับเรื่องที่มีคนดำเนินการแล้ว) |
| POST | `/notifications/{id}/read` · `/read-all` | **ทำแล้ว** · ทำซ้ำได้ · คืน `{unread}` ล่าสุด |
| DELETE | `/notifications/{id}/read` | **ทำแล้ว** · กลับเป็นยังไม่อ่าน |
| POST | `/devices` (FCM/APNs token) | ยังไม่ทำ (ยังไม่มี push) |

ผู้รับเป็น **รายบุคคล (Staff.Id)** หรือ **กลุ่มบทบาท (bitmask ของ UserRole)** — ไม่มีทางดึงรายชื่อพนักงานตามบทบาท
จาก legacy จึงคัดตอนอ่านด้วย role ใน JWT · สถานะอ่านแยกต่อคน (`svc_NotificationRead`) · ชนิด/ผู้รับ/การปิดเรื่อง
ดูที่ `CLAUDE.md` หัวข้อ "แจ้งเตือนบนเว็บ" · client poll `unread-count` ทุก 30 วิ จนกว่าจะมี realtime

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

---

## 16 · Self-serve trial (shard `sp1`) **[เพิ่ม 2026-10-14]**

> อู่ที่สมัครเองอยู่ shard `sp1` = ฐาน `GarageSelfServe` (ตารางรูปร่างเหมือน Garage + `svc_*` + `sp_*`) · API/เว็บ/แอปตัวเดียวกับลูกค้าเดิม
> ภาพรวมและการตัดสินใจ: [12-self-serve-trial-proposal.md](12-self-serve-trial-proposal.md) · ทุก endpoint ด้านล่างตอบ envelope เดิม
> ถ้า API ไม่ได้ตั้ง `SelfServe:ConnectionString` = ไม่มี self-serve: signup ตอบ `SIGNUP_DISABLED` (503) · `/selfserve/*` ตอบ `SELFSERVE_NOT_APPLICABLE` (404)

### 16.1 สมัคร (anonymous · rate limit ต่อ IP แยกจากฟอร์มติดต่อ — `API/RateLimiting/RateLimitRules.cs`)
| Method · Path | Body → Data | หมายเหตุ |
|---|---|---|
| `GET /api/v1/public/signup/config` | → `{enabled, googleEnabled, emailEnabled, turnstileSiteKey?}` | `SelfServe:Enabled=false` → `enabled:false` (เว็บแสดง "เปิดรับสมัครเร็วๆ นี้") · Production ที่อีเมลยังเป็น `log` หรือ `cloudflare` ที่ตั้งค่าไม่ครบ (`IEmailSender.IsConfigured=false`) → `emailEnabled:false` · ตั้งค่า Cloudflare: [docs/16](16-cloudflare-email-setup.md) |
| `GET /api/v1/public/signup/provinces` | → `[{id, name}]` | อ่านตาราง `province` (cache 24 ชม.) |
| `POST …/email/start` | `{requestId, email, password, turnstileToken, website, consent:true}` → `{resendAfterSeconds}` | honeypot `website` มีค่า = 200 หลอก · อีเมลที่มีบัญชีแล้ว = 200 เหมือนกัน (ส่งอีเมล "มีบัญชีอยู่แล้ว" แทน OTP) · 3/ชม. + 10/วันต่อ IP |
| `POST …/email/resend` | `{email}` → `{resendAfterSeconds}` | ≥ 60 วิ · ≤ 3 ครั้ง/ชม./อีเมล |
| `POST …/email/verify` | `{email, code, password}` → `{signupToken, expiresAt}` | **ตรวจ OTP ก่อน แล้วตรวจรหัสผ่านกับที่ตั้งตอน start** (กันคนอื่นสั่ง start ทับด้วยรหัสของตัวเอง) · ผิดรวม 5 ครั้ง = `SIGNUP_OTP_LOCKED` · token ใช้ครั้งเดียว 15 นาที |
| `POST …/google` | `{idToken, turnstileToken, website}` → `{signupToken, expiresAt}` | ตรวจ ID token กับ Google (`SelfServe:Google:ClientId`) ต้อง `email_verified` · บัญชีที่ใช้งานอยู่แล้ว = `SIGNUP_ACCOUNT_EXISTS` |
| `POST …/workspace` | `{requestId, signupToken, ownerName?, garageName, phone, provinceId?, size? ('1-3'\|'4-10'\|'10+'), sampleData}` (`ownerName` = ชื่อ-นามสกุลเจ้าของอู่ คำแรกเป็นชื่อ · ไม่ส่ง = ชื่อจาก Google ไม่งั้นส่วนหน้าของอีเมล) → ฟิลด์ `LoginResult` ทั้งหมด + `workspaceNo` + `sampleTechnicians?[{userName,password}]` | สร้างสาขา (กลุ่ม 7) + เจ้าของ `SPnnnn.M01` (Manager ผ่าน override · **ไม่ใช่ IsAdministrator**) + กะ + หมวดหมู่/คลังตั้งต้น + ข้อมูลตัวอย่าง (best effort) + LINE ทีมขาย + อีเมลต้อนรับ · ส่งซ้ำด้วย token+requestId เดิม = ผลเดิม (ไม่สร้างซ้ำ) |

### 16.2 เข้าสู่ระบบ
- `POST /api/v1/auth/login` เดิม รับ **อีเมลเจ้าของ** (หา `sp_Account` → `SPnnnn.M01`) หรือ **`SPnnnn.Xnn`** (พนักงาน) หรือรหัสพนักงาน GaragePro เดิม → shard ตามที่หาเจอ
- `POST /api/v1/auth/google {idToken}` → `LoginResult` · บัญชีไม่มี = `AUTH_GOOGLE_NOT_REGISTERED` (404) · ไม่ได้ตั้ง Google = `AUTH_GOOGLE_DISABLED` (503)
- `AuthUser.plan = {kind: trial|paid|legacy|suspended, trialEndsAt?, daysLeft?, readOnly, features[], limits?}` + `AuthUser.workspace = {workspaceNo, name}` — **สาขา GaragePro เดิมไม่มีสองฟิลด์นี้** (null ถูกตัดออกจาก JSON) · JWT มี claim `plan`/`plan_exp`
- login ตอนแพ็กเกจ locked (หมดอายุเกิน 90 วันหรือถูกระงับ) = `TRIAL_EXPIRED` (403) · ช่วงอ่านอย่างเดียวยัง login ได้

### 16.3 แพ็กเกจและข้อจำกัด (บังคับที่ API)
| สถานการณ์ | ผล |
|---|---|
| ฟีเจอร์ขั้นสูงบน trial (`[RequireFeature]`) | 403 `PLAN_FEATURE_LOCKED` — จัดซื้อ/ซัพพลายเออร์ · สต็อก/เบิก/คลัง · POS/โปรโมชัน · ใบกำกับภาษี · รายงานขั้นสูง (รอบเวลา/ยอดขาย-กำไร/ขายหน้าร้าน/สต็อก/เวลาทำงานช่าง) · ส่งออก Excel · สลับสาขา — **ยกเว้น GET รายการคลัง/ซัพพลายเออร์** (ฟอร์มสินค้าใช้) |
| เกินลิมิต (`SelfServe:Limits`: พนักงาน 5 · สินค้า 200 · ลูกค้า 200 · จ๊อบ 100) | 422 `PLAN_LIMIT_REACHED` |
| เขียนข้อมูลหลังหมดอายุ (อ่านอย่างเดียว 90 วัน) | 403 `TRIAL_EXPIRED` — GET ได้ปกติ · ยกเว้น `/auth/*`, `/public/*`, `/selfserve/*`, `/notifications/*` |
| สร้างพนักงานบน sp1 | ต้องส่ง `role` · ระบบเติมเพศ/ตำแหน่ง/แผนกเอง · username `SPnnnn.Tnn` (ช่าง/หัวหน้าช่าง) หรือ `.Snn` · `account.initialPassword` คืนครั้งเดียว |

### 16.4 สถานะอู่ทดลอง (`[Authorize]` · shard ต้องเป็น sp1 ไม่งั้น 404 `SELFSERVE_NOT_APPLICABLE`)
| Method · Path | หมายเหตุ |
|---|---|
| `GET /api/v1/selfserve/status` | `{workspaceNo, name, plan, status, readOnly, trialEndsAt, daysLeft, limits{staff,catalog,customers,jobs: {used,max}}, sampleDataLoadedAt?, extensionRequestedAt?, checklist{staffCount, catalogCount, jobCount, quotationSentCount, mobileLoginSeen}}` |
| `POST /api/v1/selfserve/extension-request` `{requestId, note?}` | แจ้ง LINE ทีมขาย · ซ้ำภายใน 24 ชม. = 409 `SELFSERVE_EXTENSION_ALREADY_REQUESTED` |
| `POST /api/v1/selfserve/sample-data` · `POST …/sample-data/clear` | ผู้จัดการเท่านั้น (`SELFSERVE_SAMPLE_FORBIDDEN`) · ล้างตาม ledger `sp_SampleEntity` กดซ้ำได้ |

### 16.4.1 แพ็กเกจและการแจ้งชำระเงิน **[เพิ่ม 2026-10-21 · สัญญาเต็มที่ [docs/17 §5](17-package-renewal.md)]**
เรียกได้แม้อู่อ่านอย่างเดียว (อยู่ใต้ `/selfserve`) · ตัวเงิน/หมายเหตุ/สลิปเห็นเฉพาะผู้จัดการ (role อื่นได้ null ที่ server)
| Method · Path | หมายเหตุ |
|---|---|
| `GET /api/v1/selfserve/package` | `PackageOverview` `{workspaceNo, plan, status, endsAt?, lastUsableDate?, daysLeft?, readOnly, monthOptions[{months, price?, previewEndsAt, previewLastUsableDate}], paymentAccount?, pending?, canSubmit, cannotSubmitCode?, cannotSubmitReasonTh?, paidAtMaxPastDays}` · `cannotSubmitCode`: not_renewable → unavailable → not_manager → pending_exists (เหตุผลแรกที่เข้าเงื่อนไข) |
| `GET /api/v1/selfserve/package/payments?take=20` | `PackagePayment[]` ใหม่สุดก่อน (take ≤ 50) |
| `POST /api/v1/selfserve/package/payments` | multipart `requestId · paidAt · amount · monthCount · note? · slip` (ผู้จัดการ · 10 ครั้ง/ชม. ต่ออู่) → `PackagePayment` · requestId เดิม = แถวเดิม ไม่อัปโหลด/ไม่ส่ง LINE ซ้ำ · request ≤ 16 MB (สลิป ≤ 15 MB) · content-type ของสลิปไม่ระบุหรือ `application/octet-stream` ได้ (ตัดสินจากเนื้อไฟล์) |
| `POST /api/v1/selfserve/package/payments/{id}/cancel` | ผู้จัดการยกเลิกรายการรอตรวจของอู่ตัวเอง |
| `GET /api/v1/selfserve/package/payments/{id}/slip` | ไฟล์ `image/jpeg` หรือ `application/pdf` · ผู้จัดการ · ต้องมี Bearer |
| `GET /api/v1/public/package-slips/{id}?exp=&sig=` | anonymous · ลิงก์มีลายเซ็น HMAC ที่ส่งเข้า LINE ทีมงาน (อายุ 7 วัน) · ผิดทุกกรณี 404 เปล่า · ถังจำกัดอัตรา 300/ชม. ต่อ IP แยกจากฟอร์มติดต่อ |

Error: `PACKAGE_PAYMENT_FORBIDDEN` 403 · `PACKAGE_PAYMENT_UNAVAILABLE` 503 · `PACKAGE_PAYMENT_VALIDATION` 422 (+`field`: requestId · paidAt · amount · monthCount · note · slip · until · reason · id) ·
`PACKAGE_PAYMENT_PENDING_EXISTS` 409 · `PACKAGE_NOT_RENEWABLE` 409 · `PACKAGE_PAYMENT_NOT_FOUND` 404 · `PACKAGE_PAYMENT_NOT_PENDING` 409 · `PACKAGE_APPROVAL_CONFLICT` 409 ·
`PACKAGE_SLIP_UPLOAD_FAILED` 503 · `ATTACHMENT_EMPTY` 422 · `ATTACHMENT_TYPE_INVALID` 422 · `ATTACHMENT_TOO_LARGE` 413 · `RATE_LIMITED` 429
· แจ้งเตือนในระบบหลังทีมงานตรวจ: kind `package.payment_approved` / `package.payment_rejected` · `entityType: "PackagePayment"` · `linkHint: "package"`

### 16.5 งานเบื้องหลังและเครื่องมือผู้ดูแล
- งานรายวันใน API (`SelfServeDailyJob`, ปิดได้ด้วย `SelfServe:Jobs:Enabled=false`): 03:30 เตือนก่อนหมด 7/1 วัน · หมดอายุวันที่ 30 · อีเมลชวนกลับวันที่ 45 หลังหมด · ลบข้อมูลเมื่อพ้นช่วงอ่านอย่างเดียว (เก็บ `NormalizedEmail` ไว้กันสมัครซ้ำ) · อู่ที่ย้ายไป db2 แล้วลบเมื่อพ้น `PurgeAfter` · ล้างอู่ที่สร้างค้าง > 1 ชม. · 08:30 สรุปส่ง LINE ทีมขาย · กันรันซ้ำข้ามเครื่องด้วย `sp_JobRun`
- งานรายวัน (docs/17 กฎข้อ 13): อู่ที่มีรายการแจ้งชำระรอตรวจไม่ได้อีเมลเตือน/เชิญกลับและไม่ถูกลบ · อู่ paid ได้อีเมลเตือนก่อนหมด 14 และ 3 วัน (`SelfServe:Package:PaidReminderDays`) · สรุปรายวันต่อท้าย " · แจ้งชำระรอตรวจ N"
- `tools/spadmin` (CLI): `list · show · extend · set-plan · suspend · purge · run-jobs · convert · payments · payment · approve-payment · reject-payment` · รายละเอียดใน `tools/spadmin/README.md` · **`convert` ย้ายเฉพาะข้อมูลหลัก** (หมวดหมู่ · คลัง · สินค้า · เทมเพลต · พนักงาน · ลูกค้า · รถ) ไปสาขา GaragePro ที่สร้างด้วย GaragePro Admin แล้ว ผ่าน service เดิม · `--dry-run` ก่อนเสมอ

### 16.6 Error code ใหม่ (ข้อความไทยอยู่ที่ `Application/SelfServe/*Errors.cs`, `Application/Common/PlanErrors.cs`, `Application/Auth/SelfServeAuthErrors.cs`)
`SIGNUP_DISABLED` · `SIGNUP_VALIDATION` · `SIGNUP_BOT_CHECK_FAILED` · `SIGNUP_EMAIL_DISPOSABLE` · `SIGNUP_EMAIL_DOMAIN_INVALID` · `SIGNUP_EMAIL_SEND_FAILED` · `SIGNUP_LOOKUP_UNAVAILABLE` · `SIGNUP_OTP_THROTTLED` · `SIGNUP_OTP_INVALID` · `SIGNUP_OTP_EXPIRED` · `SIGNUP_OTP_LOCKED` · `SIGNUP_PASSWORD_MISMATCH` · `SIGNUP_TOKEN_INVALID` · `SIGNUP_GOOGLE_INVALID` · `SIGNUP_ACCOUNT_EXISTS` · `SIGNUP_PROVISION_FAILED` · `SIGNUP_RATE_LIMITED` · `AUTH_RATE_LIMITED` · `AUTH_GOOGLE_NOT_REGISTERED` · `AUTH_GOOGLE_DISABLED` · `TRIAL_EXPIRED` · `PLAN_FEATURE_LOCKED` · `PLAN_LIMIT_REACHED` · `STAFF_ROLE_REQUIRED` · `STAFF_ACCOUNT_CONFLICT` · `STAFF_PASSWORD_TOO_SHORT` · `STAFF_PASSWORD_TOO_LONG` · `STAFF_WORKSPACE_NOT_FOUND` · `SELFSERVE_NOT_APPLICABLE` · `SELFSERVE_VALIDATION` · `SELFSERVE_EXTENSION_ALREADY_REQUESTED` · `SELFSERVE_NOTIFY_FAILED` · `SELFSERVE_SAMPLE_FORBIDDEN` · `SELFSERVE_SAMPLE_UNAVAILABLE` · `SELFSERVE_SAMPLE_FAILED` · `SELFSERVE_SAMPLE_WORKSPACE_NOT_READY` · `SELFSERVE_SAMPLE_CLEAR_INCOMPLETE` · `SELFSERVE_WORKSPACE_NOT_FOUND` · `SELFSERVE_PURGE_REFUSED` · `POS_RECEIPT_CONFLICT` (409 — เลขใบเสร็จชน) · `AUTH_BRANCH_FORBIDDEN` (ขอกะของสาขาอื่น)

## 17 · API ภายในสำหรับ GaragePro Admin **[เพิ่ม 2026-10-22 · สัญญาเต็มที่ [docs/17 §8.6](17-package-renewal.md)]**
- ทางเข้า: docker network `garagepro-servicepro` → `http://garagepro_service_api:8080` เท่านั้น · Nginx ของโดเมน API ตอบ 404 ให้ `/api/v1/internal` จากภายนอกเสมอ
  (header `X-GP-Internal-Deny: nginx` · ติดตั้งด้วย `deploy/production/add-api-internal-deny.sh`)
- auth: header `X-Internal-Key` เทียบแบบ constant-time กับ `Internal:ApiKey` (≥ 32 ตัว · user-secrets/env file) · ไม่ใช้ JWT ·
  server ไม่ตั้งคีย์ = 404 เปล่าทุก route · ไม่มี/ผิดคีย์ = 401 `INTERNAL_AUTH_REQUIRED` · คำตอบทุกแบบ `Cache-Control: no-store`
- ไม่ผ่าน `SessionRevocationMiddleware` และ `TrialReadOnlyFilter` (token ที่ติดมาด้วยไม่มีผล)

| Method · Path | หมายเหตุ |
|---|---|
| `GET /api/v1/internal/sites` | envelope ปกติ · `data: { generatedAt, sites: [{ source, shardKey, branchId, code, name, phone?, plan, workspaceStatus?, branchActive?, startsAt?, endsAt?, lastUsableDate?, daysLeft?, usage, pendingPayments?, latestPaymentAt?, extensionRequestedAt?, jobsLast30Days, lastJobAt?, createdAt? }] }` · อ่านฐานไม่สำเร็จ = 503 `INTERNAL_SOURCE_UNAVAILABLE` |
