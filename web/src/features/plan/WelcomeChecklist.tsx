import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  CheckCircle2,
  Circle,
  CircleAlert,
  Copy,
  Eraser,
  KeyRound,
  ListChecks,
  RefreshCw,
  X,
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { clearSampleData, isSelfServeNotApplicable } from '../../api/selfserve'
import { ConfirmModal } from '../../components/ConfirmModal'
import { SkeletonRows } from '../../components/StateBlock'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { copyText } from './copyText'
import { MobileAppInstall } from './MobileAppInstall'
import { useSelfServeStatus, useSessionPlan } from './usePlan'
import {
  checklistProgress,
  credentialsBelongTo,
  deriveChecklist,
  limitUsageItems,
  parseSampleCredentials,
  SAMPLE_CREDENTIALS_KEY,
  welcomeDismissKey,
  type ChecklistItem,
  type SampleCredentials,
} from './trialUx'
import './plan.css'

/**
 * การ์ดเริ่มต้นใช้งานบนหน้าจ๊อบ (docs/12 §4.3) — เฉพาะอู่ที่ "ทดลองใช้" · ปิดได้ (จำต่อ workspace ในเครื่องนี้)
 * `?welcome=1` (มาจากหน้าสมัคร) บังคับเปิดอีกครั้ง + toast ต้อนรับ · ลูกค้าเดิม/แพ็กเกจเต็มไม่ render อะไร
 */
export function WelcomeChecklist({ onCreateJob }: { onCreateJob: () => void }) {
  const { plan, workspace } = useSessionPlan()
  if (plan?.kind !== 'trial') return null
  return <WelcomeChecklistCard workspaceNo={workspace?.workspaceNo} onCreateJob={onCreateJob} />
}

function readStorage(storage: 'local' | 'session', key: string): string | null {
  try {
    return (storage === 'local' ? window.localStorage : window.sessionStorage).getItem(key)
  } catch {
    return null
  }
}

function writeStorage(storage: 'local' | 'session', key: string, value: string | null) {
  try {
    const target = storage === 'local' ? window.localStorage : window.sessionStorage
    if (value === null) target.removeItem(key)
    else target.setItem(key, value)
  } catch {
    // storage ถูกบล็อก (โหมดส่วนตัว/นโยบายเบราว์เซอร์) — การ์ดยังทำงานได้ แค่จำการปิดไม่ได้
  }
}

function readCredentials(workspaceNo: string | undefined): SampleCredentials | null {
  const credentials = parseSampleCredentials(readStorage('session', SAMPLE_CREDENTIALS_KEY))
  return credentials && credentialsBelongTo(credentials, workspaceNo) ? credentials : null
}

