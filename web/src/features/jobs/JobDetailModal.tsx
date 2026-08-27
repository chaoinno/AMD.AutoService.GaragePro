import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { attachmentFileUrl, getJobAttachments } from '../../api/attachments'
import { isApiError } from '../../api/client'
import { getJob } from '../../api/jobs'
import { createQuotation, getQuotations } from '../../api/quotations'
import type { LegacyJob } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { StateBlock } from '../../components/StateBlock'
import { StatusChip } from '../../components/StatusChip'
import { VehicleImage } from '../../components/VehicleImage'
import { Button } from '../../components/ui/button'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../components/ui/tabs'
import { formatDateTime } from '../../lib/format'

type JobDetailModalProps = {
  jobId: number | null
  onClose: () => void
}

export function JobDetailModal({ jobId, onClose }: JobDetailModalProps) {
  const [tab, setTab] = useState('info')

  const jobQuery = useQuery({
    queryKey: ['job-detail', jobId],
    queryFn: () => getJob(jobId!),
    enabled: jobId !== null,
  })

  const close = () => {
    setTab('info')
    onClose()
  }

  return (
    <ConfirmModal
      open={jobId !== null}
      title={jobQuery.data ? `จัดการจ๊อบ ${jobQuery.data.jobNo}` : 'จัดการจ๊อบ'}
      description={jobQuery.data?.vehicleRegistration
        ? `ทะเบียน ${jobQuery.data.vehicleRegistration} · ${jobQuery.data.customerName || 'ไม่ระบุชื่อลูกค้า'}`
        : 'ข้อมูลจ๊อบ งานซ่อม รูปถ่าย และการชำระเงิน'}
      onClose={close}
      size="xlarge"
    >
      {jobId === null ? null : jobQuery.isPending ? (
        <StateBlock
          variant="loading"
          title="กำลังโหลดข้อมูลจ๊อบ"
          reason="ระบบกำลังอ่านข้อมูลล่าสุดจาก PJCarPickUp"
          traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
          actionLabel="โหลดใหม่"
          onAction={() => void jobQuery.refetch()}
        />
      ) : jobQuery.isError || !jobQuery.data ? (
        <StateBlock
          variant="error"
          title="โหลดข้อมูลจ๊อบไม่สำเร็จ"
          reason={isApiError(jobQuery.error) ? jobQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
          traceId={isApiError(jobQuery.error) ? jobQuery.error.traceId : undefined}
          actionLabel="ลองใหม่"
          onAction={() => void jobQuery.refetch()}
        />
      ) : (
        <Tabs value={tab} onValueChange={setTab} aria-label="จัดการจ๊อบ" className="job-detail-tabs">
          <TabsList>
            <TabsTrigger value="info">ข้อมูลจ๊อบ</TabsTrigger>
            <TabsTrigger value="repair">เสนอราคา/งานซ่อม</TabsTrigger>
            <TabsTrigger value="photos">รูปถ่าย</TabsTrigger>
            <TabsTrigger value="payment">ชำระเงิน</TabsTrigger>
          </TabsList>
          <TabsContent value="info"><JobInfoTab job={jobQuery.data} /></TabsContent>
          <TabsContent value="repair"><JobRepairTab job={jobQuery.data} onNavigate={close} /></TabsContent>
          <TabsContent value="photos"><JobPhotosTab jobId={jobQuery.data.jobId} /></TabsContent>
          <TabsContent value="payment"><JobPaymentTab onClose={close} /></TabsContent>
        </Tabs>
      )}
    </ConfirmModal>
  )
}

function JobInfoTab({ job }: { job: LegacyJob }) {
  return (
    <div className="job-detail-info">
      <VehicleImage path={job.vehicleImagePath} className="job-detail-info__image" />
      <dl className="job-detail-info__grid">
        <div><dt>เลขงาน</dt><dd>{job.jobNo}</dd></div>
        <div><dt>ประเภท</dt><dd>{job.pjTypeName || 'ไม่ระบุ'}</dd></div>
        <div><dt>สถานะ (ระบบเดิม)</dt><dd>{job.legacyStatusName || 'ไม่ระบุ'}</dd></div>
        <div><dt>สาขา</dt><dd>{job.branchName}</dd></div>
        <div><dt>ทะเบียนรถ</dt><dd>{job.vehicleRegistration || 'ไม่ระบุทะเบียน'}</dd></div>
        <div><dt>ยี่ห้อ / รุ่น</dt><dd>{job.vehicleModel || 'ไม่ระบุรุ่น'}</dd></div>
        <div><dt>เลขตัวถัง</dt><dd>{job.vehicleVin || 'ไม่ระบุ'}</dd></div>
        <div><dt>ผู้ติดต่อ</dt><dd>{job.customerName || 'ไม่ระบุชื่อ'}</dd></div>
        <div><dt>เบอร์โทร</dt><dd>{job.customerPhone || 'ไม่ระบุเบอร์โทร'}</dd></div>
        <div><dt>วันที่สร้างจ๊อบ</dt><dd>{formatDateTime(job.createdDate)}</dd></div>
        <div><dt>วันที่นัดรับรถ</dt><dd>{job.promiseAt ? formatDateTime(job.promiseAt) : 'ไม่ระบุ'}</dd></div>
      </dl>
    </div>
  )
}

function JobRepairTab({ job, onNavigate }: { job: LegacyJob; onNavigate: () => void }) {
  const navigate = useNavigate()
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

  if (query.isPending) {
    return (
      <StateBlock
        variant="loading"
        title="กำลังโหลดใบเสนอราคา"
        reason="ระบบกำลังอ่านใบเสนอราคาของจ๊อบนี้"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void query.refetch()}
      />
    )
  }

  if (query.isError) {
    return (
      <StateBlock
        variant="error"
        title="โหลดใบเสนอราคาไม่สำเร็จ"
        reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(query.error) ? query.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void query.refetch()}
      />
    )
  }

  if (!query.data.length) {
    return (
      <div className="job-detail-empty">
        <p>งานนี้ยังไม่มีใบเสนอราคา</p>
        {createMutation.error ? (
          <p className="field-error">
            {isApiError(createMutation.error) ? createMutation.error.messageTh : 'สร้างใบเสนอราคาไม่สำเร็จ'}
          </p>
        ) : null}
        <Button
          onClick={() => createMutation.mutate({ jobId: job.jobId })}
          disabled={createMutation.isPending}
        >
          {createMutation.isPending ? 'กำลังสร้าง…' : 'สร้างใบเสนอราคา'}
        </Button>
      </div>
    )
  }

  return (
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
  )
}

