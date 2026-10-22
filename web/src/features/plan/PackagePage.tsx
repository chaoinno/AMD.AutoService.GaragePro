import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  BadgeCheck,
  Ban,
  CalendarCheck,
  CalendarClock,
  Check,
  CircleAlert,
  CircleCheck,
  ExternalLink,
  Eye,
  FlaskConical,
  Hourglass,
  Lock,
  ReceiptText,
  RefreshCw,
  ShieldCheck,
  Undo2,
  type LucideIcon,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { isApiError, isForbiddenError } from '../../api/client'
import {
  cancelPackagePayment,
  getPackageOverview,
  getPackagePayments,
  type PackageOverview,
  type PackagePayment,
} from '../../api/package'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { formatDateOnly, formatDateTime } from '../../lib/format'
import { PAID_WARN_DAYS, TRIAL_WARN_DAYS } from '../../lib/plan'
import { useSession } from '../../lib/session'
import { PackageRenewForm } from './PackageRenewForm'
import { PAYMENT_STATUS_ICONS, PackagePaymentStatusChip, PackageSlipDialog, PackageSlipThumb } from './PackageParts'
import {
  cannotSubmitReason,
  currentPeriodDays,
  monthOptionLabel,
  packagePlanLabel,
  packageStatusLabel,
  packageUrgency,
  paymentStatusMeta,
  rejectReasonText,
  remainingFraction,
  type PackageUrgency,
} from './packageUx'
import { isSelfServePlan, LINE_OA_ID, LINE_OA_URL } from './trialUx'
import { ME_QUERY_KEY, SELFSERVE_STATUS_QUERY_KEY, useSessionPlan } from './usePlan'
import './package.css'

export const PACKAGE_OVERVIEW_QUERY_KEY = ['package-overview'] as const
export const PACKAGE_PAYMENTS_QUERY_KEY = ['package-payments'] as const
const HISTORY_TAKE = 20

function isNotApplicable(error: unknown): boolean {
  // เฉพาะรหัสนี้: 404 อื่น (เช่น backend ยังไม่มี route) ต้องเป็น error ปกติ ไม่ใช่ "หน้านี้ไม่ใช่ของอู่คุณ"
  return isApiError(error) && error.code === 'SELFSERVE_NOT_APPLICABLE'
}

/**
 * หน้า "แพ็กเกจและการชำระเงิน" (docs/17 §6.1 · §6.3): อู่ที่สมัครเอง (sp1) ดูวันหมดอายุ แจ้งชำระพร้อมสลิป และติดตามผล
 * เปิดได้ทุกบทบาท · แจ้งชำระ/ยกเลิก/ดูสลิปเป็นของผู้จัดการ (server ตัดยอดเงิน หมายเหตุ และสลิปให้บทบาทอื่น)
 * เปิดได้แม้อ่านอย่างเดียว (route ใต้ /api/v1/selfserve ที่ TrialReadOnlyFilter ยกเว้น)
 *
 * [UI] โครงหน้า: การ์ดหัวสีกรมท่า (แพ็กเกจปัจจุบัน) → รายการรอตรวจ → ต่ออายุ 3 ขั้น + สรุปยอด → ประวัติแบบ timeline
 * ใช้สี/ฟอนต์/token เดิมของระบบเท่านั้น (สีบนพื้นเข้มชุดเดียวกับหน้า landing)
 */
