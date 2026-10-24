import type { PlanInfo } from '../api/types'

/**
 * ทะเบียนฟีเจอร์ที่แพ็กเกจล็อกได้ — key ต้องตรงกับสตริงที่ backend ส่งใน `plan.features[]`
 * (`PlanFeature` flags → camelCase) · pure TS ไม่ import React/lucide เพื่อให้ `node --test` import ได้
 */
export type FeatureKey =
  | 'purchasing'
  | 'inventory'
  | 'pos'
  | 'taxInvoice'
  | 'advancedReports'
  | 'export'
  | 'multiBranch'

export type FeatureInfo = {
  key: FeatureKey
  /** ชื่อสั้นสำหรับเมนู/ป้ายกุญแจ */
  label: string
  /** คำอธิบายหนึ่งประโยคสำหรับหน้า "ฟีเจอร์นี้เปิดในแพ็กเกจเต็ม" */
  description: string
  bullets: string[]
  /** path บนเว็บที่ทั้งหน้าเป็นของฟีเจอร์นี้ (จับคู่แบบ prefix) — ว่าง = ล็อกเฉพาะปุ่ม ไม่ใช่ทั้งหน้า */
  paths: string[]
}

// [BIZ] ข้อความต้องเป็นความสามารถที่มีจริงของระบบเท่านั้น (กฎเดียวกับหน้าขาย) — ห้ามแต่งเติมสิ่งที่ยังไม่ได้ทำ
export const FEATURES: Record<FeatureKey, FeatureInfo> = {
  purchasing: {
    key: 'purchasing',
    label: 'จัดซื้อ',
    description: 'ใบขอซื้อ ใบสั่งซื้อ รับสินค้า และทะเบียนซัพพลายเออร์ ในระบบเดียวกับจ๊อบ',
    bullets: [
      'ใบขอซื้อ (PR) → ใบสั่งซื้อ (PO) → รับสินค้า (GRN) พร้อมด่านอนุมัติตามวงเงิน',
      'ทะเบียนซัพพลายเออร์และซัพพลายเออร์ประจำของแต่ละสินค้า',
      'แจ้งเตือนเอกสารรออนุมัติถึงผู้จัดการทั้งบนเว็บและแอป',
    ],
    paths: ['/purchasing', '/purchasing/pr', '/purchasing/po', '/suppliers'],
  },
  inventory: {
    key: 'inventory',
    label: 'สต็อกสินค้า',
    description: 'สต็อกตามล็อตแบบ FIFO ใบเบิกสินค้าผูกกับจ๊อบ และคลังสินค้าหลายแห่ง',
    bullets: [
      'ยอดคงเหลือ/จอง/พร้อมใช้ ต่อคลัง ต่อล็อต ตามต้นทุนจริง (FIFO)',
      'ใบเบิกสินค้าตามรายการที่ลูกค้าอนุมัติในใบเสนอราคา',
      'ยอดยกมาและประวัติความเคลื่อนไหวของสต็อก',
    ],
    paths: ['/inventory', '/warehouses'],
  },
  pos: {
    key: 'pos',
    label: 'ขายหน้าร้าน (POS)',
    description: 'ขายสินค้าหน้าร้านตัดสต็อกทันที พร้อมส่วนลด โปรโมชัน และใบเสร็จ',
    bullets: [
      'บิลขายหน้าร้านตัดสต็อก FIFO และยกเลิกบิลคืนล็อตเดิมได้',
      'ส่วนลดและโปรโมชันต่อบิล',
      'พิมพ์ใบเสร็จทั้ง A4 และกระดาษม้วน 80/58 มม.',
    ],
    paths: ['/sales', '/promotions'],
  },
  taxInvoice: {
    key: 'taxInvoice',
    label: 'ใบกำกับภาษีเต็มรูป',
    description: 'ออกใบกำกับภาษีเต็มรูปจากใบเสร็จของจ๊อบ ในนามบุคคลหรือบริษัท',
    bullets: [
      'เลขที่เอกสารแยกจากใบเสร็จ (IV-)',
      'ข้อมูลผู้ซื้อเติมจากลูกค้าและแก้ไขได้ตอนออก',
      'รายการและยอดคัดจากใบเสร็จ ตรวจยอดให้ตรงกันก่อนออก',
    ],
    paths: [],
  },
  advancedReports: {
    key: 'advancedReports',
    label: 'รายงานขั้นสูง',
    description: 'ยอดขาย-ต้นทุน-กำไร รอบเวลาทำงาน เวลาทำงานช่าง ขายหน้าร้าน และสต็อก',
    bullets: [
      'ยอดขาย ต้นทุน และกำไร แยกตามประเภทและช่าง',
      'รอบเวลาแต่ละสถานะของจ๊อบ (เฉลี่ย/P90) และจ๊อบที่ค้างนานที่สุด',
      'เวลาทำงานของช่างต่อจ๊อบ ยอดขายหน้าร้าน และอายุสต็อก',
    ],
    paths: [
      '/reports/cycle-time',
      '/reports/sales-margin',
      '/reports/retail-sales',
      '/reports/stock',
      '/reports/stock-usage',
      '/reports/work-intervals',
    ],
  },
  export: {
    key: 'export',
    label: 'ส่งออก Excel/CSV',
    description: 'ส่งออกข้อมูลลูกค้า รถ และรายงานเป็นไฟล์ Excel/CSV',
    bullets: ['ส่งออกรายชื่อลูกค้าและรถ', 'ส่งออกรายงาน เช่น รถใกล้ครบรอบบริการสำหรับโทรตาม'],
    paths: [],
  },
  multiBranch: {
    key: 'multiBranch',
    label: 'หลายสาขา',
    description: 'สลับสาขาจากบัญชีเดียวสำหรับกิจการที่มีอู่หลายแห่ง',
    bullets: ['ผู้ดูแลระบบสลับสาขาจากแถบด้านบนโดยไม่ต้องออกจากระบบ', 'ข้อมูลของแต่ละสาขาแยกจากกัน'],
    paths: [],
  },
}

