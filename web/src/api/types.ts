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
  isAdministrator: boolean
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

export type StaffSummary = {
  id: number
  code: string
  fullName: string
  phoneNumber1: string | null
  email: string | null
  departmentName: string | null
  sectorName: string | null
  positionName: string | null
  pictureUrl: string | null
  isActive: boolean
  isAdministrator: boolean
  lastUpdated: string | null
}

export type StaffSectorPosition = {
  id: number
  departmentId: number
  departmentName: string
  sectorId: number
  sectorName: string
  positionId: number
  positionName: string
  isMain: boolean
}

export type StaffDetail = {
  id: number
  branchId: number
  branchName: string
  code: string
  firstName: string
  lastName: string
  genderId: number | null
  genderName: string | null
  idCard: string | null
  address1: string | null
  address2: string | null
  provinceId: number | null
  provinceName: string | null
  amphureId: number | null
  amphureName: string | null
  districtId: number | null
  districtName: string | null
  zipCode: string | null
  phoneNumber1: string | null
  phoneNumber2: string | null
  email: string | null
  lineId: string | null
  salary: number | null
  staffSkillLevelId: number | null
  staffSkillLevelName: string | null
  experienceYear: number | null
  experienceMonth: number | null
  startJobDate: string | null
  endJobDate: string | null
  note: string | null
  pictureUrl: string | null
  isActive: boolean
  account: { id: number; userName: string; isAdministrator: boolean; isActive: boolean }
  sectorPositions: StaffSectorPosition[]
  createdDate: string | null
  lastUpdated: string | null
}

export type StaffInput = {
  branchId: number
  firstName: string
  lastName: string
  genderId: number
  idCard?: string
  address1?: string
  address2?: string
  provinceId?: number
  amphureId?: number
  districtId?: number
  zipCode?: string
  phoneNumber1: string
  phoneNumber2?: string
  email?: string
  lineId?: string
  salary: number
  staffSkillLevelId?: number
  experienceYear: number
  experienceMonth: number
  startJobDate?: string
  endJobDate?: string
  note?: string
  mainSectorId: number
  positionId: number
  additionalSectorIds: number[]
  userName?: string
  password?: string
}

export type StaffReferenceData = {
  branches: LookupItem[]
  genders: LookupItem[]
  departments: LookupItem[]
  positions: LookupItem[]
  skillLevels: LookupItem[]
}

export type StaffCodePreview = { code: string; userName: string; password: string }

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
  jobId: string
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
  jobId: string
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

export type JobStatusToken =
  | 'waitinspect'
  | 'waitquote'
  | 'waitapprove'
  | 'approved'
  | 'inprogress'
  | 'waitparts'
  | 'qc'
  | 'ready'
  | 'completed'
  | 'cancelled'

export type Job = {
  jobId: string
  jobNo: string
  branchId: number
  branchName: string
  customerId: number
  customerName: string
  customerPhone: string | null
  vehicleId: number
  vehicleImagePath: string | null
  vehicleRegistration: string
  vehicleModel: string | null
  vehicleVin: string | null
  createdAt: string
  promiseAt: string | null
  jobTypeId: number
  jobTypeName: string | null
  status: JobStatusToken
  statusLabel: string
  isOverdue: boolean
}

export type TransitionJobInput = { toStatus: JobStatusToken | string; reason?: string }
export type JobTransitionResult = { status: JobStatusToken; statusLabel: string }

export type JobStatusOption = { token: string; label: string }

export type AttachmentKind =
  | 'signature'
  | 'intake'
  | 'inspection'
  | 'repair-before'
  | 'repair-after'
  | 'qc'
  | 'document'

export type Attachment = {
  id: string
  kind: AttachmentKind
  entityId: string | null
  fileName: string
  contentType: string
  sizeBytes: number
  relativePath: string
  url: string
  uploadedByName: string
  uploadedAt: string
}

export type IntakeCheckResult = 'pending' | 'ok' | 'issue' | 'na'

export type IntakeChecklistTemplateItem = {
  categoryKey: string
  categoryLabelTh: string
  itemCode: string
  labelTh: string
  hintTh: string | null
}

export type IntakeChecklistItem = {
  id: string
  itemCode: string
  categoryKey: string
  labelTh: string
  hintTh: string | null
  result: IntakeCheckResult
  note: string | null
  updatedAt: string | null
  updatedByUserName: string | null
}

export type IntakeChecklist = {
  id: string
  jobId: string
  isLocked: boolean
  submittedAt: string | null
  submittedByUserName: string | null
  items: IntakeChecklistItem[]
}

export type SaveIntakeChecklistItemInput = { result: IntakeCheckResult; note?: string }

export type SubmitIntakeChecklistResult = { id: string; submittedAt: string }

export type CreateJobInput = {
  customerId: number
  vehicleId: number
  jobTypeId: number
  senderName?: string
  senderPhoneNumber?: string
  detail?: string
}

export type CreatedJob = { jobId: string; jobNo: string }

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

export type CatalogManagementItem = CatalogItem & {
  stockLocked: boolean
  id: string
  typeLabelTh: string
  damaged: number
  isActive: boolean
  categoryId: string | null
  warehouseId: string | null
}

export type CatalogItemInput = {
  code: string
  type: 'part' | 'labor'
  name: string
  compatibility?: string
  unit: string
  cost: number
  price: number
  standardHours?: number
  onHand: number
  reserved: number
  onOrder: number
  damaged: number
  etaNote?: string
  categoryId?: string
  warehouseId?: string
}

export type CatalogItemSupplier = {
  id: string
  catalogItemId: string
  supplierId: string
  supplierCode: string
  supplierName: string
  supplierItemCode: string | null
  supplierCost: number | null
  leadTimeDays: number | null
  minOrderQty: number | null
  isPreferred: boolean
  isActive: boolean
  createdDate: string
  lastUpdated: string
}

