import type { PlanInfo } from '../../api/types'
import type { TrialTone } from '../../lib/plan'
import type { SelfServeLimits, SelfServeStatus } from '../../api/selfserve'

/**
 * ตรรกะล้วนของ UX ทดลองใช้ (แถบ · เช็คลิสต์ · ลิมิต · ป้ายกุญแจ) — ไม่ import React/lucide และไม่ import "ค่า" จากโมดูลอื่น
 * (node --test โหลด .ts ตรงๆ แต่ resolve import ที่ไม่มีนามสกุลไม่ได้) จึงรับผลของ `lib/plan.ts` เป็นพารามิเตอร์แทน
 */

/** อู่ที่สมัครเอง (shard sp1) — ลูกค้าเดิมไม่มี plan หรือเป็น legacy = ไม่แสดงอะไรของทดลองใช้เลย */
export function isSelfServePlan(plan: PlanInfo | undefined): boolean {
  return Boolean(plan && plan.kind !== 'legacy')
}

export const LINE_OA_ID = '@garagepro'
export const LINE_OA_URL = 'https://line.me/R/ti/p/@garagepro'

// ---- แถบทดลองใช้ ----------------------------------------------------------------------------------

export type TrialBannerCopy = {
  title: string
  detail: string
}

/**
 * ข้อความของแถบทดลองใช้ตามโทน — `daysLabel` มาจาก `formatDaysLeft()` ของ lib/plan.ts (ข้อความวันเดียวกับทุกจุดในระบบ)
 * null = ไม่ต้องแสดงแถบ (ลูกค้าเดิม/แพ็กเกจเต็ม)
 */
export function trialBannerCopy(tone: TrialTone, daysLabel: string, suspended = false): TrialBannerCopy | null {
  switch (tone) {
    case 'none':
      return null
    case 'neutral':
      return {
        title: `ทดลองใช้ · ${daysLabel}`,
        detail: 'ใช้จ๊อบ ใบเสนอราคา และแอปช่างได้ครบ · เมนูที่มีกุญแจเปิดในแพ็กเกจเต็ม',
      }
    case 'warn':
      return {
        title: `ทดลองใช้ · ${daysLabel}`,
        detail: 'ใกล้ครบช่วงทดลองใช้ สมัครแพ็กเกจหรือขอต่ออายุก่อนระบบเป็นอ่านอย่างเดียว',
      }
    case 'expired':
      return suspended
        ? {
            title: 'บัญชีทดลองใช้ถูกระงับ ดูและพิมพ์ข้อมูลได้ แต่แก้ไขไม่ได้',
            detail: `ติดต่อทีมงานทาง LINE ${LINE_OA_ID} เพื่อขอเปิดใช้งานอีกครั้ง`,
          }
        : {
            title: 'ช่วงทดลองใช้ครบ 30 วันแล้ว ดูและพิมพ์ข้อมูลได้ แต่แก้ไขไม่ได้',
            detail: 'ข้อมูลของอู่ยังอยู่ครบ ขอต่ออายุหรือสมัครแพ็กเกจได้เลย',
          }
  }
}

function parseInstant(value: string): number {
  // ค่าจาก API ที่ไม่มีโซนเวลาคือ UTC เสมอ (CLAUDE.md เรื่อง DateTimeKind)
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/.test(value)
  return Date.parse(hasZone ? value : `${value}Z`)
}

/** คำขอต่ออายุส่งได้วันละครั้ง (backend ตอบ 409 ถ้าซ้ำ) — ภายใน 24 ชม. ถือว่าส่งแล้ว ปุ่มปิดพร้อมเหตุผล */
export function isExtensionRecentlyRequested(requestedAt: string | null | undefined, now: Date = new Date()): boolean {
  if (!requestedAt) return false
  const at = parseInstant(requestedAt)
  if (Number.isNaN(at)) return false
  return now.getTime() - at < 24 * 60 * 60 * 1000
}

export const EXTENSION_REQUESTED_REASON = 'ส่งคำขอแล้ว ทีมงานจะติดต่อกลับ'

/** หมายเหตุที่ส่งไปกับคำขอเมื่อกด "สนใจฟีเจอร์นี้" บนหน้าฟีเจอร์ที่ล็อก (ทีมขายเห็นใน LINE) */
export function featureInterestNote(featureLabel: string): string {
  return `สนใจฟีเจอร์: ${featureLabel}`
}

/** aria-label ของเมนูที่ล็อก — ต้องอ่านออกเสียงได้ว่าล็อก ไม่ใช่แค่เห็นไอคอน */
export function lockedNavLabel(label: string): string {
  return `${label} (ล็อกในแพ็กเกจทดลองใช้)`
}

