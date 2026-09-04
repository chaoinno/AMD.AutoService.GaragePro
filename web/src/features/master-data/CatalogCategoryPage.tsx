import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronDown, ChevronRight, CircleOff as ToggleLeft, FolderTree, Pencil, Plus, Search, WandSparkles } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { toast } from 'sonner'
import { createCatalogCategory, getCatalogCategories, getCatalogCategory, setCatalogCategoryStatus, updateCatalogCategory } from '../../api/masterData'
import type { CatalogCategory, CatalogCategoryInput } from '../../api/types'
import { DataTable } from '../../components/DataTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { useSession } from '../../lib/session'
import { Field, InlineError, PermissionNote, QueryState, StatusBadge } from './MasterDataCommon'

const emptyForm: CatalogCategoryInput = { code: '', name: '', sortOrder: 0 }

export function CatalogCategoryPage() {
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const [keyword, setKeyword] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [formId, setFormId] = useState<string | 'new' | null>(null)
  const [parentForNew, setParentForNew] = useState<string | undefined>()
  const [statusTarget, setStatusTarget] = useState<CatalogCategory | null>(null)
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['catalog-categories', keyword, includeInactive], queryFn: () => getCatalogCategories({ keyword: keyword || undefined, includeInactive }) })
  const categories = useMemo(() => query.data ?? [], [query.data])
  const flat = useMemo(() => flatten(categories), [categories])
  const status = useMutation({ mutationFn: (target: CatalogCategory) => setCatalogCategoryStatus(target.id, !target.isActive), onSuccess: () => { toast.success(statusTarget?.isActive ? 'ปิดใช้งานหมวดหมู่แล้ว' : 'เปิดใช้งานหมวดหมู่แล้ว'); setStatusTarget(null); void queryClient.invalidateQueries({ queryKey: ['catalog-categories'] }) } })

  const openNew = (parentId?: string) => { setParentForNew(parentId); setFormId('new') }

  return <AppShell title="หมวดหมู่สินค้า">
    <section className="page-heading"><div><p className="eyebrow">ข้อมูลหลัก / แคตตาล็อก</p><h2>จัดการหมวดหมู่สินค้า</h2><p>จัดหมวดหมู่แบบลำดับชั้น และเลือกใช้กับสินค้าได้เฉพาะหมวดปลายทาง</p></div><Button disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มหมวดหมู่ได้' : undefined} onClick={() => openNew()}><Plus aria-hidden="true" /> เพิ่มหมวดหมู่หลัก</Button></section>
    <PermissionNote canManage={canManage} />
    <Card className="management-filters master-filters"><div className="filter-search input-with-icon"><Search aria-hidden="true" /><Input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="ค้นหารหัสหรือชื่อหมวดหมู่" aria-label="ค้นหาหมวดหมู่" /></div><label className="filter-check"><input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} /> รวมรายการปิดใช้งาน</label></Card>
    <QueryState query={query} loadingTitle="กำลังโหลดหมวดหมู่" emptyTitle="ยังไม่มีหมวดหมู่สินค้า" emptyReason="เพิ่มหมวดหมู่หลักเพื่อเริ่มสร้างโครงสร้างสินค้า" onRetry={() => void query.refetch()}>
      <Card className="management-table-card"><p className="data-table__scope">เรียงหมวดหมู่ในระดับเดียวกัน โดยคงหมวดแม่และหมวดย่อยไว้ด้วยกัน</p><DataTable<CatalogCategory> sortable data={categories} getRowId={item => item.id} getSubRows={item => item.children} columns={[
        { id: 'name', header: 'หมวดหมู่', accessorFn: item => item.name, size: 380, cell: ({ row }) => <div className="category-table-name" style={{ paddingLeft: row.depth * 28 }}><button type="button" className="category-expand" disabled={!row.getCanExpand()} aria-label={`${row.getIsExpanded() ? 'ยุบ' : 'ขยาย'} ${row.original.name}`} aria-expanded={row.getCanExpand() ? row.getIsExpanded() : undefined} onClick={row.getToggleExpandedHandler()}>{row.getCanExpand() ? row.getIsExpanded() ? <ChevronDown /> : <ChevronRight /> : <span className="category-leaf-mark" />}</button><FolderTree className="category-icon" aria-hidden="true" /><strong>{row.original.name}</strong></div> },
        { id: 'code', header: 'รหัส', accessorFn: item => item.code, size: 160 },
        { id: 'order', header: 'ลำดับ', accessorFn: item => item.sortOrder ?? 0, size: 110 },
        { id: 'status', header: 'สถานะ', accessorFn: item => item.isActive ? 'ใช้งาน' : 'ปิดใช้งาน', size: 150, cell: ({ row }) => <StatusBadge isActive={row.original.isActive} /> },
        { id: 'actions', header: '', enableSorting: false, size: 150, cell: ({ row }) => { const item = row.original; const cannotDisable = item.isActive && item.hasChildren; return <div className="row-actions"><Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขได้' : 'เพิ่มหมวดย่อย'} aria-label={`เพิ่มหมวดย่อยใต้ ${item.name}`} onClick={() => openNew(item.id)}><Plus /></Button><Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขได้' : 'แก้ไขหมวดหมู่'} aria-label={`แก้ไข ${item.name}`} onClick={() => setFormId(item.id)}><Pencil /></Button><Button size="icon" variant="ghost" disabled={!canManage || cannotDisable} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เปลี่ยนสถานะได้' : cannotDisable ? 'ต้องปิดใช้งานหมวดย่อยทั้งหมดก่อน' : item.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} aria-label={`${item.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${item.name}`} onClick={() => setStatusTarget(item)}><ToggleLeft /></Button></div> } },
      ]} /></Card>
    </QueryState>
    <CatalogCategoryFormModal open={formId !== null} categoryId={formId === 'new' ? null : formId} initialParentId={parentForNew} categories={flat} onClose={() => setFormId(null)} />
    <ConfirmModal open={Boolean(statusTarget)} title={statusTarget?.isActive ? 'ปิดใช้งานหมวดหมู่' : 'เปิดใช้งานหมวดหมู่'} description="ระบบจะตรวจสอบลำดับชั้นก่อนเปลี่ยนสถานะ" onClose={() => { setStatusTarget(null); status.reset() }} footer={<><Button variant="ghost" onClick={() => setStatusTarget(null)}>ยกเลิก</Button><Button variant={statusTarget?.isActive ? 'destructive' : 'default'} disabled={status.isPending || Boolean(statusTarget?.isActive && statusTarget.hasChildren)} title={statusTarget?.hasChildren ? 'ต้องปิดใช้งานหมวดย่อยทั้งหมดก่อน' : undefined} onClick={() => statusTarget && status.mutate(statusTarget)}>{status.isPending ? 'กำลังบันทึก…' : statusTarget?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}</Button></>}>{statusTarget?.hasChildren && statusTarget.isActive ? <p className="disabled-reason">หมวดนี้ยังมีหมวดย่อยที่ใช้งานอยู่ ต้องปิดใช้งานหมวดย่อยทั้งหมดก่อน</p> : null}{status.isError ? <InlineError error={status.error} /> : null}<p>หมวดหมู่: <strong>{statusTarget?.code} · {statusTarget?.name}</strong></p></ConfirmModal>
  </AppShell>
}

