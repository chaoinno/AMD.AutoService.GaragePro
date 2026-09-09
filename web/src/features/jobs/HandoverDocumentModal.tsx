import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { downloadAttachment } from '../../api/attachments'
import { isApiError, isForbiddenError } from '../../api/client'
import { getHandover } from '../../api/handover'
import type { Job } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { HandoverDocument } from './HandoverDocument'

type HandoverDocumentModalProps = { open: boolean; job: Job; onClose: () => void }

/// เอกสารใบส่งมอบรถซ้อนบน job card modal — window.print() พิมพ์เฉพาะ .handover-document-print-area
/// ด้วย body.printing-handover-document (ดู @media print ใน index.css) — pattern เดียวกับใบเสร็จ/ใบเบิกสินค้า
/// พิมพ์ได้ทุกสถานะ (ก่อน/หลังยืนยันส่งมอบ) — ก่อนยืนยันจะเป็นเส้นว่างให้เซ็นด้วยปากกาแทนลายเซ็นดิจิทัล
export function HandoverDocumentModal({ open, job, onClose }: HandoverDocumentModalProps) {
  const query = useQuery({
    queryKey: ['handover', job.jobId],
    queryFn: () => getHandover(job.jobId),
    enabled: open,
  })

  const signaturePath = query.data?.signatureImagePath ?? null
  // endpoint ไฟล์แนบต้อง Bearer token เสมอ — <img src> เพียวๆ ไม่แนบ header ให้ ต้องโหลดผ่าน fetch แล้วแปลงเป็น
  // blob URL แทน (pattern เดียวกับ StaffAvatar.tsx/getStaffImage)
  const signatureQuery = useQuery({
    queryKey: ['attachment-blob', signaturePath],
    queryFn: () => downloadAttachment(signaturePath!),
    enabled: Boolean(signaturePath),
  })
  const [signatureImageUrl, setSignatureImageUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!signatureQuery.data) { setSignatureImageUrl(null); return }
    const nextUrl = URL.createObjectURL(signatureQuery.data)
    setSignatureImageUrl(nextUrl)
    return () => URL.revokeObjectURL(nextUrl)
  }, [signatureQuery.data])

  useEffect(() => {
    if (!open) return
    document.body.classList.add('printing-handover-document')
    return () => document.body.classList.remove('printing-handover-document')
  }, [open])

  let body = null

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
  } else {
    body = (
      <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden">
          <Button onClick={() => window.print()}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
        </div>
        <div className="handover-document-print-area">
          <HandoverDocument job={job} handover={query.data} signatureImageUrl={signatureImageUrl} />
        </div>
      </div>
    )
  }

  return (
    <ConfirmModal open={open} title={`ใบส่งมอบรถ ${job.jobNo}`} onClose={onClose} size="xlarge">
      {body}
    </ConfirmModal>
  )
}
