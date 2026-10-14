import { apiRequest, isApiError } from './client'

export type LimitUsage = { used: number; max: number }

export type SelfServeLimits = {
  staff?: LimitUsage
  catalog?: LimitUsage
  customers?: LimitUsage
  jobs?: LimitUsage
}

export type SelfServeChecklistCounts = {
  staffCount?: number
  catalogCount?: number
  jobCount?: number
  quotationSentCount?: number
  mobileLoginSeen?: boolean
}

/** API ตัดฟิลด์ที่เป็น null ทิ้ง (CLAUDE.md) — ทุกฟิลด์ที่อาจว่างจึงเป็น optional ห้ามเช็ค `=== null` */
export type SelfServeStatus = {
  workspaceNo: string | number
  plan: string
  status: string
  trialEndsAt?: string
  daysLeft?: number
  limits?: SelfServeLimits
  sampleDataLoadedAt?: string
  extensionRequestedAt?: string
  checklist?: SelfServeChecklistCounts
}

/**
 * Endpoint ของอู่ที่สมัครเอง (shard sp1) — ลูกค้าเดิมได้ 404 `SELFSERVE_NOT_APPLICABLE` เสมอ
 * ทุกตัวเรียกได้แม้ทดลองใช้หมดอายุแล้ว (backend ติด [AllowWhenTrialExpired])
 */
export function getSelfServeStatus() {
  return apiRequest<SelfServeStatus>('/api/v1/selfserve/status')
}

export type ExtensionRequestInput = {
  /** สร้างครั้งเดียวต่อการกรอก — กดส่งซ้ำหลังเน็ตหลุดใช้ค่าเดิม ทีมขายจะไม่ได้ข้อความซ้ำ */
  requestId: string
  note?: string
}

export function requestExtension(input: ExtensionRequestInput) {
  return apiRequest<unknown>('/api/v1/selfserve/extension-request', {
    method: 'POST',
    body: JSON.stringify({ requestId: input.requestId, note: input.note?.trim() || undefined }),
  })
}

export function clearSampleData() {
  return apiRequest<unknown>('/api/v1/selfserve/sample-data/clear', { method: 'POST' })
}

export function isSelfServeNotApplicable(error: unknown): boolean {
  return isApiError(error) && (error.code === 'SELFSERVE_NOT_APPLICABLE' || error.status === 404)
}

/** 409 = วันนี้ส่งคำขอไปแล้ว — ถือว่าสำเร็จจากมุมผู้ใช้ (ทีมงานได้เรื่องแล้ว) */
export function isExtensionAlreadyRequested(error: unknown): boolean {
  return isApiError(error) && (error.code === 'SELFSERVE_EXTENSION_ALREADY_REQUESTED' || error.status === 409)
}
