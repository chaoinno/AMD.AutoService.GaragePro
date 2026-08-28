import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { AlertTriangle, Download, Pencil, Plus, Search, ShieldAlert, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import {
  createCustomer,
  deleteCustomer,
  exportCustomers,
  getAmphures,
  getCustomer,
  getCustomers,
  getDistricts,
  getModels,
  getProvinces,
  getVehicleReferenceData,
  getZipCode,
  triggerDownload,
  updateCustomer,
  type CustomerFilters,
} from '../../api/customerVehicles'
import { isApiError, isForbiddenError } from '../../api/client'
import type { CustomerDuplicate, CustomerInput, CustomerSummary } from '../../api/types'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { DataTable } from '../../components/DataTable'
import { Pagination } from '../../components/Pagination'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'

const customerSchema = z.object({
  firstName: z.string().trim().min(1, 'กรุณากรอกชื่อ'),
  lastName: z.string().trim().min(1, 'กรุณากรอกนามสกุล'),
  phoneNumber1: z.string().trim().regex(/^[0-9+()\-\s]{8,20}$/, 'รูปแบบเบอร์โทรไม่ถูกต้อง'),
  phoneNumber2: z.string().trim().refine((v) => !v || /^[0-9+()\-\s]{8,20}$/.test(v), 'รูปแบบเบอร์โทรไม่ถูกต้อง'),
  idCard: z.string().trim().refine((v) => !v || /^\d{13}$/.test(v), 'เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก'),
  driverLicense: z.string().trim(),
  genderId: z.string(),
  dateOfBirth: z.string(),
  address1: z.string().trim(),
  address2: z.string().trim(),
  provinceId: z.string(),
  amphureId: z.string(),
  districtId: z.string(),
  zipCode: z.string().trim(),
  email: z.string().trim().refine((v) => !v || z.email().safeParse(v).success, 'รูปแบบอีเมลไม่ถูกต้อง'),
  lineId: z.string().trim(),
  isBlacklist: z.boolean(),
  blacklistRemark: z.string().trim(),
}).refine((v) => !v.isBlacklist || v.blacklistRemark.length > 0, {
  path: ['blacklistRemark'], message: 'กรุณาระบุเหตุผลที่ติดแบล็กลิสต์',
})

type CustomerFormValues = z.infer<typeof customerSchema>
const emptyForm: CustomerFormValues = {
  firstName: '', lastName: '', phoneNumber1: '', phoneNumber2: '', idCard: '', driverLicense: '',
  genderId: '1', dateOfBirth: '', address1: '', address2: '', provinceId: '', amphureId: '',
  districtId: '', zipCode: '', email: '', lineId: '', isBlacklist: false, blacklistRemark: '',
}

function useDebounced<T>(value: T, delay = 350) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => { const timer = window.setTimeout(() => setDebounced(value), delay); return () => window.clearTimeout(timer) }, [delay, value])
  return debounced
}

