import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { ColumnDef } from '@tanstack/react-table'
import { AlertTriangle, Boxes, CircleOff, Package, Pencil, Plus, Search, ShieldAlert, Trash2, Wrench } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import {
  createCatalogItem,
  getCatalogItem,
  getCatalogItems,
  setCatalogItemStatus,
  updateCatalogItem,
  type CatalogFilters,
} from '../../api/catalog'
import {
  getCatalogCategories,
  getCatalogItemSuppliers,
  getSuppliers,
  getWarehouses,
  removeCatalogItemSupplier,
  upsertCatalogItemSupplier,
} from '../../api/masterData'
import { isApiError, isForbiddenError } from '../../api/client'
import type { CatalogCategory, CatalogItemInput, CatalogItemSupplier, CatalogItemSupplierInput, CatalogManagementItem, PagedResult, Supplier } from '../../api/types'
import { ManagementTable } from '../../components/ManagementTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { DataTable } from '../../components/DataTable'
import { Money } from '../../components/Money'
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
import { useSession } from '../../lib/session'

const nonNegativeMoney = (label: string) => z.string().trim()
  .refine((value) => value !== '' && Number.isFinite(Number(value)) && Number(value) >= 0, `${label}ต้องไม่น้อยกว่า 0`)
const nonNegativeInteger = z.string().trim()
  .refine((value) => /^\d+$/.test(value), 'จำนวนต้องเป็นเลขจำนวนเต็มตั้งแต่ 0 ขึ้นไป')

const catalogSchema = z.object({
  code: z.string().trim().min(1, 'กรุณากรอกรหัสสินค้า').max(60, 'รหัสต้องยาวไม่เกิน 60 ตัวอักษร'),
  type: z.enum(['part', 'labor']),
  name: z.string().trim().min(1, 'กรุณากรอกชื่อสินค้า').max(300, 'ชื่อต้องยาวไม่เกิน 300 ตัวอักษร'),
  compatibility: z.string().trim().max(500, 'ข้อมูลรุ่นรถต้องยาวไม่เกิน 500 ตัวอักษร'),
  unit: z.string().trim().min(1, 'กรุณากรอกหน่วยนับ').max(40, 'หน่วยนับต้องยาวไม่เกิน 40 ตัวอักษร'),
  cost: nonNegativeMoney('ต้นทุน'),
  price: nonNegativeMoney('ราคาขาย'),
  standardHours: z.string().trim(),
  onHand: nonNegativeInteger,
  reserved: nonNegativeInteger,
  onOrder: nonNegativeInteger,
  damaged: nonNegativeInteger,
  etaNote: z.string().trim().max(200, 'หมายเหตุต้องยาวไม่เกิน 200 ตัวอักษร'),
  categoryId: z.string(),
  warehouseId: z.string(),
}).superRefine((value, ctx) => {
  if (value.type === 'labor' && (value.standardHours === '' || Number(value.standardHours) <= 0)) {
    ctx.addIssue({ code: 'custom', path: ['standardHours'], message: 'กรุณาระบุชั่วโมงมาตรฐานมากกว่า 0' })
  }
})

type CatalogFormValues = z.infer<typeof catalogSchema>

const emptyForm: CatalogFormValues = {
  code: '', type: 'part', name: '', compatibility: '', unit: 'ชิ้น', cost: '0', price: '0',
  standardHours: '', onHand: '0', reserved: '0', onOrder: '0', damaged: '0', etaNote: '',
  categoryId: '', warehouseId: '',
}

function flattenCategories(categories: CatalogCategory[], level = 0): Array<CatalogCategory & { level: number }> {
  return categories.flatMap((category) => [
    { ...category, level },
    ...(category.children ? flattenCategories(category.children, level + 1) : []),
  ])
}

function listResult<T>(result: T[] | PagedResult<T> | undefined): T[] {
  return Array.isArray(result) ? result : result?.items ?? []
}

function useDebounced<T>(value: T, delay = 350) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay)
    return () => window.clearTimeout(timer)
  }, [delay, value])
  return debounced
}

