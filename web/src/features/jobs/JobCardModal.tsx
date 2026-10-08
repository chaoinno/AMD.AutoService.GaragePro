import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CalendarClock, CheckCircle2, Circle, Printer, TriangleAlert, Trash2 } from 'lucide-react'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { getJobAttachments, uploadAttachment } from '../../api/attachments'
import { isApiError } from '../../api/client'
import { updateVehicleImage } from '../../api/customerVehicles'
import { getHandover, saveHandoverItem, submitHandover, type HandoverItem } from '../../api/handover'
import { getJobIntakeChecklist } from '../../api/intake'
import {
  convertJobToInShop,
  getJob,
  getJobScheduleHistory,
  transitionJob,
  updateJobAppointment,
  updateJobPromise,
} from '../../api/jobs'
import { AttachmentImage } from '../../components/AttachmentImage'
import {
  getPaymentSummary,
  getTaxInvoiceState,
  issueReceipt,
  paymentCommand,
  pendingPaymentCommand,
  recordPayment,
  removePayment,
  setVatIncluded,
  type PaymentMethod,
} from '../../api/pos'
import { getJobWithdrawals, type WithdrawalSummary } from '../../api/purchasing'
import { getQcChecklist, saveQcChecklistItem, saveQcTestDrive, type QcChecklistItem } from '../../api/qc'
import {
  applyQuotationTemplate,
  createQuotation,
  getQuotations,
} from '../../api/quotations'
import type { JobStatusToken, Job, UpsertLineSource } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { JobStatusChip } from '../../components/JobStatusChip'
import { Money } from '../../components/Money'
import { StateBlock } from '../../components/StateBlock'
import { StatusChip } from '../../components/StatusChip'
import { VehicleImage } from '../../components/VehicleImage'
import { JobChatWidget } from './chat/JobChatWidget'
import { Button } from '../../components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Separator } from '../../components/ui/separator'
import { SignaturePad, type SignaturePadHandle } from '../../components/ui/signature-pad'
import { Textarea } from '../../components/ui/textarea'
import {
  formatDateTime,
  formatKm,
  formatMoney,
  isoToLocalInput,
  localInputToIso,
  nowLocalInputValue,
} from '../../lib/format'
import { useSession } from '../../lib/session'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import { StockWithdrawalDocumentModal } from '../purchasing/StockWithdrawalDocumentModal'
import { StockWithdrawalModal } from '../purchasing/StockWithdrawalModal'
import { IntakeChecklistPanel } from './IntakeChecklistPanel'
import { invalidateJobSchedule } from './scheduleQueries'
import { IntakeReceiptModal } from './IntakeReceiptModal'
import { HandoverDocumentModal } from './HandoverDocumentModal'
import { HandoverServiceInfoSection, handoverServiceInfoMissing } from './HandoverServiceInfoSection'
import { mileageInputError } from './mileage'
import { UpdateMileageModal } from './UpdateMileageModal'
import { BillingDocumentModal } from './billing/BillingDocumentModal'
import './billing/billing.css'
import type { BillingKind } from './billing/BillingDocument'
import { TaxInvoiceIssueModal } from './billing/TaxInvoiceIssueModal'
import { QuotationDocumentModal } from '../quotations/QuotationDocumentModal'
import { QuotationEditorModal } from '../quotations/QuotationEditorModal'
import { QuotationTemplatePickerModal } from '../quotations/QuotationTemplatePickerModal'

export type StageKey = 'intake' | 'inspect' | 'quote' | 'repair' | 'qc' | 'payment'

const STAGES: { key: StageKey; label: string }[] = [
  { key: 'intake', label: 'รับรถ' },
  { key: 'inspect', label: 'ตรวจสอบ' },
  { key: 'quote', label: 'เสนอราคา/งานซ่อม' },
  { key: 'repair', label: 'เบิกสินค้า/ดำเนินการซ่อม' },
  { key: 'qc', label: 'QC' },
  { key: 'payment', label: 'ชำระเงิน/ส่งมอบ' },
]

// job.status (svc_Job — เจ้าของสถานะจริง) -> ดัชนี stage บน stepper
const STAGE_INDEX_BY_STATUS: Record<JobStatusToken, number> = {
  waitinspect: 1,
  waitquote: 2,
  waitapprove: 2,
  approved: 2,
  inprogress: 3,
  waitparts: 3,
  qc: 4,
  ready: 5,
  completed: 5,
  cancelled: 5,
}

export function isStageKey(value: string | null): value is StageKey {
  return STAGES.some((stage) => stage.key === value)
}

type JobCardModalProps = {
  jobId: string | null
  onClose: () => void
  /// เปิดมาที่ขั้นนี้แทนขั้นปัจจุบันของจ๊อบ — ใช้กับลิงก์จากแจ้งเตือน (เช่น ผลอนุมัติใบเสนอราคา)
  initialStage?: StageKey
  /// เปิดแผงแชทค้างไว้เลย — ใช้กับแจ้งเตือน "ถูกกล่าวถึงในแชท"
  openChat?: boolean
}

