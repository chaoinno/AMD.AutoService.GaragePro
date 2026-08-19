# 04 · แผนสร้าง 3 Project

> อ่านคู่กับ [01-workflow.md](01-workflow.md) · [02-domain-model.md](02-domain-model.md) · [03-api-contract.md](03-api-contract.md)

---

## 1. Repo Layout (เสนอ — ตาม pattern ของ `AMD.GaragePro.Admin`)

```
AMD.AutoService.GaragePro/
├── AMD.AutoService.GaragePro.sln
├── docs/                              ← เอกสารชุดนี้
├── GaragePro Service Ops Design/      ← prototype (source of truth ของ UI)
├── backend/
│   ├── AMD.AutoService.GaragePro.API/          .NET 8 Web API
│   ├── AMD.AutoService.GaragePro.Domain/       entities + enums + state machine
│   ├── AMD.AutoService.GaragePro.Application/  services + validators + rules
│   ├── AMD.AutoService.GaragePro.Infrastructure/ EF Core + Dapper + storage + SignalR
│   └── AMD.AutoService.GaragePro.API.Tests/
├── mobile/                            Flutter (frontdesk + technician + QC)
├── web/                               React (manager / office / cashier)
├── shared/
│   └── contracts/                     OpenAPI spec → codegen ทั้ง Flutter + React
├── docker-compose.yml
└── nginx/
```

**เหตุผลเลือก mono-repo:** enum/status token 10 ตัว + error code + design token ต้องตรงกัน 3 ที่ · แยก repo แล้วจะ drift ทันที · ทีม `AMD.GaragePro.Admin` ใช้ layout `backend/ + frontend/ + sln` อยู่แล้ว ต่อยอดได้เลย

---

## 2. Stack แต่ละ Project

### 2.1 API — .NET 8 (`backend/`)
ยืมชุด package ที่ทีมใช้อยู่แล้วใน `AMD.GaragePro.Admin` เพื่อไม่ต้องเรียนใหม่:

| ชั้น | เลือกใช้ | เหตุผลจาก design |
|---|---|---|
| Web | ASP.NET Core 8 + `Asp.Versioning.Mvc` 8.1 | ตาม pattern เดิม (`/api/v1`) |
| ORM | EF Core 8 (write) + **Dapper** (read/report) | รายงาน 6 แท็บ + drill-down ทุกตัวเลข = query หนัก EF ไม่เหมาะ |
| DB | MSSQL | ตาม stack องค์กร |
| Auth | JWT Bearer + BCrypt | ตาม pattern เดิม |
| Validation | **FluentValidation** | business rule 15 invariant + guard ต่อ transition |
| Realtime | **SignalR** (`/hubs/ops`) | Open Question #2 — .NET native, ประหยัดเวลาที่สุด |
| Docs | Swashbuckle → **OpenAPI เป็น contract กลาง** | codegen client ทั้ง Flutter + React |
| Mapping | Mapster (เร็วกว่า AutoMapper) | field-level security ตาม role |
| Storage | FluentFTP หรือ S3-compatible | ~25–30 ไฟล์/งาน (รูป+ลายเซ็น) |
| Background | Hangfire หรือ BackgroundService | export รายงาน + QR payment status polling + reminder เกินกำหนด |
| Test | xUnit + Testcontainers(MSSQL) | state machine + 15 invariant ต้องมี test |

**สิ่งที่ต้องทำเป็นชิ้นแรกสุด (ไม่ใช่ CRUD):**
1. `JobStateMachine` — transition table 12 เส้นทาง + guard เป็นโค้ดที่ test ได้ ไม่กระจายอยู่ใน controller
2. `ActivityEventInterceptor` — เขียน audit ทุก state change พร้อม `source` อัตโนมัติ
3. `DestructiveActionFilter` — บังคับ `reason` + `approvedBy` ก่อนถึง handler
4. `RoleFieldSerializer` — strip `cost`/`margin`/`commission` ตาม role **ที่ server**
5. `IdempotencyMiddleware` — `Idempotency-Key` (payment) + `clientLocalId` (sync)

### 2.2 Flutter App (`mobile/`)
เป้าหมาย: iPhone 15 Pro Max 430×932 · รองรับ Android ≥360px

