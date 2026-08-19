# Spec: ปรับ web ให้ใช้ shadcn/ui + lucide (และเพิ่มหน้า login / เลือกสาขา-กะ)

ทำในโฟลเดอร์ `web/` เท่านั้น — **ห้ามแก้ไฟล์นอก `web/`**
โค้ดเดิมใช้งานได้อยู่แล้ว (คิว · editor 3 พาเนล · เอกสาร A4) งานนี้คือ **ยกเครื่อง UI ไม่ใช่เขียนใหม่ทั้งหมด** — พฤติกรรมและ business rule เดิมต้องอยู่ครบ

---

## 1. ติดตั้ง shadcn/ui + lucide

```bash
pnpm add lucide-react class-variance-authority clsx tailwind-merge tw-animate-css
pnpm dlx shadcn@latest init      # style: new-york · base color: slate
```

เพิ่ม component เท่าที่ใช้จริง:
```
button card table badge input select dialog sheet dropdown-menu
tabs separator skeleton alert tooltip sonner label textarea avatar scroll-area
```

**เปลี่ยนไอคอนทั้งหมดเป็น `lucide-react`** — ตอนนี้ยังมีตัวอักษร/สัญลักษณ์แทนไอคอนอยู่หลายที่ (`▣` `✓` `✕` `◐` `!` `ล` `ช`) ต้องหายไปให้หมด

แนะนำการจับคู่:
| ที่ใช้ | ไอคอน |
|---|---|
| สถานะ draft / sent / partial / approved / rejected / superseded / expired | `FileEdit` `Send` `MinusCircle` `CheckCircle2` `XCircle` `ArrowLeftRight` `Clock` |
| เมนู: ใบเสนอราคา · งานซ่อม · ลูกค้า · คลังอะไหล่ · รายงาน | `FileText` `Wrench` `Users` `Package` `BarChart3` |
| การกระทำ: พิมพ์ · สร้าง · ค้นหา · ลบ · กลับ · รีเฟรช | `Printer` `Plus` `Search` `Trash2` `ArrowLeft` `RefreshCw` |
| กลุ่มรายการ: ลูกค้าขอ / ช่างแนะนำ | `UserRound` / `Wrench` |
| เตือน / ผิดพลาด / ข้อมูล | `TriangleAlert` `CircleAlert` `Info` |

---

## 2. Design token ต้องคงเดิม (สำคัญ)

โทเคนมาจาก prototype ที่อนุมัติแล้ว — **shadcn ต้องถูกปรับให้เข้ากับโทเคนนี้ ไม่ใช่กลับกัน**

```
navy/900  #0F2440   navy/700  #1B3557
blue/600  #1B6BE3   blue/50   #E6EFFC
teal/500  #0E9F8C   amber/500 #E0930C
red/600   #C02626   orange/600 #C2410C
page bg   #EEF1F6   card #FFFFFF   border #E3E8EF
text #0F2440   muted #64748B   faint #94A3B8

radius: chip 6 · input 10 · card 14 · pill 999
font: 'Noto Sans Thai', 'IBM Plex Sans', system-ui
ตัวเลขเงิน: 'IBM Plex Mono' + font-variant-numeric: tabular-nums
sidebar 212px / ย่อ 68px ที่ 1024px · < 1024px ไม่รองรับ
```

ตั้ง CSS variable ของ shadcn (`--primary`, `--background`, `--border`, `--radius`, …) ให้ชี้มาที่ค่าข้างบน
**ห้ามใช้ชุดสีเริ่มต้นของ shadcn ทับ**

สีสถานะ (bg/fg) คงเดิมทุกค่า:
`draft` `#EEF1F5`/`#475569` · `sent` `#E6EFFC`/`#1552B3` · `partial` `#FDF3E2`/`#8A5A00` ·
`approved` `#E3F5F1`/`#0B6D5E` · `rejected` `#FBE9E9`/`#A31D1D` · `superseded` `#E4E9F0`/`#0F2440` · `expired` `#FBE9E9`/`#A31D1D`

