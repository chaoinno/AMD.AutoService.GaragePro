import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { AlertTriangle, CarFront, Download, ImagePlus, Pencil, Plus, Search, Trash2, UserRound } from 'lucide-react'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import {
  apiDownload,
  isApiError,
  isForbiddenError,
} from '../../api/client'
import {
  createVehicle,
  deleteVehicle,
  exportVehicles,
  getCustomers,
  getModels,
  getNicknames,
  getProvinces,
  getVehicle,
  getVehicleReferenceData,
  getVehicles,
  triggerDownload,
  updateVehicle,
  type VehicleFilters,
} from '../../api/customerVehicles'
import type { CustomerSummary, VehicleInput, VehicleSummary } from '../../api/types'
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

const vehicleSchema = z.object({
  customerId: z.number().int().positive('กรุณาเลือกลูกค้าเจ้าของรถ'),
  registration: z.string().trim().min(1, 'กรุณากรอกทะเบียนรถ'),
  provinceId: z.string().min(1, 'กรุณาเลือกจังหวัดจดทะเบียน'),
  brandId: z.string().min(1, 'กรุณาเลือกยี่ห้อ'),
  modelId: z.string().min(1, 'กรุณาเลือกรุ่น'),
  nicknameId: z.string().min(1, 'กรุณาเลือกโฉมรถ'),
  yearId: z.string().min(1, 'กรุณาเลือกปีรถ'),
  primaryColorId: z.string(), colorMixId: z.string(), gearId: z.string(), machineId: z.string(), driveSystemId: z.string(),
  vin: z.string().trim().refine((v) => !v || /^[A-Za-z0-9]{17}$/.test(v), 'VIN ต้องมี 17 ตัวอักษรหรือตัวเลข'),
  engineNumber: z.string().trim(), insuranceId: z.string(), insuranceExpiredDate: z.string(),
})
type VehicleFormValues = z.infer<typeof vehicleSchema>
const emptyVehicle: VehicleFormValues = { customerId: 0, registration: '', provinceId: '', brandId: '', modelId: '', nicknameId: '', yearId: '', primaryColorId: '', colorMixId: '', gearId: '', machineId: '', driveSystemId: '', vin: '', engineNumber: '', insuranceId: '', insuranceExpiredDate: '' }
const vehicleSearchFields = [
  ['registration', 'ทะเบียน'], ['customerName', 'ชื่อลูกค้า'], ['phone', 'เบอร์โทร'],
  ['idCard', 'เลขบัตร'], ['vin', 'VIN'], ['engineNo', 'เลขเครื่อง'],
] as const

function useDebounced<T>(value: T, delay = 350) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => { const timer = window.setTimeout(() => setDebounced(value), delay); return () => window.clearTimeout(timer) }, [delay, value])
  return debounced
}

