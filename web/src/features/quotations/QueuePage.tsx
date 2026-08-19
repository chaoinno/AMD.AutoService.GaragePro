import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { CheckCircle2, Circle, CircleAlert, LoaderCircle, Plus, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate, useSearchParams } from 'react-router'
import { z } from 'zod'
import { toast } from 'sonner'
import { isApiError, isForbiddenError } from '../../api/client'
import { searchJobs } from '../../api/jobs'
import { createQuotation, getQuotations, type QuotationFilter } from '../../api/quotations'
import type { LegacyJob, QuotationSummary } from '../../api/types'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { DataTable } from '../../components/DataTable'
import { Money } from '../../components/Money'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { StatusChip } from '../../components/StatusChip'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Tabs, TabsList, TabsTrigger } from '../../components/ui/tabs'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { getPendingAge } from '../../lib/format'

const filters: { label: string; value: QuotationFilter }[] = [
  { label: 'ทั้งหมด', value: '' },
  { label: 'รอเสนอราคา', value: 'todo' },
  { label: 'รออนุมัติ', value: 'wait' },
  { label: 'ขอแก้ไข', value: 'rev' },
  { label: 'อนุมัติแล้ว', value: 'done' },
]

const createSchema = z.object({
  jobId: z.number().int().positive('กรุณาเลือกงานซ่อม'),
  validUntil: z.string().optional(),
  depositAmount: z.number().min(0, 'ยอดมัดจำต้องไม่ติดลบ').optional(),
})

type CreateFormValues = z.infer<typeof createSchema>

function getJobId(job: LegacyJob) {
  return job.jobId
}

function getSummaryTotal(summary: QuotationSummary) {
  return summary.total
}

export function QueuePage() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const rawFilter = searchParams.get('filter') ?? ''
  const filter = filters.some((item) => item.value === rawFilter)
    ? (rawFilter as QuotationFilter)
    : ''
  const [createOpen, setCreateOpen] = useState(false)

  const query = useQuery({
    queryKey: ['quotations', filter],
    queryFn: () => getQuotations(filter),
  })

  const columns = useMemo<ColumnDef<QuotationSummary, unknown>[]>(
    () => [
      {
        id: 'code',
        header: 'เลขที่',
        size: 175,
        cell: ({ row }) => (
          <div className="quotation-code-cell">
            <strong>{row.original.code}</strong>
            <span className="version-chip">v{row.original.version}</span>
          </div>
        ),
      },
      {
        id: 'job',
        header: 'งาน / ทะเบียน',
        size: 240,
        cell: ({ row }) => (
          <div className="two-line-cell">
            <strong>{row.original.jobNo}</strong>
            <span>
              {row.original.vehicleRegistration} · {row.original.vehicleModel || 'ไม่ระบุรุ่น'}
            </span>
          </div>
        ),
      },
      {
        id: 'customer',
        header: 'ลูกค้า',
        size: 220,
        cell: ({ row }) => (
          <div className="two-line-cell">
            <strong>{row.original.customerName}</strong>
            <span>ดูรายละเอียดลูกค้าในใบเสนอราคา</span>
          </div>
        ),
      },
      {
        id: 'status',
        header: 'สถานะ',
        size: 190,
        cell: ({ row }) => (
          <StatusChip status={row.original.status} label={row.original.statusLabelTh} />
        ),
      },
      {
        id: 'total',
        header: () => <span className="table-heading--right">ยอดสุทธิ</span>,
        size: 170,
        cell: ({ row }) => (
          <span className="table-money">
            <Money value={getSummaryTotal(row.original)} />
          </span>
        ),
      },
      {
        id: 'age',
        header: 'ค้างมานาน',
        size: 115,
        cell: ({ row }) => <span className="age-cell">{getPendingAge(row.original)}</span>,
      },
    ],
    [],
  )

  const openCreate = () => setCreateOpen(true)

  let content
  if (query.isPending) {
    content = (
      <StateBlock
        variant="loading"
        title="กำลังโหลดคิวใบเสนอราคา"
        reason="ระบบกำลังดึงรายการล่าสุดจากบริการใบเสนอราคา"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void query.refetch()}
      >
        <SkeletonRows count={5} />
      </StateBlock>
    )
  } else if (query.isError && isForbiddenError(query.error)) {
    content = (
      <StateBlock
        variant="forbidden"
        title="ไม่มีสิทธิ์ดูคิวใบเสนอราคา"
        reason={isApiError(query.error) ? query.error.messageTh : 'บัญชีนี้ไม่มีสิทธิ์เข้าถึงข้อมูล'}
        traceId={isApiError(query.error) ? query.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void query.refetch()}
      />
    )
  } else if (query.isError) {
    content = (
      <StateBlock
        variant="error"
        title="โหลดคิวใบเสนอราคาไม่สำเร็จ"
        reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(query.error) ? query.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void query.refetch()}
      />
    )
  } else if (!query.data?.length) {
    content = (
      <StateBlock
        variant="empty"
        title="ยังไม่มีใบเสนอราคาในคิวนี้"
        reason="ไม่พบรายการที่ตรงกับตัวกรองปัจจุบัน สามารถสร้างใบเสนอราคาจากงานซ่อมได้ทันที"
        traceId="คำขอนี้สำเร็จและไม่พบรายการ"
        actionLabel="สร้างใบเสนอราคา"
        onAction={openCreate}
      />
    )
  } else {
    content = (
      <Card className="queue-table-card">
        <DataTable
          data={query.data}
          columns={columns}
          getRowLabel={(row) => `เปิดใบเสนอราคา ${row.code}`}
          onRowClick={(row) => navigate(`/quotations/${row.id}`)}
        />
        <footer className="queue-table-card__footer">
          แสดง {query.data.length} รายการ
        </footer>
      </Card>
    )
  }

  return (
    <AppShell title="คิวใบเสนอราคา">
      <section className="page-heading">
        <div>
          <p className="eyebrow">พื้นที่จัดทำใบเสนอราคา</p>
          <h2>คิวใบเสนอราคา</h2>
          <p>ติดตามงานที่ต้องจัดทำ ส่งอนุมัติ และออกฉบับแก้ไข</p>
        </div>
        <Button onClick={openCreate}>
          <Plus aria-hidden="true" /> สร้างใบเสนอราคา
        </Button>
      </section>

      <Tabs
        value={filter}
        onValueChange={(value) => setSearchParams(value ? { filter: value } : {})}
        aria-label="กรองสถานะใบเสนอราคา"
      >
        <TabsList className="filter-row">
          {filters.map((item) => (
            <TabsTrigger key={item.value || 'all'} value={item.value}>
              {item.label}
            </TabsTrigger>
          ))}
        </TabsList>
      </Tabs>

      {content}
      <CreateQuotationModal open={createOpen} onClose={() => setCreateOpen(false)} />
    </AppShell>
  )
}

function CreateQuotationModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const navigate = useNavigate()
  const [queryText, setQueryText] = useState('')
  const [selectedJob, setSelectedJob] = useState<LegacyJob | null>(null)
  const {
    register,
    handleSubmit,
    setValue,
    reset,
    formState: { errors },
  } = useForm<CreateFormValues>({
    resolver: zodResolver(createSchema),
  })

  const jobsQuery = useQuery({
    queryKey: ['jobs', queryText],
    queryFn: () => searchJobs(queryText.trim()),
    enabled: open && queryText.trim().length >= 2,
  })

  const createMutation = useMutation({
    mutationFn: createQuotation,
    onSuccess: (quotation) => {
      toast.success('สร้างใบเสนอราคาเรียบร้อยแล้ว')
      onClose()
      navigate(`/quotations/${quotation.id}/edit`)
    },
  })

  const close = () => {
    setQueryText('')
    setSelectedJob(null)
    reset()
    createMutation.reset()
    onClose()
  }

  const selectJob = (job: LegacyJob) => {
    setSelectedJob(job)
    setValue('jobId', getJobId(job), { shouldValidate: true })
  }

  const submit = handleSubmit((values) => {
    createMutation.mutate({
      jobId: values.jobId,
      validUntil: values.validUntil || undefined,
      depositAmount: values.depositAmount,
    })
  })

  return (
    <ConfirmModal
      open={open}
      title="สร้างใบเสนอราคา"
      description="ค้นหาและเลือกงานซ่อมที่ต้องการออกใบเสนอราคา"
      onClose={close}
      size="large"
      footer={
        <>
          <Button variant="ghost" onClick={close}>
            ยกเลิก
          </Button>
          <Button
            type="submit"
            form="create-quotation-form"
            disabled={!selectedJob || createMutation.isPending}
          >
            {createMutation.isPending ? 'กำลังสร้าง…' : 'สร้างใบเสนอราคา'}
          </Button>
        </>
      }
    >
      <form id="create-quotation-form" className="create-form" onSubmit={submit}>
        <Label className="field field--search">
          <span>ค้นหางาน</span>
          <div className="input-with-icon">
            <Search aria-hidden="true" />
            <Input
              type="search"
              value={queryText}
              placeholder="พิมพ์เลขงาน ชื่อลูกค้า หรือทะเบียน (อย่างน้อย 2 ตัว)"
              onChange={(event) => {
                setQueryText(event.target.value)
                setSelectedJob(null)
              }}
              autoComplete="off"
            />
          </div>
        </Label>

        <div className="job-results" aria-live="polite">
          {queryText.trim().length < 2 ? (
            <p className="inline-hint">พิมพ์อย่างน้อย 2 ตัวอักษรเพื่อเริ่มค้นหา</p>
          ) : jobsQuery.isPending ? (
            <div className="compact-loading">
              <LoaderCircle className="spin" aria-hidden="true" />
              <span>
                <strong>กำลังค้นหางานซ่อม</strong>
                <small>ยังไม่มี traceId ระหว่างรอการตอบกลับ</small>
              </span>
              <Button variant="link" onClick={() => setQueryText('')}>ล้างคำค้น</Button>
            </div>
          ) : jobsQuery.isError ? (
            <div className="inline-error">
              <CircleAlert aria-hidden="true" />
              <strong>{isApiError(jobsQuery.error) ? jobsQuery.error.messageTh : 'ค้นหาไม่สำเร็จ'}</strong>
              <small>
                รหัสติดตาม (traceId): {isApiError(jobsQuery.error) ? jobsQuery.error.traceId : 'ไม่พบรหัสติดตาม'}
              </small>
              <Button type="button" variant="link" onClick={() => void jobsQuery.refetch()}>
                ลองใหม่
              </Button>
            </div>
          ) : jobsQuery.data?.length ? (
            jobsQuery.data.map((job) => {
              const selected = selectedJob ? getJobId(selectedJob) === getJobId(job) : false
              return (
                <Button
                  key={getJobId(job)}
                  variant="ghost"
                  className={`job-result ${selected ? 'job-result--selected' : ''}`}
                  type="button"
                  aria-pressed={selected}
                  onClick={() => selectJob(job)}
                >
                  <span className="job-result__check" aria-hidden="true">
                    {selected ? <CheckCircle2 /> : <Circle />}
                  </span>
                  <span>
                    <strong>{job.jobNo}</strong>
                    <small>
                      {job.vehicleRegistration} · {job.vehicleModel || 'ไม่ระบุรุ่น'}
                    </small>
                  </span>
                  <span>
                    <strong>{job.customerName}</strong>
                    <small>{job.customerPhone || 'ไม่ระบุเบอร์โทร'}</small>
                  </span>
                  <span className="job-result__status">
                    {job.legacyStatusName || 'พร้อมออกใบเสนอราคา'}
                  </span>
                </Button>
              )
            })
          ) : (
            <div className="inline-empty">
              <strong>ไม่พบงานซ่อม</strong>
              <span>ตรวจสอบคำค้นแล้วลองใหม่อีกครั้ง</span>
              <small>รหัสติดตาม (traceId): คำขอนี้สำเร็จและไม่พบรายการ</small>
              <Button variant="link" onClick={() => setQueryText('')}>ล้างคำค้น</Button>
            </div>
          )}
        </div>

        <input type="hidden" {...register('jobId', { valueAsNumber: true })} />
        {errors.jobId ? <p className="field-error">{errors.jobId.message}</p> : null}

        <div className="form-grid form-grid--two">
          <Label className="field">
            <span>ใช้ได้ถึงวันที่ (ไม่บังคับ)</span>
            <Input type="date" {...register('validUntil')} />
          </Label>
          <Label className="field">
            <span>ยอดมัดจำ (บาท)</span>
            <Input
              type="number"
              min="0"
              step="0.01"
              placeholder="0.00"
              {...register('depositAmount', {
                setValueAs: (value) => (value === '' ? undefined : Number(value)),
              })}
            />
            {errors.depositAmount ? (
              <small className="field-error">{errors.depositAmount.message}</small>
            ) : null}
          </Label>
        </div>

        {createMutation.isError ? (
          <Alert variant="destructive" className="form-error">
            <CircleAlert aria-hidden="true" />
            <div>
              <AlertTitle>
                {isApiError(createMutation.error)
                  ? createMutation.error.messageTh
                  : 'สร้างใบเสนอราคาไม่สำเร็จ'}
              </AlertTitle>
              <AlertDescription className="trace-id">
                รหัสติดตาม (traceId):{' '}
                {isApiError(createMutation.error) ? createMutation.error.traceId : 'ไม่พบรหัสติดตาม'}
              </AlertDescription>
            </div>
          </Alert>
        ) : null}
      </form>
    </ConfirmModal>
  )
}
