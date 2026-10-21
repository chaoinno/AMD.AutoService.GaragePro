import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  CalendarClock,
  CircleAlert,
  CircleCheck,
  Copy,
  ExternalLink,
  Eye,
  Hourglass,
  Landmark,
  Lock,
  ReceiptText,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import { useState } from 'react'
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
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { formatDateOnly, formatDateTime } from '../../lib/format'
import { PAID_WARN_DAYS, TRIAL_WARN_DAYS } from '../../lib/plan'
import { useSession } from '../../lib/session'
import { copyText } from './copyText'
import { PackagePaymentForm } from './PackagePaymentForm'
import { PackagePaymentStatusChip, PackageSlipDialog } from './PackageParts'
import {
  cannotSubmitReason,
  formatFileSize,
  monthOptionLabel,
  packageDaysText,
  packagePlanLabel,
  packageUrgency,
  paymentStatusDetail,
  paymentStatusMeta,
  type PackageUrgency,
} from './packageUx'
import { isSelfServePlan, LINE_OA_ID, LINE_OA_URL } from './trialUx'
import { ME_QUERY_KEY, SELFSERVE_STATUS_QUERY_KEY, useSessionPlan } from './usePlan'
import './plan.css'

export const PACKAGE_OVERVIEW_QUERY_KEY = ['package-overview'] as const
export const PACKAGE_PAYMENTS_QUERY_KEY = ['package-payments'] as const
const HISTORY_TAKE = 20

function isNotApplicable(error: unknown): boolean {
  // เฉพาะรหัสนี้: 404 อื่น (เช่น backend ยังไม่มี route) ต้องเป็น error ปกติ ไม่ใช่ "หน้านี้ไม่ใช่ของอู่คุณ"
  return isApiError(error) && error.code === 'SELFSERVE_NOT_APPLICABLE'
}

