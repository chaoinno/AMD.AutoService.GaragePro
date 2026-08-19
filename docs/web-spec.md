# Spec สำหรับสร้าง React web app (flow แรก: ใบเสนอราคา + เอกสาร)

สร้างที่ `web/` ในโฟลเดอร์นี้ (mono-repo `AMD.AutoService.GaragePro`)
**ห้ามแก้ไฟล์นอก `web/` เด็ดขาด** — `backend/`, `docs/`, `GaragePro Service Ops Design/` เป็น read-only สำหรับงานนี้

---

## 1. Stack ที่ต้องใช้ (ตัดสินใจแล้ว ห้ามเปลี่ยน)

- **Vite + React 19 + TypeScript** (ไม่ใช่ Next.js)
- `react-router` v7 — routing
- `@tanstack/react-query` v5 — data fetching
- `@tanstack/react-table` v8 — ตารางคิวงาน
- `tailwindcss` v4 — styling
- `react-hook-form` + `zod` — ฟอร์ม
- pnpm เป็น package manager

Target: **desktop 1440×940 เป็นหลัก · 1024px ย่อ sidebar · < 1024px ไม่รองรับ** (แสดงข้อความแนะนำให้ใช้เดสก์ท็อป)

---

## 2. Design tokens (คัดลอกให้ตรงเป๊ะ — มาจาก prototype ที่อนุมัติแล้ว)

```
navy/900  #0F2440    navy/700  #1B3557
blue/600  #1B6BE3    blue/50   #E6EFFC
teal/500  #0E9F8C    amber/500 #E0930C
red/600   #C02626    orange/600 #C2410C
page bg   #EEF1F6    card bg   #FFFFFF    border #E3E8EF
text      #0F2440    muted     #64748B    faint  #94A3B8

space   4 · 8 · 16 · 24 · 32 · 48 · 64
radius  chip 6px · input 10px · card 14px · pill 999px
type    32/24/20/16/14/12 · line-height 1.6–1.7 · font-weight ≥400
font    'Noto Sans Thai', 'IBM Plex Sans', system-ui
ตัวเลขเงินทุกที่: font-family 'IBM Plex Mono', monospace + font-variant-numeric: tabular-nums
sidebar 212px (เต็ม) / 68px (ย่อที่ 1024px)
```

### สีสถานะใบเสนอราคา (bg / fg)
| token | ไทย | bg | fg |
|---|---|---|---|
| `draft` | ฉบับร่าง | `#EEF1F5` | `#475569` |
| `sent` | ส่งให้ลูกค้าแล้ว | `#E6EFFC` | `#1552B3` |
| `partial` | อนุมัติบางส่วน | `#FDF3E2` | `#8A5A00` |
| `approved` | อนุมัติครบ | `#E3F5F1` | `#0B6D5E` |
| `rejected` | ลูกค้าไม่อนุมัติ | `#FBE9E9` | `#A31D1D` |
| `superseded` | ถูกแทนที่ | `#E4E9F0` | `#0F2440` |
| `expired` | หมดอายุ | `#FBE9E9` | `#A31D1D` |

**กฎบังคับ:** ทุกป้ายสถานะต้องมี **สี + ไอคอน + ข้อความ** ห้ามใช้สีอย่างเดียวสื่อความหมาย

---

## 3. API (รันที่ `http://localhost:5080`)

ทุก response ห่อด้วย envelope:
```ts
type Envelope<T> = {
  success: boolean
  data: T | null
  error: { code: string; messageTh: string; field?: string; details?: unknown } | null
  traceId: string
}
```

Header ที่ต้องส่งทุก request (P0 ยังไม่มี JWT — ใช้ header ชั่วคราว):
```
X-Shard-Key: db2
X-Branch-Id: 105
X-User-Id: 1
X-User-Name: ปวีณา เอกสาร
X-User-Role: Office      // Office | Manager | Cashier | Technician | FrontDesk | Lead
X-Client-Source: web
```

### Endpoints
```
GET    /api/v1/quotations?filter=todo|wait|rev|done   → QuotationSummary[]
GET    /api/v1/quotations/{id}                        → Quotation
POST   /api/v1/quotations            body: { jobId, validUntil?, depositAmount? }
POST   /api/v1/quotations/{id}/lines body: UpsertLine
PUT    /api/v1/quotations/{id}/lines/{lineId}         body: UpsertLine
DELETE /api/v1/quotations/{id}/lines/{lineId}
GET    /api/v1/quotations/{id}/validate               → { isValid, errors[], warnings[] }
POST   /api/v1/quotations/{id}/send
POST   /api/v1/quotations/{id}/revise                 body: { revisionReason }
GET    /api/v1/jobs/search?q=&take=25                 → LegacyJob[]
GET    /api/v1/catalog?q=                             → CatalogItem[]
GET    /api/v1/technicians                            → { staffId, name, skillLevel }[]
```

