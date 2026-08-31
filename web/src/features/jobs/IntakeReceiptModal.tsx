import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect } from 'react'
import { getJobAttachments } from '../../api/attachments'
import { isApiError, isForbiddenError } from '../../api/client'
import { getJobIntakeChecklist } from '../../api/intake'
import type { Job } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { IntakeReceiptDocument } from './IntakeReceiptDocument'

type IntakeReceiptModalProps = {
  open: boolean
  job: Job
  onClose: () => void
}

/// ใบรับรถซ้อนบน job card modal — window.print() พิมพ์เฉพาะ .intake-receipt-print-area
/// ด้วย body.printing-intake-receipt (ดู @media print ใน index.css) ไม่ต้องปิด job card modal ก่อน
export function IntakeReceiptModal({ open, job, onClose }: IntakeReceiptModalProps) {
  const checklistQuery = useQuery({
    queryKey: ['job-intake-checklist', job.jobId],
    queryFn: () => getJobIntakeChecklist(job.jobId),
    enabled: open,
  })

  const attachmentsQuery = useQuery({
    queryKey: ['job-attachments', job.jobId, 'intake'],
    queryFn: () => getJobAttachments(job.jobId, 'intake'),
    enabled: open,
  })

  useEffect(() => {
    if (!open) return
    document.body.classList.add('printing-intake-receipt')
    return () => document.body.classList.remove('printing-intake-receipt')
  }, [open])

  return (
    <ConfirmModal open={open} title={`ใบรับรถ ${job.jobNo}`} onClose={onClose} size="xlarge">
      {!open ? null : checklistQuery.isPending ? (
        <StateBlock
          variant="loading"
          title="กำลังจัดเตรียมเอกสาร"
          reason="ระบบกำลังโหลดข้อมูลล่าสุดเพื่อจัดวางเอกสารสำหรับพิมพ์"
          traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
          actionLabel="โหลดใหม่"
          onAction={() => void checklistQuery.refetch()}
        >
          <SkeletonRows count={5} />
        </StateBlock>
      ) : checklistQuery.isError ? (
        <StateBlock
          variant={isForbiddenError(checklistQuery.error) ? 'forbidden' : 'error'}
          title={isForbiddenError(checklistQuery.error) ? 'ไม่มีสิทธิ์ดูเอกสารนี้' : 'เปิดเอกสารไม่สำเร็จ'}
          reason={isApiError(checklistQuery.error) ? checklistQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
          traceId={isApiError(checklistQuery.error) ? checklistQuery.error.traceId : undefined}
          actionLabel="ลองใหม่"
          onAction={() => void checklistQuery.refetch()}
        />
      ) : (
        <div className="intake-receipt-modal-body">
          <div className="job-card-panel-actions print-hidden">
            <Button onClick={() => window.print()}>
              <Printer aria-hidden="true" /> พิมพ์
            </Button>
          </div>
          <div className="intake-receipt-print-area">
            <IntakeReceiptDocument job={job} checklist={checklistQuery.data} photos={attachmentsQuery.data ?? []} />
          </div>
        </div>
      )}
    </ConfirmModal>
  )
}
