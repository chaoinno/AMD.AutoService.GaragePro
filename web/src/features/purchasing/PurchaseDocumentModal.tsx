import { useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect } from 'react'
import { purchase, type PurchaseKind } from '../../api/purchasing'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { useSession } from '../../lib/session'
import { QueryState } from '../master-data/MasterDataCommon'
import { purchaseStatusLabels } from './purchaseExport'

const money = (value: number) => value.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const date = (value: string | null) => value ? new Date(value).toLocaleDateString('th-TH') : '—'

export function PurchaseDocumentModal({ kind, id, onClose }: { kind: PurchaseKind; id: string; onClose: () => void }) {
  const query = useQuery({ queryKey: ['purchase-print', kind, id], queryFn: () => purchase(kind, id), staleTime: 0 })
  const { session } = useSession()
  const doc = query.data
  const title = kind === 'PR' ? 'ใบขอซื้อ' : 'ใบสั่งซื้อ'
  useEffect(() => {
    document.body.classList.add('printing-purchase')
    return () => document.body.classList.remove('printing-purchase')
  }, [])
  return <ConfirmModal open title={`${title} ${doc?.number || ''}`} size="xlarge" onClose={onClose}>
    <QueryState query={query} loadingTitle="กำลังจัดเตรียมเอกสาร" emptyTitle="ไม่พบเอกสาร" emptyReason="กรุณาโหลดใหม่" onRetry={() => void query.refetch()}>
      {doc && <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden"><Button disabled={query.isFetching} onClick={() => window.print()}><Printer aria-hidden="true" /> พิมพ์ {kind}</Button></div>
        <div className="purchase-print-area"><article className="quotation-document">
          <header className="document-header">
            <div className="document-branch"><div className="document-brand-mark" aria-hidden="true">GP</div><div><h1>{session?.branchName || 'GaragePro'}</h1><p>เอกสารจัดซื้อ</p></div></div>
            <div className="document-title"><span>{kind === 'PR' ? 'Purchase Requisition' : 'Purchase Order'}</span><h2>{title}</h2><strong>{doc.number}</strong></div>
          </header>
          <section className="document-info-grid">
            <section className="document-info-block"><h3>ข้อมูลเอกสาร</h3><Info label="วันที่สร้าง" value={date(doc.createdAt)} /><Info label="สถานะ" value={purchaseStatusLabels[doc.status] || doc.status} /><Info label="วันที่ต้องการ" value={date(doc.requiredDate)} /><Info label="คลังรับสินค้า" value={doc.warehouseName} /></section>
            <section className="document-info-block"><h3>ผู้เกี่ยวข้อง</h3><Info label="ซัพพลายเออร์" value={doc.supplierName || 'ยังไม่ระบุ'} /><Info label="ผู้สร้าง" value={doc.createdByName} /><Info label="ผู้อนุมัติ" value={doc.approvedByName || 'ยังไม่อนุมัติ'} /><Info label="วันที่อนุมัติ" value={date(doc.approvedAt)} /></section>
          </section>
          <section className="document-lines"><table><thead><tr><th style={{ width: 48 }}>ลำดับ</th><th>รหัส</th><th>รายการ</th><th className="document-number">จำนวน</th><th>หน่วย</th><th className="document-number">ราคา/หน่วย</th><th className="document-number">มูลค่า</th></tr></thead><tbody>{doc.lines.map((line, index) => <tr key={line.id}><td>{index + 1}</td><td>{line.code}</td><td>{line.name}</td><td className="document-number">{line.quantity}</td><td>{line.unit}</td><td className="document-number">{money(line.unitCost)}</td><td className="document-number">{money(line.quantity * line.unitCost)}</td></tr>)}</tbody></table></section>
          <section className="document-summary-section"><div className="document-notes"><strong>หมายเหตุ / เงื่อนไขชำระเงิน</strong><p>{doc.note || '—'}</p>{doc.paymentTerms && <p>{doc.paymentTerms}</p>}{doc.cancelReason && <p>เหตุผลยกเลิก: {doc.cancelReason}</p>}</div><p className="purchase-total">มูลค่าก่อนภาษี <strong>{money(doc.total)} บาท</strong></p></section>
          <footer className="document-footer"><div className="signature-slots">{[['ผู้จัดทำ', doc.createdByName], ['ผู้อนุมัติ', doc.approvedByName || '']].map(([label, name]) => <div className="signature-slot" key={label}><span /><strong>{label}</strong><small>{name}</small><small>วันที่ ____ / ____ / ______</small></div>)}</div><div className="document-footer__meta"><span>{session?.branchName}</span><span>{doc.number}</span></div></footer>
        </article></div>
      </div>}
    </QueryState>
  </ConfirmModal>
}

function Info({ label, value }: { label: string; value: string }) {
  return <div className="document-info-row"><span>{label}</span><p>{value}</p></div>
}
