import type { HandoverServiceItem, HandoverServiceItemInput } from '../../api/handover'
import type { ServiceReminderTemplate } from '../../api/serviceReminders'
import { formatKm } from '../../lib/format.ts'
import { mileageInputError } from './mileage.ts'

// ต้องตรงกับ ServiceSchedule ฝั่ง backend (Min/MaxMonths · MaxItemsPerHandover · ItemNameMaxLength) — server ตรวจซ้ำเสมอ
export const MIN_MONTHS = 1
export const MAX_MONTHS = 24
export const MAX_SERVICE_ITEMS = 10
export const SERVICE_ITEM_NAME_MAX = 100
/// รายการที่พิมพ์เอง เริ่มที่ห่างจากไมล์ส่งมอบ 10,000 กม. / 6 เดือน (ค่าเดียวกับ "เช็คระยะ" ตั้งต้น) แก้ได้ทุกช่อง
export const CUSTOM_ROW_DEFAULTS = { intervalKm: 10_000, months: 6 } as const

/// หนึ่งแถวในฟอร์มนัดบริการ — mileageText เป็นค่าที่ผู้ใช้พิมพ์เอง · autoInterval != null = ไมล์นัดตามไมล์ส่งมอบ + ระยะนี้
/// อัตโนมัติ (แถวที่เพิ่งเลือกจากรายการตั้งต้น) จนกว่าผู้ใช้จะแก้ช่องไมล์เอง
export type ServiceRow = {
  key: string
  templateId: string | null
  name: string
  /** แถวที่พิมพ์ชื่อเองได้ (ไม่ได้มาจากรายการตั้งต้น) */
  customName: boolean
  mileageText: string
  monthsText: string
  autoInterval: number | null
}

export type ServiceRowErrors = { name?: string; mileage?: string; months?: string }

let keySeq = 0
const nextKey = () => `row-${++keySeq}`

export function rowFromTemplate(t: ServiceReminderTemplate): ServiceRow {
  return {
    key: nextKey(), templateId: t.id, name: t.name, customName: false,
    mileageText: '', monthsText: String(t.defaultMonths), autoInterval: t.defaultMileageInterval,
  }
}

export function customRow(): ServiceRow {
  return {
    key: nextKey(), templateId: null, name: '', customName: true,
    mileageText: '', monthsText: String(CUSTOM_ROW_DEFAULTS.months), autoInterval: CUSTOM_ROW_DEFAULTS.intervalKm,
  }
}

/// แถวที่บันทึกไว้แล้ว: ไมล์ที่เก็บคือค่าจริง ไม่ใช่ผลรวมอัตโนมัติ — แถวจากรายการตั้งต้นที่ผู้จัดการลบ/ปิดไปแล้วก็ยังแก้ได้
export function rowsFromHandover(items: readonly HandoverServiceItem[] | undefined): ServiceRow[] {
  return (items ?? []).map((i) => ({
    key: nextKey(), templateId: i.templateId ?? null, name: i.name, customName: !i.templateId,
    mileageText: String(i.nextServiceMileage), monthsText: String(i.nextServiceMonths), autoInterval: null,
  }))
}

export function rowMileageText(row: ServiceRow, handoverKm: number | null): string {
  return row.autoInterval != null && handoverKm != null ? String(handoverKm + row.autoInterval) : row.mileageText
}

export function parseMonths(text: string): number | null {
  return /^\d+$/.test(text.trim()) ? Number(text.trim()) : null
}

const nameKey = (name: string) => name.trim().toLocaleLowerCase()

