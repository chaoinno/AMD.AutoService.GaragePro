/**
 * แถบสัดส่วนเดียว (ไม่ใช้ chart library — สแตกนี้เขียน UI เองไม่พึ่ง dependency ภายนอก)
 * ใช้ทั้งฟันเนลสถานะจ๊อบ (แดชบอร์ด/รอบเวลาทำงาน) และช่วงอายุสต็อก — สีมาจาก caller เพื่อคงความหมายเดิม
 * ของแต่ละสถานะ (ดู web/src/features/reports/reportColors.ts) ไม่ใช่สุ่มสีใหม่
 */
export function ReportBar({ label, valueLabel, ratio, color }: {
  label: string
  valueLabel: string
  /** 0–1 ความยาวแท่งเทียบกับค่าสูงสุดของกลุ่ม */
  ratio: number
  color: string
}) {
  const pct = Math.round(Math.max(0, Math.min(1, ratio)) * 100)
  return (
    <div className="report-bar">
      <span className="report-bar__label">{label}</span>
      <div className="report-bar__track" role="img" aria-label={`${label}: ${valueLabel}`}>
        <div className="report-bar__fill" style={{ width: `${pct}%`, background: color }} />
      </div>
      <span className="report-bar__value report-num">{valueLabel}</span>
    </div>
  )
}
