export type Envelope<T> = {
  success: boolean
  data: T | null
  error: {
    code: string
    messageTh: string
    field?: string
    details?: unknown
  } | null
  traceId: string
}

export type ApiError = Error & {
  code: string
  messageTh: string
  traceId: string
  field?: string
  details?: unknown
  status?: number
}

export type AuthUser = {
  userId: number
  userName: string
  displayName: string
  role: string
  roleLabelTh: string
  shardKey: string
  staffId: number | null
  positionName: string | null
  departmentName: string | null
  canSeeCost: boolean
  canCloseShift: boolean
}

export type BranchOption = {
  branchId: number
  name: string
  address: string | null
  phone: string | null
  pendingQuotationCount: number
  waitingApprovalCount: number
}

export type ShiftOption = {
  shiftId: string
  name: string
  startTime: string
  endTime: string
  supervisorName: string | null
  isCurrent: boolean
}

export type LoginResult = {
  accessToken: string
  expiresAt: string
  user: AuthUser
  branchId: number
  branchName: string
  branches: BranchOption[]
  requiresShiftSelection: boolean
}

export type MeResult = {
  user: AuthUser
  sessionId: string | null
  branchId: number | null
  branchName: string | null
  shiftId: string | null
  shiftName: string | null
  openedAt: string | null
}

export type QuotationStatus =
  | 'draft'
  | 'sent'
  | 'partial'
  | 'approved'
  | 'rejected'
  | 'superseded'
  | 'expired'

export type LineSource = 'customer' | 'technician'
export type UpsertLineSource = 'Customer' | 'Technician'
export type ApprovalStatus = 'pending' | 'approved' | 'rejected'
export type Promotion = 0 | 1 | 2 | 3

export type QuotationSummary = {
  id: string
  code: string
  version: number
  status: QuotationStatus
  statusLabelTh: string
  jobId: number
  jobNo: string
  customerName: string
  vehicleRegistration: string
  vehicleModel: string | null
  total: number
  createdAt: string
  sentAt: string | null
  ageLabelTh: string | null
}

export type Customer = {
  name: string
  phone: string | null
  taxId?: string | null
  address?: string | null
}

export type Vehicle = {
  registration: string
  model: string | null
  vin?: string | null
  mileage?: number | null
}

export type Branch = {
  name: string
  address: string | null
  taxId: string | null
  phone: string | null
}

export type QuotationLine = {
  id: string
  sequence: number
  catalogCode: string
  name: string
  type: 'part' | 'labor'
  source: LineSource
  quantity: number
  unit: string
  unitPrice: number
  unitCost: number | null
  discountPercent: number
  promotion: Promotion
  promotionLabel: string | null
  assignedTechnicianId: number | null
  assignedTechnicianName: string | null
  note: string | null
  standardHours: number | null
  approvalStatus: ApprovalStatus
  rejectReason: string | null
  grossAmount: number
  discountAmount: number
  promotionAmount: number
  netAmount: number
  marginAmount: number | null
}

export type ApprovedTotals = {
  approvedCount: number
  rejectedCount: number
  pendingCount: number
  net: number
  vat: number
  total: number
  grandTotal: number
}

export type QuotationTotals = {
  gross: number
  lineDiscount: number
  promotion: number
  net: number
  vatRate: number
  vat: number
  total: number
  deposit: number
  grandTotal: number
  totalCost: number | null
  marginAmount: number | null
  marginPercent: number | null
  partsNet: number
  laborNet: number
  laborHours: number
  approved: ApprovedTotals | null
}

export type QuotationApproval = {
  quotationVersion: number
  signatureImagePath: string
  signedAt: string
  consentText: string
  deviceInfo: string | null
  witnessEmployeeName: string
  approvedLineCount: number
  rejectedLineCount: number
  approvedNetAmount: number
}

export type QuotationLock = {
  userId: number
  userName: string
  lockedAt?: string
}

export type Quotation = {
  id: string
  code: string
  version: number
  status: QuotationStatus
  statusLabelTh: string
  jobId: number
  jobNo: string
  customer: Customer
  vehicle: Vehicle
  branch: Branch
  lines: QuotationLine[]
  totals: QuotationTotals
  approval: QuotationApproval | null
  supersedesQuotationId?: string | null
  supersededByQuotationId?: string | null
  revisionReason: string | null
  validUntil: string | null
  isExpired: boolean
  sentAt: string | null
  createdByUserName: string
  createdAt: string
  lock: QuotationLock | null
}

export type UpsertLine = {
  catalogCode: string
  quantity: number
  unitPrice?: number
  discountPercent: number
  promotion: Promotion
  source: UpsertLineSource
  assignedTechnicianId?: number
  note?: string
}

export type LegacyJob = {
  jobId: number
  jobNo: string
  branchId: number
  branchName: string
  customerId: number | null
  customerName: string
  customerPhone: string | null
  carId: number | null
  vehicleImagePath: string | null
  vehicleRegistration: string
  vehicleModel: string | null
  vehicleVin: string | null
  createdDate: string
  promiseAt: string | null
  legacyStatusName: string | null
  pjTypeId: number
  pjTypeName: string | null
  pjStatusId: number
}

export type JobStatusOption = { id: number; name: string }

export type JobLookup = { id: number; name: string }
export type JobModelLookup = JobLookup & { brandId: number }
export type JobNicknameLookup = JobLookup & { brandId: number; modelId: number }
export type JobColorLookup = JobLookup & { htmlCode: string | null }
export type JobFormOptions = {
  brands: JobLookup[]
  models: JobModelLookup[]
  nicknames: JobNicknameLookup[]
  primaryColors: JobColorLookup[]
}

export type CreateJobInput = {
  carNumberGroup: string
  carNumber: string
  brandId: number
  modelId: number
  carNicknameId: number
  colorType: number
  pjTypeId: number
  primaryColorId?: number
  senderFirstName?: string
  senderLastName?: string
  senderPhoneNumber?: string
  detail?: string
}

export type CreatedJob = { jobId: number; jobNo: string }

export type CatalogItem = {
  catalogCode?: string
  code: string
  name: string
  type: 'part' | 'labor'
  compatibility: string | null
  unit: string
  unitPrice?: number
  price: number
  cost: number | null
  standardHours: number | null
  onHand: number
  reserved: number
  onOrder: number
  available: number
  etaNote: string | null
}

export type Technician = {
  staffId: number
  name: string
  skillLevel: string | null
}

export type ValidationIssue =
  | string
  | {
      code?: string
      message?: string
      messageTh?: string
      field?: string
    }

export type QuotationValidation = {
  isValid: boolean
  errors: ValidationIssue[]
  warnings: ValidationIssue[]
}

export type CreateQuotationInput = {
  jobId: number
  validUntil?: string
  depositAmount?: number
}

export type AttachmentKind =
  | 'signature' | 'intake' | 'inspection' | 'repair-before' | 'repair-after' | 'qc' | 'document'

export type Attachment = {
  id: string
  kind: AttachmentKind
  fileName: string
  contentType: string
  sizeBytes: number
  relativePath: string
  url: string
  uploadedByName: string
  uploadedAt: string
}