/**
 * หน้า "แพ็กเกจและการชำระเงิน" (docs/17 §6.1): อู่ที่สมัครเอง (sp1) ดูวันหมดอายุ แจ้งชำระพร้อมสลิป และติดตามผล
 * เปิดได้ทุกบทบาท · แจ้งชำระ/ยกเลิก/ดูสลิปเป็นของผู้จัดการ (server ตัดยอดเงิน หมายเหตุ และสลิปให้บทบาทอื่น)
 * เปิดได้แม้อ่านอย่างเดียว (route ใต้ /api/v1/selfserve ที่ TrialReadOnlyFilter ยกเว้น)
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
    body = (
      <div className="package-page">
        <CurrentPackageCard overview={data} refreshing={overview.isFetching} onRefresh={refreshAll} />
        {data.pending ? <PendingPaymentCard payment={data.pending} isManager={isManager} onChanged={refreshAll} /> : null}
        <div className="package-grid">
          <Card className="package-card package-card--form">
            <header className="package-card__header">
              <h2><Wallet aria-hidden="true" /> แจ้งชำระเงิน</h2>
            </header>
            {data.canSubmit
              ? <PackagePaymentForm overview={data} onChanged={refreshAll} />
              : <CannotSubmitNotice overview={data} />}
          </Card>
          <PaymentInfoCard overview={data} />
        </div>
        <PaymentHistoryCard query={payments} isManager={isManager} />
      </div>
    )
  }

  return <AppShell title="แพ็กเกจและการชำระเงิน">{body}</AppShell>
}

// ---- แพ็กเกจปัจจุบัน -----------------------------------------------------------------------------------

const URGENCY_META: Record<Exclude<PackageUrgency, 'none'>, { icon: LucideIcon; className: string }> = {
  ok: { icon: CircleCheck, className: 'package-days--ok' },
  warn: { icon: Hourglass, className: 'package-days--warn' },
  expired: { icon: Lock, className: 'package-days--expired' },
}

function CurrentPackageCard({
  overview,
  refreshing,
  onRefresh,
}: {
  overview: PackageOverview
  refreshing: boolean
  onRefresh: () => void
}) {
  const urgency = packageUrgency(overview, overview.plan === 'paid' ? PAID_WARN_DAYS : TRIAL_WARN_DAYS)
  const daysText = packageDaysText(overview.daysLeft)
  const urgencyMeta = urgency === 'none' ? null : URGENCY_META[urgency]
  const UrgencyIcon = urgencyMeta?.icon
  return (
    <Card className="package-card">
      <header className="package-card__header">
        <h2><CalendarClock aria-hidden="true" /> แพ็กเกจปัจจุบัน</h2>
        <span className="package-card__header-side">
          {refreshing ? <span className="package-refreshing" role="status">กำลังปรับข้อมูลล่าสุด…</span> : null}
          <Button variant="ghost" size="sm" onClick={onRefresh}>โหลดใหม่</Button>
        </span>
      </header>
      <dl className="package-facts">
        <div>
          <dt>รหัสอู่</dt>
          <dd className="package-mono">{overview.workspaceNo}</dd>
        </div>
        <div>
          <dt>แพ็กเกจ</dt>
          <dd>{packagePlanLabel(overview.plan)}</dd>
        </div>
        <div>
          <dt>ใช้งานได้ถึง</dt>
          <dd>{overview.lastUsableDate ? formatDateOnly(overview.lastUsableDate) : 'ไม่มีกำหนดหมดอายุ'}</dd>
        </div>
        <div>
          <dt>สถานะ</dt>
          <dd>
            {urgencyMeta && UrgencyIcon ? (
              <span className={`package-days ${urgencyMeta.className}`}>
                <UrgencyIcon aria-hidden="true" />
                {overview.readOnly ? `อ่านอย่างเดียว${daysText ? ` · ${daysText}` : ''}` : daysText ?? 'ใช้งานได้ตามปกติ'}
              </span>
            ) : (
              <span className="package-days package-days--ok"><CircleCheck aria-hidden="true" /> ใช้งานได้ตามปกติ</span>
            )}
          </dd>
        </div>
      </dl>
      {overview.readOnly ? (
        <Alert variant="destructive" className="package-card__alert">
          <Lock aria-hidden="true" />
          <div>
            <AlertTitle>ตอนนี้ระบบเป็นอ่านอย่างเดียว ดูและพิมพ์ข้อมูลได้ แต่บันทึกงานไม่ได้</AlertTitle>
            <AlertDescription>
              ข้อมูลของอู่ยังอยู่ครบ แจ้งชำระเงินได้ตามปกติ หลังทีมงานอนุมัติ ระบบกลับมาบันทึกได้ภายในไม่กี่นาที โดยไม่ต้องเข้าสู่ระบบใหม่
            </AlertDescription>
          </div>
        </Alert>
      ) : null}
    </Card>
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
  return (
    <Card className="package-card package-card--pending">
      <header className="package-card__header">
        <h2><ReceiptText aria-hidden="true" /> รายการแจ้งชำระที่รอตรวจ</h2>
        <PackagePaymentStatusChip status={payment.status} />
      </header>
      <dl className="package-facts package-facts--pending">
        <div>
          <dt>เลขที่รายการ</dt>
          <dd className="package-mono">{payment.shortId}</dd>
        </div>
        <div>
          <dt>จำนวนเดือน</dt>
          <dd>{monthOptionLabel(payment.monthCount)}</dd>
        </div>
        {typeof payment.amount === 'number' ? (
          <div>
            <dt>ยอดที่โอน</dt>
            <dd>
              <Money value={payment.amount} suffix=" บาท" />
              {typeof payment.expectedAmount === 'number' ? (
                <small className="package-sub">ราคามาตรฐาน <Money value={payment.expectedAmount} /> บาท</small>
              ) : null}
            </dd>
          </div>
        ) : null}
        <div>
          <dt>วันเวลาที่โอน</dt>
          <dd>{formatDateTime(payment.paidAt)}</dd>
        </div>
        <div>
          <dt>แจ้งเมื่อ</dt>
          <dd>
            {formatDateTime(payment.submittedAt)}
            {payment.submittedBy ? <small className="package-sub">โดย {payment.submittedBy}</small> : null}
          </dd>
        </div>
        {payment.note ? (
          <div className="package-facts__wide">
            <dt>หมายเหตุ</dt>
            <dd>{payment.note}</dd>
          </div>
        ) : null}
      </dl>
      <p className="package-card__note">
        <Hourglass aria-hidden="true" /> ทีมงานกำลังตรวจสลิป ผลการตรวจแจ้งในกระดิ่งแจ้งเตือนและอีเมลเจ้าของอู่ ระหว่างนี้แจ้งชำระรายการใหม่ไม่ได้
      </p>
      <div className="package-card__actions">
        {payment.slip ? (
          <Button variant="outline" size="sm" onClick={() => setSlipOpen(true)}>
            <Eye aria-hidden="true" /> ดูสลิป ({formatFileSize(payment.slip.sizeBytes)})
          </Button>
        ) : null}
        {isManager ? (
          <Button variant="destructive-outline" size="sm" onClick={() => setConfirmOpen(true)}>
            ยกเลิกรายการนี้
          </Button>
        ) : (
          <span className="package-muted">ผู้จัดการอู่ยกเลิกรายการนี้ได้ เช่น แนบสลิปผิดไฟล์</span>
        )}
      </div>
      {confirmOpen ? (
        <CancelPaymentDialog payment={payment} onClose={() => setConfirmOpen(false)} onChanged={onChanged} />
      ) : null}
      {slipOpen ? <PackageSlipDialog payment={payment} onClose={() => setSlipOpen(false)} /> : null}
    </Card>
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
          {mutation.isPending ? <span className="package-form__status" role="status">กำลังยกเลิก…</span> : null}
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

// ---- แจ้งชำระไม่ได้ ------------------------------------------------------------------------------------

function CannotSubmitNotice({ overview }: { overview: PackageOverview }) {
  const reason = cannotSubmitReason(overview) ?? ''
  const pending = overview.cannotSubmitCode === 'pending_exists'
  return (
    <div className="package-cannot">
      <span className="package-cannot__icon" aria-hidden="true">{pending ? <Hourglass /> : <Lock />}</span>
      <div>
        {/* [UI] เหตุผลจาก server ตรงตัว (docs/17 §6.1 ห้ามแต่งเอง) */}
        <strong role="status">{reason}</strong>
        {overview.cannotSubmitCode === 'unavailable' || overview.cannotSubmitCode === 'not_renewable' ? (
          <p>
            สอบถามทีมงานได้ทาง{' '}
            <a href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">LINE {LINE_OA_ID} <ExternalLink aria-hidden="true" /></a>
          </p>
        ) : pending ? (
          <p>ดูรายละเอียดรายการที่รอตรวจด้านบน</p>
        ) : null}
      </div>
    </div>
  )
}