export function PackagePage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { session } = useSession()
  const { plan } = useSessionPlan()
  const selfServe = isSelfServePlan(plan)
  const isManager = (session?.user.role ?? '').toLowerCase() === 'manager'

  const overview = useQuery({
    queryKey: PACKAGE_OVERVIEW_QUERY_KEY,
    queryFn: getPackageOverview,
    enabled: selfServe,
    staleTime: 30_000,
    retry: (failureCount, error) => !isNotApplicable(error) && !isForbiddenError(error) && failureCount < 1,
  })
  const payments = useQuery({
    queryKey: [...PACKAGE_PAYMENTS_QUERY_KEY, HISTORY_TAKE],
    queryFn: () => getPackagePayments(HISTORY_TAKE),
    enabled: selfServe && overview.isSuccess,
    staleTime: 30_000,
    retry: (failureCount, error) => !isNotApplicable(error) && !isForbiddenError(error) && failureCount < 1,
  })

  const refreshAll = () => {
    void queryClient.invalidateQueries({ queryKey: PACKAGE_OVERVIEW_QUERY_KEY })
    void queryClient.invalidateQueries({ queryKey: PACKAGE_PAYMENTS_QUERY_KEY })
    // แพ็กเกจในเซสชัน (/auth/me) และสถานะ self-serve อาจเปลี่ยนตามผลการแจ้งชำระ
    void queryClient.invalidateQueries({ queryKey: ME_QUERY_KEY })
    void queryClient.invalidateQueries({ queryKey: SELFSERVE_STATUS_QUERY_KEY })
  }

  const goToJobs = () => navigate('/jobs')

  let body
  if (!selfServe || (overview.isError && isNotApplicable(overview.error))) {
    body = (
      <StateBlock
        variant="empty"
        title="หน้านี้สำหรับอู่ที่สมัครใช้ ServicePro ด้วยตัวเอง"
        reason="อู่ของคุณใช้แพ็กเกจของ GaragePro การต่ออายุทำผ่านทีมงาน GaragePro ตามกระบวนการเดิม"
        traceId={overview.isError && isApiError(overview.error) ? overview.error.traceId : 'ไม่ใช่คำขอ API'}
        actionLabel="กลับไปหน้าจ๊อบ"
        onAction={goToJobs}
      />
    )
  } else if (overview.isPending) {
    body = (
      <StateBlock
        variant="loading"
        title="กำลังโหลดข้อมูลแพ็กเกจ"
        reason="ระบบกำลังอ่านวันหมดอายุและรายการแจ้งชำระของอู่"
        actionLabel="โหลดใหม่"
        onAction={() => void overview.refetch()}
      >
        <SkeletonRows count={3} />
      </StateBlock>
    )
  } else if (overview.isError) {
    const forbidden = isForbiddenError(overview.error)
    body = (
      <StateBlock
        variant={forbidden ? 'forbidden' : 'error'}
        title={forbidden ? 'ไม่มีสิทธิ์ดูข้อมูลแพ็กเกจ' : 'โหลดข้อมูลแพ็กเกจไม่สำเร็จ'}
        reason={isApiError(overview.error) ? overview.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(overview.error) ? overview.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void overview.refetch()}
      />
    )
  } else {
    const data = overview.data
    const renewable = data.cannotSubmitCode !== 'not_renewable'
    body = (
      <div className="package-page">
        <PackageHero
          overview={data}
          periodDays={currentPeriodDays(data, payments.data)}
          refreshing={overview.isFetching}
          onRefresh={refreshAll}
        />
        {data.pending ? <PendingPaymentCard payment={data.pending} isManager={isManager} onChanged={refreshAll} /> : null}
        {renewable ? <PackageRenewForm overview={data} onChanged={refreshAll} /> : <NotRenewableCard overview={data} />}
        <PaymentHistory query={payments} isManager={isManager} />
      </div>
    )
  }

  return <AppShell title="แพ็กเกจและการชำระเงิน">{body}</AppShell>
}

// ---- การ์ดหัว: แพ็กเกจปัจจุบัน ----------------------------------------------------------------------------

const URGENCY_ICON: Record<PackageUrgency, LucideIcon> = {
  none: CircleCheck,
  ok: CircleCheck,
  warn: Hourglass,
  expired: Lock,
}

