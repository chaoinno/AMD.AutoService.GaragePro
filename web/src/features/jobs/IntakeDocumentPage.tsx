import { useQuery } from '@tanstack/react-query'
import { ArrowLeft, ChevronRight, Printer } from 'lucide-react'
import { Link, useParams } from 'react-router'
import { getJobAttachments } from '../../api/attachments'
import { isApiError, isForbiddenError } from '../../api/client'
import { getJob } from '../../api/jobs'
import { getJobIntakeChecklist } from '../../api/intake'
import { AppShell } from '../../components/AppShell'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button, buttonVariants } from '../../components/ui/button'
import { IntakeReceiptDocument } from './IntakeReceiptDocument'

export function IntakeDocumentPage() {
  const { jobId = '' } = useParams()

  const jobQuery = useQuery({
    queryKey: ['job-detail', jobId],
    queryFn: () => getJob(jobId),
    enabled: Boolean(jobId),
  })

  const checklistQuery = useQuery({
    queryKey: ['job-intake-checklist', jobId],
    queryFn: () => getJobIntakeChecklist(jobId),
    enabled: Boolean(jobId),
  })

  const attachmentsQuery = useQuery({
    queryKey: ['job-attachments', jobId, 'intake'],
    queryFn: () => getJobAttachments(jobId, 'intake'),
    enabled: Boolean(jobId),
  })

  if (jobQuery.isPending || checklistQuery.isPending) {
    return (
      <AppShell title="ใบรับรถ" documentMode>
        <StateBlock
          variant="loading"
          title="กำลังจัดเตรียมเอกสาร"
          reason="ระบบกำลังโหลดข้อมูลล่าสุดเพื่อจัดวางเอกสารสำหรับพิมพ์"
          traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
          actionLabel="โหลดใหม่"
          onAction={() => { void jobQuery.refetch(); void checklistQuery.refetch() }}
        >
          <SkeletonRows count={5} />
        </StateBlock>
      </AppShell>
    )
  }

  const failedQuery = jobQuery.isError ? jobQuery : checklistQuery.isError ? checklistQuery : null
  if (failedQuery) {
    const error = failedQuery.error
    const notFound = isApiError(error) && error.status === 404
    const forbidden = isForbiddenError(error)
    return (
      <AppShell title="ใบรับรถ" documentMode>
        <StateBlock
          variant={forbidden ? 'forbidden' : notFound ? 'empty' : 'error'}
          title={forbidden ? 'ไม่มีสิทธิ์ดูเอกสารนี้' : notFound ? 'ไม่พบข้อมูลงานนี้' : 'เปิดเอกสารไม่สำเร็จ'}
          reason={isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
          traceId={isApiError(error) ? error.traceId : undefined}
          actionLabel="ลองใหม่"
          onAction={() => { void jobQuery.refetch(); void checklistQuery.refetch() }}
        />
      </AppShell>
    )
  }

  if (!jobQuery.data || !checklistQuery.data) return null

  const job = jobQuery.data
  const checklist = checklistQuery.data
  const photos = attachmentsQuery.data ?? []

  return (
    <AppShell title="ใบรับรถ" documentMode>
      <div className="document-toolbar print-hidden">
        <div>
          <Link to="/jobs">คิวงาน</Link>
          <ChevronRight aria-hidden="true" />
          <span>{job.jobNo}</span>
        </div>
        <div>
          <Link className={buttonVariants({ variant: 'outline' })} to="/jobs">
            <ArrowLeft aria-hidden="true" /> กลับไปคิวงาน
          </Link>
          <Button onClick={() => window.print()}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
        </div>
      </div>

      <IntakeReceiptDocument job={job} checklist={checklist} photos={photos} />
    </AppShell>
  )
}
