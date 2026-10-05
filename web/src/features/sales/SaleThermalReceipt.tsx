import { useLayoutEffect, useRef, useState } from 'react'
import type { Sale } from '../../api/sales'
import { formatDateTime, formatMoney, formatNumber } from '../../lib/format'
import { paymentMethodLabel } from './saleFormat'

export type ReceiptFormat = 'a4' | 'roll80' | 'roll58'

export const RECEIPT_FORMAT_OPTIONS: { value: ReceiptFormat; label: string }[] = [
  { value: 'a4', label: 'A4' },
  { value: 'roll80', label: 'ม้วน 80 มม.' },
  { value: 'roll58', label: 'ม้วน 58 มม.' },
]

/// ความกว้างกระดาษจริง (ใช้เป็น @page size) — เนื้อหาเว้นขอบในตัวเองตามพื้นที่พิมพ์ได้ของหัวพิมพ์
/// (80 มม. พิมพ์ได้ ~72 มม. · 58 มม. พิมพ์ได้ ~48 มม.) จึงตั้ง @page margin เป็น 0
const PAPER_WIDTH_MM: Record<Exclude<ReceiptFormat, 'a4'>, number> = { roll80: 80, roll58: 58 }
const PX_PER_MM = 96 / 25.4

/// ใบเสร็จขายหน้าร้านสำหรับเครื่องพิมพ์ความร้อนกระดาษม้วน — ข้อมูลชุดเดียวกับ SaleReceiptDocument (A4)
/// [BIZ] ห้ามแสดงต้นทุน/กำไร (กฎข้อ 10) · เงินทอนมีเฉพาะตอนพิมพ์ทันทีหลังชำระ (ไม่ได้เก็บลงฐานข้อมูล)
export function SaleThermalReceipt({ sale, branchName, format, change = 0 }: {
  sale: Sale
  branchName: string
  format: Exclude<ReceiptFormat, 'a4'>
  change?: number
}) {
  const ref = useRef<HTMLElement>(null)
  const [heightMm, setHeightMm] = useState(0)
  const voided = sale.status === 'voided'
  const lineDeduction = sale.lineDiscountAmount + sale.linePromotionAmount
  const widthMm = PAPER_WIDTH_MM[format]

  // [UI] ความยาวกระดาษ = ความสูงของใบเสร็จจริง วัดจากตัวอย่างบนจอที่ render ด้วยความกว้างเท่ากระดาษ
  // ถ้าใช้ A4/297 มม. ตายตัว บิลสั้นจะป้อนกระดาษเปล่ายาวทิ้ง บิลยาวจะถูกตัดขึ้นหน้าใหม่กลางรายการ
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const measure = () => setHeightMm(Math.ceil(element.getBoundingClientRect().height / PX_PER_MM) + 4)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return (
    <>
      {/* @page ประกาศทีหลัง index.css (A4) จึงชนะเฉพาะตอนเลือกกระดาษม้วนอยู่ — ครอบคลุมกด Ctrl+P เองด้วย */}
      {heightMm > 0 ? <style>{`@page { size: ${widthMm}mm ${heightMm}mm; margin: 0; }`}</style> : null}
      <article
        ref={ref}
        className={`sale-thermal sale-thermal--${format}`}
        style={{ width: `${widthMm}mm` }}
        aria-label={`ใบเสร็จกระดาษม้วน ${widthMm} มม.`}
      >
        <header className="sale-thermal__head">
          <strong className="sale-thermal__brand">GaragePro</strong>
          <span>{branchName || 'ไม่ระบุสาขา'}</span>
          <strong className="sale-thermal__title">ใบเสร็จรับเงิน</strong>
          {voided ? <strong className="sale-thermal__voided">*** ยกเลิกแล้ว ***</strong> : null}
        </header>

        <dl className="sale-thermal__meta">
          <div><dt>เลขที่</dt><dd>{sale.receiptNo}</dd></div>
          <div><dt>วันที่</dt><dd>{formatDateTime(sale.completedAt || sale.createdAt)}</dd></div>
          <div><dt>ผู้รับเงิน</dt><dd>{sale.payments[0]?.receivedByName || '—'}</dd></div>
          <div><dt>ลูกค้า</dt><dd>{sale.customerName || 'ลูกค้าทั่วไป'}</dd></div>
          {sale.customerPhone ? <div><dt>โทร</dt><dd>{sale.customerPhone}</dd></div> : null}
        </dl>

        <hr />

        <ol className="sale-thermal__lines">
          {sale.lines.map((line) => (
            <li key={line.id}>
              <span className="sale-thermal__name">{line.name}</span>
              <div className="sale-thermal__row">
                <span>{formatNumber(line.quantity)} {line.unit} × {formatMoney(line.unitPrice)}</span>
                <span className="sale-thermal__money">{formatMoney(line.netAmount + line.discountAmount + line.promotionAmount)}</span>
              </div>
              {line.discountAmount + line.promotionAmount > 0 ? (
                <div className="sale-thermal__row sale-thermal__row--sub">
                  <span>{line.promotionName ? `โปร ${line.promotionName}` : 'ส่วนลด'}</span>
                  <span className="sale-thermal__money">−{formatMoney(line.discountAmount + line.promotionAmount)}</span>
                </div>
              ) : null}
            </li>
          ))}
        </ol>

        <hr />

        <div className="sale-thermal__totals">
          <Row label={`รวม ${formatNumber(sale.lines.reduce((sum, line) => sum + line.quantity, 0))} ชิ้น`} value={sale.grossAmount} />
          {lineDeduction > 0 ? <Row label="ส่วนลด/โปรรายการ" value={lineDeduction} deduction /> : null}
          {sale.billDiscountAmount > 0 ? <Row label="ส่วนลดท้ายบิล" value={sale.billDiscountAmount} deduction /> : null}
          {sale.billPromotionAmount > 0 ? <Row label={`โปร ${sale.billPromotionName || sale.billPromotionCode || ''}`.trim()} value={sale.billPromotionAmount} deduction /> : null}
          <Row label="ยอดก่อนภาษี" value={sale.netAmount} />
          <Row label={sale.vatIncluded ? `VAT ${formatNumber(sale.vatRate * 100)}%` : 'ไม่คิด VAT'} value={sale.vatAmount} />
          <div className="sale-thermal__row sale-thermal__grand">
            <span>ยอดสุทธิ</span>
            <span className="sale-thermal__money">{formatMoney(sale.totalAmount)}</span>
          </div>
        </div>

        <hr />

        <div className="sale-thermal__totals">
          {sale.payments.map((payment) => (
            <Row key={payment.id} label={`${paymentMethodLabel(payment.method)}${payment.reference ? ` ${payment.reference}` : ''}`} value={payment.amount} />
          ))}
          {change > 0 && !voided ? <Row label="เงินทอน" value={change} /> : null}
        </div>

        {voided ? (
          <p className="sale-thermal__note">ยกเลิก {formatDateTime(sale.voidedAt)}{sale.voidReason ? ` · ${sale.voidReason}` : ''}</p>
        ) : null}

        <footer className="sale-thermal__foot">
          <span>ขอบคุณที่ใช้บริการ</span>
          <small>ใบเสร็จรับเงิน — ไม่ใช่ใบกำกับภาษีเต็มรูป</small>
        </footer>
      </article>
    </>
  )
}

function Row({ label, value, deduction }: { label: string; value: number; deduction?: boolean }) {
  return (
    <div className="sale-thermal__row">
      <span>{label}</span>
      <span className="sale-thermal__money">{deduction ? '−' : ''}{formatMoney(value)}</span>
    </div>
  )
}