function planBadge(plan: string): { icon: LucideIcon; tone: string; label: string } {
  switch (plan.toLowerCase()) {
    case 'trial':
      return { icon: FlaskConical, tone: 'trial', label: 'ทดลองใช้' }
    case 'paid':
      return { icon: BadgeCheck, tone: 'paid', label: 'ชำระเงินแล้ว' }
    case 'suspended':
      return { icon: Ban, tone: 'suspended', label: 'ถูกระงับ' }
    default:
      return { icon: ShieldCheck, tone: 'other', label: packagePlanLabel(plan) }
  }
}

function PackageHero({
  overview,
  periodDays,
  refreshing,
  onRefresh,
}: {
  overview: PackageOverview
  periodDays: number | null
  refreshing: boolean
  onRefresh: () => void
}) {
  const plan = String(overview.plan ?? '')
  const urgency = packageUrgency(overview, plan === 'paid' ? PAID_WARN_DAYS : TRIAL_WARN_DAYS)
  const StatusIcon = URGENCY_ICON[urgency]
  const badge = planBadge(plan)
  const BadgeIcon = badge.icon
  const daysLeft = typeof overview.daysLeft === 'number' ? overview.daysLeft : undefined
  const fraction = remainingFraction(daysLeft, periodDays)

  return (
    <section className={`package-hero package-hero--${urgency}`} aria-labelledby="package-hero-date">
      <div className="package-hero__top">
        <span className="package-hero__eyebrow"><CalendarClock aria-hidden="true" /> แพ็กเกจปัจจุบัน</span>
        <span className="package-hero__code" title="รหัสอู่">
          <span className="sr-only">รหัสอู่ </span>{overview.workspaceNo}
        </span>
        <span className={`package-plan-badge package-plan-badge--${badge.tone}`}>
          <BadgeIcon aria-hidden="true" /> {badge.label}
        </span>
        <span className="package-hero__refresh">
          {refreshing ? <span className="package-hero__refreshing" role="status">กำลังปรับข้อมูลล่าสุด…</span> : null}
          <Button variant="ghost" size="sm" className="package-hero__refresh-button" onClick={onRefresh} disabled={refreshing}>
            <RefreshCw className={refreshing ? 'spin' : undefined} aria-hidden="true" /> โหลดใหม่
          </Button>
        </span>
      </div>

      <div className="package-hero__main">
        <div className="package-hero__copy">
          <p className="package-hero__label">{overview.lastUsableDate ? 'ใช้งานได้ถึง' : 'วันหมดอายุ'}</p>
          <h2 id="package-hero-date" className="package-hero__date">
            {overview.lastUsableDate ? formatDateOnly(overview.lastUsableDate) : 'ไม่มีกำหนดหมดอายุ'}
          </h2>
          <p className={`package-hero__status package-hero__status--${urgency}`}>
            <StatusIcon aria-hidden="true" /> {packageStatusLabel(overview)}
          </p>
          {!overview.readOnly && urgency !== 'expired' && overview.lastUsableDate ? (
            <p className="package-hero__note">
              <ShieldCheck aria-hidden="true" /> ต่ออายุก่อนหมดได้ วันที่เหลือไม่หาย ระบบนับต่อจากวันหมดเดิม
            </p>
          ) : null}
        </div>
        {daysLeft !== undefined ? <DaysMeter daysLeft={daysLeft} periodDays={periodDays} fraction={fraction} urgency={urgency} /> : null}
      </div>

      {overview.readOnly ? (
        <div className="package-hero__alert" role="note">
          <Lock aria-hidden="true" />
          <div>
            <strong>ตอนนี้ระบบเป็นอ่านอย่างเดียว ดูและพิมพ์ข้อมูลได้ แต่บันทึกงานไม่ได้</strong>
            <p>ข้อมูลของอู่ยังอยู่ครบ แจ้งชำระเงินได้ตามปกติ หลังทีมงานอนุมัติ ระบบกลับมาบันทึกได้ภายในไม่กี่นาที โดยไม่ต้องเข้าสู่ระบบใหม่</p>
          </div>
        </div>
      ) : null}
    </section>
  )
}

