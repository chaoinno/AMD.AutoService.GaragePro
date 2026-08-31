import { zodResolver } from '@hookform/resolvers/zod'
import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { ClipboardList, Plus, Search, UserRound } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { createJob, getJobStatusOptions, searchJobs, type JobsCursor } from '../../api/jobs'
import {
  createCustomer,
  createVehicle,
  getCustomer,
  getCustomers,
  getModels,
  getNicknames,
  getProvinces,
  getVehicleReferenceData,
} from '../../api/customerVehicles'
import type { CustomerSummary, CustomerVehicleSummary, Job } from '../../api/types'
import { isApiError } from '../../api/client'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { DataTable } from '../../components/DataTable'
import { JobStatusChip } from '../../components/JobStatusChip'
import { StateBlock } from '../../components/StateBlock'
import { VehicleImage } from '../../components/VehicleImage'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { formatDateTime } from '../../lib/format'
import { JobCardModal } from './JobCardModal'

const JOB_TYPE_OPTIONS = [
  { value: 9, label: 'รถในอู่' },
  { value: 10, label: 'รถนัดหมาย' },
] as const

const PAGE_SIZE = 50

function useDebounced<T>(value: T, delay = 350) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay)
    return () => window.clearTimeout(timer)
  }, [delay, value])
  return debounced
}

export function JobsPage() {
  const [queryText, setQueryText] = useState('')
  const [searchText, setSearchText] = useState('')
  const [typeFilter, setTypeFilter] = useState(0)
  const [statusFilter, setStatusFilter] = useState('')
  const [createOpen, setCreateOpen] = useState(false)
  const [selectedJobId, setSelectedJobId] = useState<string | null>(null)
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
      return { beforeCreatedAt: last.createdAt, beforeJobId: last.jobId }
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

  const columns = useMemo<ColumnDef<Job, unknown>[]>(() => [
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
      accessorFn: (job) => job.jobTypeName ?? '',
      header: 'ประเภท',
      size: 140,
      cell: ({ row }) => <span className="job-type-chip">{row.original.jobTypeName || 'ไม่ระบุ'}</span>,
    },
    {
      id: 'status',
      accessorFn: (job) => job.statusLabel,
      header: 'สถานะ',
      size: 160,
      cell: ({ row }) => <JobStatusChip status={row.original.status} label={row.original.statusLabel} />,
    },
    {
      id: 'createdAt',
      accessorFn: (job) => new Date(job.createdAt).getTime(),
      header: 'วันที่สร้างจ๊อบ',
      size: 220,
      sortDescFirst: true,
      cell: ({ row }) => <time className="job-created-at">{formatDateTime(row.original.createdAt)}</time>,
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
              setStatusFilter(event.target.value)
              setAutoLoadEnabled(false)
            }}
          >
            <option value="">ทุกสถานะ</option>
            {(statusOptionsQuery.data ?? []).map((item) => (
              <option key={item.token} value={item.token}>{item.label}</option>
            ))}
          </Select>
        </Label>
      </form>

      {content}
      <CreateJobModal open={createOpen} onClose={() => setCreateOpen(false)} />
      <JobCardModal jobId={selectedJobId} onClose={() => setSelectedJobId(null)} />
    </AppShell>
  )
}

// ---------- เปิดจ๊อบ: เลือก/สร้างลูกค้า → เลือก/สร้างรถ → รายละเอียดจ๊อบ ----------

const jobDetailsSchema = z.object({
  jobTypeId: z.union([z.literal(9), z.literal(10)], { message: 'กรุณาเลือกประเภทงาน' }),
  senderName: z.string().trim().max(200).optional(),
  senderPhoneNumber: z.string().trim().max(50).optional(),
  detail: z.string().trim().max(500, 'รายละเอียดต้องไม่เกิน 500 ตัวอักษร').optional(),
})
type JobDetailsValues = z.infer<typeof jobDetailsSchema>

const newCustomerSchema = z.object({
  firstName: z.string().trim().min(1, 'กรุณากรอกชื่อ'),
  lastName: z.string().trim().min(1, 'กรุณากรอกนามสกุล'),
  phoneNumber1: z.string().trim().min(1, 'กรุณากรอกเบอร์โทรศัพท์'),
})
type NewCustomerValues = z.infer<typeof newCustomerSchema>

