import { apiRequest, isApiError } from './client'

export type PaymentMethod = 'cash' | 'transfer' | 'card' | 'qr'
export type Payment = { id: string; method: PaymentMethod; amount: number; reference: string | null; receivedByName: string; receivedAt: string }
export type PaymentReceipt = { id: string; documentNo: string; netAmount: number; vatAmount: number; totalAmount: number; issuedByName: string; issuedAt: string }
export type PaymentSummary = {
  jobId: string
  netAmount: number
  vatAmount: number
  grandTotal: number
  paidAmount: number
  remainingAmount: number
  balanceSettled: boolean
  vatIncluded: boolean
  /** ล็อกแก้ vatIncluded ไม่ได้อีก — เริ่มบันทึกชำระเงินหรือออกใบเสร็จไปแล้ว */
  vatLocked: boolean
  payments: Payment[]
  receipt: PaymentReceipt | null
  /** ใบเสนอราคาที่รวมอยู่ในยอดนี้ — จ๊อบมีได้หลายใบ (บิลแยกเฉพาะใบเสนอราคา ใบเสร็จรวม) */
  quotationCodes: string[]
  /** ส่งแล้วแต่ลูกค้ายังไม่จบ — มีอยู่ = ออกใบเสร็จไม่ได้ (POS_QUOTATION_AWAITING_CUSTOMER) */
  awaitingCustomerQuotationCodes: string[]
}
export type RecordPaymentInput = { method: PaymentMethod; amount: number; reference: string | null; requestId: string }

export const getPaymentSummary = (jobId: string) =>
  apiRequest<PaymentSummary>(`/api/v1/jobs/${jobId}/payment-summary`)

export const recordPayment = (jobId: string, input: RecordPaymentInput) =>
  apiRequest<Payment>(`/api/v1/jobs/${jobId}/payments`, { method: 'POST', body: JSON.stringify(input) })

export const removePayment = (jobId: string, paymentId: string, reason: string) =>
  apiRequest<boolean>(
    `/api/v1/jobs/${jobId}/payments/${paymentId}?${new URLSearchParams({ reason })}`,
    { method: 'DELETE' },
  )

export const issueReceipt = (jobId: string) =>
  apiRequest<PaymentReceipt>(`/api/v1/jobs/${jobId}/receipt`, { method: 'POST' })

export const setVatIncluded = (jobId: string, included: boolean) =>
  apiRequest<PaymentSummary>(`/api/v1/jobs/${jobId}/payment-vat`, {
    method: 'PUT',
    body: JSON.stringify({ included }),
  })

/** branchNo: "00000" = สำนักงานใหญ่ · เลข 5 หลักอื่น = สาขา · ไม่มี = ไม่ระบุ (API ตัดฟิลด์ null ทิ้ง — เช็คแบบ truthy) */
export type TaxInvoiceParty = { name: string; address?: string | null; taxId?: string | null; phone?: string | null; branchNo?: string | null }
export type TaxInvoiceLine = {
  sequence: number
  quotationCode: string
  description: string
  quantity: number
  unit: string
  unitPrice: number
  discountAmount: number
  netAmount: number
}
export type TaxInvoice = {
  id: string
  documentNo: string
  receiptDocumentNo: string
  seller: TaxInvoiceParty
  buyer: TaxInvoiceParty
  vatRate: number
  netAmount: number
  vatAmount: number
  totalAmount: number
  lines: TaxInvoiceLine[]
  issuedByName: string
  issuedAt: string
}
export type IssueTaxInvoiceInput = { buyerName: string; buyerAddress: string; buyerTaxId: string | null; buyerBranchNo: string | null }
export type TaxInvoiceState = {
  issued?: TaxInvoice | null
  seller?: TaxInvoiceParty | null
  prefill: { buyerName: string; buyerAddress: string; buyerTaxId?: string | null; buyerBranchNo?: string | null }
  canIssue: boolean
  /** ไม่ว่าง = ยังออกไม่ได้ — server คำนวณเหตุผลเอง */
  blockedReasonTh?: string | null
}

export const getTaxInvoiceState = (jobId: string) =>
  apiRequest<TaxInvoiceState>(`/api/v1/jobs/${jobId}/tax-invoice`)

export const issueTaxInvoice = (jobId: string, input: IssueTaxInvoiceInput) =>
  apiRequest<TaxInvoice>(`/api/v1/jobs/${jobId}/tax-invoice`, { method: 'POST', body: JSON.stringify(input) })

// เก็บ RequestId+payload ค้างไว้ข้ามการปิด modal/reload กันชำระซ้ำเมื่อ retry (pattern เดียวกับ purchasing.ts)
export function pendingPaymentCommand(key: string): RecordPaymentInput | null {
  try { return JSON.parse(sessionStorage.getItem(key) || 'null') as RecordPaymentInput | null } catch { return null }
}
export async function paymentCommand<R>(key: string, input: RecordPaymentInput, send: (input: RecordPaymentInput) => Promise<R>) {
  sessionStorage.setItem(key, JSON.stringify(input))
  try { const result = await send(input); sessionStorage.removeItem(key); return result }
  catch (error) { if (isApiError(error) && error.status && error.status < 500) sessionStorage.removeItem(key); throw error }
}
