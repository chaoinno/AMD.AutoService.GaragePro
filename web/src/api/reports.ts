import { apiRequest } from './client'

export type JobStatusCount = { status: string; statusLabelTh: string; count: number }
export type OverdueJob = { jobId: string; jobNo: string; customerName: string; vehicleRegistration: string; status: string; statusLabelTh: string; promiseAt: string }
export type DashboardReport = {
  jobsByStatus: JobStatusCount[]
  overdueCount: number
  overdueJobs: OverdueJob[]
  waitingQcCount: number
  waitingPaymentCount: number
  collectedToday: number
  receiptsIssuedToday: number
  /// API เก่า (ก่อนมี POS) ไม่ส่งฟิลด์นี้ — ผู้ใช้ต้องรับกรณี undefined
  retailToday?: RetailSalesToday
}

export type RetailSalesToday = { billCount: number; totalAmount: number; itemQuantity: number; draftCount: number }

export type StatusDuration = { status: string; statusLabelTh: string; segmentCount: number; averageHours: number }
export type StuckJob = { jobId: string; jobNo: string; customerName: string; status: string; statusLabelTh: string; hoursInStatus: number; promiseAt: string | null; isOverdue: boolean }
export type CycleTimeReport = {
  fromDate: string
  toDate: string
  averageDurationByStatus: StatusDuration[]
  completedJobCount: number
  averageTotalHours: number | null
  p90TotalHours: number | null
  topStuckJobs: StuckJob[]
}

export type LineTypeTotals = { type: string; netAmount: number; costAmount: number | null; marginAmount: number | null }
export type TechnicianRevenue = { technicianName: string; lineCount: number; netAmount: number }
export type SalesMarginReport = {
  fromDate: string
  toDate: string
  quotationCount: number
  netAmount: number
  costAmount: number | null
  marginAmount: number | null
  marginPercent: number | null
  byType: LineTypeTotals[]
  byTechnician: TechnicianRevenue[]
}

export type StockAgingBucket = { bucketLabelTh: string; minDays: number; maxDays: number | null; value: number }
export type AgingStockLot = { catalogCode: string; catalogName: string; warehouseName: string; remainingQuantity: number; unitCost: number; ageDays: number }
export type StockReport = {
  totalValuation: number
  damagedValuation: number
  agingBuckets: StockAgingBucket[]
  oldestLots: AgingStockLot[]
}

export const getDashboardReport = () => apiRequest<DashboardReport>('/api/v1/reports/dashboard')

export const getCycleTimeReport = (fromDate?: string, toDate?: string) => {
  const params = new URLSearchParams()
  if (fromDate) params.set('fromDate', fromDate)
  if (toDate) params.set('toDate', toDate)
  const qs = params.toString()
  return apiRequest<CycleTimeReport>(`/api/v1/reports/cycle-time${qs ? `?${qs}` : ''}`)
}

export const getSalesMarginReport = (fromDate?: string, toDate?: string) => {
  const params = new URLSearchParams()
  if (fromDate) params.set('fromDate', fromDate)
  if (toDate) params.set('toDate', toDate)
  const qs = params.toString()
  return apiRequest<SalesMarginReport>(`/api/v1/reports/sales-margin${qs ? `?${qs}` : ''}`)
}

export const getStockReport = () => apiRequest<StockReport>('/api/v1/reports/stock')

/// ช่วงเวลาทำงานของช่างหนึ่งช่วง — มิเรอร์ `WorkIntervalDto` ฝั่ง backend
export type WorkIntervalRow = {
  id: string
  jobId: string
  jobNo: string
  vehicleRegistration: string
  technicianStaffId: number
  technicianName: string
  /** `work` หรือ `pause` */
  kind: string
  startedAt: string
  endedAt: string | null
  endReason: string | null
  /** null = ยังจับเวลาอยู่ */
  durationSeconds: number | null
  isRework: boolean
  /** ระบบตัดให้เองเพราะลืมกดหยุด — ไม่เข้าการคำนวณจนกว่าจะมีคนยืนยันเวลาจริง */
  isAutoCapped: boolean
  isVoided: boolean
  editedAt: string | null
  editReason: string | null
  voidReason: string | null
}

export const getWorkIntervals = (fromDate?: string, toDate?: string, onlyNeedsReview = false) => {
  const params = new URLSearchParams()
  if (fromDate) params.set('fromDate', fromDate)
  if (toDate) params.set('toDate', toDate)
  if (onlyNeedsReview) params.set('onlyNeedsReview', 'true')
  const qs = params.toString()
  return apiRequest<WorkIntervalRow[]>(`/api/v1/work/intervals${qs ? `?${qs}` : ''}`)
}