function JobPhotosTab({ jobId }: { jobId: number }) {
  const query = useQuery({
    queryKey: ['job-attachments', jobId],
    queryFn: () => getJobAttachments(jobId),
  })

  if (query.isPending) {
    return (
      <StateBlock
        variant="loading"
        title="กำลังโหลดรูปถ่าย"
        reason="ระบบกำลังอ่านไฟล์แนบของงานนี้"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void query.refetch()}
      />
    )
  }

  if (query.isError) {
    return (
      <StateBlock
        variant="error"
        title="โหลดรูปถ่ายไม่สำเร็จ"
        reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(query.error) ? query.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void query.refetch()}
      />
    )
  }

  if (!query.data.length) {
    return (
      <div className="job-detail-empty">
        <p>งานนี้ยังไม่มีรูปถ่ายแนบ — รูปรับรถ ตรวจเช็ค และก่อน/หลังซ่อมจะขึ้นที่นี่เมื่อพนักงานอัปโหลดจากหน้างาน</p>
      </div>
    )
  }

  return (
    <div className="job-detail-photo-grid">
      {query.data.map((a) => (
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
  )
}

function JobPaymentTab({ onClose }: { onClose: () => void }) {
  return (
    <StateBlock
      variant="empty"
      title="ยังไม่รองรับการชำระเงิน"
      reason="ระบบ POS/ชำระเงินยังอยู่ระหว่างพัฒนา (ดูสถานะใน CLAUDE.md) — ยังไม่มีข้อมูลให้แสดงในแท็บนี้"
      traceId="ฟีเจอร์นี้ยังไม่เปิดใช้งาน"
      actionLabel="ปิดหน้าต่าง"
      onAction={onClose}
    />
  )
}
