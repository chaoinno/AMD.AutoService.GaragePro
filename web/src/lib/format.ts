import type { ValidationIssue } from '../api/types'

const moneyFormatter = new Intl.NumberFormat('th-TH', {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

const numberFormatter = new Intl.NumberFormat('th-TH', {
  maximumFractionDigits: 2,
})

const dateFormatter = new Intl.DateTimeFormat('th-TH', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
})

const dateTimeFormatter = new Intl.DateTimeFormat('th-TH', {
  day: '2-digit',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

export function formatMoney(value: number | null | undefined): string {
  return moneyFormatter.format(Number.isFinite(value) ? Number(value) : 0)
}

export function formatNumber(value: number | null | undefined): string {
  return numberFormatter.format(Number.isFinite(value) ? Number(value) : 0)
}

// API ส่ง DateTime ที่เป็น UTC เสมอ — backend ระบุ Kind=Utc ผ่าน ServiceDbContext.ConfigureConventions แล้ว
// (แก้บั๊กเดิม 2026-09-16 ที่ทำให้เวลาทุกจุดบนเว็บคลาดเคลื่อน +7 ชม. ในไทย เพราะ JSON ไม่มี 'Z' ต่อท้าย)
// เผื่อ endpoint ที่ยังไม่ผ่าน EF (เช่น DTO จาก Dapper อ่าน legacy) หลุดมาแบบไม่มีโซนเวลา จึงเติม 'Z' ให้เองที่นี่
// เป็นชั้นป้องกันสุดท้าย — ไม่ควรเจอในทางปฏิบัติแล้วหลังแก้ backend
function parseApiInstant(value: string): Date {
  const hasZone = /(?:Z|[+-]\d{2}:?\d{2})$/.test(value)
  return new Date(hasZone ? value : `${value}Z`)
}

export function formatDate(value: string | null | undefined): string {
  if (!value) return 'ไม่ระบุ'
  const date = parseApiInstant(value)
  return Number.isNaN(date.getTime()) ? value : dateFormatter.format(date)
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return 'ไม่ระบุ'
  const date = parseApiInstant(value)
  return Number.isNaN(date.getTime()) ? value : dateTimeFormatter.format(date)
}

/// แปลงค่าจาก <Input type="datetime-local"> ("2026-09-20T09:30" — เวลาท้องถิ่นของเครื่อง ไม่มีโซนเวลา)
/// เป็น ISO string UTC ที่ส่งให้ API ได้ตรงๆ
export function localInputToIso(value: string): string {
  return new Date(value).toISOString()
}

function toLocalInputValue(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/// แปลงค่าจาก API (UTC) กลับเป็นรูปแบบที่ <Input type="datetime-local"> ใช้แสดงผลได้ (เวลาท้องถิ่นของเครื่อง)
export function isoToLocalInput(value: string | null | undefined): string {
  if (!value) return ''
  const d = parseApiInstant(value)
  if (Number.isNaN(d.getTime())) return ''
  return toLocalInputValue(d)
}

/// ค่า "ตอนนี้" ในรูปแบบเดียวกับ isoToLocalInput — ใช้เป็น min/max ของ <Input type="datetime-local">
/// เพื่อกันเลือกวันที่ผิดเงื่อนไขตั้งแต่ตอนกรอก (เช่น วันนัดหมายห้ามน้อยกว่าวันเวลาปัจจุบัน)
export function nowLocalInputValue(): string {
  return toLocalInputValue(new Date())
}

export function getIssueMessage(issue: ValidationIssue): string {
  if (typeof issue === 'string') return issue
  return issue.messageTh ?? issue.message ?? issue.code ?? 'พบข้อผิดพลาดที่ไม่ระบุรายละเอียด'
}

export function getPendingAge(summary: {
  ageLabelTh?: string | null
  createdAt: string
}): string {
  if (summary.ageLabelTh) return summary.ageLabelTh
  const created = new Date(summary.createdAt).getTime()
  if (Number.isNaN(created)) return 'ไม่ระบุ'
  const days = Math.max(0, Math.floor((Date.now() - created) / 86_400_000))
  return days === 0 ? 'วันนี้' : `${days} วัน`
}
