import {
  ArrowLeftRight,
  CheckCircle2,
  Clock,
  FileEdit,
  MinusCircle,
  Send,
  XCircle,
  type LucideIcon,
} from 'lucide-react'
import type { QuotationStatus } from '../api/types'
import { Badge } from './ui/badge'

const statusMeta: Record<QuotationStatus, { label: string; icon: LucideIcon }> = {
  draft: { label: 'ฉบับร่าง', icon: FileEdit },
  sent: { label: 'ส่งให้ลูกค้าแล้ว', icon: Send },
  partial: { label: 'อนุมัติบางส่วน', icon: MinusCircle },
  approved: { label: 'อนุมัติครบ', icon: CheckCircle2 },
  rejected: { label: 'ลูกค้าไม่อนุมัติ', icon: XCircle },
  superseded: { label: 'ถูกแทนที่', icon: ArrowLeftRight },
  expired: { label: 'หมดอายุ', icon: Clock },
}

type StatusChipProps = {
  status: QuotationStatus
  label?: string
}

export function StatusChip({ status, label }: StatusChipProps) {
  const meta = statusMeta[status] ?? statusMeta.draft
  const Icon = meta.icon
  return (
    <Badge className={`status-chip status-${status}`}>
      <Icon className="status-chip__icon" aria-hidden="true" />
      <span>{label || meta.label}</span>
    </Badge>
  )
}
