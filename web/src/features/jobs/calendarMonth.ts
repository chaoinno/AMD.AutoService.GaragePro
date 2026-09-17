// เดือนปฏิทิน — 42 ช่อง (6 แถว × 7 วัน) เริ่มวันอาทิตย์ ครอบคลุมทั้งวันของเดือนก่อน/ถัดไปที่โผล่มาในตาราง
// ล้วนเป็น pure function ไม่มี side effect — ทดสอบได้โดยไม่ต้องมี DOM (ดู web/tests/calendarMonth.test.mjs)
export type MonthGrid = { days: Date[]; rangeStart: Date; rangeEnd: Date }

const CELLS = 42

export function buildMonthGrid(year: number, month: number): MonthGrid {
  const first = new Date(year, month, 1)
  const start = new Date(year, month, 1 - first.getDay())
  const days = Array.from({ length: CELLS }, (_, i) =>
    new Date(start.getFullYear(), start.getMonth(), start.getDate() + i))
  const rangeEnd = new Date(start.getFullYear(), start.getMonth(), start.getDate() + CELLS)
  return { days, rangeStart: start, rangeEnd }
}

export function dayKey(d: Date): string {
  return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`
}

export function isSameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

export const WEEKDAY_LABELS_TH = ['อา', 'จ', 'อ', 'พ', 'พฤ', 'ศ', 'ส']