export const FEATURE_KEYS: readonly FeatureKey[] = Object.keys(FEATURES) as FeatureKey[]

export function isFeatureKey(value: string): value is FeatureKey {
  return Object.prototype.hasOwnProperty.call(FEATURES, value)
}

/**
 * ไม่มี plan (ลูกค้าเดิม/token ก่อนมีฟิลด์นี้) หรือ legacy/paid = เปิดทุกอย่าง (พฤติกรรมเดิมไม่เปลี่ยน)
 * trial/suspended เปิดเฉพาะที่อยู่ใน `features[]`
 */
export function isFeatureLocked(plan: PlanInfo | undefined, key: FeatureKey): boolean {
  if (!plan || plan.kind === 'legacy' || plan.kind === 'paid') return false
  const features = Array.isArray(plan.features) ? plan.features : []
  return !features.includes(key)
}

export function lockedFeatures(plan: PlanInfo | undefined): FeatureInfo[] {
  return FEATURE_KEYS.filter((key) => isFeatureLocked(plan, key)).map((key) => FEATURES[key])
}

function pathMatches(path: string, pathname: string): boolean {
  // ต้องชนขอบ segment — `/sales` ไม่ควรจับ `/sales-report`
  return pathname === path || pathname.startsWith(path.endsWith('/') ? path : `${path}/`)
}

/** ฟีเจอร์ที่เป็นเจ้าของ path นี้ (ไม่สนว่าล็อกหรือไม่) */
export function featureForPath(pathname: string): FeatureInfo | null {
  for (const key of FEATURE_KEYS) {
    const feature = FEATURES[key]
    if (feature.paths.some((path) => pathMatches(path, pathname))) return feature
  }
  return null
}

/** ฟีเจอร์ที่ล็อกอยู่และเป็นเจ้าของ path นี้ — null = เปิดหน้านี้ได้ */
export function lockedFeatureForPath(plan: PlanInfo | undefined, pathname: string): FeatureInfo | null {
  const feature = featureForPath(pathname)
  return feature && isFeatureLocked(plan, feature.key) ? feature : null
}