const RING_RADIUS = 52
const RING_CIRCUMFERENCE = 2 * Math.PI * RING_RADIUS

/** วงแหวนวันที่เหลือ: วาดเฉพาะเมื่อรู้ความยาวรอบจริง (ไม่รู้ = แสดงตัวเลขอย่างเดียว ไม่เดาสัดส่วน) */
function DaysMeter({
  daysLeft,
  periodDays,
  fraction,
  urgency,
}: {
  daysLeft: number
  periodDays: number | null
  fraction: number | null
  urgency: PackageUrgency
}) {
  // เริ่มจากวงว่างแล้วค่อยเติมหลัง mount (CSS ปิด transition เมื่อผู้ใช้ตั้งลดการเคลื่อนไหว)
  const [drawn, setDrawn] = useState(false)
  useEffect(() => {
    const frame = requestAnimationFrame(() => setDrawn(true))
    return () => cancelAnimationFrame(frame)
  }, [])
  const remaining = Math.max(0, daysLeft)
  // หมดอายุแล้ว: แสดงจำนวนวันที่เลยมา (0 เฉยๆ ไม่บอกอะไร)
  const shown = daysLeft < 0 ? -daysLeft : daysLeft
  const label = daysLeft < 0 ? 'วันหลังหมดอายุ' : 'วันที่เหลือ'
  const valueText = daysLeft < 0
    ? `หมดอายุแล้ว ${shown} วัน`
    : periodDays ? `เหลือ ${remaining} วัน จากรอบ ${periodDays} วัน` : `เหลือ ${remaining} วัน`
  const content = (
    <span className="package-meter__value">
      <strong>{shown}</strong>
      <small>{label}</small>
    </span>
  )
  if (fraction === null || !periodDays) {
    return (
      <div className={`package-meter package-meter--plain package-meter--${urgency}`} role="img" aria-label={valueText}>
        {content}
      </div>
    )
  }
  const offset = RING_CIRCUMFERENCE * (1 - (drawn ? fraction : 0))
  return (
    <figure className={`package-meter package-meter--${urgency}`}>
      <div
        className="package-meter__ring"
        role="meter"
        aria-label="เวลาใช้งานที่เหลือ"
        aria-valuemin={0}
        aria-valuemax={periodDays}
        aria-valuenow={Math.min(remaining, periodDays)}
        aria-valuetext={valueText}
      >
        <svg viewBox="0 0 128 128" aria-hidden="true">
          <circle className="package-meter__track" cx="64" cy="64" r={RING_RADIUS} />
          <circle
            className="package-meter__arc"
            cx="64"
            cy="64"
            r={RING_RADIUS}
            strokeDasharray={RING_CIRCUMFERENCE}
            strokeDashoffset={offset}
          />
        </svg>
        {content}
      </div>
      <figcaption>จากรอบ {periodDays} วัน</figcaption>
    </figure>
  )
}

// ---- รายการรอตรวจ -----------------------------------------------------------------------------------

