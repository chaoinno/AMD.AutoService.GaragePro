import {
  CheckCheck,
  CheckCircle2,
  ClipboardList,
  FileEdit,
  Package,
  PackageCheck,
  Send,
  ShieldCheck,
  Wrench,
  XCircle,
  type LucideIcon,
} from 'lucide-react'
import type { JobStatusToken } from '../api/types'
import { Badge } from './ui/badge'

const statusMeta: Record<JobStatusToken, { label: string; icon: LucideIcon }> = {
  waitinspect: { label: 'รอตรวจเช็ค', icon: ClipboardList },
  waitquote: { label: 'รอเสนอราคา', icon: FileEdit },
  waitapprove: { label: 'รออนุมัติ', icon: Send },
  approved: { label: 'อนุมัติแล้ว', icon: CheckCircle2 },
  inprogress: { label: 'กำลังซ่อม', icon: Wrench },
  waitparts: { label: 'รออะไหล่', icon: Package },
  qc: { label: 'QC ตรวจสอบ', icon: ShieldCheck },
  ready: { label: 'พร้อมส่งมอบ', icon: PackageCheck },
  completed: { label: 'เสร็จสมบูรณ์', icon: CheckCheck },
  cancelled: { label: 'ยกเลิก', icon: XCircle },
}

type JobStatusChipProps = {
  status: JobStatusToken
  label?: string
}

export function JobStatusChip({ status, label }: JobStatusChipProps) {
  const meta = statusMeta[status] ?? statusMeta.waitinspect
  const Icon = meta.icon
  return (
    <Badge className={`job-status-chip job-status-${status}`}>
      <Icon className="status-chip__icon" aria-hidden="true" />
      <span>{label || meta.label}</span>
    </Badge>
  )
}
