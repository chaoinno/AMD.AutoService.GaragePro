import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { attachmentFileUrl, getJobAttachments } from '../../api/attachments'
import { isApiError } from '../../api/client'
import { getJob, transitionJob } from '../../api/jobs'
import { createQuotation, getQuotations } from '../../api/quotations'
import type { JobStatusToken, Job } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { JobStatusChip } from '../../components/JobStatusChip'
import { Money } from '../../components/Money'
import { StateBlock } from '../../components/StateBlock'
import { StatusChip } from '../../components/StatusChip'
import { VehicleImage } from '../../components/VehicleImage'
import { Button } from '../../components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card'
import { formatDateTime } from '../../lib/format'
import { IntakeChecklistPanel } from './IntakeChecklistPanel'
import { IntakeReceiptModal } from './IntakeReceiptModal'

type StageKey = 'intake' | 'inspect' | 'quote' | 'repair' | 'qc' | 'payment'

const STAGES: { key: StageKey; label: string }[] = [
  { key: 'intake', label: 'รับรถ' },
  { key: 'inspect', label: 'ตรวจสอบ' },
  { key: 'quote', label: 'เสนอราคา/งานซ่อม' },
  { key: 'repair', label: 'เบิกอะไหล่/ดำเนินการซ่อม' },
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

  const close = () => {
    setViewStage(null)
    onClose()
  }

  const job = jobQuery.data
  const currentStageIndex = job ? STAGE_INDEX_BY_STATUS[job.status] ?? 0 : 0
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
      {jobId === null ? null : jobQuery.isPending ? (
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

          <StageContent stageKey={STAGES[stageIndex].key} job={job} onNavigate={close} />
        </div>
      )}
    </ConfirmModal>
  )
}

function StageContent({
  stageKey, job, onNavigate,
}: { stageKey: StageKey; job: Job; onNavigate: () => void }) {
  switch (stageKey) {
    case 'intake':
      return <IntakeStage job={job} />
    case 'inspect':
      return <InspectStage job={job} />
    case 'quote':
      return <QuoteStage job={job} onNavigate={onNavigate} />
    case 'repair':
      return <NotYetAvailableStage reason="ยังไม่มีระบบเบิกอะไหล่/คลัง — อยู่ระหว่างพัฒนา" />
    case 'qc':
      return <NotYetAvailableStage reason="ยังไม่มีระบบตรวจสอบคุณภาพงานซ่อม (QC) — อยู่ระหว่างพัฒนา" />
    case 'payment':
      return <PaymentStage />
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
  const attachmentsQuery = useQuery({
    queryKey: ['job-attachments', job.jobId],
    queryFn: () => getJobAttachments(job.jobId),
  })

  return (
    <div className="job-card-stage-stack">
      <div className="job-card-columns">
        <Card>
          <CardHeader><CardTitle>ข้อมูลลูกค้า &amp; รถ</CardTitle></CardHeader>
          <CardContent>
            <div className="job-detail-info">
              <VehicleImage path={job.vehicleImagePath} className="job-detail-info__image" />
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
                  <a
                    key={a.id}
                    className="job-detail-photo-grid__item"
                    href={attachmentFileUrl(a.relativePath)}
                    target="_blank"
                    rel="noreferrer"
                  >
                    <img src={attachmentFileUrl(a.relativePath)} alt={a.fileName} loading="lazy" />
                    <span className="job-detail-photo-grid__kind">{a.kind}</span>
                    <span className="job-detail-photo-grid__meta">{a.uploadedByName} · {formatDateTime(a.uploadedAt)}</span>
                  </a>
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

function QuoteStage({ job, onNavigate }: { job: Job; onNavigate: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ['job-quotations', job.jobId],
    queryFn: () => getQuotations('', job.jobId),
  })

  const createMutation = useMutation({
    mutationFn: createQuotation,
    onSuccess: (quotation) => {
      onNavigate()
      navigate(`/quotations/${quotation.id}/edit`)
    },
  })

  const transitionMutation = useMutation({
    mutationFn: () => transitionJob(job.jobId, {
      toStatus: 'inprogress',
      reason: 'ลูกค้าอนุมัติแล้ว เริ่มดำเนินการซ่อม (ยืนยันด้วยตนเอง — ยังไม่มีระบบเบิกอะไหล่รองรับ)',
    }),
    onSuccess: (result) => {
      toast.success(`เปลี่ยนสถานะเป็น ${result.statusLabel} แล้ว`)
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'เปลี่ยนสถานะไม่สำเร็จ')
    },
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>ใบเสนอราคา &amp; รายการซ่อม</CardTitle>
      </CardHeader>
      <CardContent>
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
            <Button onClick={() => createMutation.mutate({ jobId: job.jobId })} disabled={createMutation.isPending}>
              {createMutation.isPending ? 'กำลังสร้าง…' : 'สร้างใบเสนอราคา'}
            </Button>
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
                  onClick={() => { onNavigate(); navigate(`/quotations/${q.id}/edit`) }}
                >
                  เปิดใบเสนอราคา
                </Button>
              </li>
            ))}
          </ul>
        )}

        {job.status === 'approved' ? (
          <div className="job-card-panel-actions">
            <Button onClick={() => transitionMutation.mutate()} disabled={transitionMutation.isPending}>
              {transitionMutation.isPending ? 'กำลังเปลี่ยนสถานะ…' : 'ไปขั้นตอนเบิกอะไหล่/ดำเนินการซ่อม →'}
            </Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}

function PaymentStage() {
  return (
    <Card>
      <CardContent className="job-detail-empty">
        <p>ระบบ POS/ชำระเงินยังอยู่ระหว่างพัฒนา — ยังไม่มีข้อมูลให้แสดงในขั้นตอนนี้</p>
      </CardContent>
    </Card>
  )
}