function CatalogCategoryFormModal({ open, categoryId, initialParentId, categories, onClose }: { open: boolean; categoryId: string | null; initialParentId?: string; categories: CatalogCategory[]; onClose: () => void }) {
  const [form, setForm] = useState<CatalogCategoryInput>(emptyForm)
  const [errors, setErrors] = useState<Partial<Record<keyof CatalogCategoryInput, string>>>({})
  const details = useQuery({ queryKey: ['catalog-category', categoryId], queryFn: () => getCatalogCategory(categoryId!), enabled: open && Boolean(categoryId) })
  const categoriesForCode = useQuery({ queryKey: ['catalog-categories', 'code-lookup'], queryFn: () => getCatalogCategories({ includeInactive: true }), enabled: open && !categoryId })
  const queryClient = useQueryClient()
  useEffect(() => { if (!open) return; setErrors({}); if (!categoryId) setForm({ ...emptyForm, parentCategoryId: initialParentId }); else if (details.data) setForm({ code: details.data.code, name: details.data.name, parentCategoryId: details.data.parentCategoryId || undefined, sortOrder: details.data.sortOrder ?? 0 }) }, [categoryId, details.data, initialParentId, open])
  const save = useMutation({ mutationFn: (input: CatalogCategoryInput) => categoryId ? updateCatalogCategory(categoryId, input) : createCatalogCategory(input), onSuccess: (saved) => { toast.success(categoryId ? 'แก้ไขหมวดหมู่เรียบร้อยแล้ว' : 'เพิ่มหมวดหมู่เรียบร้อยแล้ว'); void queryClient.invalidateQueries({ queryKey: ['catalog-categories'] }); if (categoryId) queryClient.setQueryData(['catalog-category', categoryId], saved); onClose() } })
  const set = (key: keyof CatalogCategoryInput, value: string) => setForm((old) => ({ ...old, [key]: key === 'sortOrder' ? (value === '' ? undefined : Number(value)) : value }))
  const descendantIds = useMemo(() => categoryId ? collectDescendants(categoryId, categories) : new Set<string>(), [categoryId, categories])
  const submit = () => { const next: typeof errors = {}; if (!form.code?.trim()) next.code = 'กรุณากรอกรหัสหมวดหมู่'; else if (form.code.trim().length > 30) next.code = 'รหัสต้องยาวไม่เกิน 30 ตัวอักษร'; if (!form.name?.trim()) next.name = 'กรุณากรอกชื่อหมวดหมู่'; else if (form.name.trim().length > 200) next.name = 'ชื่อต้องยาวไม่เกิน 200 ตัวอักษร'; if (form.parentCategoryId && (form.parentCategoryId === categoryId || descendantIds.has(form.parentCategoryId))) next.parentCategoryId = 'ห้ามเลือกตัวเองหรือหมวดย่อยของตัวเองเป็นหมวดแม่'; if (typeof form.sortOrder === 'number' && form.sortOrder < 0) next.sortOrder = 'ลำดับต้องไม่น้อยกว่า 0'; setErrors(next); if (!Object.keys(next).length) save.mutate({ code: form.code.trim().toUpperCase(), name: form.name.trim(), parentCategoryId: form.parentCategoryId || undefined, sortOrder: form.sortOrder }) }
  const generateCode = () => { const used = new Set(flatten(categoriesForCode.data ?? categories).map((item) => item.code.trim().toUpperCase())); let sequence = 1; let code = `CAT-${String(sequence).padStart(3, '0')}`; while (used.has(code)) { sequence += 1; code = `CAT-${String(sequence).padStart(3, '0')}` } set('code', code); setErrors((old) => ({ ...old, code: undefined })) }
  const close = () => { save.reset(); onClose() }
  return <ConfirmModal open={open} title={categoryId ? 'แก้ไขหมวดหมู่' : form.parentCategoryId ? 'เพิ่มหมวดย่อย' : 'เพิ่มหมวดหมู่หลัก'} description="กำหนดลำดับชั้นและลำดับการแสดงผลของแคตตาล็อก" onClose={close} size="medium" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button onClick={submit} disabled={save.isPending || Boolean(categoryId && details.isPending)} title={categoryId && details.isPending ? 'กำลังโหลดข้อมูลเดิม' : undefined}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button></>}>{details.isError ? <InlineError error={details.error} /> : <form className="management-form master-form" onSubmit={(e) => { e.preventDefault(); submit() }}><section><h3>ข้อมูลหมวดหมู่</h3><div className="form-grid"><Field label="รหัสหมวดหมู่ *" error={errors.code}><div className="field-input-with-action"><Input value={form.code} maxLength={30} onChange={(e) => set('code', e.target.value)} /><Button type="button" size="sm" variant="secondary" onClick={generateCode} disabled={Boolean(categoryId) || categoriesForCode.isPending} title={categoryId ? 'สร้างรหัสอัตโนมัติเฉพาะหมวดหมู่ใหม่' : categoriesForCode.isPending ? 'กำลังตรวจสอบรหัสที่มีอยู่' : 'สร้างรหัสหมวดหมู่ที่ยังไม่ซ้ำ'}><WandSparkles aria-hidden="true" /> สร้างรหัส</Button></div></Field><Field label="ชื่อหมวดหมู่ *" error={errors.name}><Input value={form.name} maxLength={200} onChange={(e) => set('name', e.target.value)} /></Field><Field label="หมวดแม่" error={errors.parentCategoryId} wide><Select value={form.parentCategoryId || ''} onChange={(e) => set('parentCategoryId', e.target.value)}><option value="">ไม่มี (หมวดหมู่หลัก)</option>{categories.filter((item) => item.id !== categoryId && !descendantIds.has(item.id)).map((item) => <option key={item.id} value={item.id}>{'　'.repeat(itemDepth(item, categories))}{item.code} · {item.name}</option>)}</Select><small className="field-hint">ตัวเลือกที่เป็นตัวเองหรือหมวดย่อยจะไม่แสดง</small></Field><Field label="ลำดับการแสดงผล" error={errors.sortOrder}><Input type="number" min="0" step="1" value={form.sortOrder ?? ''} onChange={(e) => set('sortOrder', e.target.value)} /></Field></div></section>{save.isError ? <InlineError error={save.error} /> : null}</form>}</ConfirmModal>
}

function flatten(items: CatalogCategory[]): CatalogCategory[] { return items.flatMap((item) => [item, ...(item.children ? flatten(item.children) : [])]) }
function collectDescendants(id: string, categories: CatalogCategory[]): Set<string> { const result = new Set<string>(); const visit = (parent: string) => categories.filter((x) => x.parentCategoryId === parent).forEach((x) => { result.add(x.id); visit(x.id) }); visit(id); return result }
function itemDepth(item: CatalogCategory, categories: CatalogCategory[]): number { let depth = 0; let parent: string | null | undefined = item.parentCategoryId; const ids = new Set<string>(); while (parent && !ids.has(parent)) { ids.add(parent); depth++; parent = categories.find((x) => x.id === parent)?.parentCategoryId } return depth }
