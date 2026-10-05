import { useState } from 'react'
import type { RetailDaily } from '../../api/reports'
import { Money } from '../../components/Money'
import { formatMoney } from '../../lib/format'

const dayLabel = (iso: string, withMonth = false) => {
  const [y = 0, m = 1, d = 1] = iso.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString('th-TH', withMonth ? { day: 'numeric', month: 'short' } : { day: 'numeric' })
}

/// ยอดขายรายวัน — ซีรีส์เดียว (ไม่ต้องมี legend หัวข้อบอกชื่อแล้ว) สีเดียวคือ --blue-600 ของระบบ
/// [UI] ไม่ใช้ chart library (สแตกนี้เขียน UI เอง) · คอลัมน์กว้างไม่เกิน 24px · label ตัวเลขเฉพาะวันที่สูงสุด
/// ค่าทุกวันเข้าถึงได้โดยไม่ต้อง hover ผ่านปุ่ม "ดูเป็นตาราง" (tooltip เป็นตัวเสริม ไม่ใช่ทางเดียว)
export function RetailDailyChart({ days }: { days: RetailDaily[] }) {
  const [active, setActive] = useState<number | null>(null)
  const [asTable, setAsTable] = useState(false)
  const max = Math.max(0, ...days.map((d) => d.totalAmount))
  const peakIndex = max > 0 ? days.findIndex((d) => d.totalAmount === max) : -1
  // วันยาวๆ ป้ายแกน X ชนกัน — แสดงทุก n วัน + วันสุดท้ายเสมอ
  const every = days.length <= 14 ? 1 : days.length <= 31 ? 5 : Math.ceil(days.length / 8)
  const hovered = active !== null ? days[active] : undefined
  const peak = peakIndex >= 0 ? days[peakIndex] : undefined

  return (
    <div className="retail-chart">
      <div className="retail-chart__toolbar">
        <span className="retail-chart__caption">{peak ? <>สูงสุด <Money value={max} suffix=" บาท" /> · {dayLabel(peak.date, true)}</> : 'ยังไม่มียอดขายในช่วงนี้'}</span>
        <button type="button" className="retail-chart__toggle" onClick={() => setAsTable((v) => !v)} aria-pressed={asTable}>
          {asTable ? 'ดูเป็นกราฟ' : 'ดูเป็นตาราง'}
        </button>
      </div>

      {asTable ? (
        <table className="retail-chart__table">
          <thead><tr><th>วันที่</th><th className="report-num">บิล</th><th className="report-num">ยอดขาย (บาท)</th></tr></thead>
          <tbody>
            {days.map((d) => (
              <tr key={d.date} className={d.billCount === 0 ? 'is-empty' : undefined}>
                <td>{dayLabel(d.date, true)}</td>
                <td className="report-num">{d.billCount}</td>
                <td className="report-num">{formatMoney(d.totalAmount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className="retail-chart__frame" onPointerLeave={() => setActive(null)}>
          <div className="retail-chart__plot" role="list" aria-label="ยอดขายหน้าร้านรายวัน">
            <span className="retail-chart__grid retail-chart__grid--mid" aria-hidden="true" />
            {days.map((d, i) => {
              const pct = max > 0 ? (d.totalAmount / max) * 100 : 0
              return (
                <button
                  type="button"
                  role="listitem"
                  key={d.date}
                  className={`retail-chart__col${active === i ? ' is-active' : ''}`}
                  aria-label={`${dayLabel(d.date, true)}: ${formatMoney(d.totalAmount)} บาท จาก ${d.billCount} บิล`}
                  onPointerEnter={() => setActive(i)}
                  onFocus={() => setActive(i)}
                  onBlur={() => setActive(null)}
                >
                  {i === peakIndex ? <span className="retail-chart__peak" style={{ bottom: `${pct}%` }}>{formatMoney(d.totalAmount)}</span> : null}
                  {d.totalAmount > 0 ? <span className="retail-chart__bar" style={{ height: `${Math.max(pct, 1.5)}%` }} /> : null}
                </button>
              )
            })}
            {hovered ? (
              <div
                className="retail-chart__tooltip"
                role="status"
                style={{ left: `${((active! + 0.5) / days.length) * 100}%` }}
              >
                <strong><Money value={hovered.totalAmount} suffix=" บาท" /></strong>
                <span>{dayLabel(hovered.date, true)} · {hovered.billCount} บิล</span>
              </div>
            ) : null}
          </div>
          <div className="retail-chart__axis" aria-hidden="true">
            {days.map((d, i) => (
              <span key={d.date}>{i % every === 0 || i === days.length - 1 ? dayLabel(d.date, i === 0) : ''}</span>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
