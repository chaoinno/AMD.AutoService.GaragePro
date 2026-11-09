import { CheckCircle2, Circle, Lock } from 'lucide-react'
import type { ReactNode } from 'react'

/// แถวเอกสารในขั้นชำระเงิน — [UI] สถานะสื่อด้วย ไอคอน + ข้อความ (ไม่ใช่สีอย่างเดียว) และบอกเหตุผลเมื่อยังทำไม่ได้เสมอ
export function BillingDocRow({
  title, status, done, locked = false, children,
}: { title: string; status: string; done: boolean; locked?: boolean; children?: ReactNode }) {
  const Icon = locked ? Lock : done ? CheckCircle2 : Circle
  return (
    <li className={`billing-doc-row${done ? ' billing-doc-row--done' : ''}${locked ? ' billing-doc-row--locked' : ''}`}>
      <Icon className="billing-doc-row__icon" aria-hidden="true" />
      <div className="billing-doc-row__text">
        <strong>{title}</strong>
        <span>{status}</span>
      </div>
      <div className="billing-doc-row__actions">{children}</div>
    </li>
  )
}
