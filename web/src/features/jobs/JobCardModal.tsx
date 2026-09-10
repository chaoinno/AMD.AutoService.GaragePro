import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Circle, Printer, Trash2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { toast } from 'sonner'
import { getJobAttachments, uploadAttachment } from '../../api/attachments'
import { isApiError } from '../../api/client'
import { updateVehicleImage } from '../../api/customerVehicles'
import { getHandover, saveHandoverItem, submitHandover, type HandoverItem } from '../../api/handover'
import { getJobIntakeChecklist } from '../../api/intake'
import { getJob, transitionJob } from '../../api/jobs'
import { AttachmentImage } from '../../components/AttachmentImage'
import {
  getPaymentSummary,
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
  createQuotation,
  decideQuotationLine,
  getQuotation,
  getQuotations,
  signQuotation,
} from '../../api/quotations'
import type { JobStatusToken, Job, QuotationSummary } from '../../api/types'
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
import { formatDateTime } from '../../lib/format'
import { useSession } from '../../lib/session'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import { StockWithdrawalDocumentModal } from '../purchasing/StockWithdrawalDocumentModal'
import { StockWithdrawalModal } from '../purchasing/StockWithdrawalModal'
import { IntakeChecklistPanel } from './IntakeChecklistPanel'
import { IntakeReceiptModal } from './IntakeReceiptModal'
import { HandoverDocumentModal } from './HandoverDocumentModal'
import { PaymentReceiptModal } from './PaymentReceiptModal'
import { QuotationDocumentModal } from '../quotations/QuotationDocumentModal'
import { QuotationEditorModal } from '../quotations/QuotationEditorModal'

type StageKey = 'intake' | 'inspect' | 'quote' | 'repair' | 'qc' | 'payment'

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

type JobCardModalProps = {
  jobId: string | null
  onClose: () => void
}

export function JobCardModal({ jobId, onClose }: JobCardModalProps) {
  const [viewStage, setViewStage] = useState<number | null>(null)

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

            <StageContent stageKey={STAGES[stageIndex]?.key ?? 'intake'} job={job} checklistDone={checklistDone} />
            </div>
          )}
          <JobChatWidget jobId={jobId} />
        </>
      )}
    </ConfirmModal>
  )
}