export function CustomerPage() {
  const [keyword, setKeyword] = useState('')
  const debouncedKeyword = useDebounced(keyword)
  const [filters, setFilters] = useState<CustomerFilters>({ page: 1, pageSize: 25 })
  const [formId, setFormId] = useState<number | 'new' | null>(null)
  const [deleting, setDeleting] = useState<CustomerSummary | null>(null)
  const queryClient = useQueryClient()
  const effectiveFilters = { ...filters, keyword: debouncedKeyword || undefined }

  const query = useQuery({ queryKey: ['customers', effectiveFilters], queryFn: () => getCustomers(effectiveFilters) })
  const provinces = useQuery({ queryKey: ['provinces'], queryFn: getProvinces })
  const refs = useQuery({ queryKey: ['vehicle-reference-data'], queryFn: getVehicleReferenceData })
  const models = useQuery({
    queryKey: ['models', filters.brandId], queryFn: () => getModels(filters.brandId!), enabled: Boolean(filters.brandId),
  })
  const filterAmphures = useQuery({
    queryKey: ['amphures', filters.provinceId], queryFn: () => getAmphures(filters.provinceId!), enabled: Boolean(filters.provinceId),
  })
  const filterDistricts = useQuery({
    queryKey: ['districts', filters.amphureId], queryFn: () => getDistricts(filters.amphureId!), enabled: Boolean(filters.amphureId),
  })
  const remove = useMutation({
    mutationFn: (id: number) => deleteCustomer(id),
    onSuccess: () => { toast.success('ลบลูกค้าเรียบร้อยแล้ว'); setDeleting(null); void queryClient.invalidateQueries({ queryKey: ['customers'] }) },
  })
  const download = useMutation({
    mutationFn: () => exportCustomers(effectiveFilters),
    onSuccess: (blob) => triggerDownload(blob, `customers-${new Date().toISOString().slice(0, 10)}.csv`),
    onError: (error) => toast.error(isApiError(error)
      ? `${error.messageTh} (traceId: ${error.traceId})`
      : 'ส่งออกข้อมูลลูกค้าไม่สำเร็จ'),
  })

  const columns = useMemo<ColumnDef<CustomerSummary, unknown>[]>(() => [
    { id: 'customer', header: 'ลูกค้า', size: 260, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.fullName}</strong><span>{row.original.code} · {row.original.idCard || 'ไม่ระบุเลขบัตร'}</span></div> },
    { id: 'contact', header: 'การติดต่อ', size: 220, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.phoneNumber1 || 'ไม่ระบุเบอร์'}</strong><span>{row.original.email || row.original.phoneNumber2 || 'ไม่มีข้อมูลเพิ่มเติม'}</span></div> },
    { id: 'province', header: 'จังหวัด', size: 150, cell: ({ row }) => row.original.provinceName || 'ไม่ระบุ' },
    { id: 'vehicles', header: 'รถ', size: 90, cell: ({ row }) => <span className="count-chip">{row.original.vehicleCount} คัน</span> },
    { id: 'status', header: 'สถานะ', size: 145, cell: ({ row }) => row.original.isBlacklist ? <Badge className="blacklist-badge" title={row.original.blacklistRemark || 'ติดแบล็กลิสต์'}><ShieldAlert /> Blacklist</Badge> : row.original.isDeleted ? <Badge variant="outline">ลบแล้ว</Badge> : <Badge className="active-badge">ใช้งาน</Badge> },
    { id: 'actions', header: '', size: 110, cell: ({ row }) => <div className="row-actions"><Button size="icon" variant="ghost" aria-label={`แก้ไข ${row.original.fullName}`} onClick={(e) => { e.stopPropagation(); setFormId(row.original.id) }}><Pencil /></Button><Button size="icon" variant="ghost" disabled={row.original.isDeleted} title={row.original.isDeleted ? 'รายการนี้ถูกลบแล้ว' : 'ลบลูกค้า'} aria-label={`ลบ ${row.original.fullName}`} onClick={(e) => { e.stopPropagation(); setDeleting(row.original) }}><Trash2 /></Button></div> },
  ], [])

  let content
  if (query.isPending) content = <StateBlock variant="loading" title="กำลังโหลดรายชื่อลูกค้า" reason="ระบบกำลังค้นหาข้อมูลในสาขาปัจจุบัน" actionLabel="โหลดใหม่" onAction={() => void query.refetch()}><SkeletonRows /></StateBlock>
  else if (query.isError && isForbiddenError(query.error)) content = <StateBlock variant="forbidden" title="ไม่มีสิทธิ์ดูข้อมูลลูกค้า" reason={isApiError(query.error) ? query.error.messageTh : 'บัญชีนี้ไม่มีสิทธิ์'} traceId={isApiError(query.error) ? query.error.traceId : undefined} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (query.isError) content = <StateBlock variant="error" title="โหลดข้อมูลลูกค้าไม่สำเร็จ" reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'} traceId={isApiError(query.error) ? query.error.traceId : undefined} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (!query.data.items.length) content = <StateBlock variant="empty" title="ไม่พบลูกค้าที่ตรงกับเงื่อนไข" reason="ลองเปลี่ยนคำค้นหาหรือตัวกรอง หรือเพิ่มลูกค้าใหม่ได้ทันที" traceId="คำขอสำเร็จและไม่พบรายการ" actionLabel="เพิ่มลูกค้า" onAction={() => setFormId('new')} />
  else content = <Card className="management-table-card"><DataTable data={query.data.items} columns={columns} onRowClick={(row) => setFormId(row.id)} getRowLabel={(row) => `เปิดข้อมูลลูกค้า ${row.fullName}`} /><Pagination page={query.data.page} totalPages={query.data.totalPages} totalItems={query.data.totalItems} onPageChange={(page) => setFilters((f) => ({ ...f, page }))} /></Card>

  return (
    <AppShell title="ลูกค้า">
      <section className="page-heading"><div><p className="eyebrow">ข้อมูลลูกค้าและรถ</p><h2>จัดการลูกค้า</h2><p>ค้นหา เพิ่ม แก้ไข และดูรถของลูกค้าในสาขาปัจจุบัน</p></div><div className="heading-actions"><Button variant="outline" disabled={download.isPending} onClick={() => download.mutate()}><Download /> {download.isPending ? 'กำลังส่งออก…' : 'ส่งออก CSV'}</Button><Button onClick={() => setFormId('new')}><Plus /> เพิ่มลูกค้า</Button></div></section>
      <Card className="management-filters management-filters--customer">
        <div className="filter-search input-with-icon"><Search /><Input value={keyword} onChange={(e) => { setKeyword(e.target.value); setFilters((f) => ({ ...f, page: 1 })) }} placeholder="ค้นหาชื่อ นามสกุล เบอร์โทร หรือเลขบัตร" /></div>
        <Select value={filters.brandId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, brandId: Number(e.target.value) || undefined, modelId: undefined, page: 1 }))}><option value="">ทุกยี่ห้อ</option>{refs.data?.brands.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
        <Select value={filters.modelId ?? ''} disabled={!filters.brandId} title={!filters.brandId ? 'เลือกยี่ห้อก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, modelId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกรุ่น</option>{models.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
        <Select value={filters.provinceId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, provinceId: Number(e.target.value) || undefined, amphureId: undefined, districtId: undefined, page: 1 }))}><option value="">ทุกจังหวัด</option>{provinces.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
        <Select value={filters.amphureId ?? ''} disabled={!filters.provinceId} title={!filters.provinceId ? 'เลือกจังหวัดก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, amphureId: Number(e.target.value) || undefined, districtId: undefined, page: 1 }))}><option value="">ทุกอำเภอ / เขต</option>{filterAmphures.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
        <Select value={filters.districtId ?? ''} disabled={!filters.amphureId} title={!filters.amphureId ? 'เลือกอำเภอก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, districtId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกตำบล / แขวง</option>{filterDistricts.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
        <label className="filter-check"><input type="checkbox" checked={Boolean(filters.includeDeleted)} onChange={(e) => setFilters((f) => ({ ...f, includeDeleted: e.target.checked, page: 1 }))} /> แสดงรายการที่ถูกลบ</label>
      </Card>
      {content}
      <CustomerFormDialog open={formId !== null} customerId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
      <ConfirmModal open={Boolean(deleting)} title="ยืนยันการลบลูกค้า" description="รายการจะถูกซ่อนแบบ soft-delete และเรียกดูได้ด้วยตัวกรองรายการที่ถูกลบ" onClose={() => { if (!remove.isPending) setDeleting(null) }} size="small" footer={<><Button variant="ghost" onClick={() => setDeleting(null)}>ยกเลิก</Button><Button variant="destructive" disabled={remove.isPending} onClick={() => deleting && remove.mutate(deleting.id)}><Trash2 /> {remove.isPending ? 'กำลังลบ…' : 'ลบลูกค้า'}</Button></>}><p>ต้องการลบ <strong>{deleting?.fullName}</strong> หรือไม่?</p>{remove.isError ? <InlineError error={remove.error} /> : null}</ConfirmModal>
    </AppShell>
  )
}

function CustomerFormDialog({ open, customerId, onClose }: { open: boolean; customerId: number | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const details = useQuery({ queryKey: ['customer', customerId], queryFn: () => getCustomer(customerId!), enabled: open && customerId !== null })
  const isLoadingDetails = customerId !== null && details.isPending
  const provinces = useQuery({ queryKey: ['provinces'], queryFn: getProvinces, enabled: open })
  const [duplicate, setDuplicate] = useState<CustomerDuplicate | null>(null)
  const [pendingValues, setPendingValues] = useState<CustomerInput | null>(null)
  const { register, handleSubmit, reset, watch, setValue, formState: { errors } } = useForm<CustomerFormValues>({ resolver: zodResolver(customerSchema), defaultValues: emptyForm })
  const provinceId = Number(watch('provinceId')) || 0
  const amphureId = Number(watch('amphureId')) || 0
  const districtId = Number(watch('districtId')) || 0
  const isBlacklist = watch('isBlacklist')
  const amphures = useQuery({ queryKey: ['amphures', provinceId], queryFn: () => getAmphures(provinceId), enabled: open && provinceId > 0 })
  const districts = useQuery({ queryKey: ['districts', amphureId], queryFn: () => getDistricts(amphureId), enabled: open && amphureId > 0 })

  useEffect(() => {
    if (!open) return
    if (!customerId) { reset(emptyForm); return }
    if (details.data) reset({
      firstName: details.data.firstName, lastName: details.data.lastName, phoneNumber1: details.data.phoneNumber1 || '',
      phoneNumber2: details.data.phoneNumber2 || '', idCard: details.data.idCard || '', driverLicense: details.data.driverLicense || '',
      genderId: String(details.data.genderId || 1), dateOfBirth: details.data.dateOfBirth?.slice(0, 10) || '', address1: details.data.address1 || '',
      address2: details.data.address2 || '', provinceId: details.data.provinceId ? String(details.data.provinceId) : '', amphureId: details.data.amphureId ? String(details.data.amphureId) : '',
      districtId: details.data.districtId ? String(details.data.districtId) : '', zipCode: details.data.zipCode || '', email: details.data.email || '',
      lineId: details.data.lineId || '', isBlacklist: details.data.isBlacklist, blacklistRemark: details.data.blacklistRemark || '',
    })
  }, [customerId, details.data, open, reset])

  useEffect(() => {
    if (!districtId) return
    void getZipCode(districtId).then((x) => { if (x.zipCode) setValue('zipCode', x.zipCode) })
  }, [districtId, setValue])

  const save = useMutation({
    mutationFn: (input: CustomerInput) => customerId ? updateCustomer(customerId, input) : createCustomer(input),
    onSuccess: () => finish(customerId ? 'แก้ไขข้อมูลลูกค้าแล้ว' : 'เพิ่มลูกค้าเรียบร้อยแล้ว'),
    onError: (error) => {
      if (isApiError(error) && error.code === 'CUSTOMER_DUPLICATE' && error.details) setDuplicate(error.details as CustomerDuplicate)
    },
  })
  const merge = useMutation({
    mutationFn: () => updateCustomer(duplicate!.id, pendingValues!),
    onSuccess: () => finish('อัปเดตข้อมูลลูกค้าเดิมเรียบร้อยแล้ว'),
  })
  const finish = (message: string) => { toast.success(message); void queryClient.invalidateQueries({ queryKey: ['customers'] }); void queryClient.invalidateQueries({ queryKey: ['customer'] }); close() }
  const close = () => { reset(emptyForm); save.reset(); merge.reset(); setDuplicate(null); setPendingValues(null); onClose() }
  const submit = handleSubmit((v) => {
    const input: CustomerInput = {
      firstName: v.firstName, lastName: v.lastName, phoneNumber1: v.phoneNumber1,
      phoneNumber2: v.phoneNumber2 || undefined, idCard: v.idCard || undefined, driverLicense: v.driverLicense || undefined,
      genderId: Number(v.genderId) || undefined, dateOfBirth: v.dateOfBirth || undefined, address1: v.address1 || undefined,
      address2: v.address2 || undefined, provinceId: Number(v.provinceId) || undefined, amphureId: Number(v.amphureId) || undefined,
      districtId: Number(v.districtId) || undefined, zipCode: v.zipCode || undefined, email: v.email || undefined,
      lineId: v.lineId || undefined, isBlacklist: v.isBlacklist, blacklistRemark: v.blacklistRemark || undefined,
    }
    setPendingValues(input); save.mutate(input)
  })

  return <>
    <ConfirmModal open={open} title={customerId ? 'แก้ไขข้อมูลลูกค้า' : 'เพิ่มลูกค้า'} description="ข้อมูลที่บันทึกจะใช้ร่วมกับระบบ GaragePro เดิม" onClose={close} size="large" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button type="submit" form="customer-form" disabled={save.isPending || isLoadingDetails}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button></>}>
      {isLoadingDetails ? <SkeletonRows count={4} /> : <form id="customer-form" className="management-form" onSubmit={submit}>
        <section><h3>ข้อมูลหลัก</h3><div className="form-grid"><Field label="ชื่อ *" error={errors.firstName?.message}><Input {...register('firstName')} /></Field><Field label="นามสกุล *" error={errors.lastName?.message}><Input {...register('lastName')} /></Field><Field label="เบอร์โทรหลัก *" error={errors.phoneNumber1?.message}><Input {...register('phoneNumber1')} /></Field><Field label="เบอร์โทรสำรอง" error={errors.phoneNumber2?.message}><Input {...register('phoneNumber2')} /></Field><Field label="เลขบัตรประชาชน" error={errors.idCard?.message}><Input maxLength={13} {...register('idCard')} /></Field><Field label="เลขใบขับขี่"><Input {...register('driverLicense')} /></Field><Field label="เพศ"><Select {...register('genderId')}><option value="1">ชาย</option><option value="2">หญิง</option></Select></Field><Field label="วันเกิด"><Input type="date" {...register('dateOfBirth')} /></Field><Field label="อีเมล" error={errors.email?.message}><Input type="email" {...register('email')} /></Field><Field label="LINE ID"><Input {...register('lineId')} /></Field></div></section>
        <section><h3>ที่อยู่</h3><div className="form-grid"><Field label="ที่อยู่บรรทัด 1" wide><Input {...register('address1')} /></Field><Field label="ที่อยู่บรรทัด 2" wide><Input {...register('address2')} /></Field><Field label="จังหวัด"><Select {...register('provinceId')} onChange={(e) => { setValue('provinceId', e.target.value); setValue('amphureId', ''); setValue('districtId', '') }}><option value="">เลือกจังหวัด</option>{provinces.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="อำเภอ / เขต"><Select {...register('amphureId')} disabled={!provinceId} title={!provinceId ? 'เลือกจังหวัดก่อน' : undefined} onChange={(e) => { setValue('amphureId', e.target.value); setValue('districtId', '') }}><option value="">เลือกอำเภอ / เขต</option>{amphures.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ตำบล / แขวง"><Select {...register('districtId')} disabled={!amphureId} title={!amphureId ? 'เลือกอำเภอก่อน' : undefined}><option value="">เลือกตำบล / แขวง</option>{districts.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="รหัสไปรษณีย์"><Input {...register('zipCode')} /></Field></div></section>
        <section className={`blacklist-section ${isBlacklist ? 'blacklist-section--active' : ''}`}><label className="check-row"><input type="checkbox" {...register('isBlacklist')} /><span><strong>ติดแบล็กลิสต์</strong><small>ระบบจะแสดงเครื่องหมายเตือนชัดเจนในหน้ารายการ</small></span></label>{isBlacklist ? <Field label="เหตุผล *" error={errors.blacklistRemark?.message}><Textarea rows={3} {...register('blacklistRemark')} /></Field> : null}</section>
        {save.isError && !(isApiError(save.error) && save.error.code === 'CUSTOMER_DUPLICATE') ? <InlineError error={save.error} /> : null}
      </form>}
    </ConfirmModal>
    <ConfirmModal open={Boolean(duplicate)} title="พบข้อมูลลูกค้าซ้ำ" description="ระบบจะไม่สร้างข้อมูลซ้ำโดยอัตโนมัติ" onClose={() => setDuplicate(null)} size="small" footer={<><Button variant="ghost" onClick={() => setDuplicate(null)}>ยกเลิก</Button><Button disabled={merge.isPending} onClick={() => merge.mutate()}>{merge.isPending ? 'กำลังอัปเดต…' : 'อัปเดตข้อมูลเดิม'}</Button></>}><div className="duplicate-warning"><AlertTriangle /><div><strong>{duplicate?.fullName}</strong><p>{duplicate?.code} · {duplicate?.phoneNumber1}</p><small>{duplicate?.isDeleted ? 'รายการเดิมถูกลบไว้และจะถูกเปิดใช้งานอีกครั้ง' : 'ข้อมูลใหม่จะเขียนทับข้อมูลลูกค้าเดิม'}</small></div></div>{merge.isError ? <InlineError error={merge.error} /> : null}</ConfirmModal>
  </>
}

function Field({ label, error, wide, children }: { label: string; error?: string; wide?: boolean; children: React.ReactNode }) {
  return <Label className={`field ${wide ? 'field--wide' : ''}`}><span>{label}</span>{children}{error ? <small className="field-error">{error}</small> : null}</Label>
}

function InlineError({ error }: { error: unknown }) {
  return <Alert variant="destructive"><AlertTriangle /><div><AlertTitle>{isApiError(error) ? error.messageTh : 'ดำเนินการไม่สำเร็จ'}</AlertTitle><AlertDescription>รหัสติดตาม (traceId): {isApiError(error) ? error.traceId : 'ไม่พบรหัสติดตาม'}</AlertDescription></div></Alert>
}