export function JobCardModal({ jobId, onClose, initialStage, openChat = false }: JobCardModalProps) {
  const [viewStage, setViewStage] = useState<number | null>(null)

  // ลิงก์ใหม่ (จ๊อบหรือขั้นเปลี่ยน) ต้องพาไปขั้นที่ลิงก์ระบุ แม้ modal จะเปิดค้างอยู่แล้ว
  useEffect(() => {
    setViewStage(initialStage ? STAGES.findIndex((stage) => stage.key === initialStage) : null)
  }, [jobId, initialStage])

  const jobQuery = useQuery({
    queryKey: ['job-detail', jobId],
    queryFn: () => getJob(jobId!),
    enabled: jobId !== null,
  })

  // ใช้ query key เดียวกับ IntakeChecklistPanel — พอ panel ส่ง checklist สำเร็จแล้ว invalidate
  // key นี้ stepper ก็ได้ isLocked ใหม่มาด้วยโดยอัตโนมัติ
  const checklistQuery = useQuery({
    queryKey: ['job-intake-checklist', jobId],
    queryFn: () => getJobIntakeChecklist(jobId!),
    enabled: jobId !== null,
  })

  const close = () => {
    setViewStage(null)
    onClose()
  }

  const job = jobQuery.data
  // ส่ง checklist สภาพรถขณะรับแล้ว (ล็อกแล้ว) ถือว่าขั้น "ตรวจสอบ" เสร็จ แม้ job.status จะยังเป็น waitinspect —
  // การเปลี่ยน job.status ไป waitquote สงวนไว้สำหรับผลตรวจของช่างบนมือถือ (JobStateMachine: Technician/Mobile เท่านั้น)
  const checklistDone = job?.status === 'waitinspect' && (checklistQuery.data?.isLocked ?? false)
  const currentStageIndex = job ? (checklistDone ? 2 : STAGE_INDEX_BY_STATUS[job.status] ?? 0) : 0
  const stageIndex = viewStage ?? currentStageIndex

  return (
    <ConfirmModal
      open={jobId !== null}
      title={job ? `จัดการจ๊อบ ${job.jobNo}` : 'จัดการจ๊อบ'}
      description={job?.vehicleRegistration
        ? `ทะเบียน ${job.vehicleRegistration} · ${job.customerName || 'ไม่ระบุชื่อลูกค้า'}`
        : 'ข้อมูลจ๊อบ งานซ่อม รูปถ่าย และการชำระเงิน'}
      onClose={close}
      size="xlarge"
    >
      {jobId === null ? null : (
        <>
          {jobQuery.isPending ? (
            <StateBlock
              variant="loading"
              title="กำลังโหลดข้อมูลจ๊อบ"
              reason="ระบบกำลังอ่านข้อมูลล่าสุด"
              traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
              actionLabel="โหลดใหม่"
              onAction={() => void jobQuery.refetch()}
            />
          ) : jobQuery.isError || !job ? (
            <StateBlock
              variant="error"
              title="โหลดข้อมูลจ๊อบไม่สำเร็จ"
              reason={isApiError(jobQuery.error) ? jobQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
              traceId={isApiError(jobQuery.error) ? jobQuery.error.traceId : undefined}
              actionLabel="ลองใหม่"
              onAction={() => void jobQuery.refetch()}
            />
          ) : (
            <div className="job-card-page">
            <header className="job-card-header">
              <div>
                <div className="job-card-header__title">ใบสั่งงานซ่อม (Job Card)</div>
                <div className="job-card-header__jobno">
                  เลขที่ <strong>{job.jobNo}</strong>
                </div>
              </div>
              <div className="job-card-header__meta">
                <dl className="job-card-header__meta-item">
                  <dt>ทะเบียนรถ</dt>
                  <dd className="mono">{job.vehicleRegistration || 'ไม่ระบุทะเบียน'}</dd>
                </dl>
                <div className="job-card-header__divider" aria-hidden="true" />
                <dl className="job-card-header__meta-item">
                  <dt>วันที่รับรถ</dt>
                  <dd>{formatDateTime(job.createdAt)}</dd>
                </dl>
                <JobStatusChip status={job.status} label={job.statusLabel} />
              </div>
            </header>

            <nav className="job-card-stepper" aria-label="ขั้นตอนของงาน">
              <ol className="job-card-stepper__list">
                {STAGES.map((stage, index) => {
                  const done = index < currentStageIndex
                  const active = index === currentStageIndex
                  const viewing = index === stageIndex
                  return (
                    <li key={stage.key} className="job-card-stepper__step">
                      <button
                        type="button"
                        className="job-card-stepper__button"
                        onClick={() => setViewStage(index)}
                      >
                        <span
                          className={[
                            'job-card-stepper__circle',
                            done ? 'job-card-stepper__circle--done' : '',
                            active ? 'job-card-stepper__circle--active' : '',
                            viewing ? 'job-card-stepper__circle--viewing' : '',
                          ].filter(Boolean).join(' ')}
                        >
                          {done ? '✓' : index + 1}
                        </span>
                        <span
                          className={[
                            'job-card-stepper__label',
                            done || active ? 'job-card-stepper__label--reached' : '',
                            viewing ? 'job-card-stepper__label--viewing' : '',
                          ].filter(Boolean).join(' ')}
                        >
                          {stage.label}
                        </span>
                      </button>
                      {index < STAGES.length - 1 ? (
                        <span
                          className={`job-card-stepper__connector ${done ? 'job-card-stepper__connector--done' : ''}`}
                          aria-hidden="true"
                        />
                      ) : null}
                    </li>
                  )
                })}
              </ol>
            </nav>

            <StageContent stageKey={STAGES[stageIndex]?.key ?? 'intake'} job={job} />
            </div>
          )}
          <JobChatWidget key={`${jobId}:${openChat ? 'chat' : ''}`} jobId={jobId} defaultOpen={openChat} />
        </>
      )}
    </ConfirmModal>
  )
}

function StageContent({
  stageKey, job,
}: { stageKey: StageKey; job: Job }) {
  switch (stageKey) {
    case 'intake':
      return <IntakeStage job={job} />
    case 'inspect':
      return <InspectStage job={job} />
    case 'quote':
      return <QuoteStage job={job} />
    case 'repair':
      return <RepairStage job={job} />
    case 'qc':
      return <QcStage job={job} />
    case 'payment':
      return <PaymentStage job={job} />
  }
}

function NotYetAvailableStage({ reason }: { reason: string }) {
  return (
    <Card>
      <CardContent className="job-detail-empty">
        <p>{reason}</p>
      </CardContent>
    </Card>
  )
}

function IntakeStage({ job }: { job: Job }) {
  const queryClient = useQueryClient()
  const vehicleImageInputRef = useRef<HTMLInputElement>(null)
  const [reschedulingAppointment, setReschedulingAppointment] = useState(false)
  const [reschedulingPromise, setReschedulingPromise] = useState(false)
  const [convertingToInShop, setConvertingToInShop] = useState(false)
  const [editingMileage, setEditingMileage] = useState(false)
  const jobClosed = job.status === 'completed' || job.status === 'cancelled'

  const attachmentsQuery = useQuery({
    queryKey: ['job-attachments', job.jobId],
    queryFn: () => getJobAttachments(job.jobId),
  })

  const updateVehicleImageMutation = useMutation({
    mutationFn: (file: File) => updateVehicleImage(job.vehicleId, file),
    onSuccess: () => {
      toast.success('เปลี่ยนรูปรถแล้ว')
      void queryClient.invalidateQueries({ queryKey: ['vehicle-image', job.vehicleId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'เปลี่ยนรูปรถไม่สำเร็จ')
    },
  })

  return (
    <div className="job-card-stage-stack">
      <div className="job-card-columns">
        <Card>
          <CardHeader><CardTitle>ข้อมูลลูกค้า &amp; รถ</CardTitle></CardHeader>
          <CardContent>
            <div className="job-detail-info">
              <div className="job-detail-info__image-wrap">
                <VehicleImage vehicleId={job.vehicleId} className="job-detail-info__image" clickToPreview />
                <input
                  ref={vehicleImageInputRef}
                  type="file"
                  accept="image/*"
                  className="intake-item__file-input"
                  onChange={(e) => {
                    const file = e.target.files?.[0]
                    if (file) updateVehicleImageMutation.mutate(file)
                    e.target.value = ''
                  }}
                />
                <Button
                  variant="outline"
                  size="sm"
                  className="job-detail-info__image-change"
                  disabled={updateVehicleImageMutation.isPending}
                  onClick={() => vehicleImageInputRef.current?.click()}
                >
                  {updateVehicleImageMutation.isPending ? 'กำลังอัปโหลด…' : 'เปลี่ยนรูปรถ'}
                </Button>
              </div>
              <dl className="job-detail-info__grid">
                <div><dt>ชื่อลูกค้า</dt><dd>{job.customerName || 'ไม่ระบุชื่อ'}</dd></div>
                <div><dt>เบอร์โทรศัพท์</dt><dd>{job.customerPhone || 'ไม่ระบุเบอร์โทร'}</dd></div>
                <div><dt>ยี่ห้อ / รุ่น</dt><dd>{job.vehicleModel || 'ไม่ระบุรุ่น'}</dd></div>
                <div><dt>ทะเบียนรถ</dt><dd>{job.vehicleRegistration || 'ไม่ระบุทะเบียน'}</dd></div>
                <div><dt>เลขตัวถัง</dt><dd>{job.vehicleVin || 'ไม่ระบุ'}</dd></div>
                <div>
                  <dt>เลขไมล์ขณะรับรถ</dt>
                  <dd className="job-detail-appointment">
                    {job.mileageAtIntake != null ? (
                      <span className="job-detail-mileage">{formatKm(job.mileageAtIntake)}</span>
                    ) : (
                      <span className="job-detail-overdue" role="status">
                        <TriangleAlert aria-hidden="true" />
                        {job.jobTypeId === 10 ? 'บันทึกตอนรถเข้าอู่' : 'ยังไม่ได้บันทึกเลขไมล์'}
                      </span>
                    )}
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={jobClosed}
                      title={jobClosed ? 'จ๊อบนี้ปิดแล้ว — แก้ไขเลขไมล์ไม่ได้' : undefined}
                      onClick={() => setEditingMileage(true)}
                    >
                      {job.mileageAtIntake != null ? 'แก้ไขเลขไมล์' : 'บันทึกเลขไมล์'}
                    </Button>
                  </dd>
                </div>
                <div>
                  <dt>ประเภทงาน</dt>
                  <dd className="job-detail-type">
                    <span className="job-type-chip">{job.jobTypeName || 'ไม่ระบุ'}</span>
                    {job.jobTypeId === 10 ? (
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => setConvertingToInShop(true)}
                        title="รถมาถึงอู่แล้ว — เปลี่ยนประเภทงานเป็นรถในอู่พร้อมบันทึกวันเวลาที่เข้าจริง"
                      >
                        รถเข้าอู่แล้ว → เปลี่ยนเป็นรถในอู่
                      </Button>
                    ) : null}
                  </dd>
                </div>
                <div><dt>วันที่สร้างจ๊อบ</dt><dd>{formatDateTime(job.createdAt)}</dd></div>
                <div>
                  <dt>วันเวลานัดส่งมอบรถ</dt>
                  <dd className="job-detail-appointment">
                    <span>{job.promiseAt ? formatDateTime(job.promiseAt) : 'ยังไม่ได้นัด'}</span>
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={jobClosed}
                      title={jobClosed ? 'จ๊อบนี้ปิดแล้ว — แก้ไขวันเวลานัดส่งมอบไม่ได้' : undefined}
                      onClick={() => setReschedulingPromise(true)}
                    >
                      {job.promiseAt ? 'แก้ไข/เลื่อนวันส่งมอบ' : 'ตั้งวันนัดส่งมอบ'}
                    </Button>
                    {job.isOverdue ? (
                      <span className="job-detail-overdue" role="status">
                        <TriangleAlert aria-hidden="true" /> เกินกำหนดส่งมอบแล้ว
                      </span>
                    ) : null}
                  </dd>
                </div>
                {job.jobTypeId === 10 || job.appointmentAt ? (
                  <div>
                    <dt>วันเวลานัดหมายเข้ารับบริการ</dt>
                    <dd className="job-detail-appointment">
                      <span>{job.appointmentAt ? formatDateTime(job.appointmentAt) : 'ไม่ระบุ'}</span>
                      {job.jobTypeId === 10 ? (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={jobClosed}
                          title={jobClosed ? 'จ๊อบนี้ปิดแล้ว — แก้ไขวันเวลานัดหมายไม่ได้' : undefined}
                          onClick={() => setReschedulingAppointment(true)}
                        >
                          แก้ไข/เลื่อนนัด
                        </Button>
                      ) : null}
                    </dd>
                  </div>
                ) : null}
                {job.actualArrivalAt ? (
                  <div><dt>วันเวลาที่รถเข้าอู่จริง</dt><dd>{formatDateTime(job.actualArrivalAt)}</dd></div>
                ) : null}
              </dl>
            </div>
            <ScheduleHistory jobId={job.jobId} />
          </CardContent>
        </Card>
        {reschedulingAppointment ? (
          <RescheduleAppointmentModal job={job} onClose={() => setReschedulingAppointment(false)} />
        ) : null}
        {reschedulingPromise ? (
          <ReschedulePromiseModal job={job} onClose={() => setReschedulingPromise(false)} />
        ) : null}
        {convertingToInShop ? (
          <ConvertToInShopModal job={job} onClose={() => setConvertingToInShop(false)} />
        ) : null}
        {editingMileage ? <UpdateMileageModal job={job} onClose={() => setEditingMileage(false)} /> : null}

        <Card>
          <CardHeader><CardTitle>รูปถ่าย/เอกสารแนบของงานนี้</CardTitle></CardHeader>
          <CardContent>
            {attachmentsQuery.isPending ? (
              <p className="form-message">กำลังโหลดรูปถ่าย…</p>
            ) : attachmentsQuery.isError ? (
              <div className="form-error-panel" role="alert">
                <p>{isApiError(attachmentsQuery.error) ? attachmentsQuery.error.messageTh : 'โหลดรูปถ่ายไม่สำเร็จ'}</p>
                <Button variant="outline" size="sm" onClick={() => void attachmentsQuery.refetch()}>ลองใหม่</Button>
              </div>
            ) : !attachmentsQuery.data.length ? (
              <div className="job-detail-empty">
                <p>ยังไม่รองรับการถ่ายรูปรับรถ 5 มุมบังคับและลายเซ็นรับรถแบบอัตโนมัติ — งานนี้ยังไม่มีไฟล์แนบ</p>
              </div>
            ) : (
              <div className="job-detail-photo-grid">
                {attachmentsQuery.data.map((a) => (
                  <AttachmentImage
                    key={a.id}
                    linkClassName="job-detail-photo-grid__item"
                    relativePath={a.relativePath}
                    alt={a.fileName}
                  >
                    <span className="job-detail-photo-grid__kind">{a.kind}</span>
                    <span className="job-detail-photo-grid__meta">{a.uploadedByName} · {formatDateTime(a.uploadedAt)}</span>
                  </AttachmentImage>
                ))}
              </div>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

const SCHEDULE_FIELD_LABEL = { appointment: 'นัดเข้า', promise: 'นัดส่งมอบ' } as const

/// ประวัติการเปลี่ยนวันนัด — อ่านจาก ActivityEvent ที่ server เขียนทุกครั้งที่แก้วันนัดเข้า/นัดส่งมอบ
function ScheduleHistory({ jobId }: { jobId: string }) {
  const historyQuery = useQuery({
    queryKey: ['job-schedule-history', jobId],
    queryFn: () => getJobScheduleHistory(jobId),
  })

  if (historyQuery.isPending) return <p className="form-message">กำลังโหลดประวัติการเปลี่ยนวันนัด…</p>
  if (historyQuery.isError) {
    return (
      <div className="form-error-panel" role="alert">
        <p>
          {isApiError(historyQuery.error) ? historyQuery.error.messageTh : 'โหลดประวัติการเปลี่ยนวันนัดไม่สำเร็จ'}
          {isApiError(historyQuery.error) && historyQuery.error.traceId ? ` (traceId: ${historyQuery.error.traceId})` : ''}
        </p>
        <Button variant="outline" size="sm" onClick={() => void historyQuery.refetch()}>ลองใหม่</Button>
      </div>
    )
  }
  if (!historyQuery.data.length) return null

  return (
    <section className="job-schedule-history" aria-label="ประวัติการเปลี่ยนวันนัด">
      <h4><CalendarClock aria-hidden="true" /> ประวัติการเปลี่ยนวันนัด</h4>
      <ol>
        {historyQuery.data.map((item) => (
          <li key={item.id}>
            <span className={`job-schedule-history__field job-schedule-history__field--${item.field}`}>
              {SCHEDULE_FIELD_LABEL[item.field] ?? item.field}
            </span>
            <span className="job-schedule-history__desc">{item.descriptionTh}</span>
            <span className="job-schedule-history__meta">
              {item.performedByName || 'ไม่ระบุผู้แก้ไข'} · {formatDateTime(item.occurredAt)}
            </span>
          </li>
        ))}
      </ol>
    </section>
  )
}

function ReschedulePromiseModal({ job, onClose }: { job: Job; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [value, setValue] = useState(() => isoToLocalInput(job.promiseAt))
  // [BIZ] วันส่งมอบต้องไม่ก่อนวันที่รถเข้า (API บังคับซ้ำ) — รถในอู่ที่แปลงแล้วเทียบวันเข้าจริง ไม่ใช่วันนัดเดิม
  const arrivalLocal = isoToLocalInput(job.actualArrivalAt ?? job.appointmentAt)
  const nowLocal = nowLocalInputValue()
  const minLocal = arrivalLocal > nowLocal ? arrivalLocal : nowLocal
  const orderError = value && arrivalLocal && value < arrivalLocal
    ? `วันส่งมอบต้องไม่ก่อนวันเวลาที่รถเข้า (${formatDateTime(job.actualArrivalAt ?? job.appointmentAt)})`
    : undefined

  const mutation = useMutation({
    mutationFn: () => updateJobPromise(job.jobId, { promiseAt: localInputToIso(value) }),
    onSuccess: () => {
      toast.success('บันทึกวันเวลานัดส่งมอบแล้ว')
      invalidateJobSchedule(queryClient, job.jobId)
      onClose()
    },
  })

  return (
    <ConfirmModal
      open
      title={job.promiseAt ? 'แก้ไข/เลื่อนวันเวลานัดส่งมอบ' : 'ตั้งวันเวลานัดส่งมอบ'}
      description="ระบบจะบันทึกประวัติการเปลี่ยนแปลง (ค่าเดิม → ค่าใหม่ ผู้แก้ไข และเวลา) ไว้ในจ๊อบนี้"
      onClose={onClose}
      size="small"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>ยกเลิก</Button>
          <Button
            disabled={!value || Boolean(orderError) || mutation.isPending}
            title={!value ? 'กรุณาเลือกวันเวลานัดส่งมอบก่อน' : orderError}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึก'}
          </Button>
        </>
      }
    >
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      <Field label="วันเวลาที่นัดส่งมอบรถคืนลูกค้า *" error={orderError}>
        <Input
          type="datetime-local"
          min={minLocal}
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
      </Field>
    </ConfirmModal>
  )
}

function RescheduleAppointmentModal({ job, onClose }: { job: Job; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [value, setValue] = useState(() => isoToLocalInput(job.appointmentAt))
  // [BIZ] วันนัดเข้าห้ามเลยวันนัดส่งมอบ (API บังคับซ้ำ) — ต้องเลื่อนวันส่งมอบออกไปก่อน
  const promiseLocal = isoToLocalInput(job.promiseAt)
  const orderError = value && promiseLocal && value > promiseLocal
    ? `วันนัดเข้าต้องไม่เลยวันนัดส่งมอบ (${formatDateTime(job.promiseAt)}) — เลื่อนวันส่งมอบก่อน`
    : undefined

  const mutation = useMutation({
    mutationFn: () => updateJobAppointment(job.jobId, { appointmentAt: localInputToIso(value) }),
    onSuccess: () => {
      toast.success('บันทึกวันเวลานัดหมายใหม่แล้ว')
      invalidateJobSchedule(queryClient, job.jobId)
      onClose()
    },
  })

  return (
    <ConfirmModal
      open
      title="แก้ไข/เลื่อนวันเวลานัดหมาย"
      description="ระบบจะบันทึกประวัติการเปลี่ยนแปลง (ค่าเดิม → ค่าใหม่ ผู้แก้ไข และเวลา) ไว้ในจ๊อบนี้"
      onClose={onClose}
      size="small"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>ยกเลิก</Button>
          <Button
            disabled={!value || Boolean(orderError) || mutation.isPending}
            title={!value ? 'กรุณาเลือกวันเวลานัดหมายก่อน' : orderError}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึก'}
          </Button>
        </>
      }
    >
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      <Field label="วันเวลาที่ลูกค้าจะนำรถเข้า *" error={orderError}>
        <Input
          type="datetime-local"
          min={nowLocalInputValue()}
          max={promiseLocal || undefined}
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
      </Field>
    </ConfirmModal>
  )
}

function ConvertToInShopModal({ job, onClose }: { job: Job; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [value, setValue] = useState(() => nowLocalInputValue())
  // [BIZ] รถมาถึงอู่จริงตอนนี้ — ต้องมีเลขไมล์ (API บังคับ) เติมค่าเดิมให้ถ้าบันทึกไว้ก่อนแล้ว
  const [mileageText, setMileageText] = useState(() =>
    job.mileageAtIntake != null ? String(job.mileageAtIntake) : '')
  const mileage = mileageInputError(mileageText)

  const mutation = useMutation({
    mutationFn: () => convertJobToInShop(job.jobId, {
      actualArrivalAt: localInputToIso(value),
      mileageAtIntake: mileage.km!,
    }),
    onSuccess: () => {
      toast.success('เปลี่ยนประเภทงานเป็นรถในอู่แล้ว')
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-calendar'] })
      onClose()
    },
  })

  return (
    <ConfirmModal
      open
      title="เปลี่ยนเป็นรถในอู่"
      description="ใช้เมื่อรถมาถึงอู่จริง — ไม่ต้องตรงกับวันเวลานัดหมายที่ตั้งไว้ (มาก่อน/หลังนัดก็เปลี่ยนได้)"
      onClose={onClose}
      size="small"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>ยกเลิก</Button>
          <Button
            disabled={!value || mileage.km == null || mutation.isPending}
            title={!value ? 'กรุณาเลือกวันเวลาที่รถเข้าอู่ก่อน'
              : mileage.km == null ? (mileage.error ?? 'กรุณากรอกเลขไมล์ขณะรับรถก่อน') : undefined}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? 'กำลังบันทึก…' : 'ยืนยันเปลี่ยนเป็นรถในอู่'}
          </Button>
        </>
      }
    >
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      <Field label="วันเวลาที่รถเข้าอู่จริง *">
        <Input
          type="datetime-local"
          max={nowLocalInputValue()}
          value={value}
          onChange={(e) => setValue(e.target.value)}
        />
      </Field>
      <Field label="เลขไมล์ขณะรับรถ (กม.) *" error={mileage.error}>
        <Input
          inputMode="numeric"
          value={mileageText}
          onChange={(e) => setMileageText(e.target.value)}
          placeholder="เช่น 45210"
        />
      </Field>
    </ConfirmModal>
  )
}

function InspectStage({ job }: { job: Job }) {
  const [receiptOpen, setReceiptOpen] = useState(false)

  return (
    <div className="job-card-stage-stack">
      <div className="job-card-panel-actions intake-print-row">
        <Button variant="outline" onClick={() => setReceiptOpen(true)}>
          <Printer aria-hidden="true" /> พิมพ์ใบรับรถ
        </Button>
      </div>
      <IntakeChecklistPanel job={job} />
      <IntakeReceiptModal open={receiptOpen} job={job} onClose={() => setReceiptOpen(false)} />
    </div>
  )
}

function QuoteStage({ job }: { job: Job }) {
  const queryClient = useQueryClient()
  const [editingQuotationId, setEditingQuotationId] = useState<string | null>(null)
  const [viewingDocumentId, setViewingDocumentId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['job-quotations', job.jobId],
    queryFn: () => getQuotations('', job.jobId),
  })

  // ปุ่ม "ดำเนินการแทนลูกค้า" อยู่ในหน้ารายละเอียดของใบเสนอราคาแต่ละใบ (QuotationEditorModal) — ที่นี่แค่ชี้ทาง
  // "sent" = ส่งแล้วยังไม่เซ็น (เซ็นแล้วจะเป็น approved/partial) — จ๊อบมีได้หลายใบ ทุกใบที่รออยู่ต้องบอก
  const awaitingCustomerQuotations = query.data?.filter((q) => q.status === 'sent') ?? []
  // ลูกค้าเซ็นแล้วแต่จ๊อบยังไม่ขยับตาม (เจอจริง 2026-09-21) — มีความหมายเฉพาะตอนจ๊อบยังไม่ถึง "อนุมัติแล้ว"
  const stuckSignedQuotation = (['waitinspect', 'waitquote', 'waitapprove'] as JobStatusToken[]).includes(job.status)
    ? query.data?.find((q) => q.status === 'partial' || q.status === 'approved')
    : undefined

  const closeEditor = () => {
    setEditingQuotationId(null)
    void queryClient.invalidateQueries({ queryKey: ['job-quotations', job.jobId] })
  }

  const createMutation = useMutation({
    mutationFn: createQuotation,
    onSuccess: (quotation) => {
      setEditingQuotationId(quotation.id)
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', job.jobId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'สร้างใบเสนอราคาไม่สำเร็จ')
    },
  })

  // [BIZ] จ๊อบมีใบเสนอราคาได้หลายใบ — บิลแยกเฉพาะใบเสนอราคา ใบเสร็จรวม (คำขอผู้ใช้ 2026-10-02)
  // สร้างใบใหม่ได้เมื่อไม่มีใบร่างค้าง (ใบก่อนหน้าส่งลูกค้าแล้ว: ส่งแล้ว/อนุมัติ/ปฏิเสธ) — มิเรอร์
  // QuotationService.CreateAsync (QUOTE_ALREADY_EXISTS) · รายการนี้ไม่รวมใบที่ถูกแทนที่อยู่แล้ว (GetQueueAsync)
  // แก้ราคาใบเดิมยังใช้ "ออกฉบับแก้ไข" (version-first) เหมือนเดิม · ออกใบเสร็จแล้ว server ปฏิเสธ QUOTE_RECEIPT_ISSUED
  const openDraft = query.data?.find((q) => q.status === 'draft')
  const canCreateAdditionalQuotation = !openDraft

  // ---- เพิ่มรายการด้วยเทมเพลต (docs/08-quotation-template.md) — ถ้ามีใบร่างอยู่แล้วใช้ใบนั้น
  // ไม่มีก็สร้างใบใหม่ก่อนแล้วค่อยเพิ่มรายการต่อ (ปุ่มกดได้ทุกเงื่อนไขเดียวกับปุ่ม "+ สร้างใบเสนอราคา") ----
  const draftQuotation = openDraft
  // มีใบร่างก็เพิ่มลงใบนั้น ไม่มีก็สร้างใบใหม่ให้ — กดได้เสมอ
  const canUseTemplateButton = true
  const [showTemplatePicker, setShowTemplatePicker] = useState(false)
  const [templateTargetId, setTemplateTargetId] = useState<string | null>(null)

  const createForTemplateMutation = useMutation({
    mutationFn: createQuotation,
    onSuccess: (quotation) => {
      setTemplateTargetId(quotation.id)
      setShowTemplatePicker(true)
      toast.success(`สร้างใบเสนอราคา ${quotation.code} แล้ว — เลือกเทมเพลตที่ต้องการเพิ่มรายการ`)
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', job.jobId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'สร้างใบเสนอราคาไม่สำเร็จ')
    },
  })

  const applyTemplateFromJobCardMutation = useMutation({
    mutationFn: (input: { templateId: string; source: UpsertLineSource | null }) =>
      applyQuotationTemplate(templateTargetId!, { templateId: input.templateId, source: input.source ?? undefined }),
    onSuccess: (quotation) => {
      toast.success('เพิ่มรายการจากเทมเพลตแล้ว')
      setShowTemplatePicker(false)
      setEditingQuotationId(quotation.id)
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', job.jobId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'เพิ่มรายการจากเทมเพลตไม่สำเร็จ')
      // สร้างใบร่างไว้แล้ว (ถ้าเป็นเส้นทางสร้างใหม่) แต่ apply ล้มเหลว — ไม่ปล่อยให้หายเงียบๆ บอกให้เปิดใบนั้นต่อเอง
      if (!draftQuotation && templateTargetId) {
        toast.message('สร้างใบเสนอราคาให้แล้วแต่ยังไม่ได้เพิ่มรายการ — เปิดใบนั้นจากรายการด้านล่างเพื่อเพิ่มเอง')
      }
    },
  })

  const openTemplatePicker = () => {
    if (draftQuotation) {
      setTemplateTargetId(draftQuotation.id)
      setShowTemplatePicker(true)
    } else {
      createForTemplateMutation.mutate({ jobId: job.jobId })
    }
  }

  // [BIZ] Approved→InProgress คำนวณ guard จริงได้ (JobService.ComputeGuardAsync: isComputable=true —
  // เช็คว่ามีบรรทัดที่ลูกค้าอนุมัติแล้วจริง) จึงไม่ต้องส่ง reason และไม่ใช่ manual override เหมือน transition อื่น
  // ปุ่มนี้แค่ส่งงานต่อไปที่ขั้น "เบิกสินค้า/ดำเนินการซ่อม" (stage ถัดไป) เท่านั้น — ไม่ข้ามไปเสร็จงานทั้งหมด
  // (เดิมปุ่มนี้ไล่ transition ยาวไปจน completed ทีเดียว ทำให้ข้ามขั้นเบิกสินค้าที่เพิ่งมีระบบจริงรองรับ)
  const approveRepairMutation = useMutation({
    mutationFn: () => transitionJob(job.jobId, { toStatus: 'inprogress' }),
    onSuccess: () => {
      toast.success('อนุมัติซ่อมแล้ว — ส่งต่อไปขั้นเบิกสินค้า/ดำเนินการซ่อม')
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'เปลี่ยนสถานะไม่สำเร็จ')
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
    },
  })

  return (
    <Card>
      <CardHeader className="job-card-panel-header">
        <CardTitle>ใบเสนอราคา &amp; รายการซ่อม</CardTitle>
        <div className="job-card-panel-header__actions">
          <Button
            size="sm"
            variant="outline"
            onClick={openTemplatePicker}
            disabled={!canUseTemplateButton || createForTemplateMutation.isPending}
            title={draftQuotation ? `เพิ่มลงใบร่าง ${draftQuotation.code}` : 'สร้างใบเสนอราคาใหม่แล้วเพิ่มรายการจากเทมเพลต'}
          >
            {createForTemplateMutation.isPending ? 'กำลังสร้างใบร่าง…' : 'เพิ่มรายการด้วยเทมเพลต'}
          </Button>
          <Button
            size="sm"
            onClick={() => createMutation.mutate({ jobId: job.jobId })}
            disabled={createMutation.isPending || !canCreateAdditionalQuotation}
          >
            {createMutation.isPending
              ? 'กำลังสร้าง…'
              : query.data?.length ? '+ สร้างใบเสนอราคาใหม่ (บิลแยก)' : '+ สร้างใบเสนอราคา'}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        {openDraft ? (
          <p className="form-message">
            มีใบร่าง {openDraft.code} อยู่ — ส่งใบนั้นให้ลูกค้าก่อนจึงจะสร้างใบใหม่ได้ (ถ้าต้องแก้ราคาใบที่ส่งไปแล้ว
            ให้เปิดใบนั้นแล้วกด "ออกฉบับแก้ไข")
          </p>
        ) : (query.data?.length ?? 0) > 1 ? (
          <p className="form-message">
            งานนี้มีใบเสนอราคา {query.data!.length} ใบ — ลูกค้าอนุมัติแยกใบ แต่เก็บเงินและออกใบเสร็จรวมใบเดียว
          </p>
        ) : null}
        {query.isPending ? (
          <p className="form-message">กำลังโหลดใบเสนอราคา…</p>
        ) : query.isError ? (
          <div className="form-error-panel" role="alert">
            <p>{isApiError(query.error) ? query.error.messageTh : 'โหลดใบเสนอราคาไม่สำเร็จ'}</p>
            <Button variant="outline" size="sm" onClick={() => void query.refetch()}>ลองใหม่</Button>
          </div>
        ) : !query.data.length ? (
          <div className="job-detail-empty">
            <p>งานนี้ยังไม่มีใบเสนอราคา</p>
          </div>
        ) : (
          <ul className="job-detail-quotation-list">
            {query.data.map((q) => (
              <li key={q.id}>
                <div className="job-detail-quotation-list__main">
                  <strong>{q.code}</strong>
                  <StatusChip status={q.status} label={q.statusLabelTh} />
                </div>
                <div className="job-detail-quotation-list__meta">
                  <span>สร้างเมื่อ {formatDateTime(q.createdAt)}</span>
                  <Money value={q.total} />
                </div>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setEditingQuotationId(q.id)}
                >
                  จัดการ
                </Button>
              </li>
            ))}
          </ul>
        )}

        {awaitingCustomerQuotations.length ? (
          <p className="form-message">
            {awaitingCustomerQuotations.map((q) => q.code).join(', ')} รอลูกค้าอนุมัติบนแอปมือถือ — กด "จัดการ"
            ที่ใบนั้นเพื่อดำเนินการแทนลูกค้า
          </p>
        ) : stuckSignedQuotation ? (
          <p className="form-message">
            ลูกค้ายืนยัน {stuckSignedQuotation.code} แล้ว แต่จ๊อบยังไม่เปลี่ยนสถานะ — กด "จัดการ" ที่ใบนั้นเพื่อดำเนินการต่อ
          </p>
        ) : null}

        {job.status === 'approved' ? (
          <div className="job-card-panel-actions">
            <Button onClick={() => approveRepairMutation.mutate()} disabled={approveRepairMutation.isPending}>
              {approveRepairMutation.isPending ? 'กำลังเปลี่ยนสถานะ…' : 'อนุมัติซ่อม → เบิกสินค้า'}
            </Button>
          </div>
        ) : null}
      </CardContent>

      <QuotationEditorModal
        quotationId={editingQuotationId}
        onClose={closeEditor}
        onOpenDocument={setViewingDocumentId}
        onRevised={setEditingQuotationId}
      />
      <QuotationDocumentModal
        quotationId={viewingDocumentId}
        onClose={() => setViewingDocumentId(null)}
      />
      <QuotationTemplatePickerModal
        open={showTemplatePicker}
        onClose={() => { setShowTemplatePicker(false); applyTemplateFromJobCardMutation.reset() }}
        applying={applyTemplateFromJobCardMutation.isPending}
        applyError={applyTemplateFromJobCardMutation.error}
        onApply={(input) => applyTemplateFromJobCardMutation.mutate(input)}
      />
    </Card>
  )
}

const REPAIR_ACTIVE_STATUSES: JobStatusToken[] = ['inprogress', 'waitparts', 'qc', 'ready']

function RepairStage({ job }: { job: Job }) {
  const queryClient = useQueryClient()
  const { session } = useSession()
  const role = session?.user.role.toLowerCase() ?? ''
  const canWithdraw = ['office', 'manager'].includes(role)
  const [creating, setCreating] = useState(false)
  const [printingOperationId, setPrintingOperationId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['job-withdrawals', job.jobId],
    queryFn: () => getJobWithdrawals(job.jobId),
  })

  const canCreateWithdrawal = REPAIR_ACTIVE_STATUSES.includes(job.status)
  const started = canCreateWithdrawal || job.status === 'completed'
  // ปุ่ม "ยืนยันซ่อมเสร็จ" ส่งต่อไปขั้น QC เท่านั้น — เคยไล่ยาวไปจน completed ทีเดียว (ข้าม QC/ชำระเงินไปเลย)
  // ผู้ใช้ทดสอบแล้วพบว่าไม่ถูกต้อง แก้ให้หยุดที่ QC แล้วให้ QcStage เป็นคนตัดสินใจส่งต่อไปชำระเงินเอง
  const canSendToQc = (['inprogress', 'waitparts'] as JobStatusToken[]).includes(job.status)

  // [ASSUME] ยังไม่มีระบบ QC จริง (เช็คลิสต์/รูปก่อน-หลัง) — ยืนยันเองแทนตาม pattern manual-override เดิม
  // (guard `AllTasksDoneWithPhotos` ของ InProgress→Qc ไม่ใช่ isComputable จึงบังคับส่ง reason เสมอ)
  const REPAIR_TO_QC_REASON = 'ยืนยันด้วยตนเองจากเว็บ — ระบบตรวจสอบคุณภาพ (QC) แบบเช็คลิสต์เต็มรูปแบบยังไม่พร้อมใช้งาน (อยู่ระหว่างพัฒนา)'
  const remainingChainToQc = (status: JobStatusToken): JobStatusToken[] => {
    switch (status) {
      case 'inprogress': return ['qc']
      case 'waitparts': return ['inprogress', 'qc']
      default: return []
    }
  }
  const sendToQcMutation = useMutation({
    mutationFn: async () => {
      for (const toStatus of remainingChainToQc(job.status)) {
        await transitionJob(job.jobId, { toStatus, reason: REPAIR_TO_QC_REASON })
      }
    },
    onSuccess: () => toast.success('ส่งงานไปตรวจสอบคุณภาพ (QC) แล้ว'),
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'เปลี่ยนสถานะไม่สำเร็จ'),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
    },
  })

  return (
    <Card>
      <CardHeader className="job-card-panel-header">
        <CardTitle>เบิกสินค้า / ดำเนินการซ่อม</CardTitle>
        <Button
          size="sm"
          disabled={!canWithdraw || !canCreateWithdrawal}
          title={
            !canCreateWithdrawal
              ? 'ต้องกด "อนุมัติซ่อม" ในขั้นตอนเสนอราคาก่อน จึงจะเบิกสินค้าให้งานนี้ได้'
              : !canWithdraw
                ? 'สำหรับผู้จัดการหรือธุรการเท่านั้น'
                : undefined
          }
          onClick={() => setCreating(true)}
        >
          + สร้างใบเบิกสินค้า
        </Button>
      </CardHeader>
      <CardContent>
        {!started ? (
          <div className="job-detail-empty">
            <p>ยังไม่ถึงขั้นตอนนี้ — ต้องกด "อนุมัติซ่อม" ในขั้นตอนเสนอราคาก่อน</p>
          </div>
        ) : query.isPending ? (
          <p className="form-message">กำลังโหลดใบเบิกสินค้า…</p>
        ) : query.isError ? (
          <div className="form-error-panel" role="alert">
            <p>{isApiError(query.error) ? query.error.messageTh : 'โหลดใบเบิกสินค้าไม่สำเร็จ'}</p>
            <Button variant="outline" size="sm" onClick={() => void query.refetch()}>ลองใหม่</Button>
          </div>
        ) : !query.data.length ? (
          <div className="job-detail-empty">
            <p>งานนี้ยังไม่มีใบเบิกสินค้า</p>
          </div>
        ) : (
          <ul className="job-detail-quotation-list">
            {query.data.map((w: WithdrawalSummary) => (
              <li key={w.operationId}>
                <div className="job-detail-quotation-list__main">
                  <strong>{w.documentNumber}</strong>
                  <span>{w.lineCount} รายการ · {w.totalQuantity} ชิ้น</span>
                </div>
                <div className="job-detail-quotation-list__meta">
                  <span>ผู้เบิก {w.requesterName} · {formatDateTime(w.occurredAt)}</span>
                </div>
                <Button variant="outline" size="sm" onClick={() => setPrintingOperationId(w.operationId)}>
                  <Printer aria-hidden="true" /> พิมพ์
                </Button>
              </li>
            ))}
          </ul>
        )}

        {canSendToQc ? (
          <div className="job-card-panel-actions">
            <Button onClick={() => sendToQcMutation.mutate()} disabled={sendToQcMutation.isPending}>
              {sendToQcMutation.isPending ? 'กำลังเปลี่ยนสถานะ…' : 'ยืนยันซ่อมเสร็จ → ส่งตรวจ QC'}
            </Button>
          </div>
        ) : null}
      </CardContent>

      {creating && (
        <StockWithdrawalModal
          jobId={job.jobId}
          jobNo={job.jobNo}
          onClose={() => setCreating(false)}
          onCreated={(withdrawal) => {
            setCreating(false)
            setPrintingOperationId(withdrawal.operationId)
            void queryClient.invalidateQueries({ queryKey: ['job-withdrawals', job.jobId] })
          }}
        />
      )}
      <StockWithdrawalDocumentModal operationId={printingOperationId} onClose={() => setPrintingOperationId(null)} />
    </Card>
  )
}