function PendingPaymentCard({
  payment,
  isManager,
  onChanged,
}: {
  payment: PackagePayment
  isManager: boolean
  onChanged: () => void
}) {
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [slipOpen, setSlipOpen] = useState(false)
  const showSlip = isManager && Boolean(payment.slip)
  return (
    <section id="package-pending" className="package-pending" aria-labelledby="package-pending-title" tabIndex={-1}>
      {showSlip ? (
        <PackageSlipThumb payment={payment} onOpen={() => setSlipOpen(true)} />
      ) : (
        <span className="package-pending__icon" aria-hidden="true"><Hourglass /></span>
      )}
      <div className="package-pending__body">
        <header className="package-pending__head">
          <h2 id="package-pending-title">รายการแจ้งชำระที่รอตรวจ</h2>
          <PackagePaymentStatusChip status={payment.status} />
        </header>
        <p className="package-pending__meta">
          เลขที่ <span className="package-mono">{payment.shortId}</span> · แจ้งเมื่อ {formatDateTime(payment.submittedAt)}
          {payment.submittedBy ? ` โดย ${payment.submittedBy}` : ''}
        </p>

        <dl className="package-pending__facts">
          <div>
            <dt>ระยะเวลา</dt>
            <dd>{monthOptionLabel(payment.monthCount)}</dd>
          </div>
          {typeof payment.amount === 'number' ? (
            <div>
              <dt>ยอดที่โอน</dt>
              <dd>
                <span className="package-pending__amount"><Money value={payment.amount} /> <small>บาท</small></span>
                {typeof payment.expectedAmount === 'number' && Math.round(payment.expectedAmount * 100) !== Math.round(payment.amount * 100) ? (
                  <small className="package-pending__diff">
                    ยอดที่ต้องโอน <Money value={payment.expectedAmount} /> บาท
                  </small>
                ) : null}
              </dd>
            </div>
          ) : null}
          <div>
            <dt>วันเวลาที่โอน</dt>
            <dd>{formatDateTime(payment.paidAt)}</dd>
          </div>
          {payment.note ? (
            <div className="package-pending__wide">
              <dt>หมายเหตุ</dt>
              <dd>{payment.note}</dd>
            </div>
          ) : null}
        </dl>

        <ol className="package-progress" aria-label="ขั้นตอนหลังแจ้งชำระ">
          <li className="package-progress__item package-progress__item--done">
            <span aria-hidden="true"><Check /></span> แจ้งชำระแล้ว<span className="sr-only"> (เสร็จแล้ว)</span>
          </li>
          <li className="package-progress__item package-progress__item--current" aria-current="step">
            <span aria-hidden="true"><Hourglass /></span> ทีมงานตรวจสลิป
          </li>
          <li className="package-progress__item">
            <span aria-hidden="true"><CalendarCheck /></span> ต่ออายุทันทีที่อนุมัติ<span className="sr-only"> (ยังไม่ถึง)</span>
          </li>
        </ol>

        <p className="package-pending__note">
          ผลการตรวจแจ้งในกระดิ่งแจ้งเตือนและอีเมลเจ้าของอู่ ระหว่างนี้แจ้งชำระรายการใหม่ไม่ได้
        </p>
        <div className="package-pending__actions">
          {showSlip ? (
            <Button variant="outline" size="sm" onClick={() => setSlipOpen(true)}>
              <Eye aria-hidden="true" /> ดูสลิป
            </Button>
          ) : null}
          {isManager ? (
            <Button variant="destructive-outline" size="sm" onClick={() => setConfirmOpen(true)}>
              <Undo2 aria-hidden="true" /> ยกเลิกรายการนี้
            </Button>
          ) : (
            <span className="package-muted">ผู้จัดการอู่ยกเลิกรายการนี้ได้ เช่น แนบสลิปผิดไฟล์ · ยอดเงินและสลิปแสดงเฉพาะผู้จัดการ</span>
          )}
        </div>
      </div>
      {confirmOpen ? (
        <CancelPaymentDialog payment={payment} onClose={() => setConfirmOpen(false)} onChanged={onChanged} />
      ) : null}
      {slipOpen ? <PackageSlipDialog payment={payment} onClose={() => setSlipOpen(false)} /> : null}
    </section>
  )
}

