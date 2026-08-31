import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { AlertTriangle, Ban, CheckCircle2, ImagePlus, Pencil, Plus, Search, ShieldCheck, UserRound } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { isApiError, isForbiddenError } from '../../api/client'
import { getAmphures, getDistricts, getProvinces, getZipCode } from '../../api/customerVehicles'
import {
  createStaff, getStaff, getStaffCodePreview, getStaffImage, getStaffReferenceData, getStaffs,
  getStaffSectors, setStaffStatus, updateStaff, type StaffFilters,
} from '../../api/staffs'
import type { StaffInput, StaffSummary } from '../../api/types'
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from '../../components/ui/tabs'
import { Textarea } from '../../components/ui/textarea'
import { useSession } from '../../lib/session'

const schema = z.object({
  branchId: z.string().min(1, 'กรุณาเลือกสาขา'),
  firstName: z.string().trim().min(1, 'กรุณากรอกชื่อ'),
  lastName: z.string().trim().min(1, 'กรุณากรอกนามสกุล'),
  genderId: z.string().min(1, 'กรุณาเลือกคำนำหน้า/เพศ'),
  idCard: z.string().trim().refine((v) => !v || /^\d{13}$/.test(v), 'เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก'),
  phoneNumber1: z.string().trim().regex(/^[0-9+()\-\s]{8,20}$/, 'รูปแบบเบอร์โทรไม่ถูกต้อง'),
  phoneNumber2: z.string().trim().refine((v) => !v || /^[0-9+()\-\s]{8,20}$/.test(v), 'รูปแบบเบอร์โทรไม่ถูกต้อง'),
  email: z.string().trim().refine((v) => !v || z.email().safeParse(v).success, 'รูปแบบอีเมลไม่ถูกต้อง'),
  lineId: z.string().trim(), address1: z.string().trim(), address2: z.string().trim(),
  provinceId: z.string(), amphureId: z.string(), districtId: z.string(), zipCode: z.string().trim(),
  salary: z.string().refine((v) => Number(v) >= 0, 'เงินเดือนต้องไม่น้อยกว่า 0'),
  skillLevelId: z.string(), experienceYear: z.string().refine((v) => Number(v) >= 0, 'ต้องไม่น้อยกว่า 0'),
  experienceMonth: z.string().refine((v) => Number(v) >= 0 && Number(v) <= 11, 'ระบุ 0–11 เดือน'),
  startJobDate: z.string(), endJobDate: z.string(), note: z.string().trim(),
  departmentId: z.string().min(1, 'กรุณาเลือกฝ่าย'), mainSectorId: z.string().min(1, 'กรุณาเลือกแผนกหลัก'),
  positionId: z.string().min(1, 'กรุณาเลือกตำแหน่ง'), userName: z.string(), password: z.string(),
})
type FormValues = z.infer<typeof schema>

const baseForm: FormValues = {
  branchId: '', firstName: '', lastName: '', genderId: '1', idCard: '', phoneNumber1: '', phoneNumber2: '',
  email: '', lineId: '', address1: '', address2: '', provinceId: '', amphureId: '', districtId: '', zipCode: '',
  salary: '0', skillLevelId: '', experienceYear: '0', experienceMonth: '0', startJobDate: new Date().toISOString().slice(0, 10),
  endJobDate: '', note: '', departmentId: '', mainSectorId: '', positionId: '', userName: '', password: '',
}

function useDebounced(value: string, delay = 350) {
  const [result, setResult] = useState(value)
  useEffect(() => { const timer = window.setTimeout(() => setResult(value), delay); return () => window.clearTimeout(timer) }, [delay, value])
  return result
}

