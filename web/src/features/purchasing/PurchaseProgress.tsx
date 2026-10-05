import { useEffect, useRef } from 'react'
import type { Purchase } from '../../api/purchasing'
import { cn } from '../../lib/utils'

const REQUEST_STEPS = [
  { status: 'draft', label: 'จัดทำ PR' },
  { status: 'pending', label: 'ส่งขออนุมัติ' },
  { status: 'approved', label: 'อนุมัติแล้ว' },
  { status: 'converted', label: 'สร้าง PO แล้ว' },
]
const ORDER_STEPS = [
  { status: 'draft', label: 'จัดทำ PO' },
  { status: 'pending', label: 'ส่งขออนุมัติ' },
  { status: 'approved', label: 'อนุมัติแล้ว' },
  { status: 'sent', label: 'ส่งสั่งซื้อ' },
  { status: 'partial', label: 'รับบางส่วน' },
  { status: 'complete', label: 'รับครบแล้ว' },
]
const INHERITED_ORDER_STEPS = ORDER_STEPS.filter(step => step.status !== 'pending')

export function PurchaseProgress({ doc }: { doc: Purchase }) {
  const viewport = useRef<HTMLElement>(null)
  const currentStep = useRef<HTMLLIElement>(null)
  const inherited = doc.kind === 'PO' && Boolean(doc.sourceRequestId)
  const steps = doc.kind === 'PR' ? REQUEST_STEPS : inherited ? INHERITED_ORDER_STEPS : ORDER_STEPS
  // Reapproval shares the approval milestone; inherited POs have no second approval step.
  const currentIndex = steps.findIndex(step => step.status === (inherited && doc.status === 'pending' ? 'approved' : doc.status))
  const cancelled = doc.status === 'cancelled'
  const complete = doc.status === 'converted' || doc.status === 'complete'

  useEffect(() => {
    const container = viewport.current
    const current = currentStep.current
    if (!container) return
    const revealCurrent = () => {
      if (!current) { container.scrollLeft = 0; return }
      if (container.scrollWidth <= container.clientWidth) return
      const containerBounds = container.getBoundingClientRect()
      const currentBounds = current.getBoundingClientRect()
      container.scrollLeft += currentBounds.left - containerBounds.left - (container.clientWidth - currentBounds.width) / 2
    }
    revealCurrent()
    const observer = new ResizeObserver(revealCurrent)
    observer.observe(container)
    return () => observer.disconnect()
  }, [doc.kind, doc.status, inherited])

  return <nav ref={viewport} className="job-card-stepper purchase-progress" aria-label={`ความคืบหน้า ${doc.kind}`}>
    {cancelled && <p className="purchase-progress__cancelled" role="status">ยกเลิกเอกสาร · หยุดดำเนินการ</p>}
    <ol className="job-card-stepper__list" style={{ minWidth: steps.length * 100 }}>
      {steps.map((step, index) => {
        const current = !cancelled && index === currentIndex
        const done = !cancelled && (index < currentIndex || complete && current)
        const label = inherited && step.status === 'approved'
          ? doc.status === 'pending' ? 'รออนุมัติรายการแก้ไข' : 'อนุมัติแล้ว'
          : step.label
        return <li key={step.status} ref={current ? currentStep : undefined} className="job-card-stepper__step"
          aria-current={current ? 'step' : undefined} data-state={cancelled ? 'cancelled' : done ? 'done' : current ? 'current' : 'upcoming'}
          aria-label={`${label}: ${cancelled ? 'หยุดดำเนินการ' : done ? 'เสร็จแล้ว' : current ? 'ขั้นตอนปัจจุบัน' : 'ยังไม่ถึงขั้นตอนนี้'}`}>
          <div className="job-card-stepper__button purchase-progress__item">
            <span aria-hidden="true" className={cn('job-card-stepper__circle', {
              'job-card-stepper__circle--done': done,
              'job-card-stepper__circle--active': current && !complete,
              'job-card-stepper__circle--viewing': current && !complete,
            })}>{done ? '✓' : index + 1}</span>
            <span className={cn('job-card-stepper__label', {
              'job-card-stepper__label--reached': done || current,
              'job-card-stepper__label--viewing': current,
            })}>{label}</span>
          </div>
          {index < steps.length - 1 && <span aria-hidden="true" className={cn('job-card-stepper__connector', {
            'job-card-stepper__connector--done': done,
          })} />}
        </li>
      })}
    </ol>
  </nav>
}