function CancelPaymentDialog({
  payment,
  onClose,
  onChanged,
}: {
  payment: PackagePayment
  onClose: () => void
  onChanged: () => void
}) {
  const mutation = useMutation({
    mutationFn: () => cancelPackagePayment(payment.id),
    onSuccess: () => {
      toast.success(`ยกเลิกรายการ ${payment.shortId} แล้ว แจ้งชำระรายการใหม่ได้ทันที`)
      onChanged()
      onClose()
    },
    onError: (error) => {
      // ทีมงานตรวจไปแล้วระหว่างนี้ (PACKAGE_PAYMENT_NOT_PENDING) หรือไม่พบ → สถานะในหน้าเก่าแล้ว
      if (isApiError(error) && (error.code === 'PACKAGE_PAYMENT_NOT_PENDING' || error.code === 'PACKAGE_PAYMENT_NOT_FOUND')) {
        onChanged()
      }
    },
  })
  const error = mutation.error
  return (
    <ConfirmModal
      open
      size="small"
      title={`ยกเลิกรายการแจ้งชำระ ${payment.shortId}?`}
      description="ใช้เมื่อแจ้งผิด เช่น แนบสลิปผิดไฟล์หรือกรอกยอดผิด ยกเลิกแล้วแจ้งชำระรายการใหม่ได้ทันที"
      onClose={() => {
        if (!mutation.isPending) onClose()
      }}
      footer={
        <div className="package-dialog-footer">
          {mutation.isPending ? <span className="package-dialog-footer__status" role="status">กำลังยกเลิก…</span> : null}
          <Button variant="ghost" disabled={mutation.isPending} onClick={onClose}>ไม่ยกเลิก</Button>
          <Button variant="destructive" disabled={mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังยกเลิก…' : 'ยืนยันยกเลิก'}
          </Button>
        </div>
      }
    >
      {error ? (
        <Alert variant="destructive">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>{isApiError(error) ? error.messageTh : 'ยกเลิกไม่สำเร็จ'}</AlertTitle>
            <AlertDescription>รหัสติดตาม (traceId): {isApiError(error) ? error.traceId : 'ไม่พบรหัสติดตาม'}</AlertDescription>
          </div>
        </Alert>
      ) : (
        <p className="package-muted">รายการที่ยกเลิกยังอยู่ในประวัติ ทีมงานจะไม่ตรวจรายการนี้</p>
      )}
    </ConfirmModal>
  )
}

// ---- ต่ออายุไม่ได้ (converted · paid ไม่มีวันหมด) -----------------------------------------------------------

function NotRenewableCard({ overview }: { overview: PackageOverview }) {
  return (
    <section className="package-blocked package-blocked--card" aria-label="การต่ออายุแพ็กเกจ">
      <span className="package-blocked__icon" aria-hidden="true"><ShieldCheck /></span>
      <div>
        {/* [UI] เหตุผลจาก server ตรงตัว (docs/17 §6.1 ห้ามแต่งเอง) */}
        <strong role="status">{cannotSubmitReason(overview)}</strong>
        <p>
          สอบถามทีมงานได้ทาง{' '}
          <a href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">LINE {LINE_OA_ID} <ExternalLink aria-hidden="true" /></a>
        </p>
      </div>
    </section>
  )
}

// ---- ประวัติ (timeline) ------------------------------------------------------------------------------------

function PaymentHistory({
  query,
  isManager,
}: {
  query: { isPending: boolean; isError: boolean; error: unknown; data?: PackagePayment[]; refetch: () => unknown }
  isManager: boolean
}) {
  const [slipFor, setSlipFor] = useState<PackagePayment | null>(null)
  const items = query.data ?? []
  return (
    <section className="package-history" aria-labelledby="package-history-title">
      <header className="package-section-head package-section-head--inline">
        <div>
          <h2 id="package-history-title"><ReceiptText aria-hidden="true" /> ประวัติการแจ้งชำระ</h2>
          <p>ใหม่สุดอยู่บน · แสดง {HISTORY_TAKE} รายการล่าสุด</p>
        </div>
        {!isManager ? <span className="package-muted">ยอดเงินและสลิปแสดงเฉพาะผู้จัดการอู่</span> : null}
      </header>
      {query.isPending ? (
        <SkeletonRows count={3} />
      ) : query.isError ? (
        <Alert variant="destructive">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>{isApiError(query.error) ? query.error.messageTh : 'โหลดประวัติไม่สำเร็จ'}</AlertTitle>
            <AlertDescription>
              รหัสติดตาม (traceId): {isApiError(query.error) ? query.error.traceId : 'ไม่พบรหัสติดตาม'}{' '}
              <Button variant="link" size="sm" onClick={() => void query.refetch()}>ลองใหม่</Button>
            </AlertDescription>
          </div>
        </Alert>
      ) : items.length === 0 ? (
        <div className="package-history__empty">
          <ReceiptText aria-hidden="true" />
          <p>ยังไม่มีประวัติการแจ้งชำระเงิน รายการที่แจ้งแล้วจะแสดงที่นี่พร้อมผลการตรวจ</p>
        </div>
      ) : (
        <ol className="package-timeline">
          {items.map((item) => (
            <TimelineItem key={item.id} item={item} isManager={isManager} onViewSlip={() => setSlipFor(item)} />
          ))}
        </ol>
      )}
      {slipFor ? <PackageSlipDialog payment={slipFor} onClose={() => setSlipFor(null)} /> : null}
    </section>
  )
}

function TimelineItem({ item, isManager, onViewSlip }: { item: PackagePayment; isManager: boolean; onViewSlip: () => void }) {
  const status = paymentStatusMeta(item.status).status
  const Icon = PAYMENT_STATUS_ICONS[status]
  return (
    <li className={`package-timeline__item package-timeline__item--${status}`}>
      <span className="package-timeline__dot" aria-hidden="true"><Icon /></span>
      <article className="package-timeline__card">
        <div className="package-timeline__head">
          <PackagePaymentStatusChip status={item.status} />
          <strong className="package-timeline__months">{monthOptionLabel(item.monthCount)}</strong>
          <span className="package-timeline__id package-mono">#{item.shortId}</span>
          {isManager && typeof item.amount === 'number' ? (
            <span className="package-timeline__amount"><Money value={item.amount} /> <small>บาท</small></span>
          ) : null}
        </div>
        <div className="package-timeline__meta-row">
          <p className="package-timeline__meta">
            แจ้งเมื่อ {formatDateTime(item.submittedAt)} · โอนเมื่อ {formatDateTime(item.paidAt)}
            {item.submittedBy ? ` · โดย ${item.submittedBy}` : ''}
          </p>
          {isManager && item.slip ? (
            <Button variant="ghost" size="sm" className="package-timeline__slip" onClick={onViewSlip}>
              <Eye aria-hidden="true" /> ดูสลิป
            </Button>
          ) : null}
        </div>

        {status === 'approved' ? (
          <p className="package-timeline__result package-timeline__result--approved">
            <CalendarCheck aria-hidden="true" />
            {item.periodLastUsableDate ? `ต่ออายุแล้ว ใช้งานได้ถึง ${formatDateOnly(item.periodLastUsableDate)}` : 'ต่ออายุแล้ว'}
            {item.reviewedAt ? <small>ตรวจเมื่อ {formatDateTime(item.reviewedAt)}</small> : null}
          </p>
        ) : status === 'rejected' ? (
          <div className="package-timeline__result package-timeline__result--rejected">
            <strong>เหตุผล: {rejectReasonText(item)}</strong>
            {item.rejectNote ? <p>หมายเหตุทีมงาน: {item.rejectNote}</p> : null}
            {item.reviewedAt ? <small>ตรวจเมื่อ {formatDateTime(item.reviewedAt)}</small> : null}
          </div>
        ) : status === 'cancelled' ? (
          <p className="package-timeline__result">
            {item.cancelledAt ? `ยกเลิกเมื่อ ${formatDateTime(item.cancelledAt)}` : 'ยกเลิกแล้ว'}
          </p>
        ) : status === 'pending' ? (
          <p className="package-timeline__result package-timeline__result--pending">
            <Hourglass aria-hidden="true" /> ทีมงานกำลังตรวจสลิป
          </p>
        ) : null}

      </article>
    </li>
  )
}
