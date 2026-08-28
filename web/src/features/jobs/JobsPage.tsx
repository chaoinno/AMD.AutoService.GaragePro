import { zodResolver } from '@hookform/resolvers/zod'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { ClipboardList, Plus, Search } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { createJob, getJobFormOptions, getJobStatusOptions, searchJobs, type JobsCursor } from '../../api/jobs'
import type { JobFormOptions, LegacyJob } from '../../api/types'
import { isApiError } from '../../api/client'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { DataTable } from '../../components/DataTable'
import { StateBlock } from '../../components/StateBlock'
import { VehicleImage } from '../../components/VehicleImage'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { formatDateTime } from '../../lib/format'
import { JobDetailModal } from './JobDetailModal'

const JOB_TYPE_OPTIONS = [
  { value: 9, label: 'รถในอู่' },
  { value: 10, label: 'รถนัดหมาย' },
] as const

const formSchema = z.object({
  carNumberGroup: z.string().trim().min(1, 'กรุณาระบุหมวดทะเบียน').max(20),
  carNumber: z.string().trim().min(1, 'กรุณาระบุเลขทะเบียน').max(20),
  brandId: z.number().int().positive('กรุณาเลือกยี่ห้อรถ'),
  modelId: z.number().int().positive('กรุณาเลือกรุ่นรถ'),
  carNicknameId: z.number().int().positive('กรุณาเลือกโฉมรถ'),
  colorType: z.number().int().min(1, 'กรุณาเลือกชนิดสี').max(4),
  pjTypeId: z.union([z.literal(9), z.literal(10)], { message: 'กรุณาเลือกประเภทงาน' }),
  primaryColorId: z.number().int().positive().optional(),
  senderFirstName: z.string().trim().max(100).optional(),
  senderLastName: z.string().trim().max(100).optional(),
  senderPhoneNumber: z.string().trim().max(50).optional(),
  detail: z.string().trim().max(500, 'รายละเอียดต้องไม่เกิน 500 ตัวอักษร').optional(),
})

type JobFormValues = z.infer<typeof formSchema>

const PAGE_SIZE = 50

const defaultValues: JobFormValues = {
  carNumberGroup: '',
  carNumber: '',
  brandId: 0,
  modelId: 0,
  carNicknameId: 0,
  colorType: 0,
  pjTypeId: 9,
  senderFirstName: '',
  senderLastName: '',
  senderPhoneNumber: '',
  detail: '',
}

