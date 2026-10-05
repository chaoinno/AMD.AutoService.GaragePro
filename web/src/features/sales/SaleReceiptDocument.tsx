import type { ReactNode } from 'react'
import type { Sale } from '../../api/sales'
import { Money } from '../../components/Money'
import { formatDateTime, formatMoney, formatNumber } from '../../lib/format'
import { paymentMethodLabel } from './saleFormat'
import { BRAND_NAME, DocumentBrandMark } from '../../components/Brand'

/// ใบเสร็จขายหน้าร้าน SL- — ใช้ class เดียวกับใบเสร็จงานซ่อม (quotation-document/document-*)
/// [BIZ] เอกสารที่ให้ลูกค้าห้ามแสดงต้นทุน/กำไรเลย แม้ผู้พิมพ์จะเป็นผู้จัดการ (กฎข้อ 10)
export function SaleReceiptDocument({ sale, branchName }: { sale: Sale; branchName: string }) {
  const voided = sale.status === 'voided'
  return (
    <article className={`quotation-document sale-receipt-document${voided ? ' sale-receipt-document--voided' : ''}`}>
      {voided ? <div className="sale-receipt-watermark" aria-hidden="true">ยกเลิกแล้ว</div> : null}
      <header className="document-header">
        <div className="document-branch">
          <DocumentBrandMark />
          <div>
            <h1>{BRAND_NAME}</h1>
            <p>{branchName || 'ไม่ระบุสาขา'}</p>
          </div>
        </div>
        <div className="document-title">
          <span>ใบเสร็จรับเงิน</span>
          <h2>Receipt</h2>
          <strong>{sale.receiptNo}</strong>
        </div>
      </header>

      <section className="document-info-grid">
        <InfoBlock title="ลูกค้า">
          <InfoRow label="ชื่อ" value={sale.customerName || 'ลูกค้าทั่วไป'} strong />
          <InfoRow label="เบอร์โทร" value={sale.customerPhone || '—'} />
        </InfoBlock>
        <InfoBlock title="ข้อมูลเอกสาร">
          <InfoRow label="วันที่ขาย" value={formatDateTime(sale.completedAt || sale.createdAt)} strong />
          <InfoRow label="ผู้รับเงิน" value={sale.payments[0]?.receivedByName || '—'} />
          {voided ? <InfoRow label="ยกเลิกเมื่อ" value={`${formatDateTime(sale.voidedAt)} · ${sale.voidReason || ''}`} /> : null}
        </InfoBlock>
      </section>

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th className="document-col-sequence">#</th>
              <th>รายการ</th>
              <th className="document-number sale-col-qty">จำนวน</th>
              <th className="document-number sale-col-money">ราคา/หน่วย</th>
              <th className="document-number sale-col-money">ส่วนลด</th>
              <th className="document-number sale-col-money">จำนวนเงิน</th>
            </tr>
          </thead>
          <tbody>
            {sale.lines.map((line, index) => (
              <tr key={line.id}>
                <td>{index + 1}</td>
                <td>
                  {line.name}
                  <small className="sale-receipt-document__sub">
                    {line.code}{line.promotionName ? ` · โปร ${line.promotionName}` : ''}
                  </small>
                </td>
                <td className="document-number">{formatNumber(line.quantity)} {line.unit}</td>
                <td className="document-number">{formatMoney(line.unitPrice)}</td>
                <td className="document-number">
                  {line.discountAmount + line.promotionAmount > 0 ? `−${formatMoney(line.discountAmount + line.promotionAmount)}` : '—'}
                </td>
                <td className="document-number">{formatMoney(line.netAmount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="document-summary-section">
        <div className="document-notes">
          <strong>การชำระเงิน</strong>
          <ul className="sale-receipt-document__payments">
            {sale.payments.map((payment) => (
              <li key={payment.id}>
                <span>{paymentMethodLabel(payment.method)}{payment.reference ? ` · ${payment.reference}` : ''}</span>
                <Money value={payment.amount} suffix=" บาท" />
              </li>
            ))}
          </ul>
          <p>ใบเสร็จรับเงิน — ยังไม่รองรับใบกำกับภาษีเต็มรูปแบบ</p>
        </div>
        <div className="money-summary document-money-summary">
          <Row label="รวมเป็นเงิน" value={sale.grossAmount} />
          {sale.lineDiscountAmount + sale.linePromotionAmount > 0 ? <Row label="ส่วนลด/โปรรายการ" value={sale.lineDiscountAmount + sale.linePromotionAmount} deduction /> : null}
          {sale.billDiscountAmount > 0 ? <Row label="ส่วนลดท้ายบิล" value={sale.billDiscountAmount} deduction /> : null}
          {sale.billPromotionAmount > 0 ? <Row label={`โปร ${sale.billPromotionName || sale.billPromotionCode || ''}`.trim()} value={sale.billPromotionAmount} deduction /> : null}
          <Row label="ยอดก่อนภาษี" value={sale.netAmount} />
          <Row label={sale.vatIncluded ? `ภาษีมูลค่าเพิ่ม ${formatNumber(sale.vatRate * 100)}%` : 'ไม่คิดภาษีมูลค่าเพิ่ม'} value={sale.vatAmount} />
          <div className="money-summary__row money-summary__grand">
            <span>ยอดรวมทั้งสิ้น</span>
            <Money value={sale.totalAmount} />
          </div>
        </div>
      </section>

      <footer className="document-footer">
        <div className="document-footer__meta">
          <span>{branchName}</span>
          <span>{sale.receiptNo}</span>
        </div>
      </footer>
    </article>
  )
}

function Row({ label, value, deduction }: { label: string; value: number; deduction?: boolean }) {
  return (
    <div className={`money-summary__row${deduction ? ' money-summary__deduction' : ''}`}>
      <span>{label}</span>
      <Money value={value} prefix={deduction ? '−' : undefined} />
    </div>
  )
}

function InfoBlock({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="document-info-block">
      <h3>{title}</h3>
      {children}
    </section>
  )
}

function InfoRow({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="document-info-row">
      <span>{label}</span>
      {strong ? <strong>{value}</strong> : <p>{value}</p>}
    </div>
  )
}
