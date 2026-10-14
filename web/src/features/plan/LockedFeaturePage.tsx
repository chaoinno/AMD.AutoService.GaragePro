import { CheckCircle2, ExternalLink, Lock, Send } from 'lucide-react'
import type { ReactElement } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { AppShell } from '../../components/AppShell'
import { Button } from '../../components/ui/button'
import { lockedFeatureForPath, type FeatureInfo } from '../../lib/plan'
import { useTrialDialog } from './TrialDialog'
import { useExtensionRequest, useSessionPlan } from './usePlan'
import { EXTENSION_REQUESTED_REASON, featureInterestNote, LINE_OA_ID, LINE_OA_URL } from './trialUx'
import './plan.css'

/**
 * ครอบ route ของฟีเจอร์ที่แพ็กเกจล็อกได้ (path จาก `lib/plan.ts` FEATURES ที่เดียว) — ล็อกอยู่ = แสดงหน้าอธิบายแทน
 * และไม่ render หน้าจริงเลย หน้าจริงจึงไม่ยิง API ที่รู้อยู่แล้วว่าจะได้ 403 · ลูกค้าเดิมผ่านตลอด
 */
export function FeatureGate({ children }: { children: ReactElement }) {
  const { plan } = useSessionPlan()
  const { pathname } = useLocation()
  const feature = lockedFeatureForPath(plan, pathname)
  return feature ? <LockedFeaturePage feature={feature} /> : children
}

export function LockedFeaturePage({ feature }: { feature: FeatureInfo }) {
  return (
    <AppShell title={feature.label}>
      <LockedFeatureContent feature={feature} />
    </AppShell>
  )
}

// แยกเป็นลูกของ AppShell — useTrialDialog() ต้องอยู่ใต้ TrialDialogProvider ที่ AppShell ครอบไว้
function LockedFeatureContent({ feature }: { feature: FeatureInfo }) {
  const navigate = useNavigate()
  const openTrialDialog = useTrialDialog()
  const { alreadyRequested } = useExtensionRequest()
  return (
    <section className="locked-feature" aria-labelledby="locked-feature-title">
      <div className="locked-feature__badge">
        <Lock aria-hidden="true" /> ล็อกในแพ็กเกจทดลองใช้
      </div>
      <div className="locked-feature__body">
        <span className="locked-feature__icon" aria-hidden="true"><Lock /></span>
        <div className="locked-feature__copy">
          <h2 id="locked-feature-title">{feature.label} เปิดในแพ็กเกจเต็ม</h2>
          <p>{feature.description}</p>
          <ul className="locked-feature__bullets">
            {feature.bullets.map((bullet) => (
              <li key={bullet}><CheckCircle2 aria-hidden="true" /> {bullet}</li>
            ))}
          </ul>
          <div className="locked-feature__actions">
            <Button
              disabled={alreadyRequested}
              title={alreadyRequested ? EXTENSION_REQUESTED_REASON : undefined}
              onClick={() => openTrialDialog({ note: featureInterestNote(feature.label), featureLabel: feature.label })}
            >
              <Send aria-hidden="true" /> สนใจฟีเจอร์นี้ ติดต่อเรา
            </Button>
            <a className="ui-button ui-button--outline ui-button--size-default" href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">
              ทัก LINE {LINE_OA_ID} <ExternalLink aria-hidden="true" />
            </a>
            <Button variant="ghost" onClick={() => navigate('/jobs')}>กลับไปหน้าจ๊อบ</Button>
          </div>
          {alreadyRequested ? (
            <p className="locked-feature__sent" role="status">
              <CheckCircle2 aria-hidden="true" /> {EXTENSION_REQUESTED_REASON}
            </p>
          ) : null}
          <p className="trace-id">
            รหัสติดตาม (traceId): <span>ไม่ใช่คำขอ API, ล็อกตามแพ็กเกจของอู่</span>
          </p>
        </div>
      </div>
    </section>
  )
}
