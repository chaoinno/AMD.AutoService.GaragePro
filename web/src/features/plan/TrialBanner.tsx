import { CheckCircle2, Clock3, ExternalLink, Info, Lock, Wallet, type LucideIcon } from 'lucide-react'
import { useLocation, useNavigate } from 'react-router'
import { daysLeftOf, formatDaysLeft, trialTone, type TrialTone } from '../../lib/plan'
import { Button } from '../../components/ui/button'
import { useTrialDialog } from './TrialDialog'
import { useExtensionRequest, useSessionPlan } from './usePlan'
import {
  EXTENSION_REQUESTED_REASON,
  extensionButtonLabel,
  LINE_OA_ID,
  LINE_OA_URL,
  trialBannerCopy,
  trialBannerLabel,
} from './trialUx'

const TONE_ICONS: Record<Exclude<TrialTone, 'none'>, LucideIcon> = {
  neutral: Info,
  warn: Clock3,
  expired: Lock,
}

/** หน้าแพ็กเกจและการชำระเงิน (docs/17 §6.1) */
export const PACKAGE_PATH = '/package'

/**
 * แถบแพ็กเกจบนสุดของทุกหน้า (ก่อน topbar · ไม่พิมพ์): สี + ไอคอน + ข้อความตามกฎ UI
 * · ทดลองใช้: แสดงตลอด · แพ็กเกจที่ชำระเงินแล้ว: เฉพาะเหลือ ≤ 14 วัน หรืออ่านอย่างเดียว (`trialTone`)
 * · ลูกค้าเดิม: ไม่ render อะไรเลย
 */
export function TrialBanner() {
  const { plan } = useSessionPlan()
  const tone = trialTone(plan)
  if (tone === 'none' || !plan) return null
  return (
    <TrialBannerContent
      tone={tone}
      suspended={plan.kind === 'suspended'}
      paid={plan.kind === 'paid'}
      daysLabel={formatDaysLeft(daysLeftOf(plan))}
    />
  )
}

function TrialBannerContent({
  tone,
  suspended,
  paid,
  daysLabel,
}: {
  tone: Exclude<TrialTone, 'none'>
  suspended: boolean
  paid: boolean
  daysLabel: string
}) {
  const openTrialDialog = useTrialDialog()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const { alreadyRequested } = useExtensionRequest()
  const copy = trialBannerCopy(tone, daysLabel, suspended, paid)
  if (!copy) return null
  const Icon = TONE_ICONS[tone]
  // อู่ที่ถูกระงับแจ้งชำระไม่ได้ (server ตอบ PACKAGE_NOT_RENEWABLE) · อยู่หน้าแพ็กเกจแล้วไม่ต้องมีปุ่มพาไปซ้ำ
  const showPayButton = !suspended && pathname !== PACKAGE_PATH
  const urgent = tone !== 'neutral'
  return (
    <section
      className={`trial-banner trial-banner--${tone} print-hidden`}
      role={tone === 'neutral' ? 'status' : 'alert'}
      aria-label={trialBannerLabel(paid)}
    >
      <span className="trial-banner__icon" aria-hidden="true"><Icon /></span>
      <div className="trial-banner__copy">
        <strong>{copy.title}</strong>
        <span>{copy.detail}</span>
      </div>
      <div className="trial-banner__actions">
        <a className="trial-banner__line" href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">
          LINE {LINE_OA_ID} <ExternalLink aria-hidden="true" />
        </a>
        {alreadyRequested ? (
          <span className="trial-banner__sent" role="status">
            <CheckCircle2 aria-hidden="true" /> {EXTENSION_REQUESTED_REASON}
          </span>
        ) : null}
        <Button
          size="sm"
          variant={urgent && !showPayButton ? 'default' : 'outline'}
          disabled={alreadyRequested}
          title={alreadyRequested ? EXTENSION_REQUESTED_REASON : undefined}
          onClick={() => openTrialDialog()}
        >
          {extensionButtonLabel(paid)}
        </Button>
        {showPayButton ? (
          <Button size="sm" variant={urgent ? 'default' : 'outline'} onClick={() => navigate(PACKAGE_PATH)}>
            <Wallet aria-hidden="true" /> แจ้งชำระเงิน
          </Button>
        ) : null}
      </div>
    </section>
  )
}
