import { apiRequest } from './client'
import type { PaymentMethod, TaxInvoiceParty } from './pos'

/** ใบลดหนี้ CN- (docs/20) — อ้างใบกำกับ IV- (เอกสารภาษี) หรือใบเสร็จ RC- (ไม่ใช่เอกสารภาษี) */
export type CreditNoteSourceType = 'taxInvoice' | 'receipt'
export type CreditNoteReason = 'defect' | 'returned' | 'service' | 'overcharged' | 'other'
export type CreditNoteRefundStatus = 'none' | 'partial' | 'full'

/** ต้องตรงกับ CreditNoteMapper.ReasonLabel ฝั่ง API (ข้อความตามกรณีลดหนี้ ม.82/10) */
export const CREDIT_NOTE_REASONS: { value: CreditNoteReason; label: string }[] = [
  { value: 'defect', label: 'ลดราคาเนื่องจากสินค้า/บริการไม่ตรงตามที่ตกลงหรือชำรุดบกพร่อง' },
  { value: 'returned', label: 'รับคืนสินค้า' },
  { value: 'service', label: 'บริการไม่เป็นไปตามข้อกำหนดที่ตกลงกัน' },
  { value: 'overcharged', label: 'คำนวณราคาผิดพลาดสูงกว่าที่เป็นจริง' },
  { value: 'other', label: 'เหตุอื่น (ระบุรายละเอียด)' },
]

export type CreditPlanLine = {
  sourceLineId: string
  quotationCode: string
  description: string
  unit: string
  originalQuantity: number
  originalNetAmount: number
  creditedQuantity: number
  creditedAmount: number
  remainingQuantity: number
  remainingAmount: number
}

export type CreditNoteLine = {
  sequence: number
  sourceLineId: string
  quotationCode: string
  description: string
  unit: string
  originalQuantity: number
  originalNetAmount: number
  /** 0 = ลดเป็นยอดเงินอย่างเดียว */
  quantity: number
  amount: number
}

export type CreditNoteRefund = {
  id: string
  method: PaymentMethod
  amount: number
  reference?: string | null
  refundedByName: string
  refundedAt: string
}

export type CreditNote = {
  id: string
  jobId: string
  jobNo: string
  vehicleRegistration: string
  documentNo: string
  sourceType: CreditNoteSourceType
  isTaxDocument: boolean
  sourceDocumentNo: string
  sourceIssuedAt: string
  reason: CreditNoteReason
  reasonLabelTh: string
  reasonText?: string | null
  seller: TaxInvoiceParty
  buyer: TaxInvoiceParty
  originalNetAmount: number
  correctNetAmount: number
  diffNetAmount: number
  vatRate: number
  vatAmount: number
  totalAmount: number
  lines: CreditNoteLine[]
  refunds: CreditNoteRefund[]
  refundedAmount: number
  refundStatus: CreditNoteRefundStatus
  issuedByName: string
  issuedAt: string
}

/** source* ไม่มี = ยังไม่มีบิลให้อ้าง (API ตัดฟิลด์ null ทิ้ง — เช็คแบบ truthy) */
export type CreditNoteState = {
  sourceType?: CreditNoteSourceType | null
  sourceDocumentNo?: string | null
  sourceIssuedAt?: string | null
  vatRate: number
  originalNetAmount: number
  creditedNetAmount: number
  remainingNetAmount: number
  lines: CreditPlanLine[]
  issued: CreditNote[]
  canIssue: boolean
  blockedReasonTh?: string | null
  canRefund: boolean
}

export type RefundInput = { requestId: string; method: PaymentMethod; amount: number; reference: string | null }
export type IssueCreditNoteInput = {
  requestId: string
  reason: CreditNoteReason
  reasonText: string | null
  lines: { sourceLineId: string; quantity: number | null; amount: number | null }[]
  refund: RefundInput | null
}

export type CreditNoteReport = {
  from: string
  to: string
  count: number
  diffNetAmount: number
  vatAmount: number
  totalAmount: number
  refundedAmount: number
  outstandingRefund: number
  items: CreditNote[]
}

export type BranchTaxProfile = {
  sellerBranchNo: string
  isDefault: boolean
  updatedByName?: string | null
  updatedAt?: string | null
  canEdit: boolean
}

export const getCreditNoteState = (jobId: string) =>
  apiRequest<CreditNoteState>(`/api/v1/jobs/${jobId}/credit-notes`)

export const issueCreditNote = (jobId: string, input: IssueCreditNoteInput) =>
  apiRequest<CreditNote>(`/api/v1/jobs/${jobId}/credit-notes`, { method: 'POST', body: JSON.stringify(input) })

export const recordCreditNoteRefund = (creditNoteId: string, input: RefundInput) =>
  apiRequest<CreditNote>(`/api/v1/credit-notes/${creditNoteId}/refunds`, { method: 'POST', body: JSON.stringify(input) })

export const getCreditNoteReport = (from: string, to: string) =>
  apiRequest<CreditNoteReport>(`/api/v1/reports/credit-notes?${new URLSearchParams({ from, to })}`)

export const getBranchTaxProfile = () => apiRequest<BranchTaxProfile>('/api/v1/branch-tax-profile')

export const updateBranchTaxProfile = (sellerBranchNo: string) =>
  apiRequest<BranchTaxProfile>('/api/v1/branch-tax-profile', { method: 'PUT', body: JSON.stringify({ sellerBranchNo }) })

export const REFUND_STATUS_LABEL: Record<CreditNoteRefundStatus, string> = {
  none: 'ยังไม่คืนเงิน',
  partial: 'คืนเงินบางส่วน',
  full: 'คืนเงินครบแล้ว',
}

/** ช่องทางคืนเงิน — ชุดเดียวกับ PaymentMethod ของการรับเงิน */
export const REFUND_METHODS: { value: PaymentMethod; label: string }[] = [
  { value: 'cash', label: 'เงินสด' },
  { value: 'transfer', label: 'โอนเงิน' },
  { value: 'card', label: 'คืนเข้าบัตร' },
  { value: 'qr', label: 'พร้อมเพย์ (QR)' },
]