**กฎที่ห้ามละเมิด:** ทุกป้ายสถานะต้องมี **สี + ไอคอน + ข้อความ** — ห้ามสื่อด้วยสีอย่างเดียว

---

## 3. หน้าใหม่ที่ต้องเพิ่ม: login + เลือกสาขา/กะ

API พร้อมแล้วที่ `http://localhost:5080` (ดู `/swagger`)

```
POST /api/v1/auth/login          { userName, password }
     → { accessToken, expiresAt, user, branches[], requiresShiftSelection }
GET  /api/v1/auth/branches/{branchId}/shifts
     → [{ shiftId, name, startTime, endTime, supervisorName, isCurrent }]
POST /api/v1/auth/shift-sessions { branchId, shiftId }
     → { sessionId, accessToken, expiresAt, user, branchId, branchName, shiftId, shiftName, openedAt }
POST /api/v1/auth/shift-sessions/{sessionId}/close
GET  /api/v1/auth/me
```

**สำคัญ:** `POST /auth/login` คืน token ขั้นแรกที่**ใช้เรียก API งานไม่ได้** ต้องเลือกสาขา+กะ
แล้วเอา `accessToken` จาก `/shift-sessions` มาใช้แทน

### 3.1 `/login`
- จอเต็ม พื้น navy/900 · การ์ดขาวกลางจอ
- ฟิลด์: **รหัสพนักงาน** (`userName`) + **รหัสผ่าน** · ปุ่ม "เข้าสู่ระบบ"
- ข้อความว่าเป็นระบบภายในของอู่
- error จาก API แสดง `messageTh` ตรงๆ (เช่น "รหัสพนักงานหรือรหัสผ่านไม่ถูกต้อง") + `traceId`
- ปุ่มต้องล็อกระหว่างส่ง กันกดซ้ำ

### 3.2 `/branch` — เลือกสาขาและกะ
- ขั้น 1: การ์ดรายสาขาจาก `branches[]` แสดงชื่อ · ที่อยู่ · **รอเสนอราคา N ใบ · รออนุมัติ N ใบ**
- ขั้น 2: เลือกกะ — แสดงชื่อกะ · เวลา · หัวหน้ากะ · กะที่ตรงกับเวลาปัจจุบันติดป้าย "กะปัจจุบัน"
- กด "เข้าใช้งาน" → `POST /shift-sessions` → เก็บ token → ไป `/quotations`
- ถ้ามีสาขาเดียวให้ข้ามไปเลือกกะเลย

### 3.3 เก็บ session
- เก็บ `accessToken` + ข้อมูล user/สาขา/กะ ใน `localStorage`
- `client.ts` ส่ง `Authorization: Bearer <token>` แทน header `X-User-*` ทั้งหมด
  (ยังต้องส่ง `X-Client-Source: web`)
- **ลบ `X-User-Name` ทิ้ง** — เคยทำให้ `fetch` พังเพราะภาษาไทยใน header
- 401 → เคลียร์ session แล้วเด้งไป `/login`
- route ทั้งหมดยกเว้น `/login` และ `/branch` ต้องมี guard
- topbar แสดงชื่อจริง + บทบาท + สาขา/กะ จาก session (เลิก hardcode "สาขาพระราม 9" / "ปวีณา เอกสาร")
- เมนูผู้ใช้: ปิดกะ (ซ่อนถ้า `user.canCloseShift === false`) · ออกจากระบบ

---

## 4. ปรับหน้าเดิมให้ใช้ shadcn (พฤติกรรมเดิมทั้งหมด)

### `/quotations`
- `Table` ของ shadcn + TanStack Table เดิม · `Badge` สำหรับสถานะ · `Button` · `Skeleton` ตอนโหลด
- filter chips → `Button variant="outline"` แบบ toggle หรือ `Tabs`
- modal สร้างใบ → `Dialog` (มี focus trap ในตัว)
- toast → `sonner`

