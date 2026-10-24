import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect } from 'react'
import type { ReactNode } from 'react'
import { isApiError, isForbiddenError } from '../../api/client'
import { getReturn } from '../../api/purchasing'
import { BRAND_NAME, DocumentBrandMark } from '../../components/Brand'
import { ConfirmModal } from '../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { formatDateTime } from '../../lib/format'
import { money } from './PurchasingPage'

/// ใบคืนสินค้า (RT-) — โครงเดียวกับใบเบิก ใช้ print CSS ชุดเดียวกัน (body.printing-stock-withdrawal / .stock-withdrawal-print-area)
export function StockReturnDocumentModal({ operationId, onClose }: { operationId: string | null; onClose: () => void }) {
  const query = useQuery({
    queryKey: ['stock-return', operationId ?? ''],
    queryFn: () => getReturn(operationId!),
    enabled: Boolean(operationId),
  })
  useEffect(() => {
    if (!operationId) return
    document.body.classList.add('printing-stock-withdrawal')
    return () => document.body.classList.remove('printing-stock-withdrawal')
  }, [operationId])

  let title = 'ใบคืนสินค้า'
  let body: ReactNode = null
  if (!operationId) body = null
  else if (query.isPending) {
    body = <StateBlock variant="loading" title="กำลังจัดเตรียมเอกสาร" reason="ระบบกำลังโหลดข้อมูลล่าสุดเพื่อจัดวางเอกสารสำหรับพิมพ์" traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ" actionLabel="โหลดใหม่" onAction={() => void query.refetch()}><SkeletonRows count={5} /></StateBlock>
  } else if (query.isError) {
    body = <StateBlock variant={isForbiddenError(query.error) ? 'forbidden' : 'error'} title={isForbiddenError(query.error) ? 'ไม่มีสิทธิ์ดูเอกสารนี้' : 'เปิดเอกสารไม่สำเร็จ'} reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'} traceId={isApiError(query.error) ? query.error.traceId : undefined} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  } else {
    const r = query.data
    title = `ใบคืนสินค้า ${r.documentNumber}`
    body = (
      <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden">
          <Button onClick={() => window.print()}><Printer aria-hidden="true" /> พิมพ์</Button>
        </div>
        <div className="stock-withdrawal-print-area">
          <article className="quotation-document">
            <header className="document-header">
              <div className="document-branch"><DocumentBrandMark /><div><h1>{BRAND_NAME}</h1><p>{r.warehouseName}</p></div></div>
              <div className="document-title"><span>เอกสารคืนสินค้าเข้าคลัง</span><h2>ใบคืนสินค้า</h2><strong>{r.documentNumber}</strong></div>
            </header>
            <section className="document-info-grid">
              <section className="document-info-block"><h3>ข้อมูลการคืน</h3>
                <div className="document-info-row"><span>วันที่</span><strong>{formatDateTime(r.occurredAt)}</strong></div>
                <div className="document-info-row"><span>อ้างอิงใบเบิก</span><p>{r.withdrawalNumber || '-'}</p></div>
                <div className="document-info-row"><span>งานที่อ้างอิง</span><p>{r.jobNo || '-'}</p></div>
                <div className="document-info-row"><span>เหตุผล</span><p>{r.reasonLabelTh}{r.note ? ` · ${r.note}` : ''}</p></div>
              </section>
              <section className="document-info-block"><h3>ผู้เกี่ยวข้อง</h3>
                <div className="document-info-row"><span>ผู้เบิกเดิม</span><strong>{r.requesterName}</strong></div>
                <div className="document-info-row"><span>ผู้รับคืน (ผู้ทำรายการ)</span><p>{r.returnedByName}</p></div>
              </section>
            </section>
            <section className="document-lines">
              <table>
                <thead><tr><th className="document-col-code">รหัส</th><th>รายการ</th><th className="document-number">จำนวนที่คืน</th><th>หน่วย</th>{r.totalCost != null ? <th className="document-number">ต้นทุน/หน่วย (เฉลี่ย)</th> : null}</tr></thead>
                <tbody>{r.lines.map(line => <tr key={line.catalogItemId}><td>{line.code}</td><td>{line.name}</td><td className="document-number">{line.quantity}</td><td>{line.unit}</td>{r.totalCost != null ? <td className="document-number">{money(line.unitCost ?? 0)}</td> : null}</tr>)}</tbody>
              </table>
            </section>
            {r.totalCost != null ? <p className="purchase-total">มูลค่ารวมที่คืนเข้าคลัง <strong className="money">{money(r.totalCost)} บาท</strong></p> : null}
            <footer className="document-footer">
              <div className="signature-slots">
                <div className="signature-slot"><span /><strong>ผู้คืน</strong><small>{r.requesterName}</small><small>วันที่ ____ / ____ / ______</small></div>
                <div className="signature-slot"><span /><strong>ผู้รับคืน</strong><small>{r.returnedByName}</small><small>วันที่ ____ / ____ / ______</small></div>
              </div>
              <div className="document-footer__meta"><span>{r.warehouseName}</span><span>{r.documentNumber}</span></div>
            </footer>
          </article>
        </div>
      </div>
    )
  }
  return <ConfirmModal open={operationId !== null} title={title} onClose={onClose} size="xlarge">{body}</ConfirmModal>
}
