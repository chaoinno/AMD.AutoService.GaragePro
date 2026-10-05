import { TriangleAlert } from 'lucide-react'
import type { QuotationTotals } from '../api/types'
import { Money } from './Money'
import { Separator } from './ui/separator'

type MoneySummaryProps = {
  totals: QuotationTotals
  showCost?: boolean
  showApproved?: boolean
  className?: string
}

/** API ส่ง vatRate เป็นสัดส่วน (0.07) — แปลงเป็นเปอร์เซ็นต์สำหรับแสดงผล */
function formatVatRate(rate: number): string {
  return Number((rate * 100).toFixed(2)).toString()
}

export function MoneySummary({
  totals,
  showCost = false,
  showApproved = false,
  className = '',
}: MoneySummaryProps) {
  const lowMargin = totals.marginPercent != null && totals.marginPercent < 15

  return (
    <div className={`money-summary ${className}`.trim()}>
      <div className="money-summary__row">
        <span>รวมก่อนส่วนลด</span>
        <Money value={totals.gross} />
      </div>
      <div className="money-summary__row money-summary__deduction">
        <span>ส่วนลดรายบรรทัด</span>
        <Money value={totals.lineDiscount} prefix="−" />
      </div>
      <div className="money-summary__row money-summary__deduction">
        <span>โปรโมชัน</span>
        <Money value={totals.promotion} prefix="−" />
      </div>
      <Separator className="money-summary__divider" />
      <div className="money-summary__row">
        <span>ยอดก่อนภาษี</span>
        <Money value={totals.net} />
      </div>
      <div className="money-summary__row">
        <span>ภาษีมูลค่าเพิ่ม {formatVatRate(totals.vatRate)}%</span>
        <Money value={totals.vat} />
      </div>
      {showApproved && totals.approved ? (
        <section className="approved-total-comparison" aria-label="เปรียบเทียบยอดอนุมัติ">
          <div className="approved-total-comparison__quoted">
            <span>ยอดตามใบเสนอราคา</span>
            <Money value={totals.total} suffix=" บาท" />
          </div>
          <div className="approved-total-comparison__approved">
            <span>ยอดที่ลูกค้าอนุมัติ</span>
            <Money value={totals.approved.total} suffix=" บาท" />
            <small>
              อนุมัติ {totals.approved.approvedCount} · ไม่อนุมัติ {totals.approved.rejectedCount} รายการ
            </small>
          </div>
        </section>
      ) : (
        <>
          {/* [BIZ] ตัดเรื่องค่ามัดจำออกแล้ว (2026-10-02) — ไม่มีบรรทัด "หักมัดจำ"/"คงเหลือชำระ" อีก */}
          <div className="money-summary__total">
            <span>ยอดสุทธิ</span>
            <Money value={totals.total} />
          </div>
        </>
      )}

      {showCost && totals.totalCost != null ? (
        <section className={`margin-card ${lowMargin ? 'margin-card--warning' : ''}`}>
          <div className="money-summary__row">
            <span>ต้นทุนรวม</span>
            <Money value={totals.totalCost} />
          </div>
          {totals.marginAmount != null && totals.marginPercent != null ? (
            <div className="money-summary__row money-summary__grand">
              <span>กำไรขั้นต้น</span>
              <span>
                <Money value={totals.marginAmount} />
                <small className="money"> ({totals.marginPercent.toFixed(2)}%)</small>
              </span>
            </div>
          ) : null}
          {lowMargin ? (
            <p className="margin-card__warning">
              <TriangleAlert aria-hidden="true" /> กำไรต่ำกว่าเกณฑ์ 15%
            </p>
          ) : null}
        </section>
      ) : null}
    </div>
  )
}