export function JobsPage() {
  const [queryText, setQueryText] = useState('')
  const [searchText, setSearchText] = useState('')
  const [typeFilter, setTypeFilter] = useState(0)
  const [statusFilter, setStatusFilter] = useState(0)
  const [createOpen, setCreateOpen] = useState(false)
  const [selectedJobId, setSelectedJobId] = useState<number | null>(null)
  const [autoLoadEnabled, setAutoLoadEnabled] = useState(false)
  const sentinelRef = useRef<HTMLDivElement | null>(null)

  const statusOptionsQuery = useQuery({
    queryKey: ['job-status-options'],
    queryFn: getJobStatusOptions,
    staleTime: 5 * 60 * 1000,
  })

  const jobsQuery = useInfiniteQuery({
    queryKey: ['jobs-table', searchText, typeFilter, statusFilter],
    queryFn: ({ pageParam }) =>
      searchJobs(searchText, PAGE_SIZE, pageParam, typeFilter || undefined, statusFilter || undefined),
    initialPageParam: undefined as JobsCursor | undefined,
    getNextPageParam: (lastPage) => {
      const last = lastPage.at(-1)
      if (!last || lastPage.length < PAGE_SIZE) return undefined
      return { beforeCreatedDate: last.createdDate, beforeJobId: last.jobId }
    },
  })

  const jobs = useMemo(() => jobsQuery.data?.pages.flat() ?? [], [jobsQuery.data])

  const loadMore = () => {
    setAutoLoadEnabled(true)
    void jobsQuery.fetchNextPage()
  }

  // หลังกดโหลดเพิ่มครั้งแรก ให้เลื่อนจอใกล้ท้ายตารางแล้วโหลดหน้าถัดไปให้เองโดยไม่ต้องกดซ้ำ
  useEffect(() => {
    if (!autoLoadEnabled) return
    const node = sentinelRef.current
    if (!node) return

    const observer = new IntersectionObserver((entries) => {
      if (entries[0]?.isIntersecting && jobsQuery.hasNextPage && !jobsQuery.isFetchingNextPage) {
        void jobsQuery.fetchNextPage()
      }
    }, { rootMargin: '240px' })

    observer.observe(node)
    return () => observer.disconnect()
  }, [autoLoadEnabled, jobsQuery.hasNextPage, jobsQuery.isFetchingNextPage, jobsQuery.fetchNextPage])

  const columns = useMemo<ColumnDef<LegacyJob, unknown>[]>(() => [
    {
      id: 'image',
      header: 'รูปรถ',
      size: 116,
      enableSorting: false,
      cell: ({ row }) => <VehicleImage path={row.original.vehicleImagePath} />,
    },
    {
      id: 'vehicle',
      accessorFn: (job) => `${job.vehicleRegistration} ${job.vehicleModel ?? ''}`,
      header: 'ทะเบียน / ยี่ห้อ / รุ่น',
      size: 360,
      cell: ({ row }) => (
        <div className="job-vehicle-cell">
          <strong>{row.original.vehicleRegistration || 'ไม่ระบุทะเบียน'}</strong>
          <span>{row.original.vehicleModel || 'ไม่ระบุรุ่น'}</span>
        </div>
      ),
    },
    {
      id: 'contact',
      accessorFn: (job) => `${job.customerName} ${job.customerPhone ?? ''}`,
      header: 'ข้อมูลผู้ติดต่อ',
      size: 330,
      cell: ({ row }) => (
        <div className="job-contact-cell">
          <strong>{row.original.customerName || 'ไม่ระบุชื่อ'}</strong>
          <span>{row.original.customerPhone || 'ไม่ระบุเบอร์โทร'}</span>
        </div>
      ),
    },
    {
      id: 'type',
      accessorFn: (job) => job.pjTypeName ?? '',
      header: 'ประเภท',
      size: 140,
      cell: ({ row }) => <span className="job-type-chip">{row.original.pjTypeName || 'ไม่ระบุ'}</span>,
    },
    {
      id: 'status',
      accessorFn: (job) => job.legacyStatusName ?? '',
      header: 'สถานะ',
      size: 160,
      cell: ({ row }) => <span className="job-status-chip">{row.original.legacyStatusName || 'ไม่ระบุ'}</span>,
    },
    {
      id: 'createdDate',
      accessorFn: (job) => new Date(job.createdDate).getTime(),
      header: 'วันที่สร้างจ๊อบ',
      size: 220,
      sortDescFirst: true,
      cell: ({ row }) => <time className="job-created-at">{formatDateTime(row.original.createdDate)}</time>,
    },
    {
      id: 'actions',
      header: '',
      size: 110,
      enableSorting: false,
      cell: ({ row }) => (
        <Button variant="outline" size="sm" onClick={() => setSelectedJobId(row.original.jobId)}>
          <ClipboardList aria-hidden="true" /> จัดการ
        </Button>
      ),
    },
  ], [])

  let content
  if (jobsQuery.isPending) {
    content = (
      <StateBlock
        variant="loading"
        title="กำลังโหลดรายการจ๊อบ"
        reason="ระบบกำลังอ่านข้อมูลล่าสุดจาก"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void jobsQuery.refetch()}
      />
    )
  } else if (jobsQuery.isError && !jobs.length) {
    content = (
      <StateBlock
        variant="error"
        title="โหลดรายการจ๊อบไม่สำเร็จ"
        reason={isApiError(jobsQuery.error) ? jobsQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(jobsQuery.error) ? jobsQuery.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void jobsQuery.refetch()}
      />
    )
  } else if (!jobs.length) {
    content = (
      <StateBlock
        variant="empty"
        title="ไม่พบจ๊อบ"
        reason={searchText ? 'ไม่พบรายการที่ตรงกับคำค้นหา' : 'สาขานี้ยังไม่มีรายการจ๊อบ'}
        traceId="คำขอนี้สำเร็จและไม่พบรายการ"
        actionLabel="เปิดจ๊อบ"
        onAction={() => setCreateOpen(true)}
      />
    )
  } else {
    content = (
      <Card className="queue-table-card jobs-table-card">
        <DataTable data={jobs} columns={columns} sortable />
        <footer className="queue-table-card__footer">
          <span>แสดง {jobs.length} รายการ{jobsQuery.hasNextPage ? '' : 'ทั้งหมด'} · กดหัวคอลัมน์เพื่อเรียงข้อมูล</span>
          {jobsQuery.hasNextPage ? (
            <Button
              variant="outline"
              size="sm"
              disabled={jobsQuery.isFetchingNextPage}
              onClick={loadMore}
            >
              {jobsQuery.isFetchingNextPage ? 'กำลังโหลด…' : 'โหลดเพิ่มเติม'}
            </Button>
          ) : null}
        </footer>
        {jobsQuery.hasNextPage ? <div ref={sentinelRef} className="jobs-table-sentinel" aria-hidden="true" /> : null}
        {jobsQuery.isError && jobs.length ? (
          <p className="jobs-table-loadmore-error" role="alert">
            โหลดรายการเพิ่มเติมไม่สำเร็จ — {isApiError(jobsQuery.error) ? jobsQuery.error.messageTh : 'ลองใหม่อีกครั้ง'}
          </p>
        ) : null}
      </Card>
    )
  }

  return (
    <AppShell title="จ๊อบ">
      <section className="page-heading">
        <div>
          <h2>จ๊อบ</h2>
          <p>ดูรายการรถที่เข้ารับบริการและเปิดจ๊อบใหม่</p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus aria-hidden="true" /> เปิดจ๊อบ
        </Button>
      </section>

      <form
        className="jobs-toolbar"
        onSubmit={(event) => {
          event.preventDefault()
          setSearchText(queryText.trim())
          setAutoLoadEnabled(false)
        }}
      >
        <label className="jobs-search">
          <Search aria-hidden="true" />
          <span className="sr-only">ค้นหาจ๊อบ</span>
          <Input
            type="search"
            value={queryText}
            placeholder="ค้นหาเลขจ๊อบ ทะเบียน ชื่อลูกค้า หรือเบอร์โทร"
            onChange={(event) => setQueryText(event.target.value)}
          />
        </label>
        <Button type="submit" variant="outline">ค้นหา</Button>
        {searchText ? (
          <Button
            variant="ghost"
            onClick={() => {
              setQueryText('')
              setSearchText('')
              setAutoLoadEnabled(false)
            }}
          >
            ล้างคำค้น
          </Button>
        ) : null}

        <Label className="field jobs-filter">
          <span className="sr-only">กรองตามประเภท</span>
          <Select
            value={typeFilter}
            onChange={(event) => {
              setTypeFilter(Number(event.target.value))
              setAutoLoadEnabled(false)
            }}
          >
            <option value={0}>ทุกประเภท</option>
            {JOB_TYPE_OPTIONS.map((item) => (
              <option key={item.value} value={item.value}>{item.label}</option>
            ))}
          </Select>
        </Label>

        <Label className="field jobs-filter">
          <span className="sr-only">กรองตามสถานะ</span>
          <Select
            value={statusFilter}
            onChange={(event) => {
              setStatusFilter(Number(event.target.value))
              setAutoLoadEnabled(false)
            }}
          >
            <option value={0}>ทุกสถานะ</option>
            {(statusOptionsQuery.data ?? []).map((item) => (
              <option key={item.id} value={item.id}>{item.name}</option>
            ))}
          </Select>
        </Label>
      </form>

      {content}
      <CreateJobModal open={createOpen} onClose={() => setCreateOpen(false)} />
      <JobDetailModal jobId={selectedJobId} onClose={() => setSelectedJobId(null)} />
    </AppShell>
  )
}

function CreateJobModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()
  const optionsQuery = useQuery({
    queryKey: ['job-form-options'],
    queryFn: getJobFormOptions,
    enabled: open,
    staleTime: 5 * 60 * 1000,
  })
  const {
    register,
    handleSubmit,
    reset,
    setValue,
    watch,
    formState: { errors },
  } = useForm<JobFormValues>({
    resolver: zodResolver(formSchema),
    defaultValues,
  })

  const brandId = watch('brandId')
  const modelId = watch('modelId')
  const models = optionsQuery.data?.models.filter((item) => item.brandId === brandId) ?? []
  const nicknames = optionsQuery.data?.nicknames.filter(
    (item) => item.brandId === brandId && item.modelId === modelId,
  ) ?? []

  const mutation = useMutation({
    mutationFn: createJob,
    onSuccess: (job) => {
      toast.success(`เปิดจ๊อบ ${job.jobNo} เรียบร้อยแล้ว`)
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
      close()
    },
  })

  const close = () => {
    reset(defaultValues)
    mutation.reset()
    onClose()
  }

  const submit = handleSubmit((values) => {
    mutation.mutate({
      ...values,
      primaryColorId: values.primaryColorId || undefined,
      senderFirstName: values.senderFirstName || undefined,
      senderLastName: values.senderLastName || undefined,
      senderPhoneNumber: values.senderPhoneNumber || undefined,
      detail: values.detail || undefined,
    })
  })

  return (
    <ConfirmModal
      open={open}
      title="เปิดจ๊อบ"
      description="สร้างข้อมูลรถ ลูกค้า และจ๊อบตามรูปแบบ ProjectAdd"
      onClose={close}
      size="large"
      footer={
        <>
          <Button variant="ghost" onClick={close}>ยกเลิก</Button>
          <Button
            type="submit"
            form="create-job-form"
            disabled={optionsQuery.isPending || optionsQuery.isError || mutation.isPending}
          >
            {mutation.isPending ? 'กำลังเปิดจ๊อบ…' : 'เปิดจ๊อบ'}
          </Button>
        </>
      }
    >
      {optionsQuery.isPending ? (
        <p className="form-message">กำลังโหลดตัวเลือกสำหรับเปิดจ๊อบ…</p>
      ) : optionsQuery.isError ? (
        <div className="form-error-panel" role="alert">
          <p>{isApiError(optionsQuery.error) ? optionsQuery.error.messageTh : 'โหลดตัวเลือกไม่สำเร็จ'}</p>
          <Button variant="outline" size="sm" onClick={() => void optionsQuery.refetch()}>ลองใหม่</Button>
        </div>
      ) : (
        <JobForm
          options={optionsQuery.data!}
          models={models}
          nicknames={nicknames}
          register={register}
          setValue={setValue}
          errors={errors}
          submit={submit}
          mutationError={mutation.error}
        />
      )}
    </ConfirmModal>
  )
}