export function CatalogPage() {
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const [keyword, setKeyword] = useState('')
  const debouncedKeyword = useDebounced(keyword)
  const [filters, setFilters] = useState<CatalogFilters>({ page: 1, pageSize: 25 })
  const [formId, setFormId] = useState<string | 'new' | null>(null)
  const [statusTarget, setStatusTarget] = useState<CatalogManagementItem | null>(null)
  const effectiveFilters = { ...filters, keyword: debouncedKeyword || undefined }
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['catalog-management', effectiveFilters], queryFn: () => getCatalogItems(effectiveFilters) })
  const categoriesQuery = useQuery({ queryKey: ['catalog-categories', 'lookup'], queryFn: () => getCatalogCategories() })
  const warehousesQuery = useQuery({ queryKey: ['warehouses', 'lookup'], queryFn: () => getWarehouses() })
  const categoryNames = useMemo(() => new Map(flattenCategories(categoriesQuery.data ?? []).map((category) => [category.id, category.name])), [categoriesQuery.data])
  const warehouseNames = useMemo(() => new Map((warehousesQuery.data ?? []).map((warehouse) => [warehouse.id, warehouse.name])), [warehousesQuery.data])
  const status = useMutation({
    mutationFn: (item: CatalogManagementItem) => setCatalogItemStatus(item.id, !item.isActive),
    onSuccess: () => {
      toast.success(statusTarget?.isActive ? 'ปิดใช้งานสินค้าแล้ว' : 'เปิดใช้งานสินค้าแล้ว')
      setStatusTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['catalog-management'] })
      void queryClient.invalidateQueries({ queryKey: ['catalog'] })
    },
  })

  const columns = useMemo<ColumnDef<CatalogManagementItem, unknown>[]>(() => [
    {
      id: 'item', accessorFn: (x) => x.name, header: 'สินค้า / บริการ', size: 270,
      cell: ({ row }) => <div className="two-line-cell"><strong>{row.original.name}</strong><span className="catalog-code">{row.original.code}</span></div>,
    },
    {
      id: 'type', accessorFn: (x) => x.typeLabelTh, header: 'ประเภท', size: 105,
      cell: ({ row }) => <Badge variant="outline" className={`type-chip type-chip--${row.original.type}`}>
        {row.original.type === 'part' ? <Package /> : <Wrench />} {row.original.typeLabelTh}
      </Badge>,
    },
    {
      id: 'price', accessorFn: (x) => x.price, header: 'ราคา', size: 155,
      cell: ({ row }) => <div className="two-line-cell"><strong><Money value={row.original.price} /></strong><span>{row.original.cost === null ? 'ไม่เปิดเผยต้นทุน' : <>ทุน <Money value={row.original.cost} /></>}</span></div>,
    },
    {
      id: 'stock', accessorFn: (x) => x.type === 'part' ? x.available : x.standardHours, header: 'คงเหลือ', size: 135,
      cell: ({ row }) => row.original.type === 'part'
        ? <div className="two-line-cell"><strong className={row.original.available <= 0 ? 'stock-empty' : ''}>{row.original.available.toLocaleString('th-TH')} {row.original.unit}</strong><span>มี {row.original.onHand} · จอง {row.original.reserved} · รอ {row.original.onOrder}</span></div>
        : <div className="two-line-cell"><strong>{row.original.standardHours?.toLocaleString('th-TH')} ชม.</strong><span>ชั่วโมงมาตรฐาน</span></div>,
    },
    {
      id: 'compatibility', accessorFn: (x) => x.compatibility, header: 'รุ่นรถ / รายละเอียด', size: 240,
      cell: ({ row }) => <span className="catalog-compatibility">{row.original.compatibility || 'ใช้ได้ทั่วไป'}</span>,
    },
    {
      id: 'category', accessorFn: (x) => x.categoryId ? categoryNames.get(x.categoryId) : '', header: 'หมวดหมู่', size: 150,
      cell: ({ row }) => <span>{row.original.categoryId ? categoryNames.get(row.original.categoryId) || 'กำหนดหมวดหมู่แล้ว' : 'ไม่ระบุ'}</span>,
    },
    {
      id: 'warehouse', accessorFn: (x) => x.warehouseId ? warehouseNames.get(x.warehouseId) : '', header: 'คลังหลัก', size: 145,
      cell: ({ row }) => <span>{row.original.warehouseId ? warehouseNames.get(row.original.warehouseId) || 'กำหนดคลังแล้ว' : 'ไม่ระบุ'}</span>,
    },
    {
      id: 'status', accessorFn: (x) => x.isActive ? 'ใช้งาน' : 'ปิดใช้', header: 'สถานะ', size: 105,
      cell: ({ row }) => row.original.isActive
        ? <Badge className="active-badge">ใช้งาน</Badge>
        : <Badge variant="outline"><CircleOff /> ปิดใช้</Badge>,
    },
    {
      id: 'actions', enableSorting: false, header: '', size: 100,
      cell: ({ row }) => <div className="row-actions">
        <Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขสินค้าได้' : 'แก้ไขสินค้า'} aria-label={`แก้ไข ${row.original.name}`} onClick={(event) => { event.stopPropagation(); setFormId(row.original.id) }}><Pencil /></Button>
        <Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เปลี่ยนสถานะได้' : row.original.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} aria-label={`${row.original.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${row.original.name}`} onClick={(event) => { event.stopPropagation(); setStatusTarget(row.original) }}><CircleOff /></Button>
      </div>,
    },
  ], [canManage, categoryNames, warehouseNames])

  let content
  if (query.isPending) content = <StateBlock variant="loading" title="กำลังโหลดรายการสินค้า" reason="ระบบกำลังค้นหาข้อมูลในสาขาปัจจุบัน" actionLabel="โหลดใหม่" onAction={() => void query.refetch()}><SkeletonRows /></StateBlock>
  else if (query.isError && isForbiddenError(query.error)) content = <StateBlock variant="forbidden" title="ไม่มีสิทธิ์ดูรายการสินค้า" reason={errorMessage(query.error)} traceId={traceId(query.error)} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (query.isError) content = <StateBlock variant="error" title="โหลดรายการสินค้าไม่สำเร็จ" reason={errorMessage(query.error)} traceId={traceId(query.error)} actionLabel="ลองใหม่" onAction={() => void query.refetch()} />
  else if (!query.data.items.length) content = <StateBlock variant="empty" title="ไม่พบสินค้าที่ตรงกับเงื่อนไข" reason="ลองเปลี่ยนคำค้นหาหรือตัวกรอง หรือเพิ่มสินค้าใหม่ได้ทันที" traceId="คำขอสำเร็จและไม่พบรายการ" actionLabel={canManage ? 'เพิ่มสินค้า' : 'ล้างตัวกรอง'} onAction={() => canManage ? setFormId('new') : setFilters({ page: 1, pageSize: 25 })} />
  else content = <Card className="management-table-card"><DataTable sortable sortScope="page" data={query.data.items} columns={columns} onRowClick={canManage ? (row) => setFormId(row.id) : undefined} getRowLabel={(row) => `เปิดข้อมูลสินค้า ${row.name}`} /><Pagination page={query.data.page} totalPages={query.data.totalPages} totalItems={query.data.totalItems} onPageChange={(page) => setFilters((old) => ({ ...old, page }))} /></Card>

  return <AppShell title="สินค้า">
    <section className="page-heading"><div><p className="eyebrow">แคตตาล็อกสินค้าและบริการ</p><h2>จัดการสินค้า</h2><p>จัดการอะไหล่ ค่าแรง ราคา และจำนวนคงเหลือของสาขาปัจจุบัน</p></div><Button disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มสินค้าได้' : undefined} onClick={() => setFormId('new')}><Plus /> เพิ่มสินค้า</Button></section>
    {!canManage ? <Alert className="catalog-permission-note"><ShieldAlert /><div><AlertTitle>ดูข้อมูลได้อย่างเดียว</AlertTitle><AlertDescription>ต้นทุนและคำสั่งแก้ไขเปิดให้เฉพาะผู้จัดการสาขา</AlertDescription></div></Alert> : null}
    <Card className="management-filters management-filters--catalog">
      <div className="filter-search input-with-icon"><Search /><Input value={keyword} onChange={(event) => { setKeyword(event.target.value); setFilters((old) => ({ ...old, page: 1 })) }} placeholder="ค้นหารหัส ชื่อสินค้า หรือรุ่นรถ" /></div>
      <Select value={filters.type || ''} onChange={(event) => setFilters((old) => ({ ...old, type: (event.target.value || undefined) as CatalogFilters['type'], page: 1 }))}><option value="">ทุกประเภท</option><option value="part">อะไหล่</option><option value="labor">ค่าแรง</option></Select>
      <label className="filter-check"><input type="checkbox" checked={Boolean(filters.lowStockOnly)} onChange={(event) => setFilters((old) => ({ ...old, lowStockOnly: event.target.checked, page: 1 }))} /> เฉพาะของหมด/ถูกจองหมด</label>
      <label className="filter-check"><input type="checkbox" checked={Boolean(filters.includeInactive)} onChange={(event) => setFilters((old) => ({ ...old, includeInactive: event.target.checked, page: 1 }))} /> รวมรายการปิดใช้งาน</label>
    </Card>
    {content}
    <CatalogFormModal open={formId !== null} itemId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
    <ConfirmModal open={Boolean(statusTarget)} title={statusTarget?.isActive ? 'ปิดใช้งานสินค้า' : 'เปิดใช้งานสินค้า'} description="ข้อมูลจะไม่ถูกลบและสามารถเปิดใช้งานกลับมาได้" onClose={() => setStatusTarget(null)} size="small" footer={<><Button variant="ghost" onClick={() => setStatusTarget(null)}>ยกเลิก</Button><Button variant={statusTarget?.isActive ? 'destructive' : 'default'} disabled={status.isPending} onClick={() => statusTarget && status.mutate(statusTarget)}>{status.isPending ? 'กำลังบันทึก…' : statusTarget?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}</Button></>}>
      <p>สินค้า: <strong>{statusTarget?.code} · {statusTarget?.name}</strong></p>{status.isError ? <InlineError error={status.error} /> : null}
    </ConfirmModal>
  </AppShell>
}