| เรื่อง | เลือกใช้ | เหตุผลจาก design |
|---|---|---|
| State | **Riverpod 2** | offline queue + realtime + optimistic update ซ้อนกัน |
| Routing | `go_router` | 15 route มี deep link จาก notification + QR |
| Local DB | **Drift (SQLite)** | queue ต้อง query ได้ (จัดกลุ่มตามงาน, นับรูป, นับ bytes, retry) — Hive ไม่พอ |
| HTTP | `dio` + interceptor (auth/retry/offline) | |
| Codegen | `openapi-generator` หรือ `retrofit` + `freezed` | contract จาก OpenAPI |
| รูปภาพ | `camera` + `image` (compress ก่อน queue) | 25–30 ไฟล์/งาน ต้องบีบก่อนเก็บ |
| ลายเซ็น | `signature` package | 2 จุด: รับรถ + อนุมัติราคา (+ ส่งมอบเมื่อออกแบบเสร็จ) |
| QR | `mobile_scanner` | + **[GAP]** state สิทธิ์กล้องถูกปฏิเสธ |
| Push | FCM + APNs | ทีมมี cert อยู่แล้ว (`iOS-Cert/`, `AMD.VehicleUserApp.GaragePro/APNs/`) |
| Test | `flutter_test` + `patrol` (E2E) | |

**สถาปัตยกรรมบังคับ — offline-first ไม่ใช่ option:**
```
UI → Riverpod notifier → Repository
                          ├─ Drift (local, source of truth ระหว่างออฟไลน์)
                          └─ SyncQueue → dio → /sync/batch
```
- ทุก write เขียน Drift ก่อน แล้ว enqueue — UI ไม่รอ network
- `TMP-xxxx` แสดงพร้อมป้าย "รอซิงก์" · เมื่อได้ `serverId` แสดงการแทนที่ TMP → เลขจริง
- 5 สถานะซิงก์ (`local/syncing/synced/failed/conflict`) ต้องเห็นทุกหน้าที่มีข้อมูลค้าง
- `/reference-data` cache ทั้งหมด (template ตรวจ 31 รายการ, QC 6 ข้อ, เหตุผล dropdown ทุกชุด) — ตรวจเช็คทำงานได้ 100% ออฟไลน์

### 2.3 React Web (`web/`)
เป้าหมาย: desktop 1440×940 (หลัก) · 1024px (ย่อ) · **< 1024px ไม่รองรับ**

| เรื่อง | เลือกใช้ | เหตุผลจาก design |
|---|---|---|
| Build | **Vite + React 19 + TS** | เป็น internal tool หลัง login ล้วน — ไม่มี SEO/SSR ให้ต้องใช้ Next.js · เบากว่า เร็วกว่า deploy ง่ายกว่า |
| Routing | `react-router` 7 | 18 route |
| Data | **TanStack Query** | คิวงานอัปเดตสด + saved views + optimistic + invalidate จาก SignalR event |
| Realtime | `@microsoft/signalr` | เชื่อม event → `queryClient.invalidateQueries` |
| Table | **TanStack Table** | DataTable: sort/paging/saved view/แถวไฮไลต์สด/ซ่อนคอลัมน์ที่ 1024px |
| Form | `react-hook-form` + `zod` | quotation editor 3 พาเนล, recon, PO, GRN |
| UI | Tailwind + Radix UI | ตรงกับ `garage-pro-chatbot/apps/web` ที่ทีมใช้อยู่ |
| Chart | Recharts | **[UI]** ทุกกราฟต้องมีตารางเทียบเท่า → wrap เป็น `ChartWithTable` ตัวเดียว |
| Test | Vitest + Playwright | |

> **ถ้าอยากใช้ Next.js** (มี `AMD.NextJs.GaragePro` อยู่แล้ว) ก็ได้ — แต่จะได้แค่ App Router + client component ทั้งหมด ไม่ได้ประโยชน์จาก SSR เลย และเพิ่มความซับซ้อนของ build/deploy ผมแนะนำ Vite SPA

---

## 3. Shared Contract Layer — งานที่ต้องทำก่อนใครแตะโค้ด client

ที่ต้องตรงกัน 3 ที่ ห้าม copy มือ:

| สิ่งที่ share | วิธี |
|---|---|
| 10 JobStatus + enum อื่นทั้งหมด (§ enums doc 02) | นิยามที่ Domain → OpenAPI → codegen Dart + TS |
| Error code ทุกตัว | enum ที่ API + ตาราง code→ข้อความไทย ที่ client |
| Design token (สี/space/radius/type/status color) | `shared/tokens.json` → gen `tokens.dart` + `tailwind.config.js` |
| Status → สี/ไอคอน/ข้อความ 10 ตัว | gen จาก tokens.json ทั้ง 2 client |
| Component contract 20 ตัว | ชื่อเดียวกันทั้ง Flutter widget และ React component |

**CI gate:** OpenAPI เปลี่ยน → regenerate client → build ทั้ง 3 · ถ้า client compile ไม่ผ่าน = API break contract

---

## 4. ลำดับงาน — ตัดเป็น Vertical Slice ตาม Lifecycle (ไม่ใช่ตาม layer)

