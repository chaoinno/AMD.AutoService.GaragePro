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

export function formatDate(value: string | null | undefined): string {
  if (!value) return 'ไม่ระบุ'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : dateFormatter.format(date)
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return 'ไม่ระบุ'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : dateTimeFormatter.format(date)
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