const TEST_DRIVE_NOTE_SUGGESTIONS = [
  'ขับทดสอบปกติดี ไม่มีเสียงผิดปกติ',
  'เบรกทำงานปกติ ไม่ดึงซ้าย-ขวา',
  'พวงมาลัยตรง ไม่มีอาการสั่นที่ความเร็วสูง',
  'เกียร์เปลี่ยนเรียบร้อย ไม่มีอาการกระตุก',
  'เครื่องยนต์เดินเรียบ ไม่มีเสียงหรือกลิ่นผิดปกติ',
]

function QcStage({ job }: { job: Job }) {
  const queryClient = useQueryClient()
  const started = job.status === 'qc' || (['ready', 'completed'] as JobStatusToken[]).includes(job.status)
  const canDecide = job.status === 'qc'

  const query = useQuery({
    queryKey: ['qc-checklist', job.jobId],
    queryFn: () => getQcChecklist(job.jobId),
    enabled: started,
  })
  const checklist = query.data

  const [testDriveKm, setTestDriveKm] = useState('')
  const [testDriveNote, setTestDriveNote] = useState('')

  // ซิงก์ค่าในช่องกรอกจากข้อมูลที่โหลดมาเฉพาะตอนเปลี่ยน checklist (เช่นโหลดครั้งแรก) — กันไม่ให้ค่าที่พิมพ์ค้างหาย
  // ทุกครั้งที่ query refetch หลัง mutation อื่นสำเร็จ
  useEffect(() => {
    if (!checklist) return
    setTestDriveKm(checklist.testDriveKm != null ? String(checklist.testDriveKm) : '')
    setTestDriveNote(checklist.testDriveNote ?? '')
  }, [checklist?.id])

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['qc-checklist', job.jobId] })
    void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
    void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
  }

  const toggleItemMutation = useMutation({
    mutationFn: ({ item, pass }: { item: QcChecklistItem; pass: boolean }) =>
      saveQcChecklistItem(job.jobId, item.id, { result: pass ? 'pass' : 'pending', note: null }),
    onSuccess: invalidate,
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกผลตรวจไม่สำเร็จ'),
  })

  const saveTestDriveMutation = useMutation({
    mutationFn: () => saveQcTestDrive(job.jobId, { km: Number(testDriveKm), note: testDriveNote.trim() }),
    onSuccess: () => { toast.success('บันทึกผลทดลองขับแล้ว'); invalidate() },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกผลทดลองขับไม่สำเร็จ'),
  })

  // [BIZ] Qc→Ready คำนวณ guard จริงจากเช็คลิสต์นี้ (JobService.ComputeGuardAsync) — ไม่ต้องส่ง reason อีกต่อไป
  // ไม่มีปุ่ม "ไม่ผ่าน"/ตีกลับ (คำขอผู้ใช้ 2026-09-09) — ถ้ายังไม่ผ่านให้ไปแจ้งช่างแก้นอกระบบแล้วย้อนมาติ๊กผ่านทีหลัง
  const passMutation = useMutation({
    mutationFn: () => transitionJob(job.jobId, { toStatus: 'ready' }),
    onSuccess: () => { toast.success('ผ่าน QC แล้ว — ส่งต่อไปขั้นตอนชำระเงิน/ส่งมอบ'); invalidate() },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'เปลี่ยนสถานะไม่สำเร็จ'),
  })

  const allItemsPassed = Boolean(checklist?.items.length) && checklist!.items.every(i => i.result === 'pass')
  const testDriveRecorded = Boolean(checklist?.testDriveRecordedAt)
  const readyToPass = allItemsPassed && testDriveRecorded

  return (
    <Card>
      <CardHeader className="job-card-panel-header">
        <CardTitle>ตรวจสอบคุณภาพ (QC)</CardTitle>
      </CardHeader>
      <CardContent>
        {!started ? (
          <div className="job-detail-empty">
            <p>ยังไม่ถึงขั้นตอนนี้ — ต้องกด "ยืนยันซ่อมเสร็จ" ในขั้นตอนเบิกสินค้า/ดำเนินการซ่อมก่อน</p>
          </div>
        ) : query.isPending ? (
          <p className="form-message">กำลังโหลดเช็คลิสต์ QC…</p>
        ) : query.isError ? (
          <div className="form-error-panel" role="alert">
            <p>{isApiError(query.error) ? query.error.messageTh : 'โหลดเช็คลิสต์ QC ไม่สำเร็จ'}</p>
            <Button variant="outline" size="sm" onClick={() => void query.refetch()}>ลองใหม่</Button>
          </div>
        ) : !checklist ? (
          <div className="job-detail-empty">
            <p>ไม่พบเช็คลิสต์ QC ของงานนี้</p>
          </div>
        ) : (
          <>
            <p className="form-message">
              ตรวจแต่ละรายการที่ซ่อมจริงในใบเสนอราคาที่อนุมัติแล้ว — ไม่มีปุ่ม "ไม่ผ่าน" ถ้ารายการไหนยังไม่เรียบร้อย
              ให้ไปแจ้งช่างแก้ไขนอกระบบก่อน แล้วย้อนมาติ๊กผ่านทีหลัง
            </p>

            <div className="purchase-table-scroll">
              <table className="master-table purchase-lines">
                <thead><tr><th>รหัส</th><th>รายการ</th><th>ประเภท</th><th>ผลตรวจ</th></tr></thead>
                <tbody>
                  {checklist.items.map(item => (
                    <tr key={item.id}>
                      <td>{item.catalogCode || '—'}</td>
                      <td>{item.name}</td>
                      <td>{item.type === 'labor' ? 'ค่าแรง' : 'อะไหล่'}</td>
                      <td>
                        <Button
                          size="sm"
                          variant={item.result === 'pass' ? 'default' : 'outline'}
                          disabled={checklist.isLocked || toggleItemMutation.isPending}
                          onClick={() => toggleItemMutation.mutate({ item, pass: item.result !== 'pass' })}
                        >
                          {item.result === 'pass' ? <CheckCircle2 aria-hidden="true" /> : <Circle aria-hidden="true" />}
                          {item.result === 'pass' ? 'ผ่านแล้ว' : 'รอตรวจ'}
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <section className="form-grid">
              <Field label="ระยะทางทดลองขับ (กม.) *">
                <Input
                  type="number" min="0" step="0.1" disabled={checklist.isLocked}
                  value={testDriveKm} onChange={e => setTestDriveKm(e.target.value)}
                />
              </Field>
              <Field label="ผลการทดลองขับ *" wide>
                {!checklist.isLocked && (
                  <div className="qc-test-drive-suggestions">
                    <span className="section-help">คลิกเพื่อเพิ่มข้อความ:</span>
                    {TEST_DRIVE_NOTE_SUGGESTIONS.map(text => (
                      <Button
                        key={text}
                        variant="outline"
                        size="sm"
                        onClick={() => setTestDriveNote(old => old.trim() ? `${old.trim()} ${text}` : text)}
                      >
                        {text}
                      </Button>
                    ))}
                  </div>
                )}
                <Textarea
                  maxLength={1000} disabled={checklist.isLocked} placeholder="เช่น ขับทดสอบปกติดี ไม่มีเสียงผิดปกติ"
                  value={testDriveNote} onChange={e => setTestDriveNote(e.target.value)}
                />
              </Field>
            </section>
            {checklist.testDriveRecordedAt && (
              <p className="section-help">
                บันทึกล่าสุดโดย {checklist.testDriveRecordedByUserName} · {formatDateTime(checklist.testDriveRecordedAt)}
              </p>
            )}
            {!checklist.isLocked && (
              <div className="job-card-panel-actions">
                <Button
                  variant="outline"
                  disabled={saveTestDriveMutation.isPending || !testDriveKm || !testDriveNote.trim()}
                  onClick={() => saveTestDriveMutation.mutate()}
                >
                  {saveTestDriveMutation.isPending ? 'กำลังบันทึก…' : 'บันทึกผลทดลองขับ'}
                </Button>
              </div>
            )}
            {saveTestDriveMutation.isError && <InlineError error={saveTestDriveMutation.error} />}

            {canDecide ? (
              <div className="job-card-panel-actions">
                <Button
                  onClick={() => passMutation.mutate()}
                  disabled={passMutation.isPending || !readyToPass}
                  title={readyToPass ? undefined : 'ต้องติ๊กผ่านครบทุกรายการและบันทึกผลทดลองขับก่อน'}
                >
                  {passMutation.isPending ? 'กำลังเปลี่ยนสถานะ…' : 'ผ่าน QC → ส่งไปชำระเงิน/ส่งมอบ'}
                </Button>
              </div>
            ) : (
              <div className="job-detail-empty">
                <p>ผ่าน QC แล้ว — งานอยู่ระหว่างขั้นตอนชำระเงิน/ส่งมอบ</p>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}

const PAYMENT_METHOD_OPTIONS: { value: PaymentMethod; label: string }[] = [
  { value: 'cash', label: 'เงินสด' },
  { value: 'transfer', label: 'โอนเงิน' },
  { value: 'card', label: 'บัตรเครดิต/เดบิต' },
  { value: 'qr', label: 'พร้อมเพย์ (QR)' },
]

const PAYMENT_STARTED_STATUSES: JobStatusToken[] = ['ready', 'completed']

// [ASSUME] MVP บนเว็บ — ชำระเงินบันทึกยอดเดียวต่อครั้ง (ไม่มี split/EDC/QR gateway จริง) และส่งมอบรถยืนยันชั่วคราว
// บนเว็บแทนมือถือที่ยังไม่ได้ออกแบบ (docs/01-workflow.md §3.9/§11) — ตัดขอบเขต reconciliation/ใบกำกับภาษี/
// ลูกหนี้/reprint-void ออกทั้งหมดตามที่ยืนยันไว้แล้ว ดูรายละเอียดในแผนงาน
/// แถวเอกสารในขั้นชำระเงิน — [UI] สถานะสื่อด้วย ไอคอน + ข้อความ (ไม่ใช่สีอย่างเดียว) และบอกเหตุผลเมื่อยังทำไม่ได้เสมอ
function BillingDocRow({
  title, status, done, children,
}: { title: string; status: string; done: boolean; children: ReactNode }) {
  const Icon = done ? CheckCircle2 : Circle
  return (
    <li className={`billing-doc-row${done ? ' billing-doc-row--done' : ''}`}>
      <Icon className="billing-doc-row__icon" aria-hidden="true" />
      <div className="billing-doc-row__text">
        <strong>{title}</strong>
        <span>{status}</span>
      </div>
      <div className="billing-doc-row__actions">{children}</div>
    </li>
  )
}

function PaymentStage({ job }: { job: Job }) {
  const queryClient = useQueryClient()
  const { session } = useSession()
  const role = session?.user.role.toLowerCase() ?? ''
  const canUsePos = ['cashier', 'office', 'manager'].includes(role)
  const started = PAYMENT_STARTED_STATUSES.includes(job.status)

  const summaryQuery = useQuery({
    queryKey: ['payment-summary', job.jobId],
    queryFn: () => getPaymentSummary(job.jobId),
    enabled: started,
  })
  const handoverQuery = useQuery({
    queryKey: ['handover', job.jobId],
    queryFn: () => getHandover(job.jobId),
    enabled: started,
  })
  const summary = summaryQuery.data
  const handover = handoverQuery.data

  const invalidate = () => {
    void queryClient.invalidateQueries({ queryKey: ['payment-summary', job.jobId] })
    void queryClient.invalidateQueries({ queryKey: ['handover', job.jobId] })
    void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
    void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
  }

  // ---- ชำระเงิน ----
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  const [documentKind, setDocumentKind] = useState<BillingKind | null>(null)
  const [taxInvoiceFormOpen, setTaxInvoiceFormOpen] = useState(false)

  useEffect(() => {
    if (summary && !amount) setAmount(summary.remainingAmount > 0 ? String(summary.remainingAmount) : '')
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [summary?.remainingAmount])

  const paymentKey = `payment:${session?.user.shardKey}:${session?.branchId}:${job.jobId}`
  const pendingPayment = pendingPaymentCommand(paymentKey)

  const recordMutation = useMutation({
    mutationFn: (input: { method: PaymentMethod; amount: number; reference: string | null; requestId: string }) =>
      paymentCommand(paymentKey, input, (i) => recordPayment(job.jobId, i)),
    onSuccess: () => {
      toast.success('บันทึกการชำระเงินแล้ว')
      setAmount('')
      setReference('')
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกการชำระเงินไม่สำเร็จ'),
    onSettled: invalidate,
  })

  const [removingPaymentId, setRemovingPaymentId] = useState<string | null>(null)
  const [removeReason, setRemoveReason] = useState('')

  const removeMutation = useMutation({
    mutationFn: ({ paymentId, reason }: { paymentId: string; reason: string }) =>
      removePayment(job.jobId, paymentId, reason),
    onSuccess: () => {
      toast.success('ลบรายการชำระเงินแล้ว')
      setRemovingPaymentId(null)
      setRemoveReason('')
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'ลบรายการไม่สำเร็จ'),
    onSettled: invalidate,
  })

  const issueMutation = useMutation({
    mutationFn: () => issueReceipt(job.jobId),
    onSuccess: () => {
      toast.success('ออกใบเสร็จแล้ว')
      setDocumentKind('receipt')
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'ออกใบเสร็จไม่สำเร็จ'),
    onSettled: invalidate,
  })

  // [BIZ] ไม่ติ๊ก VAT = ลดยอดที่ต้องชำระจริง (ไม่ใช่แค่ปรับการแสดงผล) ล็อกแก้ไม่ได้ทันทีที่เริ่มบันทึกชำระเงิน/
  // ออกใบเสร็จแล้ว (summary.vatLocked, ตรวจซ้ำฝั่ง server เสมอ)
  const vatMutation = useMutation({
    mutationFn: (included: boolean) => setVatIncluded(job.jobId, included),
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'เปลี่ยนการคิด VAT ไม่สำเร็จ'),
    onSettled: invalidate,
  })

  const submitPayment = () => {
    if (pendingPayment) {
      recordMutation.mutate(pendingPayment)
      return
    }
    const amt = Number(amount)
    if (!amt || amt <= 0) {
      toast.error('กรุณาระบุจำนวนเงินให้ถูกต้อง')
      return
    }
    recordMutation.mutate({
      method, amount: amt, reference: reference.trim() || null, requestId: crypto.randomUUID(),
    })
  }

  // ---- ส่งมอบรถ ----
  const [editingNoteItemId, setEditingNoteItemId] = useState<string | null>(null)
  const [noteDraft, setNoteDraft] = useState('')
  const signatureHandleRef = useRef<SignaturePadHandle | null>(null)
  const [hasSignature, setHasSignature] = useState(false)
  const [handoverDocOpen, setHandoverDocOpen] = useState(false)

  const saveHandoverItemMutation = useMutation({
    mutationFn: ({ item, isReturned, note }: { item: HandoverItem; isReturned: boolean; note: string | null }) =>
      saveHandoverItem(job.jobId, item.id, { isReturned, note }),
    onSuccess: () => {
      setEditingNoteItemId(null)
      setNoteDraft('')
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกรายการไม่สำเร็จ'),
    onSettled: invalidate,
  })

  const submitHandoverMutation = useMutation({
    mutationFn: async () => {
      const blob = await signatureHandleRef.current?.toBlob()
      if (!blob) throw new Error('กรุณาเซ็นยืนยันการส่งมอบก่อน')
      const file = new File([blob], `handover-${job.jobId}.png`, { type: 'image/png' })
      const attachment = await uploadAttachment({ jobId: job.jobId, kind: 'handover-signature', file })
      return submitHandover(job.jobId, attachment.relativePath)
    },
    onSuccess: () => toast.success('ยืนยันส่งมอบรถแล้ว'),
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'ยืนยันส่งมอบไม่สำเร็จ'),
    onSettled: invalidate,
  })

  const allItemsDecided = Boolean(handover?.items.length) && handover!.items.every((i) => i.updatedAt)

  // [BIZ] ใบเสร็จรวมออกได้ครั้งเดียว — มิเรอร์ลำดับการตรวจของ PosService.IssueReceiptAsync (server ตรวจซ้ำเสมอ)
  const awaitingQuotations = summary?.awaitingCustomerQuotationCodes ?? []
  const receiptBlockedReason = awaitingQuotations.length > 0
    ? `ใบเสนอราคา ${awaitingQuotations.join(', ')} ยังรอลูกค้าตัดสินใจ/เซ็นยืนยัน — ให้จบก่อนออกใบเสร็จรวม`
    : !summary?.balanceSettled
      ? 'ยอดคงเหลือยังไม่เป็นศูนย์ — บันทึกชำระเงินให้ครบก่อน'
      : null

  // ใบกำกับภาษีออกได้หลังออกใบเสร็จเท่านั้น — ไม่ถามสถานะก่อนหน้านั้น (คำขอนี้อ่านข้อมูลสาขา/ลูกค้าจากฐานเดิม)
  const taxInvoiceQuery = useQuery({
    queryKey: ['tax-invoice', job.jobId],
    queryFn: () => getTaxInvoiceState(job.jobId),
    enabled: started && canUsePos && Boolean(summary?.receipt),
  })
  const taxInvoice = taxInvoiceQuery.data?.issued ?? null
  const taxInvoiceBlockedReason = !summary?.receipt
    ? 'ออกได้หลังออกใบเสร็จแล้ว'
    : !summary.vatIncluded
      ? 'งานนี้ไม่ได้คิดภาษีมูลค่าเพิ่ม — ออกใบกำกับภาษีไม่ได้'
      : taxInvoiceQuery.isPending
        ? 'กำลังตรวจสอบ…'
        : taxInvoiceQuery.isError
          ? isApiError(taxInvoiceQuery.error) ? taxInvoiceQuery.error.messageTh : 'ตรวจสอบสถานะใบกำกับภาษีไม่สำเร็จ'
          : taxInvoiceQuery.data?.blockedReasonTh || null

  // [BIZ] จ่ายเงิน → ออกใบเสร็จ → ค่อยเซ็นรับรถ (HandoverService.SubmitAsync) — มิเรอร์ลำดับเดียวกับ server
  // ใบเสร็จมาก่อนเพราะเป็นเงื่อนไขที่ต้องรอฝั่งเก็บเงิน ต่างจากอีกสองข้อที่แก้ได้เองตรงหน้าจอนี้
  const handoverBlockedReason = !handover?.receiptIssued
    ? 'ต้องรับชำระเงินให้ครบและออกใบเสร็จก่อนจึงจะยืนยันส่งมอบรถได้'
    : !allItemsDecided
      ? 'ตรวจของในรถให้ครบทุกรายการก่อน'
      : handover && handoverServiceInfoMissing(handover)
        ? 'บันทึกเลขไมล์ตอนส่งมอบและนัดเข้ารับบริการครั้งถัดไปก่อน'
      : !hasSignature
        ? 'กรุณาเซ็นยืนยันการส่งมอบก่อน'
        : null

  // ---- ปิดงาน ----
  // [BIZ] Ready→Completed คำนวณ guard จริงจาก PosService/HandoverService (JobService.ComputeGuardAsync) —
  // ไม่ต้องส่ง reason อีกต่อไป (เหมือน Qc→Ready) มิเรอร์เงื่อนไข 3 ข้อฝั่ง client เพื่ออธิบายเหตุผลปุ่ม disabled เท่านั้น
  // เซิร์ฟเวอร์ยังตรวจซ้ำเสมอ
  const missingReasons: string[] = []
  if (!summary?.balanceSettled) missingReasons.push('ยอดคงเหลือยังไม่เป็นศูนย์')
  if (!summary?.receipt) missingReasons.push('ยังไม่ได้ออกใบเสร็จ')
  if (!handover?.isLocked) missingReasons.push('ยังไม่ได้ยืนยันส่งมอบรถ')
  const readyToComplete = missingReasons.length === 0

  const completeMutation = useMutation({
    mutationFn: () => transitionJob(job.jobId, { toStatus: 'completed' }),
    onSuccess: () => toast.success('ปิดงานเรียบร้อย — เสร็จสมบูรณ์'),
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'ปิดงานไม่สำเร็จ'),
    onSettled: invalidate,
  })

  if (!started) {
    return (
      <NotYetAvailableStage reason='ยังไม่ถึงขั้นตอนนี้ — ต้องผ่าน QC ("ผ่าน QC → ส่งไปชำระเงิน/ส่งมอบ") ก่อน' />
    )
  }

  if (!canUsePos) {
    return <NotYetAvailableStage reason="สำหรับแคชเชียร์ ธุรการ หรือผู้จัดการเท่านั้นที่ใช้ขั้นตอนนี้ได้" />
  }

  return (
    <div className="job-card-stage-stack">
      <div className="job-card-columns">
        <Card>
          <CardHeader><CardTitle>ชำระเงิน</CardTitle></CardHeader>
          <CardContent>
            {summaryQuery.isPending ? (
              <p className="form-message">กำลังโหลดยอดชำระ…</p>
            ) : summaryQuery.isError ? (
              <div className="form-error-panel" role="alert">
                <p>{isApiError(summaryQuery.error) ? summaryQuery.error.messageTh : 'โหลดยอดชำระไม่สำเร็จ'}</p>
                <Button variant="outline" size="sm" onClick={() => void summaryQuery.refetch()}>ลองใหม่</Button>
              </div>
            ) : !summary ? null : (
              <>
                <label
                  className="filter-check"
                  title={summary.vatLocked ? 'เริ่มบันทึกการชำระเงินหรือออกใบเสร็จไปแล้ว — เปลี่ยนการคิด VAT ไม่ได้อีก' : undefined}
                >
                  <input
                    type="checkbox"
                    checked={summary.vatIncluded}
                    disabled={summary.vatLocked || vatMutation.isPending}
                    onChange={(e) => vatMutation.mutate(e.target.checked)}
                  />
                  คิดภาษีมูลค่าเพิ่ม (VAT) — ไม่ติ๊กจะลดยอดที่ต้องชำระจริง
                </label>

                {(summary.quotationCodes?.length ?? 0) > 1 ? (
                  <p className="form-message">
                    ยอดรวมจากใบเสนอราคา {summary.quotationCodes.length} ใบ: {summary.quotationCodes.join(', ')} — ออกใบเสร็จรวมใบเดียว
                  </p>
                ) : null}

                <div className="money-summary">
                  <div className="money-summary__row">
                    <span>ยอดก่อนภาษี</span>
                    <Money value={summary.netAmount} />
                  </div>
                  <div className="money-summary__row">
                    <span>ภาษีมูลค่าเพิ่ม</span>
                    <Money value={summary.vatAmount} />
                  </div>
                  <div className="money-summary__total">
                    <span>ยอดรวมทั้งสิ้น</span>
                    <Money value={summary.grandTotal} />
                  </div>
                  <div className="money-summary__row">
                    <span>ชำระแล้ว</span>
                    <Money value={summary.paidAmount} />
                  </div>
                  <div className="money-summary__row money-summary__grand">
                    <span>คงเหลือ</span>
                    <Money value={summary.remainingAmount} />
                  </div>
                </div>

                {summary.payments.length > 0 ? (
                  <ul className="job-detail-quotation-list">
                    {summary.payments.map((p) => (
                      <li key={p.id}>
                        <div className="job-detail-quotation-list__main">
                          <strong>{PAYMENT_METHOD_OPTIONS.find((m) => m.value === p.method)?.label ?? p.method}</strong>
                          <Money value={p.amount} />
                        </div>
                        <div className="job-detail-quotation-list__meta">
                          <span>{p.receivedByName} · {formatDateTime(p.receivedAt)}{p.reference ? ` · อ้างอิง ${p.reference}` : ''}</span>
                        </div>
                        {!summary.receipt ? (
                          removingPaymentId === p.id ? (
                            <div className="job-card-panel-actions">
                              <Textarea
                                maxLength={500}
                                placeholder="เหตุผลที่ลบรายการชำระเงินนี้"
                                value={removeReason}
                                onChange={(e) => setRemoveReason(e.target.value)}
                              />
                              <Button
                                size="sm"
                                disabled={!removeReason.trim() || removeMutation.isPending}
                                onClick={() => removeMutation.mutate({ paymentId: p.id, reason: removeReason.trim() })}
                              >
                                ยืนยันลบ
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => { setRemovingPaymentId(null); setRemoveReason('') }}
                              >
                                ยกเลิก
                              </Button>
                            </div>
                          ) : (
                            <Button
                              variant="ghost"
                              size="icon"
                              aria-label={`ลบรายการชำระ ${p.amount}`}
                              onClick={() => { setRemovingPaymentId(p.id); setRemoveReason('') }}
                            >
                              <Trash2 />
                            </Button>
                          )
                        ) : null}
                      </li>
                    ))}
                  </ul>
                ) : (
                  <div className="job-detail-empty"><p>ยังไม่มีรายการชำระเงิน</p></div>
                )}

                {!summary.receipt ? (
                  <section className="form-grid">
                    <Field label="ช่องทางชำระเงิน">
                      <Select
                        value={method}
                        disabled={Boolean(pendingPayment)}
                        onChange={(e) => setMethod(e.target.value as PaymentMethod)}
                      >
                        {PAYMENT_METHOD_OPTIONS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                      </Select>
                    </Field>
                    <Field label="จำนวนเงิน">
                      <Input
                        type="number" min="0" step="0.01" disabled={Boolean(pendingPayment)}
                        value={pendingPayment ? String(pendingPayment.amount) : amount}
                        onChange={(e) => setAmount(e.target.value)}
                      />
                    </Field>
                    <Field label="อ้างอิง (ถ้ามี)" wide>
                      <Input
                        disabled={Boolean(pendingPayment)}
                        value={pendingPayment ? pendingPayment.reference ?? '' : reference}
                        onChange={(e) => setReference(e.target.value)}
                      />
                    </Field>
                  </section>
                ) : null}
                {recordMutation.isError && <InlineError error={recordMutation.error} />}

                {!summary.receipt ? (
                  <div className="job-card-panel-actions">
                    <Button onClick={submitPayment} disabled={recordMutation.isPending}>
                      {recordMutation.isPending
                        ? 'กำลังบันทึก…'
                        : pendingPayment ? 'ตรวจสอบ / ส่งคำขอเดิมซ้ำ' : '+ บันทึกการชำระเงิน'}
                    </Button>
                  </div>
                ) : null}
              </>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>เอกสาร &amp; ส่งมอบรถ</CardTitle></CardHeader>
          <CardContent>
            <ul className="billing-doc-list">
              <BillingDocRow
                title="ใบแจ้งยอด"
                done={false}
                status={summary
                  ? summary.balanceSettled ? 'ชำระครบแล้ว — พิมพ์ได้ทุกเมื่อ' : `ยอดคงเหลือ ${formatMoney(summary.remainingAmount)} บาท — พิมพ์ได้ทุกเมื่อ`
                  : 'กำลังโหลดยอดชำระ…'}
              >
                <Button variant="outline" size="sm" disabled={!summary} onClick={() => setDocumentKind('statement')}>
                  <Printer aria-hidden="true" /> พิมพ์ใบแจ้งยอด
                </Button>
              </BillingDocRow>

              <BillingDocRow
                title="ใบเสร็จรับเงิน"
                done={Boolean(summary?.receipt)}
                status={summary?.receipt
                  ? `ออกแล้ว ${summary.receipt.documentNo} · ${formatDateTime(summary.receipt.issuedAt)}`
                  : receiptBlockedReason ?? 'พร้อมออกใบเสร็จ'}
              >
                {summary?.receipt ? (
                  <Button variant="outline" size="sm" onClick={() => setDocumentKind('receipt')}>
                    <Printer aria-hidden="true" /> พิมพ์ใบเสร็จ
                  </Button>
                ) : (
                  <Button
                    size="sm"
                    onClick={() => issueMutation.mutate()}
                    disabled={Boolean(receiptBlockedReason) || issueMutation.isPending}
                  >
                    {issueMutation.isPending ? 'กำลังออกใบเสร็จ…' : 'ออกใบเสร็จ'}
                  </Button>
                )}
              </BillingDocRow>

              <BillingDocRow
                title="ใบกำกับภาษี (เต็มรูป)"
                done={Boolean(taxInvoice)}
                status={taxInvoice
                  ? `ออกแล้ว ${taxInvoice.documentNo} · ในนาม ${taxInvoice.buyer.name}`
                  : taxInvoiceBlockedReason ?? 'พร้อมออกใบกำกับภาษี — ยืนยันข้อมูลผู้ซื้อก่อน'}
              >
                {taxInvoice ? (
                  <Button variant="outline" size="sm" onClick={() => setDocumentKind('taxInvoice')}>
                    <Printer aria-hidden="true" /> พิมพ์ใบกำกับภาษี
                  </Button>
                ) : (
                  <Button
                    size="sm"
                    disabled={Boolean(taxInvoiceBlockedReason)}
                    onClick={() => setTaxInvoiceFormOpen(true)}
                  >
                    ออกใบกำกับภาษี
                  </Button>
                )}
              </BillingDocRow>

              <BillingDocRow
                title="ใบส่งมอบรถ"
                done={Boolean(handover?.isLocked)}
                status={handover?.isLocked ? 'ยืนยันส่งมอบแล้ว' : 'พิมพ์ได้ทุกเมื่อ — มีช่องให้ลูกค้าเซ็นบนกระดาษ'}
              >
                <Button variant="outline" size="sm" onClick={() => setHandoverDocOpen(true)}>
                  <Printer aria-hidden="true" /> พิมพ์ใบส่งมอบรถ
                </Button>
              </BillingDocRow>
            </ul>

            <Separator />

            {handoverQuery.isPending ? (
              <p className="form-message">กำลังโหลดเช็คลิสต์ส่งมอบ…</p>
            ) : handoverQuery.isError ? (
              <div className="form-error-panel" role="alert">
                <p>{isApiError(handoverQuery.error) ? handoverQuery.error.messageTh : 'โหลดเช็คลิสต์ส่งมอบไม่สำเร็จ'}</p>
                <Button variant="outline" size="sm" onClick={() => void handoverQuery.refetch()}>ลองใหม่</Button>
              </div>
            ) : !handover ? null : (
              <>
                <p className="form-message">
                  ตรวจของในรถกับลูกค้าก่อนส่งมอบ — รายการที่สูญหายต้องระบุรายละเอียดเสมอ
                </p>
                <div className="purchase-table-scroll">
                  <table className="master-table purchase-lines">
                    <thead><tr><th>ของในรถ</th><th>สถานะ</th></tr></thead>
                    <tbody>
                      {handover.items.map((item) => (
                        <tr key={item.id}>
                          <td>{item.name}</td>
                          <td>
                            {editingNoteItemId === item.id ? (
                              <div className="job-card-panel-actions">
                                <Textarea
                                  maxLength={500}
                                  placeholder="รายละเอียดของที่สูญหาย"
                                  value={noteDraft}
                                  onChange={(e) => setNoteDraft(e.target.value)}
                                />
                                <Button
                                  size="sm"
                                  disabled={!noteDraft.trim() || saveHandoverItemMutation.isPending}
                                  onClick={() => saveHandoverItemMutation.mutate({ item, isReturned: false, note: noteDraft.trim() })}
                                >
                                  บันทึก
                                </Button>
                                <Button variant="ghost" size="sm" onClick={() => setEditingNoteItemId(null)}>ยกเลิก</Button>
                              </div>
                            ) : (
                              <div className="job-card-panel-actions">
                                <Button
                                  size="sm"
                                  variant={item.updatedAt && item.isReturned ? 'default' : 'outline'}
                                  disabled={handover.isLocked || saveHandoverItemMutation.isPending}
                                  onClick={() => saveHandoverItemMutation.mutate({ item, isReturned: true, note: null })}
                                >
                                  <CheckCircle2 aria-hidden="true" /> คืนแล้ว/ไม่มี
                                </Button>
                                <Button
                                  size="sm"
                                  variant={item.updatedAt && !item.isReturned ? 'default' : 'outline'}
                                  disabled={handover.isLocked}
                                  onClick={() => { setEditingNoteItemId(item.id); setNoteDraft(item.note ?? '') }}
                                >
                                  <Circle aria-hidden="true" /> สูญหาย
                                </Button>
                              </div>
                            )}
                            {item.updatedAt && !item.isReturned && item.note ? (
                              <p className="section-help">เหตุผล: {item.note}</p>
                            ) : null}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>

                <HandoverServiceInfoSection jobId={job.jobId} handover={handover} onSaved={invalidate} />

                {!handover.isLocked ? (
                  <>
                    {/* [UI] ปุ่มที่ปิดใช้งานต้องบอกเหตุผลเสมอ — ไม่พึ่ง title ที่ต้องเอาเมาส์ไปชี้ก่อน */}
                    {handoverBlockedReason ? (
                      <p className="section-help">{handoverBlockedReason}</p>
                    ) : null}
                    <Field label="ลายเซ็นยืนยันส่งมอบ" wide>
                      <SignaturePad handleRef={(h) => { signatureHandleRef.current = h }} onChange={setHasSignature} />
                    </Field>
                    <div className="job-card-panel-actions">
                      <Button
                        variant="outline"
                        onClick={() => { signatureHandleRef.current?.clear(); setHasSignature(false) }}
                        disabled={submitHandoverMutation.isPending}
                      >
                        ล้างลายเซ็น
                      </Button>
                      <Button
                        onClick={() => submitHandoverMutation.mutate()}
                        disabled={!!handoverBlockedReason || submitHandoverMutation.isPending}
                        title={handoverBlockedReason ?? undefined}
                      >
                        {submitHandoverMutation.isPending ? 'กำลังยืนยัน…' : 'ยืนยันส่งมอบรถ'}
                      </Button>
                    </div>
                  </>
                ) : (
                  <p className="section-help">
                    ยืนยันส่งมอบแล้วโดย {handover.submittedByUserName} · {formatDateTime(handover.submittedAt!)}
                  </p>
                )}
              </>
            )}
          </CardContent>
        </Card>
      </div>

      {job.status !== 'completed' ? (
        <Card>
          <CardContent>
            <div className="job-card-panel-actions">
              <Button onClick={() => completeMutation.mutate()} disabled={!readyToComplete || completeMutation.isPending}>
                {completeMutation.isPending ? 'กำลังปิดงาน…' : 'ปิดงาน (เสร็จสมบูรณ์)'}
              </Button>
            </div>
            {!readyToComplete ? (
              <p className="section-help">ยังปิดงานไม่ได้: {missingReasons.join(' · ')}</p>
            ) : null}
          </CardContent>
        </Card>
      ) : (
        <div className="job-detail-empty"><p>งานนี้เสร็จสมบูรณ์แล้ว</p></div>
      )}

      <BillingDocumentModal
        open={documentKind !== null}
        job={job}
        kind={documentKind ?? 'statement'}
        onClose={() => setDocumentKind(null)}
      />
      <TaxInvoiceIssueModal
        open={taxInvoiceFormOpen}
        job={job}
        onClose={() => setTaxInvoiceFormOpen(false)}
        onIssued={() => { setTaxInvoiceFormOpen(false); setDocumentKind('taxInvoice') }}
      />
      <HandoverDocumentModal open={handoverDocOpen} job={job} onClose={() => setHandoverDocOpen(false)} />
    </div>
  )
}