export function CatalogFormModal({ open, itemId, onClose, initialName, onSuccess }: {
  open: boolean
  itemId: string | null
  onClose: () => void
  initialName?: string
  onSuccess?: (item: CatalogManagementItem) => void
}) {
  const queryClient = useQueryClient()
  const details = useQuery({ queryKey: ['catalog-management-item', itemId], queryFn: () => getCatalogItem(itemId!), enabled: open && Boolean(itemId) })
  const categoriesQuery = useQuery({ queryKey: ['catalog-categories', 'lookup-with-inactive'], queryFn: () => getCatalogCategories({ includeInactive: true }), enabled: open })
  const warehousesQuery = useQuery({ queryKey: ['warehouses', 'lookup-with-inactive'], queryFn: () => getWarehouses({ includeInactive: true }), enabled: open })
  const suppliersQuery = useQuery({ queryKey: ['suppliers', 'lookup-with-inactive'], queryFn: () => getSuppliers({ includeInactive: true, page: 1, pageSize: 200 }), enabled: open })
  const itemSuppliersQuery = useQuery({ queryKey: ['catalog-item-suppliers', itemId], queryFn: () => getCatalogItemSuppliers(itemId!), enabled: open && Boolean(itemId) })
  const { register, handleSubmit, reset, watch, formState: { errors } } = useForm<CatalogFormValues>({ resolver: zodResolver(catalogSchema), defaultValues: emptyForm })
  const type = watch('type')
  const selectedCategoryId = watch('categoryId')
  const selectedWarehouseId = watch('warehouseId')
  const [linkForm, setLinkForm] = useState({ supplierId: '', supplierItemCode: '', supplierCost: '0', leadTimeDays: '', minOrderQty: '', isPreferred: false, isActive: true })
  const [editingLink, setEditingLink] = useState<CatalogItemSupplier | null>(null)
  const [removeLinkTarget, setRemoveLinkTarget] = useState<CatalogItemSupplier | null>(null)
  const categories = useMemo(() => flattenCategories(categoriesQuery.data ?? []).filter((category) => !category.hasChildren && (category.isActive || category.id === selectedCategoryId)), [categoriesQuery.data, selectedCategoryId])
  const suppliers = useMemo(() => listResult<Supplier>(suppliersQuery.data).filter((supplier) => supplier.isActive || supplier.id === linkForm.supplierId), [linkForm.supplierId, suppliersQuery.data])
  const warehouses = useMemo(() => (warehousesQuery.data ?? []).filter((warehouse) => warehouse.isActive || warehouse.id === selectedWarehouseId), [selectedWarehouseId, warehousesQuery.data])
  useEffect(() => {
    if (!open) return
    if (!itemId) { reset(emptyForm); setEditingLink(null); setLinkForm({ supplierId: '', supplierItemCode: '', supplierCost: '0', leadTimeDays: '', minOrderQty: '', isPreferred: false, isActive: true }); return }
    const item = details.data
    if (!item) return
    reset({
      code: item.code, type: item.type, name: item.name, compatibility: item.compatibility || '', unit: item.unit,
      cost: String(item.cost ?? 0), price: String(item.price), standardHours: item.standardHours === null ? '' : String(item.standardHours),
      onHand: String(item.onHand), reserved: String(item.reserved), onOrder: String(item.onOrder), damaged: String(item.damaged), etaNote: item.etaNote || '',
      categoryId: item.categoryId || '', warehouseId: item.warehouseId || '',
    })
  }, [details.data, itemId, open, reset, initialName])
  const save = useMutation({
    mutationFn: (input: CatalogItemInput) => itemId ? updateCatalogItem(itemId, input) : createCatalogItem(input),
    onSuccess: (item) => {
      toast.success(itemId ? 'แก้ไขสินค้าเรียบร้อยแล้ว' : 'เพิ่มสินค้าเรียบร้อยแล้ว')
      void queryClient.invalidateQueries({ queryKey: ['catalog-management'] })
      void queryClient.invalidateQueries({ queryKey: ['catalog'] })
      onSuccess?.(item)
      onClose()
    },
  })
  const submit = handleSubmit((values) => save.mutate({
    code: values.code, type: values.type, name: values.name, compatibility: values.compatibility || undefined,
    unit: values.unit, cost: Number(values.cost), price: Number(values.price),
    standardHours: values.type === 'labor' ? Number(values.standardHours) : undefined,
    onHand: values.type === 'part' ? Number(values.onHand) : 0,
    reserved: values.type === 'part' ? Number(values.reserved) : 0,
    onOrder: values.type === 'part' ? Number(values.onOrder) : 0,
    damaged: values.type === 'part' ? Number(values.damaged) : 0,
    etaNote: values.type === 'part' ? values.etaNote || undefined : undefined,
    categoryId: values.categoryId || undefined,
    warehouseId: values.warehouseId || undefined,
  }))
  const linkMutation = useMutation({
    mutationFn: ({ supplierId, input }: { supplierId: string; input: CatalogItemSupplierInput }) => upsertCatalogItemSupplier(itemId!, supplierId, input),
    onSuccess: () => {
      toast.success(editingLink ? 'แก้ไขซัพพลายเออร์ของสินค้าแล้ว' : 'เพิ่มซัพพลายเออร์ของสินค้าแล้ว')
      void queryClient.invalidateQueries({ queryKey: ['catalog-item-suppliers', itemId] })
      setEditingLink(null)
      setLinkForm({ supplierId: '', supplierItemCode: '', supplierCost: '0', leadTimeDays: '', minOrderQty: '', isPreferred: false, isActive: true })
    },
  })
  const unlinkMutation = useMutation({
    mutationFn: (supplierId: string) => removeCatalogItemSupplier(itemId!, supplierId),
    onSuccess: () => {
      toast.success('ยกเลิกการผูกซัพพลายเออร์แล้ว')
      setRemoveLinkTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['catalog-item-suppliers', itemId] })
    },
  })
  const saveLink = () => {
    if (!itemId || !linkForm.supplierId) return
    linkMutation.mutate({
      supplierId: linkForm.supplierId,
      input: {
        supplierItemCode: linkForm.supplierItemCode.trim() || undefined,
        supplierCost: Number(linkForm.supplierCost || 0),
        leadTimeDays: linkForm.leadTimeDays === '' ? undefined : Number(linkForm.leadTimeDays),
        minOrderQty: linkForm.minOrderQty === '' ? undefined : Number(linkForm.minOrderQty),
        isPreferred: linkForm.isPreferred,
        isActive: linkForm.isActive,
      },
    })
  }
  const editLink = (link: CatalogItemSupplier) => {
    setEditingLink(link)
    setLinkForm({ supplierId: link.supplierId, supplierItemCode: link.supplierItemCode || '', supplierCost: link.supplierCost === null ? '0' : String(link.supplierCost), leadTimeDays: link.leadTimeDays === null ? '' : String(link.leadTimeDays), minOrderQty: link.minOrderQty === null ? '' : String(link.minOrderQty), isPreferred: link.isPreferred, isActive: link.isActive })
  }
  const close = () => { save.reset(); onClose() }

  return <ConfirmModal open={open} title={itemId ? 'แก้ไขสินค้า' : 'เพิ่มสินค้า'} description="ข้อมูลจะใช้ในแคตตาล็อกใบเสนอราคาของสาขาปัจจุบัน" onClose={close} size="large" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button type="submit" form="catalog-form" disabled={save.isPending || Boolean(itemId && details.isPending)}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button></>}>
    {itemId && details.isPending ? <SkeletonRows count={5} /> : details.isError ? <StateBlock variant="error" title="โหลดข้อมูลสินค้าไม่สำเร็จ" reason={errorMessage(details.error)} traceId={traceId(details.error)} actionLabel="ลองใหม่" onAction={() => void details.refetch()} /> : <form id="catalog-form" className="management-form catalog-form" onSubmit={submit}>
      <section><h3>ข้อมูลสินค้า</h3><div className="form-grid">
        <Field label="รหัสสินค้า *" error={errors.code?.message}><Input maxLength={60} className="catalog-code-input" {...register('code')} /></Field>
        <Field label="ประเภท *" error={errors.type?.message}><Select {...register('type')}><option value="part" disabled={details.data?.stockLocked && type !== 'part'}>อะไหล่</option><option value="labor" disabled={details.data?.stockLocked && type !== 'labor'}>ค่าแรง</option></Select></Field>
        <Field label="ชื่อสินค้า / บริการ *" error={errors.name?.message} wide><Input maxLength={300} {...register('name')} /></Field>
        <Field label="รุ่นรถที่รองรับ / รายละเอียด" error={errors.compatibility?.message} wide><Textarea rows={3} maxLength={500} {...register('compatibility')} /></Field>
        <Field label="หน่วยนับ *" error={errors.unit?.message}><Input readOnly={details.data?.stockLocked} maxLength={40} placeholder={type === 'labor' ? 'งาน' : 'ชิ้น / ชุด / ลิตร'} {...register('unit')} /></Field>
        <Field label="หมวดหมู่สินค้า" error={errors.categoryId?.message}><Select {...register('categoryId')} disabled={categoriesQuery.isPending}><option value="">ไม่ระบุหมวดหมู่</option>{categories.map((category) => <option key={category.id} value={category.id}>{'— '.repeat(category.level)}{category.name}{category.isActive ? '' : ' (ปิดใช้งาน)'}</option>)}</Select>{categoriesQuery.isError ? <small className="field-hint">โหลดหมวดหมู่ไม่สำเร็จ</small> : null}</Field>
        <Field label="คลังหลัก" error={errors.warehouseId?.message}><Select {...register('warehouseId')} disabled={warehousesQuery.isPending}><option value="">ไม่ระบุคลัง</option>{warehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.name} ({warehouse.code}){warehouse.isActive ? '' : ' (ปิดใช้งาน)'}</option>)}</Select>{warehousesQuery.isError ? <small className="field-hint">โหลดคลังไม่สำเร็จ</small> : null}</Field>
        {type === 'labor' ? <Field label="ชั่วโมงมาตรฐาน *" error={errors.standardHours?.message}><Input className="money" type="number" min="0.01" max="9999.99" step="0.01" {...register('standardHours')} /></Field> : null}
      </div></section>
      <section><h3>ราคา</h3><div className="form-grid">
        <Field label="ต้นทุน (บาท) *" error={errors.cost?.message}><Input className="money" type="number" min="0" step="0.01" {...register('cost')} /></Field>
        <Field label="ราคาขาย (บาท) *" error={errors.price?.message}><Input className="money" type="number" min="0" step="0.01" {...register('price')} /></Field>
      </div></section>
      {type === 'part' ? <section><h3>สต็อกอะไหล่</h3><div className="form-grid catalog-stock-grid">
        {details.data?.stockLocked && <p className="field--wide section-help">ยอดนี้ดูแลผ่านโมดูลจัดซื้อและสต็อก FIFO แล้ว จึงไม่สามารถแก้จำนวนหรือหน่วยนับที่นี่ได้</p>}
        <Field label="คงคลัง" error={errors.onHand?.message}><Input readOnly={details.data?.stockLocked} type="number" min="0" step="1" {...register('onHand')} /></Field>
        <Field label="จองแล้ว" error={errors.reserved?.message}><Input readOnly={details.data?.stockLocked} type="number" min="0" step="1" {...register('reserved')} /></Field>
        <Field label="กำลังสั่งซื้อ" error={errors.onOrder?.message}><Input readOnly={details.data?.stockLocked} type="number" min="0" step="1" {...register('onOrder')} /></Field>
        <Field label="ชำรุด" error={errors.damaged?.message}><Input readOnly={details.data?.stockLocked} type="number" min="0" step="1" {...register('damaged')} /></Field>
        <Field label="หมายเหตุกำหนดรับสินค้า" error={errors.etaNote?.message} wide><Input maxLength={200} placeholder="เช่น สั่งได้ภายใน 2 วัน" {...register('etaNote')} /></Field>
      </div><p className="stock-formula"><Boxes /> จำนวนพร้อมใช้คำนวณจาก “คงคลัง − จองแล้ว” โดยไม่รวมกำลังสั่งซื้อและของชำรุด</p></section> : null}
      <section><div className="section-heading-row"><div><h3>ซัพพลายเออร์ของสินค้า</h3><p className="section-help">กำหนดรหัสสินค้า ต้นทุน และเงื่อนไขสั่งซื้อแยกตามซัพพลายเออร์</p></div>{itemId ? <Badge variant="outline">{itemSuppliersQuery.data?.length ?? 0} ราย</Badge> : null}</div>
        {!itemId ? <p className="section-help">บันทึกสินค้าให้เรียบร้อยก่อน จึงจะเพิ่มซัพพลายเออร์ได้</p> : <>
          <div className="supplier-link-form form-grid">
            <Field label="ซัพพลายเออร์ *"><Select value={linkForm.supplierId} onChange={(event) => setLinkForm((old) => ({ ...old, supplierId: event.target.value }))} disabled={suppliersQuery.isPending}><option value="">เลือกซัพพลายเออร์</option>{suppliers.map((supplier) => <option key={supplier.id} value={supplier.id}>{supplier.name} ({supplier.code})</option>)}</Select></Field>
            <Field label="รหัสสินค้าซัพพลายเออร์"><Input value={linkForm.supplierItemCode} maxLength={60} onChange={(event) => setLinkForm((old) => ({ ...old, supplierItemCode: event.target.value }))} /></Field>
            <Field label="ต้นทุนจากรายนี้ (บาท) *"><Input className="money" type="number" min="0" step="0.01" value={linkForm.supplierCost} onChange={(event) => setLinkForm((old) => ({ ...old, supplierCost: event.target.value }))} /></Field>
            <Field label="ระยะเวลาส่ง (วัน)"><Input type="number" min="0" step="1" value={linkForm.leadTimeDays} onChange={(event) => setLinkForm((old) => ({ ...old, leadTimeDays: event.target.value }))} /></Field>
            <Field label="ขั้นต่ำต่อครั้ง"><Input type="number" min="0" step="1" value={linkForm.minOrderQty} onChange={(event) => setLinkForm((old) => ({ ...old, minOrderQty: event.target.value }))} /></Field>
            <div className="supplier-link-flags"><label className="filter-check"><input type="checkbox" checked={linkForm.isPreferred} onChange={(event) => setLinkForm((old) => ({ ...old, isPreferred: event.target.checked }))} /> ซัพพลายเออร์หลัก</label><label className="filter-check"><input type="checkbox" checked={linkForm.isActive} onChange={(event) => setLinkForm((old) => ({ ...old, isActive: event.target.checked }))} /> ใช้งาน</label></div>
            <div className="supplier-link-actions"><Button type="button" size="sm" title={!linkForm.supplierId ? 'เลือกซัพพลายเออร์ก่อน' : linkForm.supplierCost.trim() === '' ? 'กรอกต้นทุนก่อน' : undefined} onClick={saveLink} disabled={!linkForm.supplierId || linkForm.supplierCost.trim() === '' || linkMutation.isPending}>{linkMutation.isPending ? 'กำลังบันทึก…' : editingLink ? 'บันทึกการแก้ไข' : 'เพิ่มซัพพลายเออร์'}</Button>{editingLink ? <Button type="button" size="sm" variant="ghost" onClick={() => { setEditingLink(null); setLinkForm({ supplierId: '', supplierItemCode: '', supplierCost: '0', leadTimeDays: '', minOrderQty: '', isPreferred: false, isActive: true }) }}>ยกเลิกแก้ไข</Button> : null}</div>
          </div>
          {suppliersQuery.isError ? <InlineError error={suppliersQuery.error} /> : null}
          {linkMutation.isError ? <InlineError error={linkMutation.error} /> : null}
          {itemSuppliersQuery.isPending ? <SkeletonRows count={2} /> : itemSuppliersQuery.isError ? <InlineError error={itemSuppliersQuery.error} /> : itemSuppliersQuery.data?.length ? <div className="supplier-link-table-wrap management-table-card"><ManagementTable data={itemSuppliersQuery.data} columns={[
        { id: 'supplier', header: 'ซัพพลายเออร์', value: (link) => link.supplierName, size: 250, render: (link) => <div className="two-line-cell"><strong>{link.supplierName}</strong><span>{link.supplierCode}</span></div> },
        { id: 'code', header: 'รหัสภายนอก', value: (link) => link.supplierItemCode, render: (link) => <>{link.supplierItemCode || '—'}</> },
        { id: 'cost', header: 'ต้นทุน', value: (link) => link.supplierCost, render: (link) => <><Money value={link.supplierCost} /></> },
        { id: 'lead', header: 'ส่ง/ขั้นต่ำ', value: (link) => link.leadTimeDays, render: (link) => <>{link.leadTimeDays ?? '—'} วัน / {link.minOrderQty ?? '—'}</> },
        { id: 'status', header: 'สถานะ', value: (link) => link.isPreferred ? 'หลัก' : link.isActive ? 'ใช้งาน' : 'ปิดใช้', render: (link) => <>{link.isPreferred ? <Badge className="active-badge">หลัก</Badge> : null} {link.isActive ? <Badge variant="outline">ใช้งาน</Badge> : <Badge variant="outline"><CircleOff /> ปิดใช้</Badge>}</> },
        { id: 'actions', header: '', render: (link) => <><div className="row-actions"><Button type="button" size="icon" variant="ghost" title="แก้ไขการผูก" onClick={() => editLink(link)}><Pencil /></Button><Button type="button" size="icon" variant="ghost" title="ยกเลิกการผูก" onClick={() => setRemoveLinkTarget(link)}><Trash2 /></Button></div></> },
      ]} /></div> : <p className="section-help">ยังไม่ได้ผูกซัพพลายเออร์</p>}
        </>}
      </section>
      {save.isError ? <InlineError error={save.error} /> : null}
    </form>}
    <ConfirmModal open={Boolean(removeLinkTarget)} title="ยกเลิกการผูกซัพพลายเออร์" description="ข้อมูลการผูกจะถูกลบออกจากสินค้านี้" onClose={() => setRemoveLinkTarget(null)} size="small" footer={<><Button variant="ghost" onClick={() => setRemoveLinkTarget(null)}>ยกเลิก</Button><Button variant="destructive" disabled={unlinkMutation.isPending} onClick={() => removeLinkTarget && unlinkMutation.mutate(removeLinkTarget.supplierId)}>{unlinkMutation.isPending ? 'กำลังลบ…' : 'ยืนยันลบ'}</Button></>}>
      <p>ซัพพลายเออร์: <strong>{removeLinkTarget?.supplierName}</strong></p>{unlinkMutation.isError ? <InlineError error={unlinkMutation.error} /> : null}
    </ConfirmModal>
  </ConfirmModal>
}

function Field({ label, error, wide, children }: { label: string; error?: string; wide?: boolean; children: React.ReactNode }) {
  return <Label className={`field ${wide ? 'field--wide' : ''}`}><span>{label}</span>{children}{error ? <small className="field-error">{error}</small> : null}</Label>
}

function InlineError({ error }: { error: unknown }) {
  return <Alert variant="destructive"><AlertTriangle /><div><AlertTitle>{errorMessage(error)}</AlertTitle><AlertDescription>รหัสติดตาม (traceId): {traceId(error) || 'ไม่พบรหัสติดตาม'}</AlertDescription></div></Alert>
}

function errorMessage(error: unknown) { return isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ' }
function traceId(error: unknown) { return isApiError(error) ? error.traceId : undefined }
