import type {
  PackageCannotSubmitCode,
  PackageMonthOption,
  PackageOverview,
  PackagePayment,
  PackagePaymentStatus,
  PackageRejectReason,
} from '../../api/package'

/**
 * ตรรกะล้วนของหน้าแพ็กเกจและการชำระเงิน (docs/17 §5.1 · §6.1): import ได้เฉพาะ type
 * (node --test โหลด .ts ตรงๆ แต่ resolve import "ค่า" ที่ไม่มีนามสกุลไม่ได้) ตัวจัดรูปวันที่จึงรับเป็นพารามิเตอร์
 *
 * การตรวจฝั่ง client มีไว้บอกผู้ใช้ก่อนส่งเท่านั้น server ยังตรวจซ้ำทุกข้อและเป็นผู้ตัดสินเสมอ
 */

/** เวลาไทยไม่มี DST จึงใช้ offset คงที่ได้ · ค่าในช่องวันที่/เวลาของฟอร์มตีความเป็นเวลาไทยเสมอ ไม่ใช่เวลาของเครื่อง */
export const THAI_OFFSET = '+07:00'
const THAI_OFFSET_MS = 7 * 60 * 60 * 1000
const DAY_MS = 24 * 60 * 60 * 1000

/** ตรงกับ tolerance ของวันนัดหมาย (docs/17 กฎข้อ 4) กันนาฬิกาเครื่องกับ server คลาดกัน */
export const PAID_AT_FUTURE_TOLERANCE_MS = 5 * 60 * 1000
/** ใช้เมื่อ server ไม่ส่ง `paidAtMaxPastDays` (ค่าเริ่มต้นของ `SelfServe:Package:PaidAtMaxPastDays`) */
export const DEFAULT_PAID_AT_MAX_PAST_DAYS = 60
/** `AttachmentOptions.MaxSizeBytes` เดิม (15 MB) */
export const SLIP_MAX_BYTES = 15 * 1024 * 1024
export const AMOUNT_MAX = 999_999.99
export const NOTE_MAX_LENGTH = 500
export const SLIP_CONTENT_TYPES: readonly string[] = ['image/png', 'image/jpeg', 'image/webp', 'application/pdf']
export const SLIP_ACCEPT = 'image/png,image/jpeg,image/webp,application/pdf,.png,.jpg,.jpeg,.webp,.pdf'

const EXTENSION_TYPES: Record<string, string> = {
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  webp: 'image/webp',
  pdf: 'application/pdf',
}

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

// ---- วันเวลาที่โอน ---------------------------------------------------------------------------------

/** วันที่และเวลาปัจจุบันตามเวลาไทย ในรูปแบบของ `<input type="date">` / `<input type="time">` */
export function thaiNowInputs(now: Date = new Date()): { date: string; time: string } {
  const thai = new Date(now.getTime() + THAI_OFFSET_MS)
  return {
    date: `${thai.getUTCFullYear()}-${pad(thai.getUTCMonth() + 1)}-${pad(thai.getUTCDate())}`,
    time: `${pad(thai.getUTCHours())}:${pad(thai.getUTCMinutes())}`,
  }
}

function parseDateInput(value: string): { year: number; month: number; day: number } | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value.trim())
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const probe = new Date(Date.UTC(year, month - 1, day))
  // กันวันที่ที่ไม่มีจริง เช่น 2026-02-30 (Date.UTC เลื่อนไปเดือนถัดไปเงียบๆ)
  if (probe.getUTCFullYear() !== year || probe.getUTCMonth() !== month - 1 || probe.getUTCDate() !== day) return null
  return { year, month, day }
}

/** เลื่อนวันที่แบบ "yyyy-MM-dd" ตามปฏิทิน (ไม่ขึ้นกับโซนเวลาเครื่อง) · ใช้ทำ `min` ของช่องวันที่ */
export function addDaysToDateInput(value: string, days: number): string {
  const parsed = parseDateInput(value)
  if (!parsed) return value
  const shifted = new Date(Date.UTC(parsed.year, parsed.month - 1, parsed.day) + days * DAY_MS)
  return `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}`
}

/**
 * รวมช่องวันที่ + เวลา เป็น ISO 8601 ที่มี offset ไทย เช่น `2026-10-21T14:30:00+07:00` (รูปแบบที่ docs/17 §5.1 กำหนด)
 * null = ช่องว่างหรือรูปแบบผิด
 */