/// มิเรอร์ HandoverService.ValidateServiceInfo ต่อแถว (ชื่อไม่ว่าง/ไม่ซ้ำ · ไมล์ > ไมล์ส่งมอบ · เดือน 1–24)
export function validateRow(row: ServiceRow, rows: readonly ServiceRow[], handoverKm: number | null): ServiceRowErrors {
  const errors: ServiceRowErrors = {}
  const name = row.name.trim()
  if (!name) errors.name = 'กรอกชื่อรายการ'
  else if (name.length > SERVICE_ITEM_NAME_MAX) errors.name = `ชื่อยาวไม่เกิน ${SERVICE_ITEM_NAME_MAX} ตัวอักษร`
  else if (rows.some((r) => r.key !== row.key && nameKey(r.name) === nameKey(name))) errors.name = 'ชื่อรายการซ้ำกัน'

  const mileage = mileageInputError(rowMileageText(row, handoverKm))
  if (mileage.error) errors.mileage = mileage.error
  else if (mileage.km != null && handoverKm != null && mileage.km <= handoverKm) {
    errors.mileage = `ต้องมากกว่าเลขไมล์ตอนส่งมอบ (${formatKm(handoverKm)})`
  }

  const months = parseMonths(row.monthsText)
  if (row.monthsText.trim() && (months == null || months < MIN_MONTHS || months > MAX_MONTHS)) {
    errors.months = `กรอกจำนวนเดือน ${MIN_MONTHS}–${MAX_MONTHS}`
  }
  return errors
}

/// ไมล์นัดที่แถวนี้จะบันทึก — null ถ้ายังกรอกไม่ครบ/ไม่ถูกต้อง
export function rowMileageKm(row: ServiceRow, handoverKm: number | null): number | null {
  return mileageInputError(rowMileageText(row, handoverKm)).km
}

export function rowsToInput(rows: readonly ServiceRow[], handoverKm: number | null): HandoverServiceItemInput[] {
  return rows.map((r) => ({
    templateId: r.templateId,
    name: r.name.trim(),
    nextServiceMileage: rowMileageKm(r, handoverKm)!,
    nextServiceMonths: parseMonths(r.monthsText)!,
  }))
}

/// ลายเซ็นของข้อมูลที่จะบันทึก (ใช้เทียบกับที่ server เก็บไว้ว่ามีการแก้ไขหรือไม่) — ไม่รวม templateId เพราะแก้ไม่ได้ในฟอร์ม
export function serviceInfoSignature(handoverKm: number | null, items: ReadonlyArray<{ name: string; nextServiceMileage: number | null; nextServiceMonths: number | null }>): string {
  return JSON.stringify([handoverKm, items.map((i) => [i.name.trim(), i.nextServiceMileage, i.nextServiceMonths])])
}

/// ข้อมูลส่งมอบที่บังคับก่อนเซ็น: ไมล์ส่งมอบ + อย่างน้อย 1 รายการนัด (HANDOVER_SERVICE_INFO_REQUIRED)
export function handoverServiceInfoMissing(handover: { mileageAtHandover?: number | null; serviceItems?: readonly unknown[] }): boolean {
  return handover.mileageAtHandover == null || (handover.serviceItems?.length ?? 0) === 0
}

/// เหตุผลที่ปุ่มบันทึกกดไม่ได้ (null = บันทึกได้) — เรียงจากสิ่งที่ผู้ใช้แก้เองไม่ได้ไปหาสิ่งที่แก้ตรงนี้ได้
export function serviceInfoBlockedReason(
  intakeKm: number | null,
  handoverKm: number | null,
  handoverError: string | undefined,
  rows: readonly ServiceRow[],
): string | null {
  if (intakeKm == null) return 'งานนี้ยังไม่มีเลขไมล์ขณะรับรถ บันทึกที่ขั้น "รับรถ" ของการ์ดนี้ก่อน'
  if (handoverKm == null) return 'กรอกเลขไมล์ตอนส่งมอบ'
  if (handoverError) return handoverError
  if (rows.length === 0) return 'เพิ่มรายการนัดบริการอย่างน้อย 1 รายการ'
  for (const row of rows) {
    const label = row.name.trim() || 'รายการใหม่'
    const errors = validateRow(row, rows, handoverKm)
    if (errors.name) return `${label}: ${errors.name}`
    if (rowMileageKm(row, handoverKm) == null && !errors.mileage) return `${label}: กรอกเลขไมล์ที่นัด`
    if (errors.mileage) return `${label}: ${errors.mileage}`
    if (parseMonths(row.monthsText) == null && !errors.months) return `${label}: เลือกระยะเวลานัด (เดือน)`
    if (errors.months) return `${label}: ${errors.months}`
  }
  return null
}