const newVehicleSchema = z.object({
  registration: z.string().trim().min(1, 'กรุณากรอกทะเบียนรถ'),
  provinceId: z.string().min(1, 'กรุณาเลือกจังหวัดจดทะเบียน'),
  brandId: z.string().min(1, 'กรุณาเลือกยี่ห้อ'),
  modelId: z.string().min(1, 'กรุณาเลือกรุ่น'),
  nicknameId: z.string().min(1, 'กรุณาเลือกโฉมรถ'),
  yearId: z.string().min(1, 'กรุณาเลือกปีรถ'),
  primaryColorId: z.string(),
  vin: z.string().trim().refine((v) => !v || /^[A-Za-z0-9]{17}$/.test(v), 'VIN ต้องมี 17 ตัวอักษรหรือตัวเลข'),
})
type NewVehicleValues = z.infer<typeof newVehicleSchema>
const emptyNewVehicle: NewVehicleValues = {
  registration: '', provinceId: '', brandId: '', modelId: '', nicknameId: '', yearId: '', primaryColorId: '', vin: '',
}

function CreateJobModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const queryClient = useQueryClient()

  const [selectedCustomerId, setSelectedCustomerId] = useState<number | null>(null)
  const [customerSearch, setCustomerSearch] = useState('')
  const [creatingCustomer, setCreatingCustomer] = useState(false)
  const debouncedCustomerSearch = useDebounced(customerSearch)

  const [selectedVehicleId, setSelectedVehicleId] = useState<number | null>(null)
  const [creatingVehicle, setCreatingVehicle] = useState(false)

  const customersQuery = useQuery({
    queryKey: ['job-open-customer-search', debouncedCustomerSearch],
    queryFn: () => getCustomers({ keyword: debouncedCustomerSearch, pageSize: 8 }),
    enabled: open && debouncedCustomerSearch.trim().length >= 2,
  })

  const customerDetailQuery = useQuery({
    queryKey: ['job-open-customer-detail', selectedCustomerId],
    queryFn: () => getCustomer(selectedCustomerId!),
    enabled: open && selectedCustomerId !== null,
  })

  const refsQuery = useQuery({
    queryKey: ['vehicle-reference-data'],
    queryFn: getVehicleReferenceData,
    enabled: open && creatingVehicle,
  })

  const close = () => {
    setSelectedCustomerId(null)
    setCustomerSearch('')
    setCreatingCustomer(false)
    setSelectedVehicleId(null)
    setCreatingVehicle(false)
    jobForm.reset(jobDetailsDefaults)
    createJobMutation.reset()
    onClose()
  }

  const createJobMutation = useMutation({
    mutationFn: createJob,
    onSuccess: (job) => {
      toast.success(`เปิดจ๊อบ ${job.jobNo} เรียบร้อยแล้ว`)
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
      close()
    },
  })

  const jobDetailsDefaults: JobDetailsValues = { jobTypeId: 9, senderName: '', senderPhoneNumber: '', detail: '' }
  const jobForm = useForm<JobDetailsValues>({
    resolver: zodResolver(jobDetailsSchema),
    defaultValues: jobDetailsDefaults,
  })

  const chooseCustomer = (customer: CustomerSummary) => {
    setSelectedCustomerId(customer.id)
    setCustomerSearch('')
    setSelectedVehicleId(null)
    setCreatingVehicle(false)
  }

  const chooseVehicle = (vehicle: CustomerVehicleSummary) => {
    setSelectedVehicleId(vehicle.id)
    setCreatingVehicle(false)
  }

  const submitJob = jobForm.handleSubmit((values) => {
    if (!selectedCustomerId || !selectedVehicleId) return
    createJobMutation.mutate({
      customerId: selectedCustomerId,
      vehicleId: selectedVehicleId,
      jobTypeId: values.jobTypeId,
      senderName: values.senderName || undefined,
      senderPhoneNumber: values.senderPhoneNumber || undefined,
      detail: values.detail || undefined,
    })
  })

  const customer = customerDetailQuery.data
  const canSubmit = Boolean(selectedCustomerId && selectedVehicleId)

  return (
    <ConfirmModal
      open={open}
      title="เปิดจ๊อบ"
      description="เลือกลูกค้าและรถที่มีอยู่แล้ว หรือสร้างใหม่ — ข้อมูลจ๊อบจะบันทึกในระบบนี้เท่านั้น"
      onClose={close}
      size="large"
      footer={
        <>
          <Button variant="ghost" onClick={close}>ยกเลิก</Button>
          <Button
            type="submit"
            form="create-job-form"
            disabled={!canSubmit || createJobMutation.isPending}
            title={!canSubmit ? 'กรุณาเลือกลูกค้าและรถก่อน' : undefined}
          >
            {createJobMutation.isPending ? 'กำลังเปิดจ๊อบ…' : 'เปิดจ๊อบ'}
          </Button>
        </>
      }
    >
      <form id="create-job-form" className="create-form job-create-form" onSubmit={submitJob}>
        {createJobMutation.isError ? (
          <div className="form-error-panel" role="alert">
            {isApiError(createJobMutation.error) ? createJobMutation.error.messageTh : 'เปิดจ๊อบไม่สำเร็จ กรุณาลองใหม่'}
          </div>
        ) : null}

        <section>
          <h3>ลูกค้า *</h3>
          {selectedCustomerId ? (
            <div className="selected-owner">
              <UserRound aria-hidden="true" />
              <span>
                <strong>{customer ? `${customer.firstName} ${customer.lastName}` : 'กำลังโหลด…'}</strong>
                <small>{customer?.code} · {customer?.phoneNumber1 || 'ไม่ระบุเบอร์'}</small>
              </span>
              <Button type="button" variant="ghost" onClick={() => { setSelectedCustomerId(null); setSelectedVehicleId(null) }}>
                เปลี่ยน
              </Button>
            </div>
          ) : creatingCustomer ? (
            <NewCustomerForm
              onCancel={() => setCreatingCustomer(false)}
              onCreated={(id) => { setCreatingCustomer(false); setSelectedCustomerId(id) }}
            />
          ) : (
            <>
              <div className="input-with-icon">
                <Search aria-hidden="true" />
                <Input
                  value={customerSearch}
                  onChange={(e) => setCustomerSearch(e.target.value)}
                  placeholder="ค้นหาชื่อ เบอร์โทร หรือเลขบัตร อย่างน้อย 2 ตัว"
                />
              </div>
              {customerSearch.trim().length >= 2 ? (
                <div className="owner-results">
                  {customersQuery.isPending ? (
                    <span>กำลังค้นหา…</span>
                  ) : customersQuery.data?.items.length ? (
                    customersQuery.data.items.map((c) => (
                      <button type="button" key={c.id} onClick={() => chooseCustomer(c)}>
                        <strong>{c.fullName}</strong>
                        <small>{c.code} · {c.phoneNumber1}</small>
                      </button>
                    ))
                  ) : (
                    <span>ไม่พบลูกค้าที่ตรงกับคำค้น</span>
                  )}
                </div>
              ) : null}
              <Button type="button" variant="outline" size="sm" onClick={() => setCreatingCustomer(true)}>
                <Plus aria-hidden="true" /> ลูกค้าใหม่
              </Button>
            </>
          )}
        </section>

        {selectedCustomerId ? (
          <section>
            <h3>รถ *</h3>
            {selectedVehicleId ? (
              <div className="selected-owner">
                <span>
                  <strong>
                    {customer?.vehicles.find((v) => v.id === selectedVehicleId)?.registration ?? 'รถที่เพิ่งสร้าง'}
                  </strong>
                </span>
                <Button type="button" variant="ghost" onClick={() => setSelectedVehicleId(null)}>เปลี่ยน</Button>
              </div>
            ) : creatingVehicle ? (
              <NewVehicleForm
                customerId={selectedCustomerId}
                refs={refsQuery.data}
                onCancel={() => setCreatingVehicle(false)}
                onCreated={(id) => { setCreatingVehicle(false); setSelectedVehicleId(id) }}
              />
            ) : (
              <>
                {customerDetailQuery.isPending ? (
                  <p className="form-message">กำลังโหลดรถของลูกค้า…</p>
                ) : customer?.vehicles.length ? (
                  <div className="owner-results">
                    {customer.vehicles.map((v) => (
                      <button type="button" key={v.id} onClick={() => chooseVehicle(v)}>
                        <strong>{v.registration}</strong>
                        <small>{v.brandName || ''} {v.modelName || ''} · {v.nickname || 'ไม่ระบุโฉม'}</small>
                      </button>
                    ))}
                  </div>
                ) : (
                  <p className="form-message">ลูกค้ายังไม่มีรถในระบบ</p>
                )}
                <Button type="button" variant="outline" size="sm" onClick={() => setCreatingVehicle(true)}>
                  <Plus aria-hidden="true" /> รถคันใหม่
                </Button>
              </>
            )}
          </section>
        ) : null}

        {selectedCustomerId && selectedVehicleId ? (
          <section>
            <h3>รายละเอียดจ๊อบ</h3>
            <div className="form-grid--two job-color-row">
              <Field label="ประเภทงาน *" error={jobForm.formState.errors.jobTypeId?.message}>
                <Select {...jobForm.register('jobTypeId', { valueAsNumber: true })}>
                  {JOB_TYPE_OPTIONS.map((item) => (
                    <option key={item.value} value={item.value}>{item.label}</option>
                  ))}
                </Select>
              </Field>
              <div className="job-defaults" aria-label="ค่าเริ่มต้นของจ๊อบ">
                <div><span>สถานะ</span><strong>รอตรวจสอบ</strong></div>
              </div>
            </div>
            <div className="form-grid--two">
              <Field label="ชื่อผู้ส่งรถ"><Input {...jobForm.register('senderName')} /></Field>
              <Field label="เบอร์โทรผู้ส่งรถ" error={jobForm.formState.errors.senderPhoneNumber?.message}>
                <Input type="tel" autoComplete="tel" {...jobForm.register('senderPhoneNumber')} />
              </Field>
            </div>
            <Field label="รายละเอียดเพิ่มเติม" error={jobForm.formState.errors.detail?.message}>
              <Textarea rows={3} {...jobForm.register('detail')} />
            </Field>
          </section>
        ) : null}
      </form>
    </ConfirmModal>
  )
}

