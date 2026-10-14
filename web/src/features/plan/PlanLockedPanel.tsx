import { CheckCircle2, ExternalLink, Lock, Send } from 'lucide-react'
import { Button, buttonVariants } from '../../components/ui/button'
import { FEATURES, type FeatureKey } from '../../lib/plan'
import { useTrialDialog } from './TrialDialog'
import { useExtensionRequest } from './usePlan'
import { EXTENSION_REQUESTED_REASON, featureInterestNote, LINE_OA_ID, LINE_OA_URL, lockedActionReason } from './trialUx'
import './plan.css'

/**
 * ส่วนย่อยของหน้าที่เปิดให้ทดลองใช้ แต่เป็นของฟีเจอร์ที่แพ็กเกจล็อก (เช่น เบิกสินค้าในการ์ดจ๊อบ · ซัพพลายเออร์ในฟอร์มสินค้า)
 * — แสดงกุญแจ + เหตุผล + ทางติดต่อ แทนการเรียก API ที่รู้อยู่แล้วว่าได้ 403 (ไม่ขึ้น "โหลด…ไม่สำเร็จ")
 * · ผู้เรียกเป็นคนตัดสินว่าล็อกหรือไม่ด้วย `isFeatureLocked` — ลูกค้าเดิม/แพ็กเกจเต็มต้องไม่เห็น component นี้เลย
 */
export function PlanLockedPanel({ feature, title, detail }: { feature: FeatureKey; title?: string; detail?: string }) {
  const info = FEATURES[feature]
  return (
    <div className="plan-locked-panel" role="note">
      <span className="plan-locked-panel__icon" aria-hidden="true"><Lock /></span>
      <div className="plan-locked-panel__copy">
        <strong>{title ?? lockedActionReason(info.label)}</strong>
        <span>{detail ?? info.description}</span>
        <PlanInterestActions feature={feature} withLine />
      </div>
    </div>
  )
}

/**
 * ปุ่ม "สนใจ ติดต่อเรา" (เปิดหน้าต่างคำขอถึงทีมขายของ W2) — ส่งได้วันละครั้ง ส่งแล้วปุ่มปิดพร้อมข้อความเหตุผลข้างปุ่ม
 * `withLine` เพิ่มลิงก์ LINE ไว้ข้างกัน (ใช้ได้เสมอ แม้หน้าต่างคำขอใช้ไม่ได้)
 */
export function PlanInterestActions({ feature, withLine = false }: { feature: FeatureKey; withLine?: boolean }) {
  const info = FEATURES[feature]
  const openTrialDialog = useTrialDialog()
  const { alreadyRequested } = useExtensionRequest()
  return (
    <span className="plan-locked-panel__actions">
      <Button
        type="button"
        size="sm"
        variant="outline"
        disabled={alreadyRequested}
        title={alreadyRequested ? EXTENSION_REQUESTED_REASON : undefined}
        onClick={() => openTrialDialog({ note: featureInterestNote(info.label), featureLabel: info.label })}
      >
        <Send aria-hidden="true" /> สนใจ ติดต่อเรา
      </Button>
      {withLine ? (
        <a className={buttonVariants({ variant: 'ghost', size: 'sm' })} href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">
          ทัก LINE {LINE_OA_ID} <ExternalLink aria-hidden="true" />
        </a>
      ) : null}
      {alreadyRequested ? (
        <span className="plan-locked-panel__sent" role="status">
          <CheckCircle2 aria-hidden="true" /> {EXTENSION_REQUESTED_REASON}
        </span>
      ) : null}
    </span>
  )
}