type JobFormProps = {
  options: JobFormOptions
  models: JobFormOptions['models']
  nicknames: JobFormOptions['nicknames']
  register: ReturnType<typeof useForm<JobFormValues>>['register']
  setValue: ReturnType<typeof useForm<JobFormValues>>['setValue']
  errors: ReturnType<typeof useForm<JobFormValues>>['formState']['errors']
  submit: () => void
  mutationError: Error | null
}

function JobForm({
  options,
  models,
  nicknames,
  register,
  setValue,
  errors,
  submit,
  mutationError,
}: JobFormProps) {
  return (
    <form id="create-job-form" className="create-form job-create-form" onSubmit={submit}>
      {mutationError ? (
        <div className="form-error-panel" role="alert">
          {isApiError(mutationError) ? mutationError.messageTh : 'เปิดจ๊อบไม่สำเร็จ กรุณาลองใหม่'}
        </div>
      ) : null}

      <div className="form-grid--two job-color-row">
        <Field label="ประเภทงาน *" error={errors.pjTypeId?.message}>
          <Select {...register('pjTypeId', { valueAsNumber: true })}>
            {JOB_TYPE_OPTIONS.map((item) => (
              <option key={item.value} value={item.value}>{item.label}</option>
            ))}
          </Select>
        </Field>
        <div className="job-defaults" aria-label="ค่าเริ่มต้นของจ๊อบ">
          <div><span>สถานะ</span><strong>รอตรวจสอบ</strong></div>
        </div>
      </div>

      <div className="form-grid--two job-color-row">
        <Field label="ชนิดสีรถ *" error={errors.colorType?.message}>
          <Select {...register('colorType', { valueAsNumber: true })}>
            <option value={0}>เลือกชนิดสี</option>
            <option value={1}>สีทั่วไป</option>
            <option value={2}>ทูโทน</option>
            <option value={3}>สีมุก</option>
            <option value={4}>สีแก้ว</option>
          </Select>
        </Field>
      </div>

      <div className="plate-fields">
        <Field label="หมวดทะเบียน *" error={errors.carNumberGroup?.message}>
          <Input placeholder="เช่น 1กก" autoComplete="off" {...register('carNumberGroup')} />
        </Field>
        <span aria-hidden="true">–</span>
        <Field label="เลขทะเบียน *" error={errors.carNumber?.message}>
          <Input placeholder="เช่น 9999" inputMode="numeric" autoComplete="off" {...register('carNumber')} />
        </Field>
      </div>

      <div className="form-grid--two">
        <Field label="ยี่ห้อรถ *" error={errors.brandId?.message}>
          <Select
            {...register('brandId', {
              valueAsNumber: true,
              onChange: () => {
                setValue('modelId', 0)
                setValue('carNicknameId', 0)
              },
            })}
          >
            <option value={0}>เลือกยี่ห้อ</option>
            {options.brands.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        </Field>
        <Field label="รุ่นรถ *" error={errors.modelId?.message}>
          <Select
            disabled={!models.length}
            {...register('modelId', {
              valueAsNumber: true,
              onChange: () => setValue('carNicknameId', 0),
            })}
          >
            <option value={0}>เลือกรุ่น</option>
            {models.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        </Field>
      </div>

      <div className="form-grid--two">
        <Field label="โฉมรถ *" error={errors.carNicknameId?.message}>
          <Select disabled={!nicknames.length} {...register('carNicknameId', { valueAsNumber: true })}>
            <option value={0}>เลือกโฉม</option>
            {nicknames.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        </Field>
        <Field label="สีรถ">
          <Select
            {...register('primaryColorId', {
              setValueAs: (value) => value === '' ? undefined : Number(value),
            })}
          >
            <option value="">ไม่ระบุสี</option>
            {options.primaryColors.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
          </Select>
        </Field>
      </div>

      <div className="form-grid--two">
        <Field label="ชื่อผู้ส่งรถ"><Input {...register('senderFirstName')} /></Field>
        <Field label="นามสกุลผู้ส่งรถ"><Input {...register('senderLastName')} /></Field>
      </div>
      <Field label="เบอร์โทรศัพท์" error={errors.senderPhoneNumber?.message}>
        <Input type="tel" autoComplete="tel" {...register('senderPhoneNumber')} />
      </Field>
      <Field label="รายละเอียดเพิ่มเติม" error={errors.detail?.message}>
        <Textarea rows={3} {...register('detail')} />
      </Field>

    </form>
  )
}

function Field({
  label,
  error,
  children,
}: {
  label: string
  error?: string
  children: ReactNode
}) {
  return (
    <Label className="field">
      <span>{label}</span>
      {children}
      {error ? <span className="field-error">{error}</span> : null}
    </Label>
  )
}
