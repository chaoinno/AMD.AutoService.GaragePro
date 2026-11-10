import { apiRequest } from './client'

/** ผู้ประกอบการ (อู่) ที่หัวรายงานภาษี — branchNo "00000" = สำนักงานใหญ่ · taxId ไม่มี = ข้อมูลสาขาไม่ใช่เลข 13 หลัก */
export type TaxReportParty = { name: string; address?: string; taxId?: string; branchNo: string }

/** taxInvoice = ใบกำกับ IV- · creditNote = ใบลดหนี้ (ยอดติดลบ) · receipt = ใบเสร็จงานซ่อม RC- · sale = บิลขายหน้าร้าน SL- */
export type OutputVatRowKind = 'taxInvoice' | 'creditNote' | 'receipt' | 'sale'

export type OutputVatRow = {
  sequence: number
  kind: OutputVatRowKind
  id: string
  jobId?: string
  documentNo: string
  issuedAt: string
  buyerName: string
  buyerTaxId?: string
  buyerBranchNo?: string
  netAmount: number
  vatAmount: number
  totalAmount: number
  /** ใบลดหนี้ = บิลที่อ้าง · ใบเสร็จ = เลขที่งาน */
  referenceDocumentNo?: string
}

/** docs/20 §3 (Q1): rows = รายการตามกฎหมาย · uninvoiced = รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับ แยกไว้ให้บัญชีเห็นยอดครบ */
export type OutputVatReport = {
  year: number
  month: number
  seller?: TaxReportParty
  rows: OutputVatRow[]
  netAmount: number
  vatAmount: number
  totalAmount: number
  uninvoiced: OutputVatRow[]
  uninvoicedNetAmount: number
  uninvoicedVatAmount: number
  uninvoicedTotalAmount: number
}

export type PurchaseTaxInvoice = {
  id: string
  supplierId?: string
  supplierName: string
  supplierTaxId: string
  supplierBranchNo: string
  invoiceNo: string
  /** yyyy-MM-dd ตามใบกำกับ (ไม่มีเวลา) */
  invoiceDate: string
  claimYear: number
  claimMonth: number
  netAmount: number
  vatAmount: number
  totalAmount: number
  purchaseOrderId?: string
  purchaseOrderNumber?: string
  goodsReceiptId?: string
  goodsReceiptNumber?: string
  note?: string
  recordedByName: string
  recordedAt: string
  isVoided: boolean
  voidedAt?: string
  voidedByName?: string
  voidReason?: string
}

export type PendingGoodsReceipt = {
  goodsReceiptId: string
  number: string
  receivedAt: string
  purchaseOrderNumber: string
  supplierName?: string
  deliveryNumber?: string
  netAmount: number
  vatAmount: number
}

export type InputVatReport = {
  year: number
  month: number
  buyer?: TaxReportParty
  rows: PurchaseTaxInvoice[]
  netAmount: number
  vatAmount: number
  totalAmount: number
  voided: PurchaseTaxInvoice[]
  pendingGoodsReceipts: PendingGoodsReceipt[]
}

export type PurchaseTaxInvoicePrefill = {
  goodsReceiptId: string
  goodsReceiptNumber: string
  purchaseOrderId: string
  purchaseOrderNumber: string
  supplierId?: string
  supplierName?: string
  supplierTaxId?: string
  deliveryNumber?: string
  receivedDate: string
  hasVat: boolean
  vatRate: number
  netAmount: number
  vatAmount: number
}

export type RecordPurchaseTaxInvoiceInput = {
  requestId: string
  supplierId: string | null
  supplierName: string
  supplierTaxId: string
  supplierBranchNo: string
  invoiceNo: string
  invoiceDate: string
  claimYear: number
  claimMonth: number
  netAmount: number
  vatAmount: number
  goodsReceiptId: string | null
  note: string | null
}

/** ใช้สิทธิ์ภาษีซื้อได้ในเดือนของใบกำกับหรือภายใน 6 เดือนถัดไป — ต้องตรงกับ TaxReportService.ClaimWindowMonths */
export const CLAIM_WINDOW_MONTHS = 6

const period = (year: number, month: number) => new URLSearchParams({ year: String(year), month: String(month) })

export const getOutputVatReport = (year: number, month: number) =>
  apiRequest<OutputVatReport>(`/api/v1/reports/output-vat?${period(year, month)}`)

export const getInputVatReport = (year: number, month: number) =>
  apiRequest<InputVatReport>(`/api/v1/reports/input-vat?${period(year, month)}`)

export const getPurchaseTaxInvoicePrefill = (goodsReceiptId: string) =>
  apiRequest<PurchaseTaxInvoicePrefill>(`/api/v1/purchase-tax-invoices/prefill?${new URLSearchParams({ goodsReceiptId })}`)

export const recordPurchaseTaxInvoice = (input: RecordPurchaseTaxInvoiceInput) =>
  apiRequest<PurchaseTaxInvoice>('/api/v1/purchase-tax-invoices', { method: 'POST', body: JSON.stringify(input) })

export const voidPurchaseTaxInvoice = (id: string, reason: string) =>
  apiRequest<PurchaseTaxInvoice>(`/api/v1/purchase-tax-invoices/${id}/void`, { method: 'POST', body: JSON.stringify({ reason }) })

export const THAI_MONTHS = ['มกราคม', 'กุมภาพันธ์', 'มีนาคม', 'เมษายน', 'พฤษภาคม', 'มิถุนายน',
  'กรกฎาคม', 'สิงหาคม', 'กันยายน', 'ตุลาคม', 'พฤศจิกายน', 'ธันวาคม']

/** "พฤศจิกายน 2569" — ปีตามปฏิทินไทย */
export const formatTaxMonth = (year: number, month: number) => `${THAI_MONTHS[month - 1]} ${year + 543}`