หลักการ: **แต่ละ phase เดินได้จริงข้ามทั้ง 3 project** ใช้ 13 ขั้นของ Demo เป็น acceptance test เพิ่มขึ้นทีละขั้น — ไม่ใช่ "ทำ API ให้เสร็จก่อนแล้วค่อยทำ UI"

| Phase | ขอบเขต | Demo ขั้นที่ | API | Flutter | React |
|---|---|---|---|---|---|
| **P0 · Foundation** | solution scaffold · auth · branch/shift · state machine engine · activity log · error envelope · design token pipeline · CI/codegen | – | ✅ แกน 5 ชิ้น | login, shift, home shell, bottom nav | login, branch, AppShell, StateBlock |
| **P1 · รับรถ + ตรวจเช็ค** | intake 6 ขั้น (draft ทุกขั้น) · inspection 31 รายการ · upload รูป/ลายเซ็น · **offline queue ตัวจริง** | 1–3 | jobs, intake drafts, inspection, attachments, `/sync/batch` | `/intake/*`, `/inspection`, `/sync`, `/scan` | `/jobs`, `/jobs/board`, `/jobs/:id` แท็บ ภาพรวม+ตรวจเช็ค+กิจกรรม |
| **P2 · เสนอราคา + อนุมัติ** | quotation + **version/superseded** · line editor · catalog · warnings · ลายเซ็นผูก version | 4–6 | quotation ทั้งชุด + discount approval | `/approval/:id` + SignaturePad | `/quotations`, `/:id/edit` 3 พาเนล, `/approval`, `/revision` |
| **P3 · ซ่อม + QC** | repair task · จับเวลา · รูปก่อน/หลัง · แจ้งรออะไหล่ · งานเพิ่ม · QC 6 ข้อ + ตีกลับ | 7, 10, 11 | repair, qc | `/repair/:id`, `/qc/:id`, `/jobs/mine` | แท็บ ซ่อม + QC |
| **P4 · คลัง + จัดซื้อ** | `available = onHand − reserved` · ledger · adjust · PO 7 สถานะ · GRN รับบางส่วน/ชำรุด | 8–9 | inventory, purchasing | แจ้งเตือน "อะไหล่ถึงแล้ว" | `/inventory`, `/purchasing` |
| **P5 · POS + เอกสาร** | recon 5 สถานะ · 4 ช่องทาง + split · idempotency · `checking` state · RC/IV · reprint/void/refund · ปิดงาน | 12–13 | pos, documents | – | `/pos`, `/checkout`, `/document`, `/audit`, `/dashboard/cashier` |
| **P6 · Realtime + Dashboard** | SignalR hub + 11 event · 9 KPI · กระดานสด · notification | ทุกขั้น | `/hubs/ops` + polling fallback | notifications + push | `/dashboard`, live rows, toast, badge |
| **P7 · รายงาน** | 6 แท็บ · 8 KPI สูตรตายตัว · drill-down ทุกตัวเลข · export + สถานะไฟล์ · field-level security | – | reports (Dapper) | – | `/reports` + `ChartWithTable` |
| **P8 · ปิด GAP** | **หน้าส่งมอบรถ + ลายเซ็นรับคืน** (สูง) · ใบกำกับนิติบุคคล/วางบิล (สูง) · `/settings` + permission matrix (กลาง) · สิทธิ์กล้อง (กลาง) | 13 | handover, billing mode, settings | `/handover/:id` | `/settings`, billing mode |
| **P9 · Hardening** | a11y (tab order, focus ring, focus trap, aria-label, ปุ่มลัด POS F2–F5/⌘K) · ไทย 200% · contrast AA · conflict resolution · load test · document lock | – | ✅ | ✅ | ✅ |

**Gate ของแต่ละ phase:** demo ขั้นที่ระบุต้องเดินผ่านจริงบนเครื่องจริง 2 เครื่อง (มือถือ + เว็บ) และตัวเลข/เวลา/สถานะตรงกันทุกจอ

---

## 5. Critical Path & ความเสี่ยง

### เรียงตามความเสี่ยง (ทำ spike ก่อนตัดสินใจ)