export type CatalogItemSupplierInput = {
  supplierItemCode?: string
  supplierCost: number
  leadTimeDays?: number
  minOrderQty?: number
  isPreferred: boolean
  isActive: boolean
}

export type Supplier = {
  id: string
  code: string
  name: string
  contactName: string | null
  phone: string | null
  email: string | null
  address: string | null
  taxId: string | null
  paymentTerms: string | null
  note: string | null
  isActive: boolean
  createdDate: string
  lastUpdated: string
}
export type SupplierInput = Omit<Supplier, 'id' | 'isActive' | 'createdDate' | 'lastUpdated'>

export type Warehouse = {
  id: string
  code: string
  name: string
  legacyShardKey: string | null
  legacyBranchId: number | null
  address: string | null
  isActive: boolean
  createdDate: string
  lastUpdated: string
}
export type WarehouseInput = { code: string; name: string; address?: string }

export type CatalogCategory = {
  id: string
  code: string
  name: string
  parentCategoryId: string | null
  sortOrder: number | null
  hasChildren: boolean
  isActive: boolean
  createdDate: string
  lastUpdated: string
  children?: CatalogCategory[]
}
export type CatalogCategoryInput = {
  code: string
  name: string
  parentCategoryId?: string
  sortOrder?: number
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
  jobId: string
  validUntil?: string
  depositAmount?: number
}

export type PagedResult<T> = {
  items: T[]
  page: number
  pageSize: number
  totalItems: number
  totalPages: number
}

export type LookupItem = { id: number; name: string; secondary?: string | null }

export type CustomerSummary = {
  id: number
  code: string
  firstName: string
  lastName: string
  fullName: string
  idCard: string | null
  phoneNumber1: string | null
  phoneNumber2: string | null
  email: string | null
  provinceName: string | null
  isBlacklist: boolean
  blacklistRemark: string | null
  isDeleted: boolean
  vehicleCount: number
  lastUpdated: string | null
}

export type CustomerVehicleSummary = {
  id: number
  registration: string
  provinceName: string | null
  brandName: string | null
  modelName: string | null
  nickname: string | null
  year: string | null
  imageUrl: string | null
  isDeleted: boolean
}

export type CustomerDetail = {
  id: number
  code: string
  firstName: string
  lastName: string
  idCard: string | null
  driverLicense: string | null
  genderId: number | null
  dateOfBirth: string | null
  address1: string | null
  address2: string | null
  provinceId: number | null
  provinceName: string | null
  amphureId: number | null
  amphureName: string | null
  districtId: number | null
  districtName: string | null
  zipCode: string | null
  phoneNumber1: string | null
  phoneNumber2: string | null
  email: string | null
  lineId: string | null
  isBlacklist: boolean
  blacklistRemark: string | null
  isDeleted: boolean
  createdDate: string | null
  lastUpdated: string | null
  vehicles: CustomerVehicleSummary[]
}

export type CustomerInput = {
  firstName: string
  lastName: string
  phoneNumber1: string
  phoneNumber2?: string
  idCard?: string
  driverLicense?: string
  genderId?: number
  dateOfBirth?: string
  address1?: string
  address2?: string
  provinceId?: number
  amphureId?: number
  districtId?: number
  zipCode?: string
  email?: string
  lineId?: string
  isBlacklist: boolean
  blacklistRemark?: string
}

export type CustomerDuplicate = {
  id: number
  code: string
  fullName: string
  phoneNumber1: string
  email: string | null
  isDeleted: boolean
}

export type VehicleSummary = {
  id: number
  registration: string
  provinceName: string | null
  brandName: string | null
  modelName: string | null
  nickname: string | null
  carTypeName: string | null
  year: string | null
  primaryColorName: string | null
  ownerName: string | null
  ownerPhone: string | null
  imageUrl: string | null
  isDeleted: boolean
  lastUpdated: string | null
}

export type VehicleOwner = {
  id: number
  code: string
  fullName: string
  phoneNumber1: string | null
  idCard: string | null
}

export type VehicleDetail = {
  id: number
  registration: string
  provinceId: number | null
  provinceName: string | null
  brandId: number | null
  brandName: string | null
  modelId: number | null
  modelName: string | null
  nicknameId: number | null
  nickname: string | null
  carTypeId: number | null
  carTypeName: string | null
  yearId: number | null
  year: string | null
  primaryColorId: number | null
  primaryColorName: string | null
  colorMixId: number | null
  colorMixName: string | null
  gearId: number | null
  gearName: string | null
  machineId: number | null
  machineName: string | null
  driveSystemId: number | null
  driveSystemName: string | null
  vin: string | null
  engineNumber: string | null
  insuranceId: number | null
  insuranceName: string | null
  insuranceExpiredDate: string | null
  imageUrl: string | null
  isDeleted: boolean
  createdDate: string | null
  lastUpdated: string | null
  owners: VehicleOwner[]
}

export type VehicleInput = {
  customerId: number
  registration: string
  provinceId: number
  brandId: number
  modelId: number
  nicknameId: number
  yearId: number
  primaryColorId?: number
  colorMixId?: number
  gearId?: number
  machineId?: number
  driveSystemId?: number
  vin?: string
  engineNumber?: string
  insuranceId?: number
  insuranceExpiredDate?: string
}

export type VehicleReferenceData = {
  brands: LookupItem[]
  years: LookupItem[]
  carTypes: LookupItem[]
  insurances: LookupItem[]
  gears: LookupItem[]
  machines: LookupItem[]
  driveSystems: LookupItem[]
  primaryColors: LookupItem[]
  colorMixes: LookupItem[]
}