`UpsertLine` = `{ catalogCode, quantity, unitPrice?, discountPercent, promotion, source, assignedTechnicianId?, note? }`
- `promotion`: `0` ไม่มี · `1` ลูกค้าประจำ −5% · `2` โปรเบรกครบชุด −300 · `3` ประกันคู่สัญญา −10%
- `source`: `"Customer"` (ลูกค้าขอ) · `"Technician"` (ช่างแนะนำ)

**สร้าง type ทั้งหมดจาก OpenAPI ที่ `http://localhost:5080/swagger/v1/swagger.json`** (API รันอยู่แล้ว)
ถ้าดึงไม่ได้ ให้เขียน type มือใน `src/api/types.ts` ตาม field ที่ระบุด้านล่าง

`Quotation` มี field: `id, code, version, status, statusLabelTh, jobId, jobNo, customer{name,phone,taxId,address}, vehicle{registration,model,vin,mileage}, branch{name,address,taxId,phone}, lines[], totals, approval, revisionReason, validUntil, isExpired, sentAt, createdByUserName, createdAt, lock`

`line` มี: `id, sequence, catalogCode, name, type("part"|"labor"), source("customer"|"technician"), quantity, unit, unitPrice, unitCost(อาจเป็น null), discountPercent, promotion, promotionLabel, assignedTechnicianId, assignedTechnicianName, note, standardHours, approvalStatus("pending"|"approved"|"rejected"), rejectReason, grossAmount, discountAmount, promotionAmount, netAmount, marginAmount(อาจ null)`

`totals` มี: `gross, lineDiscount, promotion, net, vatRate, vat, total, deposit, grandTotal, totalCost(อาจ null), marginAmount(null ได้), marginPercent(null ได้), partsNet, laborNet, laborHours, approved{approvedCount,rejectedCount,pendingCount,net,vat,total,grandTotal}`

> **สำคัญ:** field ที่เป็น `null` แปลว่า role ปัจจุบันไม่มีสิทธิ์เห็น (ต้นทุน/กำไร) — ให้**ซ่อนทั้งบล็อก** ไม่ใช่แสดง 0 หรือ `-`

---

## 4. หน้าจอที่ต้องทำ (3 หน้า)

### 4.1 `/quotations` — คิวใบเสนอราคา
- AppShell: sidebar 212px + topbar (ค้นหา · สาขา · ผู้ใช้)
- Filter chips: `ทั้งหมด · รอเสนอราคา · รออนุมัติ · ขอแก้ไข · อนุมัติแล้ว` → map เป็น `filter=`(ว่าง)`|todo|wait|rev|done`
- ตาราง (TanStack Table) คอลัมน์: **เลขที่ · งาน/ทะเบียน · ลูกค้า · สถานะ · ยอดสุทธิ · ค้างมานาน**
  - `เลขที่` = `code` + ป้ายเวอร์ชัน `v{version}`
  - `งาน/ทะเบียน` = สองบรรทัด: `jobNo` บนตัวหนา, `vehicleRegistration + vehicleModel` ล่างตัวเล็กสีจาง
  - `ยอดสุทธิ` ชิดขวา ใช้ฟอนต์ mono + tabular-nums
  - กดแถวไป `/quotations/{id}`
- ปุ่ม "สร้างใบเสนอราคา" มุมขวาบน → modal ค้นงาน (`/api/v1/jobs/search?q=`) เลือกงานแล้ว POST สร้าง แล้ว navigate ไปหน้าแก้ไข
- ต้องมี state ครบ: loading (skeleton) · empty (พร้อมปุ่มถัดไป) · error (แสดง `messageTh` + `traceId` + ปุ่มลองใหม่)

