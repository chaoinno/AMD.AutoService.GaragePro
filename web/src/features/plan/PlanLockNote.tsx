import { Lock } from 'lucide-react'
import type { FeatureKey } from '../../lib/plan'
import { useFeatureLock } from './usePlan'
import './plan.css'

/**
 * เหตุผลที่ปุ่มถูกปิดเพราะแพ็กเกจ (เช่น ส่งออก) — ข้อความจริงข้างปุ่ม ไม่ใช่ tooltip อย่างเดียว (กฎ UI: ปุ่มที่ปิดต้องบอกเหตุผล)
 * ลูกค้าเดิม/แพ็กเกจเต็มไม่ render อะไร
 */
export function PlanLockNote({ feature }: { feature: FeatureKey }) {
  const { locked, reason } = useFeatureLock(feature)
  if (!locked) return null
  return (
    <span className="plan-lock-note" role="note">
      <Lock aria-hidden="true" /> {reason}
    </span>
  )
}