// ---- บัญชีรับเงิน + ราคา --------------------------------------------------------------------------------

function PaymentInfoCard({ overview }: { overview: PackageOverview }) {
  const account = overview.paymentAccount
  const options = overview.monthOptions ?? []
  const hasAccount = Boolean(account && account.accountName && (account.accountNo || account.promptPayId))
  const anyPrice = options.some((option) => typeof option.price === 'number')
  return (
    <Card className="package-card">
      <header className="package-card__header">
        <h2><Landmark aria-hidden="true" /> โอนเงินเข้าบัญชี</h2>
      </header>
      {hasAccount && account ? (
        <dl className="package-account">
          {account.bankName ? (
            <div>
              <dt>ธนาคาร</dt>
              <dd>{account.bankName}</dd>
            </div>
          ) : null}
          {account.accountNo ? (
            <div>
              <dt>เลขที่บัญชี</dt>
              <dd>
                <span className="package-mono package-account__number">{account.accountNo}</span>
                <Button variant="ghost" size="sm" onClick={() => void copyText(account.accountNo ?? '', 'เลขที่บัญชี')}>
                  <Copy aria-hidden="true" /> คัดลอก
                </Button>
              </dd>
            </div>
          ) : null}
          <div>
            <dt>ชื่อบัญชี</dt>
            <dd>{account.accountName}</dd>
          </div>
          {account.promptPayId ? (
            <div>
              <dt>พร้อมเพย์</dt>
              <dd>
                <span className="package-mono package-account__number">{account.promptPayId}</span>
                <Button variant="ghost" size="sm" onClick={() => void copyText(account.promptPayId ?? '', 'เลขพร้อมเพย์')}>
                  <Copy aria-hidden="true" /> คัดลอก
                </Button>
              </dd>
            </div>
          ) : null}
        </dl>
      ) : (
        <Alert variant="warning" className="package-card__alert">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>ยังไม่ได้ตั้งบัญชีรับเงิน</AlertTitle>
            <AlertDescription>
              ติดต่อทีมงานทาง <a href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">LINE {LINE_OA_ID}</a> เพื่อสอบถามช่องทางชำระค่าแพ็กเกจ
            </AlertDescription>
          </div>
        </Alert>
      )}

      <h3 className="package-subheading">ราคาแพ็กเกจ</h3>
      {options.length ? (
        <table className="package-price-table">
          <thead>
            <tr>
              <th scope="col">จำนวนเดือน</th>
              <th scope="col" className="package-num">ราคา (บาท)</th>
              <th scope="col">ถ้าอนุมัติวันนี้ ใช้ได้ถึง</th>
            </tr>
          </thead>
          <tbody>
            {options.map((option) => (
              <tr key={option.months}>
                <td>{monthOptionLabel(option.months)}</td>
                <td className="package-num">
                  {typeof option.price === 'number' ? <Money value={option.price} /> : <span className="package-muted">ยังไม่ตั้งราคา</span>}
                </td>
                <td>{option.previewLastUsableDate ? formatDateOnly(option.previewLastUsableDate) : <span className="package-muted">ไม่ทราบ</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="package-muted">ยังไม่มีตัวเลือกจำนวนเดือน ติดต่อทีมงานทาง LINE {LINE_OA_ID}</p>
      )}
      {options.length && !anyPrice ? (
        <p className="package-muted">ยังไม่ได้ตั้งราคามาตรฐาน สอบถามยอดได้ที่ LINE {LINE_OA_ID}</p>
      ) : null}
      <p className="package-muted">วันหมดอายุใหม่คำนวณตอนทีมงานอนุมัติ ต่อจากวันหมดเดิม ไม่เสียวันที่เหลือ ถ้าหมดอายุไปแล้วนับจากวันที่อนุมัติ</p>
    </Card>
  )
}

// ---- ประวัติ --------------------------------------------------------------------------------------------

function PaymentHistoryCard({
  query,
  isManager,
}: {
  query: { isPending: boolean; isError: boolean; error: unknown; data?: PackagePayment[]; refetch: () => unknown }
  isManager: boolean
}) {
  const [slipFor, setSlipFor] = useState<PackagePayment | null>(null)
  const items = query.data ?? []
  return (
    <Card className="package-card">
      <header className="package-card__header">
        <h2><ReceiptText aria-hidden="true" /> ประวัติการแจ้งชำระ</h2>
        {!isManager ? <span className="package-muted">ยอดเงินและสลิปแสดงเฉพาะผู้จัดการอู่</span> : null}
      </header>
      {query.isPending ? (
        <SkeletonRows count={3} />
      ) : query.isError ? (
        <Alert variant="destructive" className="package-card__alert">
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
        <p className="package-muted package-empty">ยังไม่มีประวัติการแจ้งชำระเงิน รายการที่แจ้งแล้วจะแสดงที่นี่พร้อมผลการตรวจ</p>
      ) : (
        <div className="package-table-wrap">
          <table className="package-history">
            <thead>
              <tr>
                <th scope="col">แจ้งเมื่อ</th>
                <th scope="col">จำนวนเดือน</th>
                {isManager ? <th scope="col" className="package-num">ยอดที่โอน (บาท)</th> : null}
                <th scope="col">วันเวลาที่โอน</th>
                <th scope="col">สถานะ</th>
                {isManager ? <th scope="col">สลิป</th> : null}
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <HistoryRow key={item.id} item={item} isManager={isManager} onViewSlip={() => setSlipFor(item)} />
              ))}
            </tbody>
          </table>
        </div>
      )}
      {slipFor ? <PackageSlipDialog payment={slipFor} onClose={() => setSlipFor(null)} /> : null}
    </Card>
  )
}

function HistoryRow({ item, isManager, onViewSlip }: { item: PackagePayment; isManager: boolean; onViewSlip: () => void }) {
  const detail = paymentStatusDetail(item, formatDateOnly)
  const status = paymentStatusMeta(item.status).status
  return (
    <tr>
      <td>
        {formatDateTime(item.submittedAt)}
        <small className="package-sub package-mono">{item.shortId}</small>
      </td>
      <td>{monthOptionLabel(item.monthCount)}</td>
      {isManager ? (
        <td className="package-num">
          {typeof item.amount === 'number' ? <Money value={item.amount} /> : <span className="package-muted">ไม่แสดง</span>}
        </td>
      ) : null}
      <td>{formatDateTime(item.paidAt)}</td>
      <td>
        <PackagePaymentStatusChip status={item.status} />
        {detail ? <small className={`package-sub package-sub--${status}`}>{detail}</small> : null}
        {status === 'rejected' && item.rejectNote ? <small className="package-sub">หมายเหตุทีมงาน: {item.rejectNote}</small> : null}
        {status === 'cancelled' && item.cancelledAt ? <small className="package-sub">ยกเลิกเมื่อ {formatDateTime(item.cancelledAt)}</small> : null}
        {(status === 'approved' || status === 'rejected') && item.reviewedAt ? (
          <small className="package-sub">ตรวจเมื่อ {formatDateTime(item.reviewedAt)}</small>
        ) : null}
      </td>
      {isManager ? (
        <td>
          {item.slip ? (
            <Button variant="outline" size="sm" onClick={onViewSlip}>
              <Eye aria-hidden="true" /> ดูสลิป
            </Button>
          ) : (
            <Badge variant="outline">ไม่มีไฟล์</Badge>
          )}
        </td>
      ) : null}
    </tr>
  )
}
