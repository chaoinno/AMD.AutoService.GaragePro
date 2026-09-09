import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { isApiError, isForbiddenError } from '../../api/client'
import { getPaymentSummary } from '../../api/pos'
import type { Job } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { PaymentReceiptDocument } from './PaymentReceiptDocument'

type PaymentReceiptModalProps = { open: boolean; job: Job; onClose: () => void }

/// เอกสารใบเสร็จรับเงินซ้อนบน job card modal — window.print() พิมพ์เฉพาะ .payment-receipt-print-area
/// ด้วย body.printing-payment-receipt (ดู @media print ใน index.css) — pattern เดียวกับใบเบิกสินค้า/ใบรับรถ
export function PaymentReceiptModal({ open, job, onClose }: PaymentReceiptModalProps) {
  const query = useQuery({
    queryKey: ['payment-summary', job.jobId],
    queryFn: () => getPaymentSummary(job.jobId),
    enabled: open,
  })

  useEffect(() => {
    if (!open) return
    document.body.classList.add('printing-payment-receipt')
    return () => document.body.classList.remove('printing-payment-receipt')
  }, [open])

  let body: ReactNode = null

  if (query.isPending) {
    body = (
      <StateBlock
        variant="loading"
        title="กำลังจัดเตรียมเอกสาร"
        reason="ระบบกำลังโหลดข้อมูลล่าสุดเพื่อจัดวางเอกสารสำหรับพิมพ์"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void query.refetch()}
      >
        <SkeletonRows count={5} />
      </StateBlock>
    )
  } else if (query.isError) {
    body = (
      <StateBlock
        variant={isForbiddenError(query.error) ? 'forbidden' : 'error'}
        title={isForbiddenError(query.error) ? 'ไม่มีสิทธิ์ดูเอกสารนี้' : 'เปิดเอกสารไม่สำเร็จ'}
        reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(query.error) ? query.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void query.refetch()}
      />
    )
  } else if (!query.data.receipt) {
    body = (
      <div className="job-detail-empty">
        <p>งานนี้ยังไม่ได้ออกใบเสร็จ</p>
      </div>
    )
  } else {
    const { receipt, payments } = query.data
    body = (
      <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden">
          <Button onClick={() => window.print()}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
        </div>
        <div className="payment-receipt-print-area">
          <PaymentReceiptDocument job={job} receipt={receipt} payments={payments} />
        </div>
      </div>
    )
  }

  return (
    <ConfirmModal
      open={open}
      title={query.data?.receipt ? `ใบเสร็จรับเงิน ${query.data.receipt.documentNo}` : 'ใบเสร็จรับเงิน'}
      onClose={onClose}
      size="xlarge"
    >
      {body}
    </ConfirmModal>
  )
}
