import { CheckCircle2, Clock3, ExternalLink, Info, Lock, type LucideIcon } from 'lucide-react'
import { daysLeftOf, formatDaysLeft, trialTone, type TrialTone } from '../../lib/plan'
import { Button } from '../../components/ui/button'
import { useTrialDialog } from './TrialDialog'
import { useExtensionRequest, useSessionPlan } from './usePlan'
import { EXTENSION_REQUESTED_REASON, LINE_OA_ID, LINE_OA_URL, trialBannerCopy } from './trialUx'

const TONE_ICONS: Record<Exclude<TrialTone, 'none'>, LucideIcon> = {
  neutral: Info,
  warn: Clock3,
  expired: Lock,
}

/**
 * แถบทดลองใช้บนสุดของทุกหน้า (ก่อน topbar · ไม่พิมพ์) — สี + ไอคอน + ข้อความตามกฎ UI
 * ลูกค้าเดิม/แพ็กเกจเต็ม: ไม่ render อะไรเลย
 */
export function TrialBanner() {
  const { plan } = useSessionPlan()
  const tone = trialTone(plan)
  if (tone === 'none' || !plan) return null
  return <TrialBannerContent tone={tone} suspended={plan.kind === 'suspended'} daysLabel={formatDaysLeft(daysLeftOf(plan))} />
}

function TrialBannerContent({ tone, suspended, daysLabel }: { tone: Exclude<TrialTone, 'none'>; suspended: boolean; daysLabel: string }) {
  const openTrialDialog = useTrialDialog()
  const { alreadyRequested } = useExtensionRequest()
  const copy = trialBannerCopy(tone, daysLabel, suspended)
  if (!copy) return null
  const Icon = TONE_ICONS[tone]
  return (
    <section
      className={`trial-banner trial-banner--${tone} print-hidden`}
      role={tone === 'neutral' ? 'status' : 'alert'}
      aria-label="สถานะแพ็กเกจทดลองใช้"
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
          variant={tone === 'neutral' ? 'outline' : 'default'}
          disabled={alreadyRequested}
          title={alreadyRequested ? EXTENSION_REQUESTED_REASON : undefined}
          onClick={() => openTrialDialog()}
        >
          ขอต่ออายุ / สมัครแพ็กเกจ
        </Button>
      </div>
    </section>
  )
}