export function composePaidAt(date: string, time: string): string | null {
  const parsed = parseDateInput(date)
  const match = /^(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(time.trim())
  if (!parsed || !match) return null
  const hour = Number(match[1])
  const minute = Number(match[2])
  const second = match[3] ? Number(match[3]) : 0
  if (hour > 23 || minute > 59 || second > 59) return null
  return `${parsed.year}-${pad(parsed.month)}-${pad(parsed.day)}T${pad(hour)}:${pad(minute)}:${pad(second)}${THAI_OFFSET}`
}

// ---- ยอดเงิน ---------------------------------------------------------------------------------------

export type AmountParse =
  | { ok: true; value: number; normalized: string }
  | { ok: false; error: 'required' | 'format' | 'not_positive' | 'too_large' }

/** "1,500" → 1500 / "1500.00" · มี `,` ได้ · ทศนิยมไม่เกิน 2 ตำแหน่ง (ตรงกับกติกาของ server) */
export function parseAmountInput(text: string): AmountParse {
  const cleaned = text.replace(/[,\s]/g, '')
  if (!cleaned) return { ok: false, error: 'required' }
  if (!/^\d+(?:\.\d{1,2})?$/.test(cleaned)) return { ok: false, error: 'format' }
  const value = Number(cleaned)
  if (!Number.isFinite(value) || value <= 0) return { ok: false, error: 'not_positive' }
  if (value > AMOUNT_MAX) return { ok: false, error: 'too_large' }
  return { ok: true, value, normalized: value.toFixed(2) }
}

const AMOUNT_ERRORS: Record<Exclude<AmountParse, { ok: true }>['error'], string> = {
  required: 'กรอกยอดเงินที่โอน',
  format: 'ยอดเงินต้องเป็นตัวเลข ทศนิยมไม่เกิน 2 ตำแหน่ง',
  not_positive: 'ยอดเงินต้องมากกว่า 0',
  too_large: 'ยอดเงินต้องไม่เกิน 999,999.99 บาท',
}

export type AmountComparison =
  | { kind: 'no_price' }
  | { kind: 'no_amount'; price: number }
  | { kind: 'match'; price: number }
  | { kind: 'mismatch'; price: number; diff: number }

/**
 * เทียบยอดที่กรอกกับราคามาตรฐานของจำนวนเดือนที่เลือก · ยอดไม่ตรงยังส่งได้ (ส่วนลด/โปรโมชัน ทีมงานตรวจเอง กฎข้อ 5)
 * เทียบเป็นสตางค์ กันทศนิยมลอยตัว (590.1 + 0.2)
 */
export function compareAmount(price: number | undefined, amount: number | null | undefined): AmountComparison {
  if (typeof price !== 'number' || !Number.isFinite(price)) return { kind: 'no_price' }
  if (typeof amount !== 'number' || !Number.isFinite(amount)) return { kind: 'no_amount', price }
  const diffSatang = Math.round(amount * 100) - Math.round(price * 100)
  return diffSatang === 0 ? { kind: 'match', price } : { kind: 'mismatch', price, diff: diffSatang / 100 }
}

/** ค่าเริ่มต้นของช่องยอดเงินเมื่อเลือกจำนวนเดือนที่ตั้งราคาไว้: รูปแบบเดียวกับที่ส่งให้ server */
export function priceAsAmountInput(price: number | undefined): string {
  return typeof price === 'number' && Number.isFinite(price) && price > 0 ? price.toFixed(2) : ''
}

// ---- ไฟล์สลิป --------------------------------------------------------------------------------------

export type SlipFileInfo = { name: string; type: string; size: number }

/** ชนิดไฟล์จาก `File.type` และสำรองด้วยนามสกุล (บางเครื่องส่ง type ว่าง) · null = ไม่รองรับ */
export function slipContentType(file: SlipFileInfo): string | null {
  const type = file.type.toLowerCase()
  if (SLIP_CONTENT_TYPES.includes(type)) return type
  if (type) return null
  const extension = file.name.toLowerCase().split('.').pop() ?? ''
  return EXTENSION_TYPES[extension] ?? null
}

export function isImageSlipType(contentType: string | null | undefined): boolean {
  return Boolean(contentType && contentType.toLowerCase().startsWith('image/'))
}

export function slipFileError(file: SlipFileInfo | null | undefined): string | null {
  if (!file) return 'แนบสลิปการโอน 1 ไฟล์'
  if (!slipContentType(file)) return 'รองรับเฉพาะไฟล์ PNG, JPEG, WebP หรือ PDF'
  if (file.size <= 0) return 'ไฟล์ว่างเปล่า เลือกไฟล์สลิปใหม่'
  if (file.size > SLIP_MAX_BYTES) return `ไฟล์มีขนาด ${formatFileSize(file.size)} เกิน 15 MB`
  return null
}

export function formatFileSize(bytes: number | null | undefined): string {
  if (typeof bytes !== 'number' || !Number.isFinite(bytes) || bytes < 0) return 'ไม่ทราบขนาด'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

// ---- ตรวจฟอร์มก่อนส่ง -------------------------------------------------------------------------------

export type PaymentFormField = 'monthCount' | 'amount' | 'paidAt' | 'slip' | 'note'
export type PaymentFormErrors = Partial<Record<PaymentFormField, string>>

export type PaymentFormInput = {
  monthCount: number | null
  amountText: string
  paidDate: string
  paidTime: string
  slip: SlipFileInfo | null
  note: string
}

export type PaymentFormContext = {
  now: Date
  /** จาก `PackageOverview.paidAtMaxPastDays` */
  maxPastDays?: number
  /** `monthOptions[].months` ที่ server ส่ง */
  monthOptions: readonly number[]
}

/**
 * ตรวจฟอร์มตามสัญญา docs/17 §5.1 · คืนข้อความไทยต่อช่อง (ว่าง = ส่งได้)
 * · วันที่ย้อนหลังเทียบตามวันปฏิทินไทย (ผ่อนกว่า server เล็กน้อย) ถ้า server ไม่รับจะตอบ 422 field `paidAt` กลับมาแสดงที่ช่องเดียวกัน
 */
export function validatePaymentForm(input: PaymentFormInput, context: PaymentFormContext): PaymentFormErrors {
  const errors: PaymentFormErrors = {}

  if (input.monthCount === null || !context.monthOptions.includes(input.monthCount)) {
    errors.monthCount = 'เลือกจำนวนเดือนที่ชำระ'
  }

  const amount = parseAmountInput(input.amountText)
  if (!amount.ok) errors.amount = AMOUNT_ERRORS[amount.error]

  const paidAtError = paidAtInputError(input.paidDate, input.paidTime, context)
  if (paidAtError) errors.paidAt = paidAtError

  const slipError = slipFileError(input.slip)
  if (slipError) errors.slip = slipError

  if (input.note.trim().length > NOTE_MAX_LENGTH) errors.note = `หมายเหตุต้องไม่เกิน ${NOTE_MAX_LENGTH} ตัวอักษร`

  return errors
}

function paidAtInputError(date: string, time: string, context: PaymentFormContext): string | null {
  if (!date.trim() || !time.trim()) return 'กรอกวันที่และเวลาที่โอน'
  const parsed = parseDateInput(date)
  // ช่องวันที่ของเบราว์เซอร์เป็น ค.ศ. เสมอ แต่พิมพ์ปีเองได้ · ระบบเดิมเคยได้ปี 2569 จากผู้ใช้ (docs/17 §1.1)
  if (parsed && parsed.year >= 2400) return 'กรุณาใช้ปี ค.ศ. เช่น 2026 (ไม่ใช่ พ.ศ.)'
  const iso = composePaidAt(date, time)
  if (!iso) return 'วันที่หรือเวลาที่โอนไม่ถูกต้อง'
  const at = Date.parse(iso)
  if (Number.isNaN(at)) return 'วันที่หรือเวลาที่โอนไม่ถูกต้อง'
  if (at > context.now.getTime() + PAID_AT_FUTURE_TOLERANCE_MS) return 'วันเวลาที่โอนต้องไม่เป็นเวลาในอนาคต'
  const maxPastDays = context.maxPastDays && context.maxPastDays > 0 ? context.maxPastDays : DEFAULT_PAID_AT_MAX_PAST_DAYS
  const earliest = addDaysToDateInput(thaiNowInputs(context.now).date, -maxPastDays)
  if (date < earliest) return `แจ้งชำระย้อนหลังได้ไม่เกิน ${maxPastDays} วัน`
  return null
}

export function hasErrors(errors: PaymentFormErrors): boolean {
  return Object.values(errors).some(Boolean)
}

// ---- ผลจาก server -------------------------------------------------------------------------------------

/**
 * ไม่รู้ว่า server บันทึกไปแล้วหรือยัง (เน็ตหลุด / proxy ตอบหน้า error ที่ไม่ใช่ envelope)
 * → ต้องส่งซ้ำด้วย requestId เดิม ห้ามสร้างใหม่ (docs/17 §5.1) ไม่งั้นอาจได้รายการซ้ำ
 */
export function isUnknownOutcomeError(code: string | undefined): boolean {
  return code === 'NETWORK_ERROR' || code === 'INVALID_RESPONSE'
}

const STATE_CHANGED_CODES: ReadonlySet<string> = new Set([
  'PACKAGE_PAYMENT_PENDING_EXISTS',
  'PACKAGE_NOT_RENEWABLE',
  'PACKAGE_PAYMENT_UNAVAILABLE',
  'PACKAGE_PAYMENT_FORBIDDEN',
])

/**
 * สถานะของอู่ฝั่ง server ไม่ตรงกับที่หน้าจอเห็น (มีรายการรอตรวจแล้ว · ต่ออายุไม่ได้ · ยังไม่ตั้งบัญชี · ไม่ใช่ผู้จัดการ)
 * → ส่งซ้ำไม่ช่วย ต้องโหลดภาพรวมใหม่ให้ฟอร์มถูกแทนด้วยเหตุผลจาก server
 */
export function isStateChangedError(code: string | undefined): boolean {
  return Boolean(code && STATE_CHANGED_CODES.has(code))
}

const FORM_FIELDS: readonly PaymentFormField[] = ['monthCount', 'amount', 'paidAt', 'slip', 'note']

/** ช่องในฟอร์มที่ error จาก server ชี้ถึง (422 มี `field`) · null = แสดงเป็นข้อความรวมเหนือปุ่ม */
export function formFieldForError(error: { code?: string; field?: string; status?: number }): PaymentFormField | null {
  const field = error.field?.trim()
  if (field) {
    const match = FORM_FIELDS.find((name) => name.toLowerCase() === field.toLowerCase())
    if (match) return match
  }
  if (error.code?.startsWith('ATTACHMENT_') || error.status === 413) return 'slip'
  return null
}

export function retryAfterText(seconds: number | undefined): string | null {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds <= 0) return null
  if (seconds < 60) return `ลองใหม่ได้ในอีก ${Math.ceil(seconds)} วินาที`
  return `ลองใหม่ได้ในอีกประมาณ ${Math.ceil(seconds / 60)} นาที`
}

// ---- สถานะรายการแจ้งชำระ ---------------------------------------------------------------------------

export type PaymentStatusMeta = { status: PackagePaymentStatus | 'unknown'; label: string }

const STATUS_LABELS: Record<PackagePaymentStatus, string> = {
  pending: 'รอตรวจสลิป',
  approved: 'อนุมัติแล้ว',
  rejected: 'ปฏิเสธ',
  cancelled: 'ยกเลิกแล้ว',
}

/** ป้ายสถานะ (component เติมไอคอน + สีตาม `status`) · ค่าที่ไม่รู้จักไม่ทำให้หน้าพัง */
export function paymentStatusMeta(status: string | undefined): PaymentStatusMeta {
  const key = (status ?? '').toLowerCase()
  if (key in STATUS_LABELS) return { status: key as PackagePaymentStatus, label: STATUS_LABELS[key as PackagePaymentStatus] }
  return { status: 'unknown', label: 'ไม่ทราบสถานะ' }
}

export const REJECT_REASON_TH: Record<PackageRejectReason, string> = {
  slip_unreadable: 'สลิปไม่ชัด อ่านไม่ออก',
  amount_mismatch: 'ยอดเงินไม่ตรงกับแพ็กเกจ',
  not_received: 'ไม่พบยอดเงินเข้าบัญชี',
  duplicate: 'สลิปซ้ำกับที่เคยแจ้ง',
  other: 'อื่นๆ',
}

/** เหตุผลปฏิเสธ: ข้อความของ server ก่อน · รหัสที่รู้จักแปลเอง · ไม่รู้จัก = แสดงว่าไม่ระบุ */
export function rejectReasonText(payment: Pick<PackagePayment, 'rejectReason' | 'rejectReasonTh'>): string {
  if (payment.rejectReasonTh) return payment.rejectReasonTh
  const code = payment.rejectReason as PackageRejectReason | undefined
  return (code && REJECT_REASON_TH[code]) || 'ไม่ระบุเหตุผล'
}

/**
 * บรรทัดรองใต้ป้ายสถานะ: `formatDate` รับ "yyyy-MM-dd" (ใช้ `formatDateOnly` ของ lib/format.ts)
 * null = ไม่มีอะไรต้องอธิบายเพิ่ม
 */
export function paymentStatusDetail(
  payment: Pick<PackagePayment, 'status' | 'periodLastUsableDate' | 'rejectReason' | 'rejectReasonTh'>,
  formatDate: (yyyyMmDd: string) => string,
): string | null {
  const { status } = paymentStatusMeta(payment.status)
  if (status === 'approved') {
    return payment.periodLastUsableDate ? `ใช้งานได้ถึง ${formatDate(payment.periodLastUsableDate)}` : null
  }
  if (status === 'rejected') return `เหตุผล: ${rejectReasonText(payment)}`
  if (status === 'pending') return 'ทีมงานกำลังตรวจสลิป'
  return null
}

// ---- แพ็กเกจปัจจุบัน ---------------------------------------------------------------------------------

export function packagePlanLabel(plan: string | undefined): string {
  switch ((plan ?? '').toLowerCase()) {
    case 'trial':
      return 'ทดลองใช้'
    case 'paid':
      return 'แพ็กเกจที่ชำระเงินแล้ว'
    case 'suspended':
      return 'ถูกระงับ'
    case 'converted':
      return 'ย้ายไประบบ GaragePro แล้ว'
    default:
      return 'ไม่ทราบแพ็กเกจ'
  }
}

/**
 * จำนวนวันที่เหลือ (ค่าเดียวกับ /auth/me: นับตามวันปฏิทินไทยถึงวันสุดท้ายที่ใช้ได้ · ติดลบ = หมดอายุไปแล้ว)
 * null = ไม่มีกำหนดหมดอายุ
 */
export function packageDaysText(daysLeft: number | undefined): string | null {
  if (typeof daysLeft !== 'number' || !Number.isFinite(daysLeft)) return null
  if (daysLeft > 0) return `เหลือ ${daysLeft} วัน`
  if (daysLeft === 0) return 'วันนี้เป็นวันสุดท้าย'
  return `หมดอายุแล้ว ${-daysLeft} วัน`
}

export type PackageUrgency = 'none' | 'ok' | 'warn' | 'expired'

/**
 * โทนของป้ายวันที่เหลือบนการ์ดแพ็กเกจปัจจุบัน · `warnDays` = TRIAL_WARN_DAYS / PAID_WARN_DAYS ของ lib/plan.ts ตามชนิดแพ็กเกจ
 * none = ไม่มีกำหนดหมดอายุ (และไม่ได้อ่านอย่างเดียว)
 */
export function packageUrgency(
  overview: Pick<PackageOverview, 'daysLeft' | 'readOnly' | 'plan'>,
  warnDays: number,
): PackageUrgency {
  if (overview.readOnly || overview.plan === 'suspended') return 'expired'
  if (typeof overview.daysLeft !== 'number' || !Number.isFinite(overview.daysLeft)) return 'none'
  if (overview.daysLeft < 0) return 'expired'
  return overview.daysLeft <= warnDays ? 'warn' : 'ok'
}

const CANNOT_SUBMIT_FALLBACK: Record<PackageCannotSubmitCode, string> = {
  not_manager: 'เฉพาะผู้จัดการอู่แจ้งชำระเงินได้',
  pending_exists: 'มีรายการแจ้งชำระรอตรวจอยู่แล้ว',
  not_renewable: 'แพ็กเกจนี้ไม่ต้องแจ้งชำระเพื่อต่ออายุ',
  unavailable: 'ยังไม่เปิดรับแจ้งชำระเงิน ติดต่อทีมงานทาง LINE @garagepro',
}

/** เหตุผลที่แจ้งชำระไม่ได้: ข้อความของ server ก่อนเสมอ (docs/17 §6.1 ห้ามแต่งเอง) · null = แจ้งได้ */
export function cannotSubmitReason(
  overview: Pick<PackageOverview, 'canSubmit' | 'cannotSubmitCode' | 'cannotSubmitReasonTh'>,
): string | null {
  if (overview.canSubmit) return null
  if (overview.cannotSubmitReasonTh) return overview.cannotSubmitReasonTh
  const code = overview.cannotSubmitCode as PackageCannotSubmitCode | undefined
  return (code && CANNOT_SUBMIT_FALLBACK[code]) || 'ยังแจ้งชำระเงินไม่ได้ในตอนนี้'
}

export function monthOptionLabel(months: number): string {
  return months === 12 ? '12 เดือน (1 ปี)' : `${months} เดือน`
}

/** พรีวิว "ถ้าอนุมัติวันนี้ ใช้งานได้ถึง …" ของตัวเลือกจำนวนเดือน (server คำนวณ) · null = server ไม่ส่ง */
export function previewUsableText(
  option: Pick<PackageMonthOption, 'previewLastUsableDate'> | undefined,
  formatDate: (yyyyMmDd: string) => string,
): string | null {
  return option?.previewLastUsableDate ? `ถ้าทีมงานอนุมัติวันนี้ ใช้งานได้ถึง ${formatDate(option.previewLastUsableDate)}` : null
}