/** เหตุผลของปุ่มที่ปิดเพราะแพ็กเกจ (เช่น ส่งออก Excel/CSV) — แสดงเป็นข้อความจริงข้างปุ่ม ไม่ใช่ tooltip อย่างเดียว */
export function lockedActionReason(featureLabel: string): string {
  return `${featureLabel} เปิดในแพ็กเกจเต็ม`
}

// ---- สถานะ self-serve (GET /api/v1/selfserve/status) ----------------------------------------------

export type ChecklistAction =
  | { kind: 'link'; to: string; label: string }
  | { kind: 'createJob'; label: string }
  | { kind: 'mobileApp'; label: string }

export type ChecklistItem = {
  key: 'staff' | 'catalog' | 'job' | 'quotation' | 'mobile'
  label: string
  hint: string
  done: boolean
  action: ChecklistAction
}

function count(value: number | undefined): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0
}

/**
 * เช็คลิสต์เริ่มต้น 5 ข้อ (docs/12 §4.3) — ติ๊กจากข้อมูลจริงเสมอ ผู้ใช้ไม่ต้องติ๊กเอง
 * · พนักงาน > 1 เพราะเจ้าของอู่นับเป็นพนักงานคนแรกอยู่แล้ว
 * · ฟิลด์ที่ server ไม่ส่ง (ถูกตัดเพราะ null) ถือว่ายังไม่ได้ทำ
 */
export function deriveChecklist(
  status: Pick<SelfServeStatus, 'checklist' | 'sampleDataLoadedAt'> | null | undefined,
): ChecklistItem[] {
  const c = status?.checklist ?? {}
  const jobCount = count(c.jobCount)
  return [
    {
      key: 'staff',
      label: 'เพิ่มช่างคนแรก',
      hint: 'ใส่ชื่อและบทบาท ระบบสร้างรหัสเข้าแอปมือถือให้ทันที',
      done: count(c.staffCount) > 1,
      action: { kind: 'link', to: '/staffs', label: 'ไปทำ' },
    },
    {
      key: 'catalog',
      label: 'ตรวจรายการสินค้า/บริการ',
      hint: 'ค่าแรงและอะไหล่พร้อมราคา ที่ดึงไปใส่ในใบเสนอราคา',
      done: count(c.catalogCount) > 0,
      action: { kind: 'link', to: '/products', label: 'ไปทำ' },
    },
    {
      key: 'job',
      label: 'เปิดจ๊อบแรก',
      hint: status?.sampleDataLoadedAt
        ? 'ลองกับลูกค้าตัวอย่าง "คุณวิชัย มาใหม่" ที่ยังไม่มีงานค้าง แล้วรับรถพร้อมเช็คลิสต์สภาพรถ'
        : 'เลือกลูกค้าและรถ แล้วรับรถพร้อมเช็คลิสต์สภาพรถ',
      done: jobCount > 0,
      action: { kind: 'createJob', label: 'ไปทำ' },
    },
    {
      key: 'quotation',
      label: 'ส่งใบเสนอราคาแรก',
      hint: jobCount > 0
        ? 'เปิดการ์ดจ๊อบ → ขั้นเสนอราคา แล้วกด "ดำเนินการแทนลูกค้า" เพื่อบันทึกการอนุมัติได้'
        : 'เปิดจ๊อบก่อน แล้วสร้างใบเสนอราคาจากการ์ดจ๊อบ',
      done: count(c.quotationSentCount) > 0,
      action: jobCount > 0
        ? { kind: 'link', to: '/jobs?view=board', label: 'ไปทำ' }
        : { kind: 'createJob', label: 'ไปทำ' },
    },
    {
      key: 'mobile',
      label: 'ให้ช่างเข้าแอปมือถือ',
      hint: 'ติดตั้งแอป ServicePro จาก Google Play แล้วเข้าด้วยรหัสช่าง ลองกด "เริ่มงาน (จับเวลา)"',
      done: c.mobileLoginSeen === true,
      action: { kind: 'mobileApp', label: 'ติดตั้งแอป' },
    },
  ]
}

export function checklistProgress(items: ChecklistItem[]): { done: number; total: number } {
  return { done: items.filter((item) => item.done).length, total: items.length }
}

const LIMIT_LABELS: ReadonlyArray<[keyof SelfServeLimits, string]> = [
  ['staff', 'พนักงาน'],
  ['catalog', 'สินค้า'],
  ['customers', 'ลูกค้า'],
  ['jobs', 'จ๊อบ'],
]

export type LimitUsageItem = { key: keyof SelfServeLimits; label: string; used: number; max: number; full: boolean }