### 4.2 `/quotations/{id}/edit` — หน้าสร้าง/แก้ไข **3 พาเนล**
```
┌─ ซ้าย 280px ────┬─ กลาง flex ──────────────┬─ ขวา 320px ──┐
│ ค้นแคตตาล็อก    │ รายการในใบเสนอราคา        │ สรุปยอด      │
│ ผลค้นหา         │  · กลุ่ม "ลูกค้าขอ"       │ (sticky)     │
│ กดเพื่อเพิ่ม     │  · กลุ่ม "ช่างแนะนำ"      │ + คำเตือน    │
└─────────────────┴──────────────────────────┴──────────────┘
```
- **พาเนลซ้าย** — ช่องค้นหา (เริ่มค้นเมื่อพิมพ์ ≥2 ตัว) แสดงการ์ดผลลัพธ์: รหัส · ชื่อ · ความเข้ากันได้ · ราคา · **คงเหลือใช้ได้ (`available`)** — ถ้า `available <= 0` ให้ขึ้นป้ายส้ม "ของไม่พอ" + `etaNote`
- **พาเนลกลาง** — แยก 2 กลุ่มตาม `source` มีหัวกลุ่มบอกจำนวนและยอดรวมของกลุ่ม แต่ละบรรทัดแก้ได้ inline:
  จำนวน · ราคา/หน่วย · ส่วนลด % · โปรโมชัน (select) · ช่าง (select จาก `/technicians`) · หมายเหตุ · ปุ่มลบ
  - ใช้ CSS grid ที่ทุกคอลัมน์เป็น `minmax(0,1fr)` — **กันข้อความไทยดันล้น**
  - แก้แล้วเรียก PUT แบบ debounce ~400ms และ optimistic update
- **พาเนลขวา (sticky)** — สรุปยอดเรียงตามนี้:
  ```
  รวมก่อนส่วนลด        {gross}
  ส่วนลดรายบรรทัด      −{lineDiscount}
  โปรโมชัน             −{promotion}
  ─────────────────────────────
  ยอดก่อนภาษี          {net}
  ภาษีมูลค่าเพิ่ม 7%    {vat}
  ═════════════════════════════
  ยอดสุทธิ             {total}     ← เน้นบนพื้นเข้ม navy/900 ตัวขาว
  หักมัดจำ             −{deposit}
  คงเหลือชำระ          {grandTotal}
  ```
  ถ้า `totals.totalCost !== null` แสดงบล็อกต้นทุน/กำไรเพิ่ม (ต้นทุน · กำไรขั้นต้น บาท+%) — ถ้า `marginPercent < 15` พื้นเหลืองเตือน
- **แถบคำเตือน** — เรียก `GET /validate` ทุกครั้งที่ line เปลี่ยน แสดง `errors` (แดง) และ `warnings` (เหลือง) เป็นรายการ
- **ปุ่มล่างขวา** "ส่งให้ลูกค้าอนุมัติ" — **disable เมื่อ `isValid === false` พร้อมบอกเหตุผลว่าติดอะไร** (ห้าม disable เฉยๆ)
- ถ้า `status !== "draft"` → ทั้งหน้าเป็น read-only + แถบบนบอกเหตุผล + ปุ่ม "ออกฉบับแก้ไข" (เปิด modal กรอกเหตุผล → POST `/revise` → ไปเวอร์ชันใหม่)
- ถ้า `lock` ไม่ null และไม่ใช่ user ปัจจุบัน → แถบเตือน "{lock.userName} กำลังแก้ไขอยู่" + read-only

### 4.3 `/quotations/{id}/document` — **เอกสารใบเสนอราคา** (สำคัญที่สุด)
หน้ากระดาษ A4 พร้อมพิมพ์ — `@media print` ต้องออกมาสวย (ซ่อน sidebar/ปุ่มทั้งหมด, `@page { size: A4; margin: 12mm }`)

