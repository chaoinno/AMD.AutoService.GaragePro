import { useMutation } from '@tanstack/react-query'
import { ArrowLeft, Ban, Banknote, CircleAlert, LoaderCircle, Plus, Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { voidSale, type Sale } from '../../api/sales'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { Button } from '../../components/ui/button'
import { Textarea } from '../../components/ui/textarea'
import { formatDateTime } from '../../lib/format'
import { useSession } from '../../lib/session'
import { SaleReceiptDocument } from './SaleReceiptDocument'
import { ErrorAlert, SaleStatusChip, normalizedRole, paymentMethodLabel } from './saleFormat'
import { useApplySale } from './useSale'

export function SaleDetail({ sale }: { sale: Sale }) {
  const navigate = useNavigate()
  const location = useLocation()
  const { session } = useSession()
  const isManager = normalizedRole(session?.user.role) === 'manager'
  const canSeeCost = Boolean(session?.user.canSeeCost)
  const [voidOpen, setVoidOpen] = useState(false)
  // เงินทอนส่งมาจาก modal ชำระเงินผ่าน navigation state — แสดงครั้งเดียวหลังปิดการขาย ไม่เก็บลงฐานข้อมูล
  const change = (location.state as { change?: number } | null)?.change ?? 0

  const margin = sale.costTotal != null ? sale.netAmount - sale.costTotal : null
  const voidDisabledReason = sale.status !== 'completed'
    ? 'บิลนี้ถูกยกเลิกไปแล้ว'
    : !isManager
      ? 'เฉพาะผู้จัดการเท่านั้นที่ยกเลิกบิลที่ออกใบเสร็จแล้วได้'
      : null

  return (
    <AppShell title={`ขายสินค้า · ${sale.receiptNo ?? 'บิลขาย'}`}>
      <section className="sale-editor-heading print-hidden">
        <Button variant="ghost" size="icon" aria-label="กลับรายการบิล" onClick={() => navigate('/sales')}>
          <ArrowLeft aria-hidden="true" />
        </Button>
        <div className="sale-editor-heading__copy">
          <span className="sale-editor-heading__eyebrow">ใบเสร็จขายหน้าร้าน</span>
          <h2>{sale.receiptNo ?? (sale.status === 'cancelled' ? 'บิลร่างที่ยกเลิก' : 'บิลขาย')}</h2>
          <p>{sale.customerName || 'ลูกค้าทั่วไป'} · {formatDateTime(sale.completedAt || sale.createdAt)}</p>
        </div>
        <SaleStatusChip status={sale.status} />
      </section>

      {change > 0 && sale.status === 'completed' ? (
        <div className="sale-change-banner print-hidden" role="status">
          <Banknote aria-hidden="true" />
          <span>ทอนเงินลูกค้า</span>
          <Money value={change} suffix=" บาท" />
        </div>
      ) : null}

      {sale.status === 'voided' ? (
        <div className="sale-void-banner print-hidden" role="status">
          <Ban aria-hidden="true" />
          <div>
            <strong>บิลนี้ถูกยกเลิกแล้ว — สต็อกคืนเข้าล็อตเดิมเรียบร้อย</strong>
            <p>{formatDateTime(sale.voidedAt)} · เหตุผล: {sale.voidReason || '—'} · คืนเงินลูกค้าทำนอกระบบ</p>
          </div>
        </div>
      ) : null}

      <div className="sale-detail-grid">
        <div className="sale-detail-document">
          {sale.receiptNo ? (
            <SaleReceiptDocument sale={sale} branchName={session?.branchName ?? ''} />
          ) : (
            <div className="line-empty">
              <strong>บิลร่างนี้ถูกยกเลิกก่อนชำระเงิน</strong>
              <p>ไม่มีการตัดสต็อกหรือรับเงิน จึงไม่มีใบเสร็จ</p>
            </div>
          )}
        </div>

        <aside className="sale-panel sale-detail-aside print-hidden">
          <div className="sale-detail-actions">
            <Button size="lg" disabled={!sale.receiptNo} onClick={() => window.print()}>
              <Printer aria-hidden="true" /> พิมพ์ใบเสร็จ
            </Button>
            <Button variant="outline" onClick={() => navigate('/sales')}>
              <Plus aria-hidden="true" /> ไปหน้าขายใหม่
            </Button>
          </div>

          <div className="sale-summary-section">
            <h4>การชำระเงิน</h4>
            {sale.payments.length ? (
              <ul className="sale-detail-payments">
                {sale.payments.map((payment) => (
                  <li key={payment.id}>
                    <span>
                      <strong>{paymentMethodLabel(payment.method)}</strong>
                      <small>{payment.reference || payment.receivedByName} · {formatDateTime(payment.receivedAt)}</small>
                    </span>
                    <Money value={payment.amount} />
                  </li>
                ))}
              </ul>
            ) : <p className="sale-muted">ไม่มีรายการรับเงิน</p>}
          </div>

          {canSeeCost && sale.costTotal != null && sale.status === 'completed' ? (
            <div className="sale-summary-section">
              <h4>ต้นทุน/กำไร (เฉพาะผู้จัดการ · ไม่พิมพ์ลงใบเสร็จ)</h4>
              <div className="money-summary__row"><span>ต้นทุนตามล็อต FIFO</span><Money value={sale.costTotal} /></div>
              <div className="money-summary__row money-summary__grand">
                <span>กำไรขั้นต้น (ก่อน VAT)</span>
                <span>
                  <Money value={margin} />
                  {sale.netAmount > 0 && margin != null ? <small className="money"> ({((margin / sale.netAmount) * 100).toFixed(1)}%)</small> : null}
                </span>
              </div>
            </div>
          ) : null}

          {sale.status === 'completed' || sale.status === 'voided' ? (
            <div className="sale-summary-section">
              <Button variant="outline" className="sale-void-button" disabled={Boolean(voidDisabledReason)} onClick={() => setVoidOpen(true)}>
                <Ban aria-hidden="true" /> ยกเลิกบิลและคืนสต็อก
              </Button>
              {voidDisabledReason ? <p className="disabled-reason"><CircleAlert aria-hidden="true" /> {voidDisabledReason}</p> : null}
            </div>
          ) : null}
        </aside>
      </div>

      <VoidSaleModal open={voidOpen} sale={sale} onClose={() => setVoidOpen(false)} />
    </AppShell>
  )
}

function VoidSaleModal({ open, sale, onClose }: { open: boolean; sale: Sale; onClose: () => void }) {
  const applySale = useApplySale()
  const [reason, setReason] = useState('')
  // requestId คงที่ตลอดการเปิด modal หนึ่งครั้ง — กดซ้ำหลังเน็ตหลุดจะไม่คืนสต็อกสองรอบ
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())

  useEffect(() => {
    if (open) { setReason(''); setRequestId(crypto.randomUUID()) }
  }, [open])

  const mutation = useMutation({
    mutationFn: () => voidSale(sale.id, { requestId, reason: reason.trim() }),
    onSuccess: (next) => {
      applySale(next)
      toast.success(`ยกเลิกบิล ${next.receiptNo} แล้ว สต็อกคืนเข้าล็อตเดิม`)
      onClose()
    },
  })

  return (
    <ConfirmModal
      open={open}
      size="small"
      title={`ยกเลิกบิล ${sale.receiptNo ?? ''}`}
      description="สินค้าทุกรายการจะคืนเข้าล็อต FIFO เดิมด้วยต้นทุนเดิม ใบเสร็จจะถูกทำเครื่องหมายว่ายกเลิก (ไม่ลบ) และการคืนเงินลูกค้าต้องทำนอกระบบ"
      onClose={() => { if (!mutation.isPending) { onClose(); mutation.reset() } }}
      footer={(
        <>
          <Button variant="outline" disabled={mutation.isPending} onClick={onClose}>ไม่ยกเลิก</Button>
          <Button variant="destructive" disabled={!reason.trim() || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? <LoaderCircle className="spin" aria-hidden="true" /> : <Ban aria-hidden="true" />}
            ยืนยันยกเลิกบิล
          </Button>
        </>
      )}
    >
      <label className="sale-field">
        <span>เหตุผลการยกเลิก *</span>
        <Textarea rows={3} maxLength={500} value={reason} placeholder="เช่น ลูกค้าคืนสินค้า / บันทึกผิดบิล" onChange={(event) => setReason(event.target.value)} />
      </label>
      {!reason.trim() ? <p className="disabled-reason"><CircleAlert aria-hidden="true" /> ต้องระบุเหตุผลก่อนยกเลิกบิล</p> : null}
      {mutation.isError ? <ErrorAlert title="ยกเลิกบิลไม่สำเร็จ" error={mutation.error} /> : null}
    </ConfirmModal>
  )
}