export function StaffPage() {
  const [keyword, setKeyword] = useState('')
  const [filters, setFilters] = useState<StaffFilters>({ page: 1, pageSize: 25 })
  const [formId, setFormId] = useState<number | 'new' | null>(null)
  const [statusTarget, setStatusTarget] = useState<StaffSummary | null>(null)
  const debounced = useDebounced(keyword)
  const effective = { ...filters, keyword: debounced || undefined }
  const refs = useQuery({ queryKey: ['staff-reference'], queryFn: getStaffReferenceData })
  const sectors = useQuery({ queryKey: ['staff-filter-sectors', filters.departmentId], queryFn: () => getStaffSectors(filters.departmentId) })
  const provinces = useQuery({ queryKey: ['provinces'], queryFn: getProvinces })
  const filterAmphures = useQuery({ queryKey: ['amphures', filters.provinceId], queryFn: () => getAmphures(filters.provinceId!), enabled: Boolean(filters.provinceId) })
  const filterDistricts = useQuery({ queryKey: ['districts', filters.amphureId], queryFn: () => getDistricts(filters.amphureId!), enabled: Boolean(filters.amphureId) })
  const query = useQuery({ queryKey: ['staffs', effective], queryFn: () => getStaffs(effective) })

  const columns = useMemo<ColumnDef<StaffSummary, unknown>[]>(() => [
    { id: 'staff', header: 'พนักงาน', size: 260, cell: ({ row }) => <div className="staff-name-cell"><span className="staff-initial">{initials(row.original.fullName)}</span><span><strong>{row.original.fullName}</strong><small>{row.original.code}{row.original.isAdministrator ? ' · ผู้ดูแลระบบ' : ''}</small></span></div> },
    { id: 'organization', header: 'ฝ่าย / แผนก / ตำแหน่ง', size: 280, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.departmentName || 'ไม่ระบุฝ่าย'} · {row.original.sectorName || 'ไม่ระบุแผนก'}</strong><span>{row.original.positionName || 'ไม่ระบุตำแหน่ง'}</span></div> },
    { id: 'contact', header: 'การติดต่อ', size: 210, cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.phoneNumber1 || 'ไม่ระบุเบอร์'}</strong><span>{row.original.email || 'ไม่ระบุอีเมล'}</span></div> },
    { id: 'status', header: 'สถานะ', size: 130, cell: ({ row }) => row.original.isActive ? <Badge className="active-badge"><CheckCircle2 /> ใช้งาน</Badge> : <Badge variant="outline"><Ban /> ปิดใช้งาน</Badge> },
    { id: 'actions', header: '', size: 110, cell: ({ row }) => <div className="row-actions"><Button size="icon" variant="ghost" aria-label={`แก้ไข ${row.original.fullName}`} onClick={(e) => { e.stopPropagation(); setFormId(row.original.id) }}><Pencil /></Button><Button size="icon" variant="ghost" aria-label={`${row.original.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${row.original.fullName}`} title={row.original.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} onClick={(e) => { e.stopPropagation(); setStatusTarget(row.original) }}>{row.original.isActive ? <Ban /> : <CheckCircle2 />}</Button></div> },
  ], [])

  let content
  if (query.isPending) content = <StateBlock variant="loading" title="กำลังโหลดรายชื่อพนักงาน" reason="ระบบกำลังค้นหาข้อมูลในสาขาปัจจุบัน" actionLabel="โหลดใหม่" onAction={() => void query.refetch()}><SkeletonRows /></StateBlock>
  else if (query.isError && isForbiddenError(query.error)) content = <StateBlock variant="forbidden" title="ไม่มีสิทธิ์ดูข้อมูลพนักงาน" reason={errorMessage(query.error)} traceId={traceId(query.error)} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (query.isError) content = <StateBlock variant="error" title="โหลดข้อมูลพนักงานไม่สำเร็จ" reason={errorMessage(query.error)} traceId={traceId(query.error)} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (!query.data.items.length) content = <StateBlock variant="empty" title="ไม่พบพนักงานที่ตรงกับเงื่อนไข" reason="ลองเปลี่ยนคำค้นหาหรือตัวกรอง หรือเพิ่มพนักงานใหม่" traceId="คำขอสำเร็จและไม่พบรายการ" actionLabel="เพิ่มพนักงาน" onAction={() => setFormId('new')} />
  else content = <Card className="management-table-card"><DataTable data={query.data.items} columns={columns} onRowClick={(x) => setFormId(x.id)} getRowLabel={(x) => `เปิดข้อมูลพนักงาน ${x.fullName}`} /><Pagination page={query.data.page} totalPages={query.data.totalPages} totalItems={query.data.totalItems} onPageChange={(page) => setFilters((f) => ({ ...f, page }))} /></Card>

  return <AppShell title="พนักงาน">
    <section className="page-heading"><div><p className="eyebrow">บุคลากรและบัญชีผู้ใช้</p><h2>จัดการพนักงาน</h2><p>ข้อมูลพนักงาน ตำแหน่ง ระดับฝีมือ และสถานะการใช้งาน</p></div><Button onClick={() => setFormId('new')}><Plus /> เพิ่มพนักงาน</Button></section>
    <Card className="management-filters management-filters--staff">
      <div className="filter-search input-with-icon"><Search /><Input value={keyword} onChange={(e) => { setKeyword(e.target.value); setFilters((f) => ({ ...f, page: 1 })) }} placeholder="ค้นหารหัส ชื่อ เลขบัตร หรือเบอร์โทร" /></div>
      <Select value={filters.departmentId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, departmentId: Number(e.target.value) || undefined, sectorId: undefined, page: 1 }))}><option value="">ทุกฝ่าย</option>{refs.data?.departments.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.sectorId ?? ''} disabled={!filters.departmentId} title={!filters.departmentId ? 'เลือกฝ่ายก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, sectorId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกแผนก</option>{sectors.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.positionId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, positionId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกตำแหน่ง</option>{refs.data?.positions.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.provinceId ?? ''} onChange={(e) => setFilters((f) => ({ ...f, provinceId: Number(e.target.value) || undefined, amphureId: undefined, districtId: undefined, page: 1 }))}><option value="">ทุกจังหวัด</option>{provinces.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.amphureId ?? ''} disabled={!filters.provinceId} title={!filters.provinceId ? 'เลือกจังหวัดก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, amphureId: Number(e.target.value) || undefined, districtId: undefined, page: 1 }))}><option value="">ทุกอำเภอ / เขต</option>{filterAmphures.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <Select value={filters.districtId ?? ''} disabled={!filters.amphureId} title={!filters.amphureId ? 'เลือกอำเภอก่อน' : undefined} onChange={(e) => setFilters((f) => ({ ...f, districtId: Number(e.target.value) || undefined, page: 1 }))}><option value="">ทุกตำบล / แขวง</option>{filterDistricts.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select>
      <label className="filter-check"><input type="checkbox" checked={Boolean(filters.includeInactive)} onChange={(e) => setFilters((f) => ({ ...f, includeInactive: e.target.checked, page: 1 }))} /> แสดงรายการที่ปิดใช้งาน</label>
    </Card>
    {content}
    <StaffFormDialog open={formId !== null} staffId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
    <StatusDialog target={statusTarget} onClose={() => setStatusTarget(null)} />
  </AppShell>
}

function StaffFormDialog({ open, staffId, onClose }: { open: boolean; staffId: number | null; onClose: () => void }) {
  const { session } = useSession()
  const isAdmin = Boolean(session?.user.isAdministrator)
  const queryClient = useQueryClient()
  const details = useQuery({ queryKey: ['staff', staffId], queryFn: () => getStaff(staffId!), enabled: open && staffId !== null })
  const refs = useQuery({ queryKey: ['staff-reference'], queryFn: getStaffReferenceData, enabled: open })
  const provinces = useQuery({ queryKey: ['provinces'], queryFn: getProvinces, enabled: open })
  const [tab, setTab] = useState('personal')
  const [additional, setAdditional] = useState<number[]>([])
  const [sectorSearch, setSectorSearch] = useState('')
  const [image, setImage] = useState<File | null>(null)
  const [preview, setPreview] = useState<string | null>(null)
  const { register, handleSubmit, reset, watch, setValue, formState: { errors } } = useForm<FormValues>({ resolver: zodResolver(schema), defaultValues: baseForm })
  const branchId = Number(watch('branchId')) || session?.branchId || 0
  const departmentId = Number(watch('departmentId')) || 0
  const provinceId = Number(watch('provinceId')) || 0
  const amphureId = Number(watch('amphureId')) || 0
  const districtId = Number(watch('districtId')) || 0
  const mainSectors = useQuery({ queryKey: ['staff-main-sectors', departmentId], queryFn: () => getStaffSectors(departmentId), enabled: open && departmentId > 0 })
  const allSectors = useQuery({ queryKey: ['staff-all-sectors'], queryFn: () => getStaffSectors(), enabled: open })
  const amphures = useQuery({ queryKey: ['amphures', provinceId], queryFn: () => getAmphures(provinceId), enabled: open && provinceId > 0 })
  const districts = useQuery({ queryKey: ['districts', amphureId], queryFn: () => getDistricts(amphureId), enabled: open && amphureId > 0 })
  const codePreview = useQuery({ queryKey: ['staff-code-preview', branchId], queryFn: () => getStaffCodePreview(branchId), enabled: open && !staffId && branchId > 0 })

  useEffect(() => {
    if (!open) return
    setTab('personal'); setImage(null); setPreview(null); setSectorSearch('')
    if (!staffId) { reset({ ...baseForm, branchId: String(session?.branchId || ''), genderId: String(refs.data?.genders[0]?.id || 1) }); setAdditional([]); return }
    if (!details.data) return
    const d = details.data; const main = d.sectorPositions.find((x) => x.isMain)
    reset({ branchId: String(d.branchId), firstName: d.firstName, lastName: d.lastName, genderId: String(d.genderId || ''), idCard: d.idCard || '', phoneNumber1: d.phoneNumber1 || '', phoneNumber2: d.phoneNumber2 || '', email: d.email || '', lineId: d.lineId || '', address1: d.address1 || '', address2: d.address2 || '', provinceId: String(d.provinceId || ''), amphureId: String(d.amphureId || ''), districtId: String(d.districtId || ''), zipCode: d.zipCode || '', salary: String(d.salary || 0), skillLevelId: String(d.staffSkillLevelId || ''), experienceYear: String(d.experienceYear || 0), experienceMonth: String(d.experienceMonth || 0), startJobDate: d.startJobDate?.slice(0, 10) || '', endJobDate: d.endJobDate?.slice(0, 10) || '', note: d.note || '', departmentId: String(main?.departmentId || ''), mainSectorId: String(main?.sectorId || ''), positionId: String(main?.positionId || ''), userName: d.account.userName, password: '' })
    setAdditional(d.sectorPositions.filter((x) => !x.isMain).map((x) => x.sectorId))
  }, [details.data, open, refs.data?.genders, reset, session?.branchId, staffId])

  useEffect(() => { if (districtId) void getZipCode(districtId).then((x) => { if (x.zipCode) setValue('zipCode', x.zipCode) }) }, [districtId, setValue])

  const save = useMutation({
    mutationFn: ({ input, file }: { input: StaffInput; file: File | null }) => staffId ? updateStaff(staffId, input, file) : createStaff(input, file),
    onSuccess: () => { toast.success(staffId ? 'แก้ไขข้อมูลพนักงานแล้ว' : 'เพิ่มพนักงานและสร้างบัญชีผู้ใช้แล้ว'); void queryClient.invalidateQueries({ queryKey: ['staffs'] }); void queryClient.invalidateQueries({ queryKey: ['staff'] }); close() },
  })
  const close = () => { save.reset(); if (preview) URL.revokeObjectURL(preview); setPreview(null); setImage(null); onClose() }
  const submit = handleSubmit((v) => save.mutate({ input: {
    branchId: Number(v.branchId), firstName: v.firstName, lastName: v.lastName, genderId: Number(v.genderId), idCard: v.idCard || undefined,
    phoneNumber1: v.phoneNumber1, phoneNumber2: v.phoneNumber2 || undefined, email: v.email || undefined, lineId: v.lineId || undefined,
    address1: v.address1 || undefined, address2: v.address2 || undefined, provinceId: Number(v.provinceId) || undefined,
    amphureId: Number(v.amphureId) || undefined, districtId: Number(v.districtId) || undefined, zipCode: v.zipCode || undefined,
    salary: Number(v.salary), staffSkillLevelId: Number(v.skillLevelId) || undefined, experienceYear: Number(v.experienceYear),
    experienceMonth: Number(v.experienceMonth), startJobDate: v.startJobDate || undefined, endJobDate: v.endJobDate || undefined,
    note: v.note || undefined, mainSectorId: Number(v.mainSectorId), positionId: Number(v.positionId), additionalSectorIds: additional,
    userName: staffId ? v.userName : undefined, password: staffId && v.password ? v.password : undefined,
  }, file: image }), (invalid) => {
    const keys = Object.keys(invalid)
    if (keys.some((x) => ['address1','address2','provinceId','amphureId','districtId','zipCode'].includes(x))) setTab('address')
    else if (keys.some((x) => ['branchId','departmentId','mainSectorId','positionId','salary','skillLevelId','experienceYear','experienceMonth','startJobDate','endJobDate','note'].includes(x))) setTab('work')
    else if (keys.some((x) => ['userName','password'].includes(x))) setTab('account')
    else setTab('personal')
    toast.error('กรุณาตรวจสอบข้อมูลที่ระบุไม่ครบหรือรูปแบบไม่ถูกต้อง')
  })

  const pickImage = async (file?: File) => {
    if (!file) return
    try {
      const resized = await resizeProfileImage(file)
      if (preview) URL.revokeObjectURL(preview)
      setImage(resized); setPreview(URL.createObjectURL(resized))
    } catch { toast.error('ไม่สามารถอ่านรูปที่เลือกได้') }
  }

  const visibleExtras = allSectors.data?.filter((x) => `${x.secondary || ''} ${x.name}`.toLocaleLowerCase('th').includes(sectorSearch.toLocaleLowerCase('th'))) || []
  const lockedReason = !isAdmin && staffId ? 'เฉพาะ Admin เท่านั้นที่แก้ไขสาขา แผนก และตำแหน่งได้' : undefined
  return <ConfirmModal open={open} title={staffId ? 'แก้ไขข้อมูลพนักงาน' : 'เพิ่มพนักงาน'} description="บัญชีผู้ใช้ผูกกับพนักงานหนึ่งต่อหนึ่งในฐาน Garage" onClose={close} size="xlarge" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button type="submit" form="staff-form" disabled={save.isPending || (staffId !== null && details.isPending)}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button></>}>
    {staffId !== null && details.isPending ? <SkeletonRows count={5} /> : <form id="staff-form" className="staff-form" onSubmit={submit}>
      <Tabs value={tab} onValueChange={setTab}><TabsList><TabsTrigger value="personal">1. ข้อมูลส่วนตัว</TabsTrigger><TabsTrigger value="address">2. ที่อยู่</TabsTrigger><TabsTrigger value="work">3. ตำแหน่งงาน</TabsTrigger><TabsTrigger value="account">4. บัญชีผู้ใช้</TabsTrigger></TabsList>
        <TabsContent value="personal" className="staff-tab"><section className="staff-photo-panel"><label className="image-drop"><input type="file" accept=".jpg,.jpeg,.png,image/jpeg,image/png" onChange={(e) => void pickImage(e.target.files?.[0])} /><span>{preview ? <img src={preview} alt="ตัวอย่างรูปพนักงาน" /> : staffId && details.data?.pictureUrl ? <StaffPhoto staffId={staffId} /> : <ImagePlus />}</span><strong>เลือกรูปโปรไฟล์</strong><small>JPG/PNG ไม่เกิน 5 MB · ระบบครอปเป็น 300×300</small></label></section><div className="form-grid"><Field label="ชื่อ *" error={errors.firstName?.message}><Input {...register('firstName')} /></Field><Field label="นามสกุล *" error={errors.lastName?.message}><Input {...register('lastName')} /></Field><Field label="คำนำหน้า/เพศ *" error={errors.genderId?.message}><Select {...register('genderId')}><option value="">เลือก</option>{refs.data?.genders.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="เลขบัตรประชาชน" error={errors.idCard?.message}><Input maxLength={13} {...register('idCard')} /></Field><Field label="เบอร์โทรหลัก *" error={errors.phoneNumber1?.message}><Input {...register('phoneNumber1')} /></Field><Field label="เบอร์โทรสำรอง" error={errors.phoneNumber2?.message}><Input {...register('phoneNumber2')} /></Field><Field label="อีเมล" error={errors.email?.message}><Input type="email" {...register('email')} /></Field><Field label="LINE ID"><Input {...register('lineId')} /></Field></div></TabsContent>
        <TabsContent value="address" className="staff-tab"><div className="form-grid"><Field label="ที่อยู่บรรทัด 1" wide><Input {...register('address1')} /></Field><Field label="ที่อยู่บรรทัด 2" wide><Input {...register('address2')} /></Field><Field label="จังหวัด"><Select {...register('provinceId')} onChange={(e) => { setValue('provinceId', e.target.value); setValue('amphureId', ''); setValue('districtId', '') }}><option value="">เลือกจังหวัด</option>{provinces.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="อำเภอ / เขต"><Select {...register('amphureId')} disabled={!provinceId} title={!provinceId ? 'เลือกจังหวัดก่อน' : undefined} onChange={(e) => { setValue('amphureId', e.target.value); setValue('districtId', '') }}><option value="">เลือกอำเภอ / เขต</option>{amphures.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ตำบล / แขวง"><Select {...register('districtId')} disabled={!amphureId} title={!amphureId ? 'เลือกอำเภอก่อน' : undefined}><option value="">เลือกตำบล / แขวง</option>{districts.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="รหัสไปรษณีย์"><Input {...register('zipCode')} /></Field></div></TabsContent>
        <TabsContent value="work" className="staff-tab"><div className="form-grid"><Field label="สาขา *" error={errors.branchId?.message}><Select {...register('branchId')} disabled={!isAdmin} title={lockedReason}><option value="">เลือกสาขา</option>{refs.data?.branches.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ระดับฝีมือ"><Select {...register('skillLevelId')}><option value="">ไม่ระบุ</option>{refs.data?.skillLevels.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ฝ่ายหลัก *" error={errors.departmentId?.message}><Select {...register('departmentId')} disabled={Boolean(lockedReason)} title={lockedReason} onChange={(e) => { setValue('departmentId', e.target.value); setValue('mainSectorId', '') }}><option value="">เลือกฝ่าย</option>{refs.data?.departments.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="แผนกหลัก *" error={errors.mainSectorId?.message}><Select {...register('mainSectorId')} disabled={Boolean(lockedReason) || !departmentId} title={lockedReason || (!departmentId ? 'เลือกฝ่ายก่อน' : undefined)}><option value="">เลือกแผนก</option>{mainSectors.data?.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="ตำแหน่ง *" error={errors.positionId?.message}><Select {...register('positionId')} disabled={Boolean(lockedReason)} title={lockedReason}><option value="">เลือกตำแหน่ง</option>{refs.data?.positions.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</Select></Field><Field label="เงินเดือน" error={errors.salary?.message}><Input type="number" min="0" step="0.01" {...register('salary')} /></Field><Field label="ประสบการณ์ (ปี)" error={errors.experienceYear?.message}><Input type="number" min="0" {...register('experienceYear')} /></Field><Field label="ประสบการณ์ (เดือน)" error={errors.experienceMonth?.message}><Input type="number" min="0" max="11" {...register('experienceMonth')} /></Field><Field label="วันเริ่มงาน"><Input type="date" {...register('startJobDate')} /></Field><Field label="วันสิ้นสุดงาน"><Input type="date" {...register('endJobDate')} /></Field><Field label="หมายเหตุ" wide><Textarea rows={3} {...register('note')} /></Field></div><section className="sector-picker"><div><strong>แผนกเสริม</strong><small>{lockedReason || 'เลือกได้หลายแผนก โดยใช้ตำแหน่งเดียวกับตำแหน่งหลัก'}</small></div><Input value={sectorSearch} onChange={(e) => setSectorSearch(e.target.value)} placeholder="ค้นหาฝ่ายหรือแผนก" disabled={Boolean(lockedReason)} /><div className="sector-picker__options">{visibleExtras.map((x) => <label key={x.id}><input type="checkbox" disabled={Boolean(lockedReason)} checked={additional.includes(x.id)} onChange={(e) => setAdditional((old) => e.target.checked ? [...old, x.id] : old.filter((id) => id !== x.id))} /><span><strong>{x.name}</strong><small>{x.secondary}</small></span></label>)}</div></section></TabsContent>
        <TabsContent value="account" className="staff-tab"><div className="credential-preview"><ShieldCheck /><div><strong>{staffId ? 'บัญชีผู้ใช้ที่ผูกกับพนักงาน' : 'ระบบจะสร้างบัญชีให้อัตโนมัติ'}</strong><p>รหัสพนักงาน: <code>{staffId ? details.data?.code : codePreview.data?.code || 'กำลังคำนวณ…'}</code></p>{!staffId ? <><p>Username: <code>{codePreview.data?.userName || '—'}</code></p><p>Password เริ่มต้น: <code>{codePreview.data?.password || '—'}</code></p></> : null}<small>รหัสผ่านเริ่มต้นเป็นค่าเดียวกับรหัสพนักงานตามนโยบายระบบเดิม</small></div></div>{staffId ? <div className="form-grid"><Field label="Username *"><Input {...register('userName')} /></Field><Field label="Password ใหม่"><Input type="password" autoComplete="new-password" placeholder="เว้นว่างเพื่อใช้รหัสเดิม" {...register('password')} /></Field></div> : null}</TabsContent>
      </Tabs>
      {save.isError ? <InlineError error={save.error} /> : null}
    </form>}
  </ConfirmModal>
}

function StatusDialog({ target, onClose }: { target: StaffSummary | null; onClose: () => void }) {
  const queryClient = useQueryClient(); const [endDate, setEndDate] = useState(new Date().toISOString().slice(0, 10))
  useEffect(() => { if (target) setEndDate(new Date().toISOString().slice(0, 10)) }, [target])
  const mutation = useMutation({ mutationFn: () => setStaffStatus(target!.id, !target!.isActive, target!.isActive ? endDate : undefined), onSuccess: () => { toast.success(target?.isActive ? 'ปิดใช้งานพนักงานแล้ว' : 'เปิดใช้งานพนักงานแล้ว'); void queryClient.invalidateQueries({ queryKey: ['staffs'] }); onClose() } })
  return <ConfirmModal open={Boolean(target)} title={target?.isActive ? 'ปิดใช้งานพนักงาน' : 'เปิดใช้งานพนักงาน'} description="เป็นการเปลี่ยนสถานะแบบ soft-disable ข้อมูลจะไม่ถูกลบ" onClose={onClose} size="small" footer={<><Button variant="ghost" onClick={onClose}>ยกเลิก</Button><Button variant={target?.isActive ? 'destructive' : 'default'} disabled={mutation.isPending || (Boolean(target?.isActive) && !endDate)} title={target?.isActive && !endDate ? 'กรุณาระบุวันสิ้นสุดงาน' : undefined} onClick={() => mutation.mutate()}>{mutation.isPending ? 'กำลังบันทึก…' : target?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}</Button></>}><p>พนักงาน: <strong>{target?.fullName}</strong></p>{target?.isActive ? <Field label="วันสิ้นสุดงาน *"><Input type="date" value={endDate} onChange={(e) => setEndDate(e.target.value)} /></Field> : <p>ระบบจะล้างวันสิ้นสุดงานและกลับมาแสดงในรายการปกติ</p>}{mutation.isError ? <InlineError error={mutation.error} /> : null}</ConfirmModal>
}

function StaffPhoto({ staffId }: { staffId: number }) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => { let active = true; let objectUrl: string | null = null; void getStaffImage(staffId).then((blob) => { if (!active) return; objectUrl = URL.createObjectURL(blob); setUrl(objectUrl) }).catch(() => setUrl(null)); return () => { active = false; if (objectUrl) URL.revokeObjectURL(objectUrl) } }, [staffId])
  return url ? <img src={url} alt="รูปพนักงานปัจจุบัน" /> : <UserRound />
}

async function resizeProfileImage(file: File) {
  if (!['image/jpeg', 'image/png'].includes(file.type) || file.size > 5 * 1024 * 1024) throw new Error('invalid image')
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  const side = Math.min(bitmap.width, bitmap.height); const sx = (bitmap.width - side) / 2; const sy = (bitmap.height - side) / 2
  const canvas = document.createElement('canvas'); canvas.width = 300; canvas.height = 300
  canvas.getContext('2d')!.drawImage(bitmap, sx, sy, side, side, 0, 0, 300, 300); bitmap.close()
  const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob((value) => value ? resolve(value) : reject(new Error('resize failed')), 'image/jpeg', .88))
  return new File([blob], `${file.name.replace(/\.[^.]+$/, '')}.jpg`, { type: 'image/jpeg' })
}

function Field({ label, error, wide, children }: { label: string; error?: string; wide?: boolean; children: React.ReactNode }) { return <Label className={`field ${wide ? 'field--wide' : ''}`}><span>{label}</span>{children}{error ? <small className="field-error">{error}</small> : null}</Label> }
function InlineError({ error }: { error: unknown }) { return <Alert variant="destructive"><AlertTriangle /><div><AlertTitle>{errorMessage(error)}</AlertTitle><AlertDescription>รหัสติดตาม (traceId): {traceId(error) || 'ไม่พบรหัสติดตาม'}</AlertDescription></div></Alert> }
function errorMessage(error: unknown) { return isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ' }
function traceId(error: unknown) { return isApiError(error) ? error.traceId : undefined }
function initials(name: string) { return name.trim().split(/\s+/).slice(0, 2).map((x) => x[0]).join('') || 'พน' }
