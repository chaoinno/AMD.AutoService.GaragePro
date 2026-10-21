import { apiDownload, apiRequest } from './client'

/**
 * แจ้งชำระเงินและต่ออายุแพ็กเกจ (docs/17 §5.1): เฉพาะอู่ที่สมัครเอง (shard sp1) · อู่อื่นได้ 404 `SELFSERVE_NOT_APPLICABLE`
 * route อยู่ใต้ `/api/v1/selfserve` จึงเรียกได้แม้อู่อยู่ช่วงอ่านอย่างเดียว
 *
 * API ตัดฟิลด์ที่เป็น null ทิ้ง (CLAUDE.md): ทุกฟิลด์ที่อาจว่างเป็น optional ห้ามเช็ค `=== null`
 * เวลาเป็น ISO UTC ลงท้าย Z · ฟิลด์ `...Date` เป็น "yyyy-MM-dd" ตามปฏิทินไทย (ใช้ `formatDateOnly` เท่านั้น ห้ามส่งเข้า `formatDate`)
 */

export type PackagePlan = 'trial' | 'paid' | 'suspended' | 'converted'
export type PackageWorkspaceStatus = 'provisioning' | 'active' | 'expired' | 'purged'
export type PackagePaymentStatus = 'pending' | 'approved' | 'rejected' | 'cancelled'
export type PackageCannotSubmitCode = 'not_manager' | 'pending_exists' | 'not_renewable' | 'unavailable'
export type PackageRejectReason = 'slip_unreadable' | 'amount_mismatch' | 'not_received' | 'duplicate' | 'other'

export type PackageMonthOption = {
  months: number
  /** ไม่อยู่ = ยังไม่ตั้งราคามาตรฐาน */
  price?: number
  /** ถ้าทีมงานอนุมัติวันนี้ (server คำนวณ) */
  previewEndsAt?: string
  previewLastUsableDate?: string
}

export type PackagePaymentAccount = {
  bankName?: string
  accountNo?: string
  accountName?: string
  promptPayId?: string
}

export type PackagePaymentSlip = {
  contentType: string
  sizeBytes: number
}

export type PackagePayment = {
  id: string
  shortId: string
  status: PackagePaymentStatus | string
  monthCount: number
  /** ไม่ใช่ผู้จัดการ = ไม่อยู่ใน JSON */
  amount?: number
  /** ไม่อยู่ = ยังไม่ตั้งราคา หรือผู้เรียกไม่ใช่ผู้จัดการ */
  expectedAmount?: number
  paidAt: string
  submittedAt: string
  submittedBy?: string
  /** ไม่ใช่ผู้จัดการ = ไม่อยู่ */
  note?: string
  /** ไม่ใช่ผู้จัดการ = ไม่อยู่ */
  slip?: PackagePaymentSlip
  periodStartsAt?: string
  periodEndsAt?: string
  /** เฉพาะ approved */
  periodLastUsableDate?: string
  reviewedAt?: string
  /** เฉพาะ rejected */
  rejectReason?: PackageRejectReason | string
  rejectReasonTh?: string
  rejectNote?: string
  cancelledAt?: string
}

export type PackageOverview = {
  workspaceNo: string
  plan: PackagePlan | string
  status: PackageWorkspaceStatus | string
  /** ไม่อยู่ = ไม่มีกำหนดหมดอายุ */
  endsAt?: string
  lastUsableDate?: string
  /** ค่าเดียวกับ /auth/me · ติดลบ = หมดอายุไปแล้วกี่วัน */
  daysLeft?: number
  readOnly: boolean
  monthOptions?: PackageMonthOption[]
  /** ไม่อยู่ = ยังไม่ตั้งบัญชีรับเงิน */
  paymentAccount?: PackagePaymentAccount
  /** ไม่อยู่ = ไม่มีรายการรอตรวจ */
  pending?: PackagePayment
  canSubmit: boolean
  cannotSubmitCode?: PackageCannotSubmitCode | string
  cannotSubmitReasonTh?: string
  paidAtMaxPastDays?: number
}

export function getPackageOverview() {
  return apiRequest<PackageOverview>('/api/v1/selfserve/package')
}

export function getPackagePayments(take = 20) {
  const safeTake = Math.min(50, Math.max(1, Math.trunc(take)))
  return apiRequest<PackagePayment[]>(`/api/v1/selfserve/package/payments?take=${safeTake}`)
}

export type SubmitPackagePaymentInput = {
  /** สร้างครั้งเดียวต่อคำขอ · ส่งซ้ำได้แถวเดิม · ห้ามสร้างใหม่เมื่อเจอ NETWORK_ERROR */
  requestId: string
  /** ISO 8601 มี offset เช่น `2026-10-21T14:30:00+07:00` */
  paidAt: string
  /** `"1500.00"` */
  amount: string
  monthCount: number
  note?: string
  slip: File
}

export function submitPackagePayment(input: SubmitPackagePaymentInput) {
  const form = new FormData()
  form.set('requestId', input.requestId)
  form.set('paidAt', input.paidAt)
  form.set('amount', input.amount)
  form.set('monthCount', String(input.monthCount))
  const note = input.note?.trim()
  if (note) form.set('note', note)
  form.set('slip', input.slip)
  // body เป็น FormData → api/client ไม่ตั้ง Content-Type ให้ เบราว์เซอร์ใส่ boundary ของ multipart เอง
  return apiRequest<PackagePayment>('/api/v1/selfserve/package/payments', { method: 'POST', body: form })
}

export function cancelPackagePayment(id: string) {
  return apiRequest<PackagePayment>(`/api/v1/selfserve/package/payments/${encodeURIComponent(id)}/cancel`, {
    method: 'POST',
  })
}

/// ต้องมี Bearer token: ใช้ผ่าน URL.createObjectURL เท่านั้น ห้ามใส่ path ตรงใน <img src> (401 เงียบๆ รูปพัง)
export function downloadPackageSlip(id: string) {
  return apiDownload(`/api/v1/selfserve/package/payments/${encodeURIComponent(id)}/slip`)
}