โครงเอกสารจากบนลงล่าง:
1. **หัวเอกสาร** — ชื่ออู่ (`branch.name`) ตัวใหญ่ · ที่อยู่ · โทร · เลขประจำตัวผู้เสียภาษี | ขวา: หัวข้อ **"ใบเสนอราคา"** + `code` + ป้ายเวอร์ชัน + ป้ายสถานะ
2. **แถบข้อมูล 2 คอลัมน์** — ซ้าย: ลูกค้า (ชื่อ/โทร/เลขผู้เสียภาษี/ที่อยู่) · ขวา: รถ (ทะเบียน/รุ่น/เลขตัวถัง/เลขไมล์) + เลขงาน + วันที่ออก + วันหมดอายุ
3. **ถ้าเป็นฉบับแก้ไข** (`revisionReason` ไม่ null) — แถบเหลืองบอกว่าเป็นฉบับแก้ไขและเหตุผล พร้อมข้อความ **"การอนุมัติในเวอร์ชันก่อนหน้าเป็นโมฆะ"**
4. **ตารางรายการ** — แยก 2 กลุ่ม (ลูกค้าขอ / ช่างแนะนำ) มีหัวกลุ่ม
   คอลัมน์: ลำดับ · รหัส · รายการ · จำนวน · หน่วย · ราคา/หน่วย · ส่วนลด · ยอดสุทธิ
   - ตัวเลขทุกช่องชิดขวา ฟอนต์ mono tabular-nums
   - ถ้ามีการอนุมัติแล้ว (`approval` ไม่ null) เพิ่มคอลัมน์ **ผลอนุมัติ** — อนุมัติ (เขียว ✓) / ไม่อนุมัติ (แดง ✕ + เหตุผลบรรทัดล่าง)
   - บรรทัดที่ไม่อนุมัติให้พื้นจาง + ขีดฆ่าตัวเลข
5. **สรุปยอด** ชิดขวา — โครงเดียวกับพาเนลขวาในหน้าแก้ไข (ไม่มีบล็อกต้นทุน/กำไรในเอกสารเด็ดขาด **แม้ role จะเห็นได้** — เอกสารนี้ให้ลูกค้า)
6. **ถ้ามี `approval`** — บล็อกลายเซ็น: รูปลายเซ็น (`signatureImagePath`) · ชื่อลูกค้า · วันเวลาที่เซ็น · พนักงานผู้รับรอง · ข้อความยินยอม · **"ลายเซ็นนี้ผูกกับใบเสนอราคาเวอร์ชันที่ {approval.quotationVersion}"**
7. **ท้ายเอกสาร** — ช่องเซ็น 2 ช่อง (ผู้เสนอราคา / ผู้อนุมัติ) + หมายเหตุเงื่อนไข + วันหมดอายุ
8. ปุ่ม (ซ่อนตอนพิมพ์): **พิมพ์** (`window.print()`) · กลับไปหน้าแก้ไข

---

## 5. สิ่งที่ต้องมีทุกหน้า (Definition of Done)

- [ ] ทุก state: loading · empty · error · forbidden — แต่ละอันมี **สาเหตุ + ปุ่มถัดไป + traceId**
- [ ] error จาก API ให้แสดง `error.messageTh` ตรงๆ (เป็นภาษาไทยอยู่แล้ว) **ห้ามแปลหรือแต่งใหม่**
- [ ] `focus-visible` ring 2px สี `#1B6BE3` ทุก interactive element
- [ ] `aria-label` ทุกปุ่มไอคอน
- [ ] focus trap ใน modal + ปิดด้วย Esc
- [ ] ตัวเลขเงินทุกที่ format `1,234.56` (2 ตำแหน่งเสมอ) ด้วย mono + tabular-nums
- [ ] ข้อความ UI ทั้งหมดเป็นภาษาไทย (โค้ด/ตัวแปรเป็นอังกฤษ)
- [ ] `pnpm build` และ `pnpm tsc --noEmit` ต้องผ่านไม่มี error

---

## 6. โครงไฟล์ที่คาดหวัง

```
web/
├── package.json  vite.config.ts  tsconfig.json  index.html
├── src/
│   ├── main.tsx  App.tsx  index.css
│   ├── api/          client.ts (fetch + header + envelope unwrap)  types.ts  quotations.ts  jobs.ts  catalog.ts
│   ├── components/   AppShell  StatusChip  StateBlock  MoneySummary  DataTable  ConfirmModal  Money
│   ├── features/quotations/  QueuePage  EditorPage  DocumentPage  CatalogPanel  LineEditor  WarningPanel
│   └── lib/          format.ts  tokens.ts
```

`client.ts` ต้องแกะ envelope: success → คืน `data`, ไม่ success → `throw` object ที่มี `code`/`messageTh`/`traceId` เพื่อให้ error boundary ใช้ได้

---

## 7. ห้ามทำ
- ห้ามแตะไฟล์นอก `web/`
- ห้ามใช้ Next.js, ห้ามใช้ CSS framework อื่นนอกจาก Tailwind
- ห้ามใส่ mock data ค้างไว้ในโค้ด production path (ถ้า API ไม่ตอบให้ขึ้น error state จริง)
- ห้ามแสดงต้นทุน/กำไรในหน้าเอกสาร (ข้อ 4.3)
- ห้ามใช้สีอย่างเดียวสื่อสถานะ