export const updateWorkInterval = (
  id: string,
  input: { startedAt: string; endedAt: string; reason: string },
) =>
  apiRequest<WorkIntervalRow>(`/api/v1/work/intervals/${id}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })

export const voidWorkInterval = (id: string, reason: string) =>
  apiRequest<WorkIntervalRow>(`/api/v1/work/intervals/${id}`, {
    method: 'DELETE',
    body: JSON.stringify({ reason }),
  })

// ---------- ขายหน้าร้าน (docs/11-retail-sale-pos.md) ----------
// ต้นทุน/กำไรเป็น null (หรือไม่มาเลยเพราะ WhenWritingNull) เมื่อผู้ใช้ไม่มีสิทธิ์เห็นต้นทุน
export type RetailDaily = { date: string; billCount: number; totalAmount: number }
export type RetailPaymentMethod = { method: 'cash' | 'transfer' | 'card' | 'qr'; paymentCount: number; amount: number }
export type RetailTopProduct = { code: string; name: string; unit: string; quantity: number; billCount: number; netAmount: number; costAmount?: number | null; marginAmount?: number | null }
export type RetailPromotionUsage = { name: string; scope: 'line' | 'bill'; useCount: number; discountAmount: number }
export type RetailSeller = { sellerName: string; billCount: number; totalAmount: number }
export type RetailVoidedSale = { saleId: string; receiptNo?: string | null; completedAt?: string | null; voidedAt?: string | null; voidedByName?: string | null; voidReason?: string | null; totalAmount: number }
export type RetailSalesReport = {
  fromDate: string
  toDate: string
  billCount: number
  totalAmount: number
  netAmount: number
  vatAmount: number
  averageBillAmount: number
  discountAmount: number
  itemQuantity: number
  costAmount?: number | null
  marginAmount?: number | null
  marginPercent?: number | null
  voidedCount: number
  voidedAmount: number
  daily: RetailDaily[]
  byPaymentMethod: RetailPaymentMethod[]
  topProducts: RetailTopProduct[]
  promotions: RetailPromotionUsage[]
  bySeller: RetailSeller[]
  voidedSales: RetailVoidedSale[]
}

/// fromDate/toDate = วันที่ตามปฏิทินไทย yyyy-MM-dd (รวมทั้งสองวัน) — server แปลงเป็นขอบเขต UTC เอง
export const getRetailSalesReport = (fromDate: string, toDate: string) =>
  apiRequest<RetailSalesReport>(`/api/v1/reports/retail-sales?fromDate=${fromDate}&toDate=${toDate}`)

// ---------- ประวัติรถ (ทุกบทบาท — ช่าง/หัวหน้าช่างไม่เห็นตัวเงิน: amount/receiptTotal ไม่มีใน JSON) ----------

export type VehicleHistoryMatch = {
  vehicleId: number
  vehicleRegistration: string
  vehicleModel?: string | null
  customerName: string
  customerPhone?: string | null
  visitCount: number
  lastVisitAt: string
  lastJobNo: string
}
export type VehicleHistorySearch = { items: VehicleHistoryMatch[]; truncated: boolean }

export type VehicleHistoryLine = {
  quotationCode: string
  name: string
  type: 'part' | 'labor'
  quantity: number
  unit: string
  /** ยอดก่อน VAT — ไม่มีเมื่อบทบาทนี้ไม่เห็นตัวเงิน */
  amount?: number | null
  technicianName?: string | null
}
export type VehicleHistoryVisit = {
  jobId: string
  jobNo: string
  openedAt: string
  handedOverAt?: string | null
  jobTypeName?: string | null
  status: string
  statusLabelTh: string
  detail?: string | null
  mileageAtIntake?: number | null
  mileageAtHandover?: number | null
  lines: VehicleHistoryLine[]
  receiptDocumentNo?: string | null
  receiptTotal?: number | null
  nextServiceMileage?: number | null
  /** yyyy-MM-dd */
  nextServiceDueOn?: string | null
}
export type VehicleHistory = {
  vehicleId: number
  vehicleRegistration: string
  vehicleModel?: string | null
  vehicleVin?: string | null
  customerName: string
  customerPhone?: string | null
  showAmounts: boolean
  nextService?: { fromJobNo: string; mileage?: number | null; dueOn: string } | null
  visits: VehicleHistoryVisit[]
}

export const searchVehicleHistory = (q: string) =>
  apiRequest<VehicleHistorySearch>(`/api/v1/reports/vehicle-history/search?q=${encodeURIComponent(q)}`)

export const getVehicleHistory = (vehicleId: number) =>
  apiRequest<VehicleHistory>(`/api/v1/reports/vehicle-history/${vehicleId}`)

// ---------- รถใกล้ครบรอบบริการ (ผู้จัดการ/ธุรการ) ----------

export type ServiceDueItem = {
  vehicleId: number
  vehicleRegistration: string
  vehicleModel?: string | null
  customerName: string
  customerPhone?: string | null
  lastJobId: string
  lastJobNo: string
  handedOverAt: string
  mileageAtHandover?: number | null
  nextServiceMileage?: number | null
  /** yyyy-MM-dd */
  nextServiceDueOn: string
  /** ติดลบ = เลยกำหนดมาแล้วกี่วัน */
  daysUntilDue: number
}
export type ServiceDueReport = {
  fromDate: string
  toDate: string
  today: string
  overdueCount: number
  dueWithin7DaysCount: number
  dueWithin30DaysCount: number
  items: ServiceDueItem[]
}

export const getServiceDueReport = (fromDate: string, toDate: string) =>
  apiRequest<ServiceDueReport>(`/api/v1/reports/service-due?fromDate=${fromDate}&toDate=${toDate}`)
