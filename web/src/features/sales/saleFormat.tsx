import { Ban, CheckCircle2, CircleAlert, FileEdit, XCircle, type LucideIcon } from 'lucide-react'
import { isApiError } from '../../api/client'
import type { PaymentMethod, SaleStatus } from '../../api/sales'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'

const statusMeta: Record<SaleStatus, { label: string; icon: LucideIcon }> = {
  draft: { label: 'ร่าง', icon: FileEdit },
  completed: { label: 'ชำระแล้ว', icon: CheckCircle2 },
  voided: { label: 'ยกเลิกบิลแล้ว', icon: Ban },
  cancelled: { label: 'ยกเลิกร่าง', icon: XCircle },
}

export const SALE_STATUS_OPTIONS = (Object.keys(statusMeta) as SaleStatus[]).map((value) => ({ value, label: statusMeta[value].label }))

/// [UI] สถานะต้องสื่อด้วยสี + ไอคอน + ข้อความพร้อมกันเสมอ — ห้ามใช้สีอย่างเดียว
export function SaleStatusChip({ status }: { status: SaleStatus }) {
  const meta = statusMeta[status] ?? statusMeta.draft
  const Icon = meta.icon
  return (
    <span className={`sale-status sale-status--${status}`}>
      <Icon aria-hidden="true" />
      {meta.label}
    </span>
  )
}

/// role จาก API ไม่รับประกันตัวพิมพ์ (เช่น "Manager") — เทียบแบบ lowercase เหมือนหน้าอื่น (JobCardModal/AppShell)
/// สิทธิ์จริงบังคับที่ SaleService เสมอ ตรงนี้ใช้แค่ตัดสินว่าจะแสดงปุ่ม/หน้าไหน
export const normalizedRole = (role: string | null | undefined) => (role ?? '').toLowerCase()
export const canUseRetailSale = (role: string | null | undefined) => ['cashier', 'office', 'manager'].includes(normalizedRole(role))

export const PAYMENT_METHOD_OPTIONS: { value: PaymentMethod; label: string }[] = [
  { value: 'cash', label: 'เงินสด' },
  { value: 'transfer', label: 'โอนเงิน' },
  { value: 'card', label: 'บัตร' },
  { value: 'qr', label: 'QR พร้อมเพย์' },
]

export const paymentMethodLabel = (method: PaymentMethod) =>
  PAYMENT_METHOD_OPTIONS.find((option) => option.value === method)?.label ?? method

/// เงินเก็บเป็นทศนิยม 2 ตำแหน่ง — เทียบ/รวมด้วยสตางค์ (จำนวนเต็ม) กัน 0.1 + 0.2 ≠ 0.3
export const toSatang = (value: number) => Math.round(value * 100)

/// ข้อความ error ของ API แสดง messageTh ตามที่ server ส่งมา (ห้ามแปล/แต่งใหม่) พร้อม traceId
export function ErrorAlert({ title, error }: { title: string; error: unknown }) {
  return (
    <Alert variant="destructive" className="form-error">
      <CircleAlert aria-hidden="true" />
      <div>
        <AlertTitle>{isApiError(error) ? error.messageTh : error instanceof Error ? error.message : title}</AlertTitle>
        <AlertDescription className="trace-id">
          {isApiError(error) ? `${title} · รหัสติดตาม (traceId): ${error.traceId}` : title}
        </AlertDescription>
      </div>
    </Alert>
  )
}