function WelcomeChecklistCard({ workspaceNo, onCreateJob }: { workspaceNo: string | undefined; onCreateJob: () => void }) {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const status = useSelfServeStatus()
  const resolvedNo = workspaceNo ?? (status.data ? String(status.data.workspaceNo) : undefined)
  const dismissKey = welcomeDismissKey(resolvedNo ?? 'unknown')
  const [dismissed, setDismissed] = useState(() => readStorage('local', dismissKey) === '1')
  const [credentials, setCredentials] = useState(() => readCredentials(workspaceNo))
  const welcomed = useRef(false)

  // workspace มาทีหลัง (อ่านจาก status) — อ่านค่าปิดการ์ดของ workspace นั้นใหม่
  useEffect(() => {
    if (searchParams.get('welcome') !== '1') setDismissed(readStorage('local', dismissKey) === '1')
    // eslint-disable-next-line react-hooks/exhaustive-deps -- เปลี่ยนตาม workspace เท่านั้น
  }, [dismissKey])

  useEffect(() => {
    if (welcomed.current || searchParams.get('welcome') !== '1') return
    welcomed.current = true
    writeStorage('local', dismissKey, null)
    setDismissed(false)
    setCredentials(readCredentials(workspaceNo))
    toast.success('ยินดีต้อนรับสู่ ServicePro', {
      description: 'เริ่มจากเช็คลิสต์ 5 ข้อบนหน้าจ๊อบ ระบบติ๊กให้เองเมื่อทำเสร็จจริง',
    })
    // เอา welcome ออกจาก URL — รีโหลดหน้าแล้วไม่ต้องต้อนรับซ้ำ (การ์ดยังเปิดอยู่เพราะล้างค่าปิดไว้แล้ว)
    const params = new URLSearchParams(window.location.search)
    params.delete('welcome')
    const search = params.toString()
    navigate({ search: search ? `?${search}` : '' }, { replace: true })
  }, [dismissKey, navigate, searchParams, workspaceNo])

  const dismiss = () => {
    writeStorage('local', dismissKey, '1')
    setDismissed(true)
  }

  const hideCredentials = () => {
    writeStorage('session', SAMPLE_CREDENTIALS_KEY, null)
    setCredentials(null)
  }

  // ไม่ใช่อู่ self-serve จริง (server ตอบ 404) — ไม่มีอะไรให้แสดง
  if (status.isError && isSelfServeNotApplicable(status.error)) return null

  const credentialsPanel = credentials ? <SampleCredentialsPanel credentials={credentials} onHide={hideCredentials} /> : null
  // รหัสช่างตัวอย่างแสดงครั้งเดียว — แม้ซ่อนการ์ดเช็คลิสต์ไปแล้วก็ยังต้องเห็นจนกว่าจะกด "จดแล้ว"
  if (dismissed) return credentialsPanel ? <div className="welcome-checklist welcome-checklist--credentials-only print-hidden">{credentialsPanel}</div> : null

  const items = deriveChecklist(status.data)
  const progress = checklistProgress(items)
  const limits = limitUsageItems(status.data?.limits)

  return (
    <section className="welcome-checklist print-hidden" aria-labelledby="welcome-checklist-title">
      <header className="welcome-checklist__header">
        <span className="welcome-checklist__icon" aria-hidden="true"><ListChecks /></span>
        <div className="welcome-checklist__heading">
          <p className="eyebrow">เริ่มต้นใช้งาน{resolvedNo ? ` · อู่ ${resolvedNo}` : ''}</p>
          <h2 id="welcome-checklist-title">เริ่มใช้ ServicePro ใน 5 ขั้น</h2>
          <p>ระบบติ๊กให้เองเมื่อทำเสร็จจริง ไม่ต้องติ๊กเอง</p>
        </div>
        {status.data ? (
          <div className="welcome-checklist__progress" aria-label={`ทำแล้ว ${progress.done} จาก ${progress.total} ขั้น`}>
            <strong>{progress.done}/{progress.total}</strong>
            <span>ทำแล้ว</span>
            <span className="welcome-checklist__bar" aria-hidden="true">
              <span style={{ width: `${(progress.done / progress.total) * 100}%` }} />
            </span>
          </div>
        ) : null}
        <Button variant="ghost" size="sm" onClick={dismiss} aria-label="ซ่อนการ์ดเริ่มต้นใช้งาน">
          <X aria-hidden="true" /> ซ่อนการ์ดนี้
        </Button>
      </header>

      {credentialsPanel}

      {status.isPending ? (
        <div className="welcome-checklist__state" role="status">
          <p>กำลังโหลดความคืบหน้าของอู่…</p>
          <SkeletonRows count={3} />
        </div>
      ) : status.isError ? (
        <Alert variant="destructive" className="welcome-checklist__state">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>{isApiError(status.error) ? status.error.messageTh : 'โหลดความคืบหน้าไม่สำเร็จ'}</AlertTitle>
            <AlertDescription>
              รหัสติดตาม (traceId): {isApiError(status.error) ? status.error.traceId : 'ไม่พบรหัสติดตาม'}
            </AlertDescription>
            <Button variant="outline" size="sm" onClick={() => void status.refetch()}>
              <RefreshCw aria-hidden="true" /> ลองใหม่
            </Button>
          </div>
        </Alert>
      ) : (
        <ol className="welcome-checklist__items">
          {items.map((item, index) => (
            <ChecklistRow key={item.key} item={item} index={index} onCreateJob={onCreateJob} />
          ))}
        </ol>
      )}

      {status.data ? (
        <footer className="welcome-checklist__footer">
          {limits.length ? (
            <p className="welcome-checklist__limits">
              <span>ใช้ไปแล้วในแพ็กเกจทดลองใช้:</span>{' '}
              {limits.map((limit, index) => (
                <span key={limit.key} className={limit.full ? 'welcome-checklist__limit--full' : undefined}>
                  {index > 0 ? ' · ' : ''}
                  {limit.label} {limit.used.toLocaleString('th-TH')}/{limit.max.toLocaleString('th-TH')}
                  {limit.full ? ' (เต็มแล้ว)' : ''}
                </span>
              ))}
            </p>
          ) : <span />}
          {status.data.sampleDataLoadedAt ? <ClearSampleDataButton /> : null}
        </footer>
      ) : null}
    </section>
  )
}

function ChecklistRow({ item, index, onCreateJob }: { item: ChecklistItem; index: number; onCreateJob: () => void }) {
  return (
    <li className={`welcome-checklist__item${item.done ? ' welcome-checklist__item--done' : ''}`}>
      <span className="welcome-checklist__status">
        {item.done ? <CheckCircle2 aria-hidden="true" /> : <Circle aria-hidden="true" />}
        <span className="welcome-checklist__status-text">{item.done ? 'เสร็จแล้ว' : `ขั้นที่ ${index + 1}`}</span>
      </span>
      <div className="welcome-checklist__text">
        <strong>{item.label}</strong>
        <span>{item.hint}</span>
        {item.action.kind === 'mobileApp' && !item.done ? <MobileAppInstall compact /> : null}
      </div>
      <div className="welcome-checklist__action">
        {item.done ? null : item.action.kind === 'link' ? (
          <Link className="ui-button ui-button--outline ui-button--size-sm" to={item.action.to}>{item.action.label}</Link>
        ) : item.action.kind === 'createJob' ? (
          <Button variant="outline" size="sm" onClick={onCreateJob}>{item.action.label}</Button>
        ) : null}
      </div>
    </li>
  )
}