export function VehiclePage() {
  const [keyword, setKeyword] = useState('')
  const debouncedKeyword = useDebounced(keyword)
  const [filters, setFilters] = useState<VehicleFilters>({ page: 1, pageSize: 25 })
  const [formId, setFormId] = useState<number | 'new' | null>(null)
  const [deleting, setDeleting] = useState<VehicleSummary | null>(null)
  const queryClient = useQueryClient()
  const effective = { ...filters, keyword: debouncedKeyword || undefined }
  const query = useQuery({ queryKey: ['vehicles', effective], queryFn: () => getVehicles(effective) })
  const refs = useQuery({ queryKey: ['vehicle-reference-data'], queryFn: getVehicleReferenceData })
  const models = useQuery({ queryKey: ['models', filters.brandId], queryFn: () => getModels(filters.brandId!), enabled: Boolean(filters.brandId) })
  const nicknames = useQuery({ queryKey: ['nicknames', filters.modelId], queryFn: () => getNicknames(filters.modelId!), enabled: Boolean(filters.modelId) })
  const remove = useMutation({ mutationFn: deleteVehicle, onSuccess: () => { toast.success('ลบรถเรียบร้อยแล้ว'); setDeleting(null); void queryClient.invalidateQueries({ queryKey: ['vehicles'] }) } })
  const download = useMutation({
    mutationFn: () => exportVehicles(effective),
    onSuccess: (blob) => triggerDownload(blob, `vehicles-${new Date().toISOString().slice(0, 10)}.csv`),
    onError: (error) => toast.error(isApiError(error)
      ? `${error.messageTh} (traceId: ${error.traceId})`
      : 'ส่งออกรายการรถไม่สำเร็จ'),
  })
  const toggleSearchField = (field: string, checked: boolean) => setFilters((current) => {
    const selected = new Set(current.searchFields ?? [])
    if (checked) selected.add(field); else selected.delete(field)
    return { ...current, searchFields: [...selected], page: 1 }
  })

  const columns = useMemo<ColumnDef<VehicleSummary, unknown>[]>(() => [
    { id: 'image', enableSorting: false, header: 'รูป', size: 76, cell: ({ row }) => row.original.imageUrl ? <ProtectedImage path={row.original.imageUrl} alt={`รถทะเบียน ${row.original.registration}`} /> : <span className="vehicle-placeholder"><CarFront /></span> },
    { id: 'registration', accessorFn: (x) => [x.registration, x.provinceName].filter(Boolean).join(' '), header: 'ทะเบียน', size: 170, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.registration}</strong><span>{row.original.provinceName || 'ไม่ระบุจังหวัด'}</span></div> },
    { id: 'model', accessorFn: (x) => [x.brandName, x.modelName, x.nickname, x.year].filter(Boolean).join(' '), header: 'รถ', size: 245, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.brandName || 'ไม่ระบุยี่ห้อ'} {row.original.modelName || ''}</strong><span>{row.original.nickname || 'ไม่ระบุโฉม'} · {row.original.year || 'ไม่ระบุปี'}</span></div> },
    { id: 'owner', accessorFn: (x) => [x.ownerName, x.ownerPhone].filter(Boolean).join(' '), header: 'ลูกค้า', size: 230, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.ownerName || 'ไม่ระบุเจ้าของ'}</strong><span>{row.original.ownerPhone || 'ไม่ระบุเบอร์โทร'}</span></div> },
    { id: 'type', accessorFn: (x) => [x.carTypeName, x.primaryColorName].filter(Boolean).join(' '), header: 'ชนิด / สี', size: 165, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.carTypeName || 'ไม่ระบุชนิด'}</strong><span>{row.original.primaryColorName || 'ไม่ระบุสี'}</span></div> },
    { id: 'status', accessorFn: (x) => x.isDeleted ? 'ลบแล้ว' : 'ใช้งาน', header: 'สถานะ', size: 105, cell: ({ row }) => row.original.isDeleted ? <Badge variant="outline">ลบแล้ว</Badge> : <Badge className="active-badge">ใช้งาน</Badge> },
    { id: 'actions', enableSorting: false, header: '', size: 110, cell: ({ row }) => <div className="row-actions"><Button size="icon" variant="ghost" onClick={(e) => { e.stopPropagation(); setFormId(row.original.id) }} aria-label={`แก้ไขรถ ${row.original.registration}`}><Pencil /></Button><Button size="icon" variant="ghost" disabled={row.original.isDeleted} title={row.original.isDeleted ? 'รายการนี้ถูกลบแล้ว' : 'ลบรถ'} onClick={(e) => { e.stopPropagation(); setDeleting(row.original) }} aria-label={`ลบรถ ${row.original.registration}`}><Trash2 /></Button></div> },
  ], [])

  let content
  if (query.isPending) content = <StateBlock variant="loading" title="กำลังโหลดรายการรถ" reason="ระบบกำลังค้นหารถและข้อมูลเจ้าของในสาขาปัจจุบัน" actionLabel="โหลดใหม่" onAction={() => void query.refetch()}><SkeletonRows /></StateBlock>
  else if (query.isError && isForbiddenError(query.error)) content = <StateBlock variant="forbidden" title="ไม่มีสิทธิ์ดูข้อมูลรถ" reason={isApiError(query.error) ? query.error.messageTh : 'บัญชีนี้ไม่มีสิทธิ์'} traceId={isApiError(query.error) ? query.error.traceId : undefined} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (query.isError) content = <StateBlock variant="error" title="โหลดข้อมูลรถไม่สำเร็จ" reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'} traceId={isApiError(query.error) ? query.error.traceId : undefined} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (!query.data.items.length) content = <StateBlock variant="empty" title="ไม่พบรถที่ตรงกับเงื่อนไข" reason="รถที่เพิ่มใหม่จะแสดงในรายการทันทีโดยไม่ต้องรอเปิดงานซ่อม" traceId="คำขอสำเร็จและไม่พบรายการ" actionLabel="เพิ่มรถ" onAction={() => setFormId('new')} />
  else content = <Card className="management-table-card"><DataTable sortable sortScope="page" data={query.data.items} columns={columns} onRowClick={(row) => setFormId(row.id)} getRowLabel={(row) => `เปิดข้อมูลรถ ${row.registration}`} /><Pagination page={query.data.page} totalPages={query.data.totalPages} totalItems={query.data.totalItems} onPageChange={(page) => setFilters((f) => ({ ...f, page }))} /></Card>

  return <AppShell title="รถลูกค้า">
    <section className="page-heading"><div><p className="eyebrow">ข้อมูลลูกค้าและรถ</p><h2>จัดการรถลูกค้า</h2><p>รถใหม่จะแสดงทันที พร้อมค้นหาจากทะเบียน เจ้าของ VIN และเลขเครื่อง</p></div><div className="heading-actions"><Button variant="outline" disabled={download.isPending} onClick={() => download.mutate()}><Download /> {download.isPending ? 'กำลังส่งออก…' : 'ส่งออก CSV'}</Button><Button onClick={() => setFormId('new')}><Plus /> เพิ่มรถ</Button></div></section>
    <Card className="management-filters management-filters--vehicle">
      <div className="filter-search input-with-icon"><Search /><Input value={keyword} onChange={(e) => { setKeyword(e.target.value); setFilters((f) => ({ ...f, page: 1 })) }} placeholder="ค้นหาทะเบียน ลูกค้า เบอร์โทร VIN หรือเลขเครื่อง" /></div>
      <Select value={filters.brandId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, brandId: Number(e.target.value) || undefined, modelId: undefined, nicknameId: undefined, page: 1 }))}><option value="">ทุกยี่ห้อ</option>{refs.data?.brands.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.modelId ?? ''} disabled={!filters.brandId} title={!filters.brandId ? 'เลือกยี่ห้อก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, modelId: Number(e.target.value) || undefined, nicknameId: undefined, page: 1 }))}><option value="">ทุกรุ่น</option>{models.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.nicknameId ?? ''} disabled={!filters.modelId} title={!filters.modelId ? 'เลือกรุ่นก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, nicknameId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกโฉม</option>{nicknames.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.carTypeId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, carTypeId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกชนิดรถ</option>{refs.data?.carTypes.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.yearId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, yearId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกปี</option>{refs.data?.years.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.insuranceId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, insuranceId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกประกัน</option>{refs.data?.insurances.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <label className="filter-check"><input type="checkbox" checked={Boolean(filters.includeDeleted)} onChange={(e) => setFilters((f) => ({ ...f, includeDeleted: e.target.checked, page: 1 }))} /> แสดงรายการที่ถูกลบ</label>
      <div className="search-field-options" aria-label="เลือกฟิลด์ที่ใช้ค้นหา">
        <span>ค้นหาใน:</span>
        <label><input type="radio" name="vehicle-search-scope" checked={!filters.searchFields?.length} onChange={() => setFilters((f) => ({ ...f, searchFields: [], page: 1 }))} /> ทุกฟิลด์</label>
        {vehicleSearchFields.map(([value, label]) => <label key={value}><input type="checkbox" checked={filters.searchFields?.includes(value) ?? false} onChange={(e) => toggleSearchField(value, e.target.checked)} /> {label}</label>)}
      </div>
    </Card>
    {content}
    <VehicleFormDialog open={formId !== null} vehicleId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
    <ConfirmModal open={Boolean(deleting)} title="ยืนยันการลบรถ" description="รถจะถูกซ่อนแบบ soft-delete และเรียกดูได้ด้วยตัวกรองรายการที่ถูกลบ" onClose={() => { if (!remove.isPending) setDeleting(null) }} size="small" footer={<><Button variant="ghost" onClick={() => setDeleting(null)}>ยกเลิก</Button><Button variant="destructive" disabled={remove.isPending} onClick={() => deleting && remove.mutate(deleting.id)}><Trash2 /> {remove.isPending ? 'กำลังลบ…' : 'ลบรถ'}</Button></>}><p>ต้องการลบรถทะเบียน <strong>{deleting?.registration}</strong> หรือไม่?</p>{remove.isError ? <InlineError error={remove.error} /> : null}</ConfirmModal>
  </AppShell>
}

function VehicleFormDialog({ open, vehicleId, onClose }: { open: boolean; vehicleId: number | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const details = useQuery({ queryKey: ['vehicle', vehicleId], queryFn: () => getVehicle(vehicleId!), enabled: open && vehicleId !== null })
  const isLoadingDetails = vehicleId !== null && details.isPending
  const refs = useQuery({ queryKey: ['vehicle-reference-data'], queryFn: getVehicleReferenceData, enabled: open })
  const provinces = useQuery({ queryKey: ['provinces'], queryFn: getProvinces, enabled: open })
  const [ownerSearch, setOwnerSearch] = useState('')
  const debouncedOwner = useDebounced(ownerSearch)
  const [selectedOwner, setSelectedOwner] = useState<CustomerSummary | null>(null)
  const [image, setImage] = useState<File | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const [imageError, setImageError] = useState<string | null>(null)
  const { register, handleSubmit, reset, watch, setValue, formState: { errors } } = useForm<VehicleFormValues>({ resolver: zodResolver(vehicleSchema), defaultValues: emptyVehicle })
  const brandId = Number(watch('brandId')) || 0
  const modelId = Number(watch('modelId')) || 0
  const models = useQuery({ queryKey: ['models', brandId], queryFn: () => getModels(brandId), enabled: open && brandId > 0 })
  const nicknames = useQuery({ queryKey: ['nicknames', modelId], queryFn: () => getNicknames(modelId), enabled: open && modelId > 0 })
  const owners = useQuery({ queryKey: ['customer-picker', debouncedOwner], queryFn: () => getCustomers({ keyword: debouncedOwner, pageSize: 8 }), enabled: open && debouncedOwner.trim().length >= 2 })

  useEffect(() => {
    if (!open) return
    if (!vehicleId) { reset(emptyVehicle); setSelectedOwner(null); return }
    if (details.data) {
      const owner = details.data.owners[0]
      setSelectedOwner(owner ? { ...owner, firstName: owner.fullName, lastName: '', phoneNumber2: null, email: null, provinceName: null, isBlacklist: false, blacklistRemark: null, isDeleted: false, vehicleCount: 0, lastUpdated: null } : null)
      reset({ customerId: owner?.id || 0, registration: details.data.registration, provinceId: String(details.data.provinceId || ''), brandId: String(details.data.brandId || ''), modelId: String(details.data.modelId || ''), nicknameId: String(details.data.nicknameId || ''), yearId: String(details.data.yearId || ''), primaryColorId: String(details.data.primaryColorId || ''), colorMixId: String(details.data.colorMixId || ''), gearId: String(details.data.gearId || ''), machineId: String(details.data.machineId || ''), driveSystemId: String(details.data.driveSystemId || ''), vin: details.data.vin || '', engineNumber: details.data.engineNumber || '', insuranceId: String(details.data.insuranceId || ''), insuranceExpiredDate: details.data.insuranceExpiredDate?.slice(0, 10) || '' })
    }
  }, [details.data, open, reset, vehicleId])

  useEffect(() => { if (!image) { setPreview(null); return }; const url = URL.createObjectURL(image); setPreview(url); return () => URL.revokeObjectURL(url) }, [image])
  const save = useMutation({ mutationFn: ({ input, file }: { input: VehicleInput; file: File | null }) => vehicleId ? updateVehicle(vehicleId, input, file) : createVehicle(input, file), onSuccess: () => { toast.success(vehicleId ? 'แก้ไขข้อมูลรถแล้ว' : 'เพิ่มรถเรียบร้อยแล้ว'); void queryClient.invalidateQueries({ queryKey: ['vehicles'] }); close() } })
  const close = () => { reset(emptyVehicle); setOwnerSearch(''); setSelectedOwner(null); setImage(null); setImageError(null); save.reset(); onClose() }
  const chooseOwner = (owner: CustomerSummary) => { setSelectedOwner(owner); setValue('customerId', owner.id, { shouldValidate: true }); setOwnerSearch('') }
  const chooseImage = (file?: File) => {
    if (!file) return
    if (!['image/jpeg', 'image/png'].includes(file.type)) { setImageError('รองรับเฉพาะไฟล์ JPG, JPEG และ PNG'); return }
    if (file.size > 5 * 1024 * 1024) { setImageError('รูปต้องมีขนาดไม่เกิน 5 MB'); return }
    setImageError(null); setImage(file)
  }
  const submit = handleSubmit((v) => save.mutate({ input: { customerId: v.customerId, registration: v.registration, provinceId: Number(v.provinceId), brandId: Number(v.brandId), modelId: Number(v.modelId), nicknameId: Number(v.nicknameId), yearId: Number(v.yearId), primaryColorId: Number(v.primaryColorId) || undefined, colorMixId: Number(v.colorMixId) || undefined, gearId: Number(v.gearId) || undefined, machineId: Number(v.machineId) || undefined, driveSystemId: Number(v.driveSystemId) || undefined, vin: v.vin || undefined, engineNumber: v.engineNumber || undefined, insuranceId: Number(v.insuranceId) || undefined, insuranceExpiredDate: v.insuranceExpiredDate || undefined }, file: image }))

  return <ConfirmModal open={open} title={vehicleId ? 'แก้ไขข้อมูลรถ' : 'เพิ่มรถลูกค้า'} description="รถที่บันทึกจะใช้ร่วมกับระบบ GaragePro เดิมและแสดงทันทีโดยไม่ต้องรอมีงานซ่อม" onClose={close} size="large" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button type="submit" form="vehicle-form" disabled={save.isPending || isLoadingDetails}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูลรถ'}</Button></>}>
    {isLoadingDetails ? <SkeletonRows count={5} /> : <form id="vehicle-form" className="management-form" onSubmit={submit}>
      <section><h3>ลูกค้าเจ้าของรถ *</h3>{selectedOwner ? <div className="selected-owner"><UserRound /><span><strong>{selectedOwner.fullName}</strong><small>{selectedOwner.code} · {selectedOwner.phoneNumber1 || 'ไม่ระบุเบอร์'}</small></span><Button type="button" variant="ghost" onClick={() => { setSelectedOwner(null); setValue('customerId', 0) }}>เปลี่ยน</Button></div> : <><div className="input-with-icon"><Search /><Input value={ownerSearch} onChange={(e) => setOwnerSearch(e.target.value)} placeholder="ค้นหาชื่อ เบอร์โทร หรือเลขบัตร อย่างน้อย 2 ตัว" /></div>{ownerSearch.trim().length >= 2 ? <div className="owner-results">{owners.isPending ? <span>กำลังค้นหา…</span> : owners.data?.items.length ? owners.data.items.map((x) => <button type="button" key={x.id} onClick={() => chooseOwner(x)}><strong>{x.fullName}</strong><small>{x.code} · {x.phoneNumber1}</small></button>) : <span>ไม่พบลูกค้าที่ตรงกับคำค้น</span>}</div> : null}</>}{errors.customerId ? <small className="field-error">{errors.customerId.message}</small> : null}</section>
      <section><h3>ข้อมูลรถ</h3><div className="form-grid"><Field label="ทะเบียน *" error={errors.registration?.message}><Input {...register('registration')} /></Field><Field label="จังหวัดจดทะเบียน *" error={errors.provinceId?.message}><Select {...register('provinceId')}><option value="">เลือกจังหวัด</option>{provinces.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ยี่ห้อ *" error={errors.brandId?.message}><Select {...register('brandId')} onChange={(e) => { setValue('brandId', e.target.value); setValue('modelId', ''); setValue('nicknameId', '') }}><option value="">เลือกยี่ห้อ</option>{refs.data?.brands.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="รุ่น *" error={errors.modelId?.message}><Select {...register('modelId')} disabled={!brandId} title={!brandId ? 'เลือกยี่ห้อก่อน' : undefined} onChange={(e) => { setValue('modelId', e.target.value); setValue('nicknameId', '') }}><option value="">เลือกรุ่น</option>{models.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="โฉม *" error={errors.nicknameId?.message}><Select {...register('nicknameId')} disabled={!modelId} title={!modelId ? 'เลือกรุ่นก่อน' : undefined}><option value="">เลือกโฉม</option>{nicknames.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ปี *" error={errors.yearId?.message}><Select {...register('yearId')}><option value="">เลือกปี</option>{refs.data?.years.map((x) => <option key={x.id} value={x.id}>{x.name} {x.secondary ? `(${x.secondary})` : ''}</option>)}</Select></Field><Field label="สีหลัก"><Select {...register('primaryColorId')}><option value="">เลือกสีหลัก</option>{refs.data?.primaryColors.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="สีผสม"><Select {...register('colorMixId')}><option value="">เลือกสีผสม</option>{refs.data?.colorMixes.map((x) => <option key={x.id} value={x.id}>{x.secondary ? `${x.secondary} · ` : ''}{x.name}</option>)}</Select></Field><Field label="VIN" error={errors.vin?.message}><Input maxLength={17} {...register('vin')} /></Field><Field label="เลขเครื่องยนต์"><Input {...register('engineNumber')} /></Field></div></section>
      <section><h3>ระบบรถและประกัน</h3><div className="form-grid"><Field label="เกียร์"><Select {...register('gearId')}><option value="">เลือกประเภทเกียร์</option>{refs.data?.gears.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="เครื่องยนต์"><Select {...register('machineId')}><option value="">เลือกเครื่องยนต์</option>{refs.data?.machines.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ระบบขับเคลื่อน"><Select {...register('driveSystemId')}><option value="">เลือกระบบขับเคลื่อน</option>{refs.data?.driveSystems.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="บริษัทประกัน"><Select {...register('insuranceId')}><option value="">เลือกบริษัทประกัน</option>{refs.data?.insurances.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ประกันหมดอายุ"><Input type="date" {...register('insuranceExpiredDate')} /></Field></div></section>
      <section><h3>รูปรถ</h3><label className="image-drop"><input type="file" accept=".jpg,.jpeg,.png,image/jpeg,image/png" onChange={(e) => chooseImage(e.target.files?.[0])} /><span>{preview ? <img src={preview} alt="ตัวอย่างรูปรถใหม่" /> : details.data?.imageUrl ? <ProtectedImage path={details.data.imageUrl} alt="รูปรถปัจจุบัน" large /> : <ImagePlus />}</span><strong>{image ? image.name : 'เลือกรูปหรือลากไฟล์มาวาง'}</strong><small>JPG, JPEG หรือ PNG ขนาดไม่เกิน 5 MB</small></label>{imageError ? <small className="field-error">{imageError}</small> : null}</section>
      {save.isError ? <InlineError error={save.error} /> : null}
    </form>}
  </ConfirmModal>
}

function ProtectedImage({ path, alt, large = false }: { path: string; alt: string; large?: boolean }) {
  const query = useQuery({ queryKey: ['protected-image', path], queryFn: () => apiDownload(path), staleTime: 5 * 60_000 })
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => { if (!query.data) return; const next = URL.createObjectURL(query.data); setUrl(next); return () => URL.revokeObjectURL(next) }, [query.data])
  return url ? <img className={large ? 'vehicle-image--large' : 'vehicle-image'} src={url} alt={alt} /> : <span className={large ? 'vehicle-placeholder vehicle-placeholder--large' : 'vehicle-placeholder'}><CarFront /></span>
}

function Field({ label, error, children }: { label: string; error?: string; children: ReactNode }) {
  return <Label className="field"><span>{label}</span>{children}{error ? <small className="field-error">{error}</small> : null}</Label>
}
function InlineError({ error }: { error: unknown }) {
  return <Alert variant="destructive"><AlertTriangle /><div><AlertTitle>{isApiError(error) ? error.messageTh : 'ดำเนินการไม่สำเร็จ'}</AlertTitle><AlertDescription>รหัสติดตาม (traceId): {isApiError(error) ? error.traceId : 'ไม่พบรหัสติดตาม'}</AlertDescription></div></Alert>
}