function StageContent({
  stageKey, job, checklistDone,
}: { stageKey: StageKey; job: Job; checklistDone: boolean }) {
  switch (stageKey) {
    case 'intake':
      return <IntakeStage job={job} />
    case 'inspect':
      return <InspectStage job={job} />
    case 'quote':
      return <QuoteStage job={job} checklistDone={checklistDone} />
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
                <div><dt>ประเภทงาน</dt><dd>{job.jobTypeName || 'ไม่ระบุ'}</dd></div>
                <div><dt>วันที่สร้างจ๊อบ</dt><dd>{formatDateTime(job.createdAt)}</dd></div>
                <div><dt>วันที่นัดรับรถ</dt><dd>{job.promiseAt ? formatDateTime(job.promiseAt) : 'ไม่ระบุ'}</dd></div>
              </dl>
            </div>
          </CardContent>
        </Card>

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

function QuoteStage({ job, checklistDone }: { job: Job; checklistDone: boolean }) {
  const queryClient = useQueryClient()
  const { session } = useSession()
  const [editingQuotationId, setEditingQuotationId] = useState<string | null>(null)
  const [viewingDocumentId, setViewingDocumentId] = useState<string | null>(null)

  const query = useQuery({
    queryKey: ['job-quotations', job.jobId],
    queryFn: () => getQuotations('', job.jobId),
  })

  // [ASSUME] ยืนยันแทนลูกค้าเองจากเว็บชั่วคราว — หน้าอนุมัติของลูกค้าบนมือถือ (docs/01-workflow.md §3.4:
  // ตัดสินใจรายบรรทัด + เซ็นยืนยัน) ยังไม่มี ตัดปุ่มนี้ออกทันทีที่มือถือทำ flow นี้ได้จริง
  // (mirror ของ JobStateMachine.WaitApprove→Approved [ASSUME] ที่เปิด Office/Manager/Web ชั่วคราวเช่นกัน)
  const CUSTOMER_APPROVAL_REASON =
    'ยืนยันแทนลูกค้าจากเว็บ — หน้าอนุมัติของลูกค้าบนมือถือยังไม่พร้อมใช้งาน (อยู่ระหว่างพัฒนา)'
  // [ASSUME] เช่นเดียวกับด้านบน — ตรวจเช็ค 31 รายการของช่างบนมือถือยังไม่มี ใช้ checklist 20 รายการบนเว็บที่
  // ส่งไปแล้ว (checklistDone) แทนหลักฐานผ่านตรวจ ต้องมี reason เสมอเพราะ guard นี้เป็น manual override ล้วน
  const INSPECTION_BYPASS_REASON =
    'ยืนยันแทนขั้นตรวจสอบจากเว็บ — ใช้ checklist สภาพรถ 20 รายการที่ส่งแล้วแทนตรวจเช็ค 31 รายการของช่างบนมือถือ (อยู่ระหว่างพัฒนา)'
  const role = session?.user.role.toLowerCase() ?? ''
  const canConfirmCustomerApproval = ['office', 'manager'].includes(role)
  const pendingQuotation = query.data?.find((q) => q.status === 'sent' || q.status === 'partial')
  const canBypassInspection = job.status !== 'waitinspect' || checklistDone

  const confirmCustomerApprovalMutation = useMutation({
    mutationFn: async (summary: QuotationSummary) => {
      // job อาจยังค้างที่ waitinspect/waitquote ได้ (ยังไม่เคยผ่าน transition จริงจากช่าง/ตอนส่งใบเสนอราคา
      // ก่อนแก้จุดนี้) — เผื่อไว้เพื่อให้จ๊อบเก่าที่ค้างอยู่กดยืนยันต่อได้โดยไม่ต้องออกใบใหม่หรือย้อนไปแก้ที่ต้นทาง
      if (job.status === 'waitinspect') {
        await transitionJob(job.jobId, { toStatus: 'waitquote', reason: INSPECTION_BYPASS_REASON })
      }
      if (job.status === 'waitinspect' || job.status === 'waitquote') {
        await transitionJob(job.jobId, { toStatus: 'waitapprove' })
      }
      const full = await getQuotation(summary.id)
      const pendingLines = full.lines.filter((l) => l.approvalStatus === 'pending')
      for (const line of pendingLines) {
        await decideQuotationLine(full.id, line.id, { decision: 'Approved', rejectReason: null })
      }
      await signQuotation(full.id, {
        signatureImagePath: 'web-manual-confirmation',
        consentText: 'ยืนยันแทนลูกค้าโดยพนักงานหน้าเว็บ (ชั่วคราว — รอหน้าอนุมัติของลูกค้าบนมือถือ)',
        deviceInfo: `เว็บ · ${session?.user.displayName ?? 'ไม่ระบุผู้ใช้'}`,
        witnessEmployeeId: session?.user.staffId ?? session?.user.userId ?? 0,
        witnessEmployeeName: session?.user.displayName ?? 'ไม่ระบุชื่อ',
      })
      await transitionJob(job.jobId, { toStatus: 'approved', reason: CUSTOMER_APPROVAL_REASON })
    },
    onSuccess: () => {
      toast.success('ยืนยันลูกค้าอนุมัติแล้ว (ชั่วคราวจากเว็บ)')
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'ยืนยันการอนุมัติไม่สำเร็จ')
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
    },
  })

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

  // [BIZ] Quotation เป็น version-first (docs/02-domain-model.md invariant #1) — สร้างใบใหม่ซ้อนใบที่ยังไม่ถูก
  // ปฏิเสธ/แทนที่ไม่ได้ (backend ตอบ QUOTE_ALREADY_EXISTS ถ้าใบล่าสุดไม่ใช่ Rejected/Superseded — ดู
  // QuotationService.CreateAsync) ต้องเทียบกับ "ใบล่าสุด" (version สูงสุด) เท่านั้น ไม่ใช่ทุกใบในประวัติ
  // (เดิมใช้ .every() ทำให้ใบเก่าที่ถูกปฏิเสธไปแล้วก่อนหน้า — ซึ่งยังค้างอยู่ในลิสต์เสมอเพราะไม่เคย superseded —
  // พอมีใบใหม่กว่าที่ยัง draft/sent มาปน จะยัง false ถูกต้องอยู่แล้ว แต่เขียนแบบนี้ไม่ตรงกับ intent ของ backend
  // ตรงๆ และทำให้พังถ้า backend เปลี่ยนไปคืน superseded ในลิสต์นี้ด้วยในอนาคต)
  const latestQuotation = query.data?.reduce<(typeof query.data)[number] | null>(
    (latest, q) => (!latest || q.version > latest.version ? q : latest),
    null,
  )
  const canCreateAdditionalQuotation =
    !latestQuotation || latestQuotation.status === 'rejected' || latestQuotation.status === 'superseded'

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
        <Button
          size="sm"
          onClick={() => createMutation.mutate({ jobId: job.jobId })}
          disabled={createMutation.isPending || !canCreateAdditionalQuotation}
          title={
            canCreateAdditionalQuotation
              ? undefined
              : `ใบเสนอราคาล่าสุด (${latestQuotation?.statusLabelTh ?? ''}) ยังไม่ถูกปฏิเสธหรือถูกแทนที่ — เปิดใบนั้นแล้วกด "ออกฉบับแก้ไข" แทน`
          }
        >
          {createMutation.isPending ? 'กำลังสร้าง…' : '+ สร้างใบเสนอราคา'}
        </Button>
      </CardHeader>
      <CardContent>
        {!canCreateAdditionalQuotation && query.data?.length ? (
          <p className="form-message">
            ใบเสนอราคาล่าสุด ({latestQuotation?.statusLabelTh}) ยังไม่ถูกปฏิเสธหรือถูกแทนที่ — เปิดใบที่มีอยู่แล้วกด
            "ออกฉบับแก้ไข" แทนการสร้างใหม่
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

        {(['waitinspect', 'waitquote', 'waitapprove'] as JobStatusToken[]).includes(job.status) && pendingQuotation ? (
          <div className="job-card-panel-actions">
            <Button
              onClick={() => confirmCustomerApprovalMutation.mutate(pendingQuotation)}
              disabled={
                !canConfirmCustomerApproval || !canBypassInspection || confirmCustomerApprovalMutation.isPending
              }
              title={
                !canConfirmCustomerApproval
                  ? 'สำหรับผู้จัดการหรือธุรการเท่านั้น'
                  : !canBypassInspection
                    ? 'ยังไม่ได้ส่ง checklist สภาพรถขณะรับ — ส่งให้ครบก่อนจึงข้ามขั้นตรวจสอบได้'
                    : 'ชั่วคราว — ยืนยันแทนลูกค้าจากเว็บ (และข้ามขั้นตรวจสอบ/เสนอราคาที่ยังไม่ขยับสถานะให้ถ้าจำเป็น) เนื่องจากบางขั้นยังไม่พร้อมใช้งานจริงบนมือถือ'
              }
            >
              {confirmCustomerApprovalMutation.isPending ? 'กำลังยืนยัน…' : 'ยืนยันลูกค้าอนุมัติ (ชั่วคราว) →'}
            </Button>
          </div>
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
                      <td>{item.catalogCode}</td>
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
  const [receiptOpen, setReceiptOpen] = useState(false)

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
      setReceiptOpen(true)
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
            <div className="job-card-panel-actions">
              {summary?.receipt ? (
                <Button variant="outline" onClick={() => setReceiptOpen(true)}>
                  <Printer aria-hidden="true" /> พิมพ์ใบเสร็จ {summary.receipt.documentNo}
                </Button>
              ) : (
                <Button
                  onClick={() => issueMutation.mutate()}
                  disabled={!summary?.balanceSettled || issueMutation.isPending}
                  title={summary?.balanceSettled ? undefined : 'ยอดคงเหลือยังไม่เป็นศูนย์ — บันทึกชำระเงินให้ครบก่อน'}
                >
                  {issueMutation.isPending ? 'กำลังออกใบเสร็จ…' : 'ออกใบเสร็จ'}
                </Button>
              )}
              <Button variant="outline" onClick={() => setHandoverDocOpen(true)}>
                <Printer aria-hidden="true" /> พิมพ์ใบส่งมอบรถ
              </Button>
            </div>

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
                  ตรวจของในรถกับลูกค้าก่อนส่งมอบ — รายการที่ไม่ได้คืนต้องระบุเหตุผลเสมอ
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
                                  placeholder="เหตุผลที่ไม่ได้คืน"
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
                                  <CheckCircle2 aria-hidden="true" /> คืนแล้ว
                                </Button>
                                <Button
                                  size="sm"
                                  variant={item.updatedAt && !item.isReturned ? 'default' : 'outline'}
                                  disabled={handover.isLocked}
                                  onClick={() => { setEditingNoteItemId(item.id); setNoteDraft(item.note ?? '') }}
                                >
                                  <Circle aria-hidden="true" /> ไม่คืน
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

                {!handover.isLocked ? (
                  <>
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
                        disabled={!allItemsDecided || !hasSignature || submitHandoverMutation.isPending}
                        title={!allItemsDecided ? 'ตรวจของในรถให้ครบทุกรายการก่อน' : !hasSignature ? 'กรุณาเซ็นยืนยันการส่งมอบก่อน' : undefined}
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

      <PaymentReceiptModal open={receiptOpen} job={job} onClose={() => setReceiptOpen(false)} />
      <HandoverDocumentModal open={handoverDocOpen} job={job} onClose={() => setHandoverDocOpen(false)} />
    </div>
  )
}
