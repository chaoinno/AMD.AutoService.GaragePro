import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { isApiError, isForbiddenError } from '../../api/client'
import { getWithdrawal } from '../../api/purchasing'
import { ConfirmModal } from '../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { StockWithdrawalDocument } from './StockWithdrawalDocument'

type StockWithdrawalDocumentModalProps = {
  operationId: string | null
  onClose: () => void
}

/// เอกสารใบเบิกสินค้าซ้อนบน job card modal (หรือหน้าสต็อก) — window.print() พิมพ์เฉพาะ .stock-withdrawal-print-area
/// ด้วย body.printing-stock-withdrawal (ดู @media print ใน index.css) ไม่ต้องปิดหน้าต่างที่ซ้อนอยู่ก่อน
export function StockWithdrawalDocumentModal({ operationId, onClose }: StockWithdrawalDocumentModalProps) {
  const query = useQuery({
    queryKey: ['stock-withdrawal', operationId ?? ''],
    queryFn: () => getWithdrawal(operationId!),
    enabled: Boolean(operationId),
  })

  useEffect(() => {
    if (!operationId) return
    document.body.classList.add('printing-stock-withdrawal')
    return () => document.body.classList.remove('printing-stock-withdrawal')
  }, [operationId])

  let title = 'ใบเบิกสินค้า'
  let body: ReactNode = null

  if (!operationId) {
    body = null
  } else if (query.isPending) {
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
    const withdrawal = query.data
    title = `ใบเบิกสินค้า ${withdrawal.documentNumber}`
    body = (
      <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden">
          <Button onClick={() => window.print()}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
        </div>
        <div className="stock-withdrawal-print-area">
          <StockWithdrawalDocument withdrawal={withdrawal} />
        </div>
      </div>
    )
  }

  return (
    <ConfirmModal open={operationId !== null} title={title} onClose={onClose} size="xlarge">
      {body}
    </ConfirmModal>
  )
}