export function limitUsageItems(limits: SelfServeLimits | null | undefined): LimitUsageItem[] {
  if (!limits) return []
  return LIMIT_LABELS.flatMap(([key, label]) => {
    const usage = limits[key]
    if (!usage || typeof usage.max !== 'number' || !Number.isFinite(usage.max)) return []
    const used = count(usage.used)
    return [{ key, label, used, max: usage.max, full: used >= usage.max }]
  })
}

const numberFormat = new Intl.NumberFormat('th-TH')

/** "พนักงาน 2/5 · สินค้า 42/200 · ลูกค้า 2/200 · จ๊อบ 2/100" — ว่างเมื่อ server ไม่ส่งลิมิต */
export function formatLimitUsage(limits: SelfServeLimits | null | undefined): string {
  return limitUsageItems(limits)
    .map((item) => `${item.label} ${numberFormat.format(item.used)}/${numberFormat.format(item.max)}`)
    .join(' · ')
}

// ---- การ์ดต้อนรับ + รหัสช่างตัวอย่าง -------------------------------------------------------------

export function welcomeDismissKey(workspaceNo: string | number): string {
  return `garagepro.web.welcomeDismissed.${workspaceNo}`
}

/** sessionStorage ที่หน้าสมัครฝากรหัสช่างตัวอย่างไว้ก่อนพามาที่ /jobs?welcome=1 */
export const SAMPLE_CREDENTIALS_KEY = 'garagepro.web.sampleCredentials'

export type SampleCredentials = {
  workspaceNo: string
  technicians: { userName: string; password: string }[]
}

/** อ่านค่าที่หน้าสมัครฝากไว้แบบไม่เชื่อรูปแบบ — เสีย/ว่าง/ไม่มีช่าง = null */
export function parseSampleCredentials(raw: string | null | undefined): SampleCredentials | null {
  if (!raw) return null
  try {
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object') return null
    const record = value as { workspaceNo?: unknown; technicians?: unknown }
    if (!Array.isArray(record.technicians)) return null
    const technicians = record.technicians.flatMap((item: unknown) => {
      if (!item || typeof item !== 'object') return []
      const tech = item as { userName?: unknown; password?: unknown }
      return typeof tech.userName === 'string' && tech.userName && typeof tech.password === 'string' && tech.password
        ? [{ userName: tech.userName, password: tech.password }]
        : []
    })
    if (!technicians.length) return null
    const workspaceNo = typeof record.workspaceNo === 'string' || typeof record.workspaceNo === 'number'
      ? String(record.workspaceNo)
      : ''
    return { workspaceNo, technicians }
  } catch {
    return null
  }
}

/** รหัสของอู่อื่น (เช่น สมัครสองอู่ในแท็บเดียว) ห้ามโชว์ — ไม่รู้ workspace ฝั่งใดฝั่งหนึ่งถือว่าตรงกัน */
export function credentialsBelongTo(credentials: SampleCredentials, workspaceNo: string | number | undefined): boolean {
  if (!credentials.workspaceNo || workspaceNo === undefined || workspaceNo === '') return true
  return credentials.workspaceNo === String(workspaceNo)
}

// ---- พนักงาน (โหมดง่ายของอู่ที่สมัครเอง) -----------------------------------------------------------

export type StaffRoleValue = 'Technician' | 'Lead' | 'FrontDesk' | 'Office' | 'Cashier' | 'Manager'

/** ชื่อ enum ตรงกับ `UserRole` ของ backend — ช่างมาก่อนเพราะเป็นบทบาทที่เพิ่มบ่อยที่สุดตอนเริ่มใช้ */
export const STAFF_ROLE_OPTIONS: ReadonlyArray<{ value: StaffRoleValue; label: string; hint: string }> = [
  { value: 'Technician', label: 'ช่าง', hint: 'ใช้แอปมือถือ ดูคิวงาน จับเวลา ถ่ายรูป' },
  { value: 'Lead', label: 'หัวหน้าช่าง', hint: 'ดูแลงานซ่อมและตรวจเวลาทำงานของช่าง' },
  { value: 'FrontDesk', label: 'หน้าร้าน', hint: 'รับรถ เปิดจ๊อบ และส่งมอบรถ' },
  { value: 'Office', label: 'ธุรการ', hint: 'ใบเสนอราคา เอกสาร และข้อมูลหลัก' },
  { value: 'Cashier', label: 'แคชเชียร์', hint: 'รับชำระเงินและออกใบเสร็จ' },
  { value: 'Manager', label: 'ผู้จัดการ', hint: 'เห็นต้นทุน/กำไร และจัดการได้ทุกอย่าง' },
]