| # | เรื่อง | ทำไมเสี่ยง | ลดความเสี่ยงยังไง |
|---|---|---|---|
| 1 | **Offline sync + conflict** | เป็นงานที่ประเมินพลาดบ่อยที่สุด · design กำหนดไว้ละเอียด (5 state, TMP→real, conflict UI) แต่**นโยบายใครชนะยังไม่มี** (OQ#3) | spike ที่ P1 ให้เสร็จจริง อย่าเลื่อนไป P9 · ต้องได้คำตอบ OQ#3 ก่อน P1 |
| 2 | **Quotation versioning** | approval + ลายเซ็นผูก version · ออกเวอร์ชันใหม่ล้างการอนุมัติทั้งชุด · งานเพิ่มระหว่างซ่อมวนกลับมาที่นี่ | model version-first ตั้งแต่ต้น ห้าม "เดี๋ยวเติม version ทีหลัง" |
| 3 | **Master data ลูกค้า/รถ** (OQ#1) | ถ้าเป็น read-through จาก GaragePro เดิม → schema + intake flow เปลี่ยนหมด | **ต้องได้คำตอบก่อน P1** — เป็น blocker ตัวเดียวที่หยุดงานได้จริง |
| 4 | **ปริมาณรูป** | ~25–30 ไฟล์/งาน × งานต่อวัน × 3 สาขา · มือถือถ่ายตอนสัญญาณแย่ | compress ที่ client + chunked upload + วัด storage จริงที่ P1 |
| 5 | **Payment idempotency + `checking`** | เก็บเงินซ้ำ = ปัญหาเงินจริง | idempotency key + test เคส QR timeout / card decline ก่อน P5 จะปิด |
| 6 | **Report performance** | drill-down ทุกตัวเลข · turnaround "ไม่รวมช่วงรออะไหล่" = ต้องคำนวณจาก event log | Dapper + index จาก ActivityEvent ตั้งแต่ P0 · พิจารณา read model ถ้าช้า |

### Blocker ที่หยุดงานได้จริง (ต้องเคลียร์ก่อน)
- **OQ#1** master data → กระทบ P1 (schema + intake)
- **OQ#3** conflict policy → กระทบ P1 (sync)
- **OQ#2** SignalR vs polling → กระทบ P6 แต่ตัดสินใจได้เลย (แนะนำ SignalR)
- **OQ#5, #6, #7, #8** (วงเงิน PO / ใบกำกับ / คืนเงิน / คอมมิชชัน) → กระทบ P4, P5, P7 ยังมีเวลา แต่ตั้งเป็น config ไว้ก่อน **อย่า hard-code**

> ค่า **[ASSUME]** ทั้ง 5 ตัว (ส่วนลด >10% · margin <15% · PO >10,000 · SLA 90% · คอมมิชชัน 8/10/12%) ให้ทำเป็น **config table ในฐานข้อมูล** ตั้งแต่วันแรก — ต้นแบบบอกชัดว่าเป็นค่าสาธิต ไม่ใช่ค่าที่องค์กรอนุมัติ

---

## 6. Definition of Done ต่อ Slice

ทุก feature ปิดได้เมื่อ:
- [ ] API มี guard ตาม transition table + FluentValidation + unit test ครอบ invariant ที่เกี่ยว
- [ ] `ActivityEvent` ถูกเขียนพร้อม `source` ถูกต้อง
- [ ] Permission ถูก enforce **ที่ server** (ไม่ใช่ซ่อนปุ่มที่ client) — 403 คืน StateBlock payload
- [ ] Field-level security: `cost`/`margin`/`commission` ไม่หลุดไป role ที่ไม่มีสิทธิ์
- [ ] ทุก state ครบ: loading · empty · error · forbidden · stale · offline — แต่ละอันมี **สาเหตุ + ปุ่มถัดไป + traceId**
- [ ] สถานะสื่อด้วย **สี + ไอคอน + ข้อความ** (ไม่มีจุดที่ใช้สีเดียว)
- [ ] Mobile: touch ≥48px · CTA 54–56px · sticky bar + draft save ในฟอร์มยาว · ทดสอบไทย 200%
- [ ] Mobile: ทำงานได้ตอนออฟไลน์ (ถ้าเป็น flow ที่ design ระบุว่าต้องได้) + queue มองเห็น
- [ ] Web: tab order · focus ring 2px · focus trap ใน modal/drawer · aria-label ปุ่มไอคอน
- [ ] Web: 1440 และ 1024 ถูกต้องทั้งคู่
- [ ] Destructive action มี ConfirmSheet + เหตุผลบังคับ + บันทึกผู้ทำ/ผู้อนุมัติ
- [ ] Demo ขั้นที่เกี่ยวข้องเดินผ่านบน 2 เครื่องจริง ตัวเลขตรงกัน

---

## 7. Next Steps

1. **ตอบ Open Question #1 และ #3** — เป็น blocker ตัวจริงของ P1
2. ยืนยัน 3 ข้อในหัวข้อ "สิ่งที่ต้องตัดสินใจ" (repo layout · Vite vs Next.js · SignalR)
3. เขียน **OpenAPI spec** ของ P0+P1 ให้จบก่อน แล้ว codegen — client 2 ตัวเริ่มขนานกันได้ทันที
4. Scaffold solution 3 project + CI (build ทั้ง 3 + regenerate client + gate contract)
5. Spike **offline sync** ด้วย flow ตรวจเช็ค 31 รายการ + รูป (เคสหนักสุด) — วัดเวลาจริงก่อนประเมิน P1