function NewCustomerForm({ onCancel, onCreated }: { onCancel: () => void; onCreated: (id: number) => void }) {
  const { register, handleSubmit, formState: { errors } } = useForm<NewCustomerValues>({
    resolver: zodResolver(newCustomerSchema),
    defaultValues: { firstName: '', lastName: '', phoneNumber1: '' },
  })

  const mutation = useMutation({
    mutationFn: (values: NewCustomerValues) => createCustomer({ ...values, isBlacklist: false }),
    onSuccess: (customer) => onCreated(customer.id),
  })

  const submit = handleSubmit((values) => mutation.mutate(values))

  return (
    <div className="inline-create-form">
      {mutation.isError ? (
        <div className="form-error-panel" role="alert">
          {isApiError(mutation.error) ? mutation.error.messageTh : 'สร้างลูกค้าไม่สำเร็จ'}
        </div>
      ) : null}
      <div className="form-grid--two">
        <Field label="ชื่อ *" error={errors.firstName?.message}><Input {...register('firstName')} /></Field>
        <Field label="นามสกุล *" error={errors.lastName?.message}><Input {...register('lastName')} /></Field>
      </div>
      <Field label="เบอร์โทรศัพท์ *" error={errors.phoneNumber1?.message}>
        <Input type="tel" autoComplete="tel" {...register('phoneNumber1')} />
      </Field>
      <div className="job-card-panel-actions">
        <Button type="button" variant="ghost" onClick={onCancel}>ยกเลิก</Button>
        <Button type="button" disabled={mutation.isPending} onClick={() => void submit()}>
          {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึกลูกค้าใหม่'}
        </Button>
      </div>
    </div>
  )
}

function NewVehicleForm({
  customerId, refs, onCancel, onCreated,
}: {
  customerId: number
  refs: import('../../api/types').VehicleReferenceData | undefined
  onCancel: () => void
  onCreated: (id: number) => void
}) {
  const { register, handleSubmit, watch, setValue, formState: { errors } } = useForm<NewVehicleValues>({
    resolver: zodResolver(newVehicleSchema),
    defaultValues: emptyNewVehicle,
  })
  const brandId = Number(watch('brandId')) || 0
  const modelId = Number(watch('modelId')) || 0
  const modelsQuery = useQuery({ queryKey: ['models', brandId], queryFn: () => getModels(brandId), enabled: brandId > 0 })
  const nicknamesQuery = useQuery({ queryKey: ['nicknames', modelId], queryFn: () => getNicknames(modelId), enabled: modelId > 0 })
  const provincesQuery = useQuery({ queryKey: ['provinces'], queryFn: getProvinces })

  const mutation = useMutation({
    mutationFn: (values: NewVehicleValues) => createVehicle({
      customerId,
      registration: values.registration,
      provinceId: Number(values.provinceId),
      brandId: Number(values.brandId),
      modelId: Number(values.modelId),
      nicknameId: Number(values.nicknameId),
      yearId: Number(values.yearId),
      primaryColorId: Number(values.primaryColorId) || undefined,
      vin: values.vin || undefined,
    }),
    onSuccess: (vehicle) => onCreated(vehicle.id),
  })

  const submit = handleSubmit((values) => mutation.mutate(values))

  return (
    <div className="inline-create-form">
      {mutation.isError ? (
        <div className="form-error-panel" role="alert">
          {isApiError(mutation.error) ? mutation.error.messageTh : 'สร้างรถไม่สำเร็จ'}
        </div>
      ) : null}
      <Field label="ทะเบียน *" error={errors.registration?.message}>
        <Input placeholder="เช่น 1กก-9999" autoComplete="off" {...register('registration')} />
      </Field>
      <div className="form-grid--two">
        <Field label="จังหวัดจดทะเบียน *" error={errors.provinceId?.message}>
          <Select {...register('provinceId')}>
            <option value="">เลือกจังหวัด</option>
            {provincesQuery.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
          </Select>
        </Field>
        <Field label="ยี่ห้อ *" error={errors.brandId?.message}>
          <Select
            {...register('brandId')}
            onChange={(e) => { setValue('brandId', e.target.value); setValue('modelId', ''); setValue('nicknameId', '') }}
          >
            <option value="">เลือกยี่ห้อ</option>
            {refs?.brands.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
          </Select>
        </Field>
      </div>
      <div className="form-grid--two">
        <Field label="รุ่น *" error={errors.modelId?.message}>
          <Select
            {...register('modelId')}
            disabled={!brandId}
            title={!brandId ? 'เลือกยี่ห้อก่อน' : undefined}
            onChange={(e) => { setValue('modelId', e.target.value); setValue('nicknameId', '') }}
          >
            <option value="">เลือกรุ่น</option>
            {modelsQuery.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
          </Select>
        </Field>
        <Field label="โฉม *" error={errors.nicknameId?.message}>
          <Select {...register('nicknameId')} disabled={!modelId} title={!modelId ? 'เลือกรุ่นก่อน' : undefined}>
            <option value="">เลือกโฉม</option>
            {nicknamesQuery.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
          </Select>
        </Field>
      </div>
      <div className="form-grid--two">
        <Field label="ปี *" error={errors.yearId?.message}>
          <Select {...register('yearId')}>
            <option value="">เลือกปี</option>
            {refs?.years.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
          </Select>
        </Field>
        <Field label="สีหลัก">
          <Select {...register('primaryColorId')}>
            <option value="">เลือกสีหลัก</option>
            {refs?.primaryColors.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}
          </Select>
        </Field>
      </div>
      <Field label="VIN" error={errors.vin?.message}>
        <Input maxLength={17} {...register('vin')} />
      </Field>
      <div className="job-card-panel-actions">
        <Button type="button" variant="ghost" onClick={onCancel}>ยกเลิก</Button>
        <Button type="button" disabled={mutation.isPending} onClick={() => void submit()}>
          {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึกรถใหม่'}
        </Button>
      </div>
    </div>
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