function SampleCredentialsPanel({ credentials, onHide }: { credentials: SampleCredentials; onHide: () => void }) {
  return (
    <div className="sample-credentials" role="region" aria-label="รหัสช่างตัวอย่าง">
      <div className="sample-credentials__head">
        <KeyRound aria-hidden="true" />
        <strong>รหัสช่างตัวอย่าง</strong>
        <span className="sample-credentials__once">แสดงครั้งเดียว</span>
      </div>
      <p>ใช้ทดลองเข้าแอป ServicePro บนมือถือในฐานะช่าง คัดลอกหรือจดไว้ก่อนกดซ่อน ระบบจะไม่แสดงรหัสผ่านนี้อีก</p>
      <ul className="sample-credentials__list">
        {credentials.technicians.map((tech) => (
          <li key={tech.userName}>
            <span className="sample-credentials__field">
              <small>ชื่อผู้ใช้</small>
              <code>{tech.userName}</code>
              <Button variant="ghost" size="icon" aria-label={`คัดลอกชื่อผู้ใช้ ${tech.userName}`} onClick={() => void copyText(tech.userName, 'ชื่อผู้ใช้')}>
                <Copy aria-hidden="true" />
              </Button>
            </span>
            <span className="sample-credentials__field">
              <small>รหัสผ่าน</small>
              <code>{tech.password}</code>
              <Button variant="ghost" size="icon" aria-label={`คัดลอกรหัสผ่านของ ${tech.userName}`} onClick={() => void copyText(tech.password, 'รหัสผ่าน')}>
                <Copy aria-hidden="true" />
              </Button>
            </span>
          </li>
        ))}
      </ul>
      <Button variant="outline" size="sm" onClick={onHide}>จดแล้ว ซ่อนรหัสนี้</Button>
    </div>
  )
}

function ClearSampleDataButton() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const clear = useMutation({
    mutationFn: clearSampleData,
    onSuccess: () => {
      setOpen(false)
      toast.success('ล้างข้อมูลตัวอย่างแล้ว', { description: 'เหลือเฉพาะข้อมูลที่อู่เพิ่มเอง' })
      // ทุกแคชอาจมีลูกค้า/จ๊อบตัวอย่างค้างอยู่ — ล้างทั้งหมด · ใช้ resetQueries แทน clear() เพราะ clear() ถอด query
      // ออกจากแคชโดยไม่ปลุก observer ที่ mount อยู่ (ตารางจ๊อบข้างใต้จะค้างข้อมูลเก่าจนกว่าจะเปลี่ยนหน้า)
      // ส่วน reset ล้างข้อมูลทุก query แล้วดึงใหม่เฉพาะที่หน้าจอใช้อยู่ทันที
      void queryClient.resetQueries()
    },
  })
  const close = () => {
    if (clear.isPending) return
    clear.reset()
    setOpen(false)
  }
  return (
    <>
      <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
        <Eraser aria-hidden="true" /> ล้างข้อมูลตัวอย่าง
      </Button>
      <ConfirmModal
        open={open}
        title="ล้างข้อมูลตัวอย่าง"
        description="ลบข้อมูลที่ระบบสร้างให้ตอนสมัคร ข้อมูลที่อู่เพิ่มเองไม่ถูกลบ"
        onClose={close}
        size="small"
        footer={
          <>
            <Button variant="ghost" disabled={clear.isPending} onClick={close}>ยกเลิก</Button>
            <Button variant="destructive" disabled={clear.isPending} onClick={() => clear.mutate()}>
              {clear.isPending ? 'กำลังล้าง…' : 'ล้างข้อมูลตัวอย่าง'}
            </Button>
          </>
        }
      >
        <div className="welcome-checklist__confirm">
          <ul>
            <li>ลบจ๊อบ ใบเสนอราคา ลูกค้า รถ และเทมเพลตตัวอย่าง</li>
            <li>ปิดใช้งานบัญชีช่างตัวอย่าง (เข้าแอปด้วยรหัสตัวอย่างไม่ได้อีก)</li>
            <li>ลบสินค้า/บริการตัวอย่างที่ยังไม่ถูกใช้ รายการที่ใช้ในใบเสนอราคาจริงแล้วจะถูกปิดใช้งานแทน</li>
          </ul>
          <p><strong>ทำย้อนกลับไม่ได้</strong></p>
          {clear.isError ? (
            <Alert variant="destructive">
              <CircleAlert aria-hidden="true" />
              <div>
                <AlertTitle>{isApiError(clear.error) ? clear.error.messageTh : 'ล้างข้อมูลตัวอย่างไม่สำเร็จ'}</AlertTitle>
                <AlertDescription>
                  รหัสติดตาม (traceId): {isApiError(clear.error) ? clear.error.traceId : 'ไม่พบรหัสติดตาม'}
                </AlertDescription>
              </div>
            </Alert>
          ) : null}
        </div>
      </ConfirmModal>
    </>
  )
}
