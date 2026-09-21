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
}

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
