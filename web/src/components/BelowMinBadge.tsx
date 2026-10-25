import { AlertTriangle } from 'lucide-react'

/** แถวที่ถึงจุดต่ำสุดใช้พื้นเหลือง (.row--below-min) ควบคู่ป้ายนี้ — สี + ไอคอน + ข้อความตามกฎ UI */
export const BELOW_MIN_ROW_CLASS = 'row--below-min'

export function BelowMinBadge({ minStock }: { minStock?: number | null }) {
  return <span className="below-min-chip" title={minStock != null ? `พร้อมใช้ต่ำกว่าหรือเท่ากับจุดต่ำสุด ${minStock}` : undefined}>
    <AlertTriangle aria-hidden="true" size={14} />ถึงจุดต่ำสุด
  </span>
}