### `/quotations/:id/edit`
- 3 พาเนลเดิม · `Card` ครอบแต่ละพาเนล
- แคตตาล็อกซ้าย: `Input` + `ScrollArea` · ของไม่พอใช้ `Badge` สีส้ม + ไอคอน
- บรรทัดกลาง: `Input`/`Select` ของ shadcn · **คง grid `minmax(0,1fr)` กันข้อความไทยดันล้น**
- พาเนลขวา sticky: ยอดสุทธิบนพื้น navy/900 ตัวขาว (เหมือนเดิม)
- แถบคำเตือน: `Alert` — error แดง / warning เหลือง
- ปุ่มส่ง disable ต้องบอกเหตุผลเสมอ (ใช้ `Tooltip` หรือข้อความใต้ปุ่ม) — **ห้าม disable เฉยๆ**

### `/quotations/:id/document`
- คงโครงเอกสารและ `@media print` เดิมไว้ทั้งหมด
- **ห้ามแสดงต้นทุน/กำไรในเอกสาร** แม้ role จะเห็นได้
- ถ้า `approval.signatureImagePath` มีค่า ให้แสดงรูปลายเซ็นจริงด้วย
  `<img src={`${API_BASE_URL}/api/v1/attachments/file?path=${encodeURIComponent(path)}`} />`
  (endpoint นี้พร้อมแล้ว — ถ้าโหลดไม่ได้ให้ fallback เป็นกรอบว่างพร้อมข้อความ ไม่ใช่รูปแตก)

---

## 5. บั๊กที่ต้องแก้ไปด้วย

1. `MoneySummary` แสดง "ภาษีมูลค่าเพิ่ม 0.07%" — API ส่ง `vatRate` เป็นสัดส่วน (0.07) ต้องคูณ 100
   *(แก้ไปแล้วบางส่วน — ตรวจให้แน่ใจว่าถูกทุกที่ที่แสดง vatRate)*
2. เอกสารของใบที่อนุมัติบางส่วนแสดง `ยอดสุทธิ` เป็นยอดเต็มใบ ทั้งที่ลูกค้าอนุมัติน้อยกว่า
   → เมื่อมี `totals.approved` ให้แสดง **สองบล็อก**: "ยอดตามใบเสนอราคา" และ "ยอดที่ลูกค้าอนุมัติ" (เน้นอันหลัง)

---

## 6. Definition of Done

- [ ] `pnpm tsc --noEmit` และ `pnpm build` ผ่าน ไม่มี error
- [ ] ไม่เหลือไอคอนที่เป็นตัวอักษร/สัญลักษณ์ — ใช้ lucide ทั้งหมด
- [ ] สีและ radius ตรงกับโทเคนใน §2 (ไม่ใช่ชุดเริ่มต้นของ shadcn)
- [ ] ป้ายสถานะทุกที่มี สี + ไอคอน + ข้อความ
- [ ] ทุก state: loading · empty · error · forbidden — มี **สาเหตุ + ปุ่มถัดไป + traceId**
- [ ] `focus-visible` ring 2px `#1B6BE3` ทุก interactive element · `aria-label` ทุกปุ่มไอคอน
- [ ] ตัวเลขเงินใช้ mono + tabular-nums ทุกที่ · format `1,234.56` เสมอ
- [ ] 1440px และ 1024px ถูกต้องทั้งคู่ · < 1024px ขึ้นข้อความแนะนำ
- [ ] ข้อความ UI เป็นภาษาไทยทั้งหมด (โค้ด/ตัวแปรเป็นอังกฤษ)
- [ ] login → เลือกสาขา/กะ → เข้าคิวใบเสนอราคาได้จริง โดยไม่มี header `X-User-*` เหลืออยู่