export type TrialTone = 'none' | 'neutral' | 'warn' | 'expired'

/** เหลือไม่เกินกี่วันถึงเปลี่ยนเป็นโทนเตือน (docs/12 §6: วันที่ 23 ของ 30) */
export const TRIAL_WARN_DAYS = 7

/**
 * [ASSUME] อู่ที่ชำระเงินแล้ว: แสดงแถบเตือนเมื่อเหลือไม่เกินกี่วัน (docs/17 §6.1 · ตรงกับอีเมลเตือนรอบแรก `PaidReminderDays` 14)
 * ต้องยืนยันกับฝ่ายขาย (docs/17 §11)
 */
export const PAID_WARN_DAYS = 14

const DAY_MS = 24 * 60 * 60 * 1000

function parseInstant(value: string): number {
  // ชั้นป้องกันเดียวกับ format.ts: ค่าจาก API ที่ไม่มีโซนเวลาคือ UTC เสมอ (ดู CLAUDE.md เรื่อง DateTimeKind)
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/.test(value)
  return Date.parse(hasZone ? value : `${value}Z`)
}

/**
 * จำนวนวันที่เหลือของแพ็กเกจ (ทดลองใช้ หรือแพ็กเกจที่ชำระเงินแล้วซึ่งมีวันหมดอายุ): ค่าจาก server เป็นหลัก
 * (คำนวณด้วยกติกาเดียวกับที่บังคับใช้) · server ส่ง `trialEndsAt`/`daysLeft` ให้ paid ด้วย (ชื่อฟิลด์เดิมจากยุคทดลองใช้)
 * คำนวณจาก `trialEndsAt` เองเฉพาะเมื่อ server ไม่ส่ง `daysLeft` · null = ไม่มีวันหมดอายุ ไม่ใช่ trial/paid หรือไม่รู้
 */
export function daysLeftOf(plan: PlanInfo | undefined, now: Date = new Date()): number | null {
  if (!plan || (plan.kind !== 'trial' && plan.kind !== 'paid')) return null
  if (typeof plan.daysLeft === 'number' && Number.isFinite(plan.daysLeft)) return Math.max(0, plan.daysLeft)
  if (plan.trialEndsAt) {
    const end = parseInstant(plan.trialEndsAt)
    if (!Number.isNaN(end)) return Math.max(0, Math.ceil((end - now.getTime()) / DAY_MS))
  }
  return null
}

/**
 * โทนของแถบแพ็กเกจ: none = ไม่แสดง · neutral = ทดลองใช้ปกติ · warn = ใกล้หมด · expired = อ่านอย่างเดียว/ถูกระงับ
 * · trial: warn เมื่อเหลือ ≤ 7 วัน
 * · paid: ไม่แสดงตามปกติ · warn เมื่อเหลือ ≤ 14 วัน · expired เมื่ออ่านอย่างเดียว (เดิมคืน none ทุกกรณี
 *   อู่ที่จ่ายแล้วจึงกลายเป็นอ่านอย่างเดียวโดยไม่มีคำอธิบาย docs/17 §6.1) · paid ที่ไม่มีวันหมดอายุ = none
 * ตรวจ readOnly ก่อนชนิดแพ็กเกจ เพราะเป็นสถานะที่ผู้ใช้ต้องรู้มากที่สุด
 */
export function trialTone(plan: PlanInfo | undefined, now: Date = new Date()): TrialTone {
  if (!plan || plan.kind === 'legacy') return 'none'
  if (plan.kind === 'suspended' || plan.readOnly) return 'expired'
  const days = daysLeftOf(plan, now)
  if (plan.kind === 'paid') return days !== null && days <= PAID_WARN_DAYS ? 'warn' : 'none'
  if (days !== null && days <= TRIAL_WARN_DAYS) return 'warn'
  return 'neutral'
}

export function formatDaysLeft(days: number | null | undefined): string {
  if (days === null || days === undefined || !Number.isFinite(days)) return 'ไม่ทราบวันหมดอายุ'
  if (days <= 0) return 'หมดอายุวันนี้'
  return `เหลือ ${days} วัน`
}
