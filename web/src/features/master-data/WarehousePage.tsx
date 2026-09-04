import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, CircleOff as ToggleLeft, MapPin, Pencil, Plus, Search, WandSparkles } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { toast } from 'sonner'
import { createWarehouse, getWarehouse, getWarehouses, setWarehouseStatus, updateWarehouse } from '../../api/masterData'
import type { Warehouse, WarehouseInput } from '../../api/types'
import { ManagementTable } from '../../components/ManagementTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Textarea } from '../../components/ui/textarea'
import { useSession } from '../../lib/session'
import { Field, InlineError, PermissionNote, QueryState, StatusBadge } from './MasterDataCommon'

const emptyForm: WarehouseInput = { code: '', name: '', address: '' }

export function WarehousePage() {
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const [keyword, setKeyword] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [formId, setFormId] = useState<string | 'new' | null>(null)
  const [statusTarget, setStatusTarget] = useState<Warehouse | null>(null)
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['warehouses', keyword, includeInactive], queryFn: () => getWarehouses({ keyword: keyword || undefined, includeInactive }) })
  const warehouses = useMemo(() => query.data ?? [], [query.data])
  const status = useMutation({ mutationFn: (target: Warehouse) => setWarehouseStatus(target.id, !target.isActive), onSuccess: () => { toast.success(statusTarget?.isActive ? 'ปิดใช้งานคลังแล้ว' : 'เปิดใช้งานคลังแล้ว'); setStatusTarget(null); void queryClient.invalidateQueries({ queryKey: ['warehouses'] }) } })

  return <AppShell title="คลัง">
    <section className="page-heading"><div><p className="eyebrow">ข้อมูลหลัก / สต็อก</p><h2>จัดการคลัง</h2><p>กำหนดคลังหลักและตำแหน่งจัดเก็บของสาขา โดยจำนวนสต็อกยังอยู่ที่สินค้า</p></div><Button disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มคลังได้' : undefined} onClick={() => setFormId('new')}><Plus aria-hidden="true" /> เพิ่มคลัง</Button></section>
    <PermissionNote canManage={canManage} />
    <Card className="management-filters master-filters"><div className="filter-search input-with-icon"><Search aria-hidden="true" /><Input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="ค้นหารหัสหรือชื่อคลัง" aria-label="ค้นหาคลัง" /></div><label className="filter-check"><input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} /> รวมรายการปิดใช้งาน</label></Card>
    <QueryState query={query} loadingTitle="กำลังโหลดคลัง" emptyTitle="ยังไม่มีคลังในสาขาปัจจุบัน" emptyReason="เพิ่มคลังหลักเพื่อระบุตำแหน่งจัดเก็บสินค้า" onRetry={() => void query.refetch()}>
      <Card className="management-table-card"><ManagementTable data={warehouses} columns={[
        { id: 'warehouse', header: 'คลัง', value: (item) => item.name, size: 250, render: (item) => <><div className="master-name"><strong>{item.name}</strong><small>{item.code}</small></div></> },
        { id: 'branch', header: 'สาขา', value: (item) => item.legacyBranchId === session?.branchId ? session.branchName : String(item.legacyBranchId ?? ''), render: (item) => <><span className="master-branch"><Building2 /> {item.legacyBranchId === session?.branchId ? session.branchName : `สาขา ${item.legacyBranchId ?? 'ไม่ระบุ'}`}</span></> },
        { id: 'address', header: 'ที่อยู่', value: (item) => item.address, size: 320, render: (item) => <>{item.address ? <span className="master-contact"><span><MapPin /> {item.address}</span></span> : <span className="muted">ไม่ระบุที่อยู่</span>}</> },
        { id: 'status', header: 'สถานะ', value: (item) => item.isActive ? 'ใช้งาน' : 'ปิดใช้งาน', render: (item) => <><StatusBadge isActive={item.isActive} /></> },
        { id: 'actions', header: '', size: 110, render: (item) => <><div className="row-actions"><Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขได้' : 'แก้ไขคลัง'} aria-label={`แก้ไข ${item.name}`} onClick={() => setFormId(item.id)}><Pencil /></Button><Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เปลี่ยนสถานะได้' : item.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} aria-label={`${item.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${item.name}`} onClick={() => setStatusTarget(item)}><ToggleLeft /></Button></div></> },
      ]} /></Card>
    </QueryState>
    <WarehouseFormModal open={formId !== null} warehouseId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
    <ConfirmModal open={Boolean(statusTarget)} title={statusTarget?.isActive ? 'ปิดใช้งานคลัง' : 'เปิดใช้งานคลัง'} description="ข้อมูลจะไม่ถูกลบและสามารถเปิดใช้งานกลับมาได้" onClose={() => { setStatusTarget(null); status.reset() }} footer={<><Button variant="ghost" onClick={() => setStatusTarget(null)}>ยกเลิก</Button><Button variant={statusTarget?.isActive ? 'destructive' : 'default'} disabled={status.isPending} onClick={() => statusTarget && status.mutate(statusTarget)}>{status.isPending ? 'กำลังบันทึก…' : statusTarget?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}</Button></>}>{status.isError ? <InlineError error={status.error} /> : null}<p>คลัง: <strong>{statusTarget?.code} · {statusTarget?.name}</strong></p></ConfirmModal>
  </AppShell>
}

function WarehouseFormModal({ open, warehouseId, onClose }: { open: boolean; warehouseId: string | null; onClose: () => void }) {
  const [form, setForm] = useState<WarehouseInput>(emptyForm)
  const [errors, setErrors] = useState<Partial<Record<keyof WarehouseInput, string>>>({})
  const details = useQuery({ queryKey: ['warehouse', warehouseId], queryFn: () => getWarehouse(warehouseId!), enabled: open && Boolean(warehouseId) })
  const warehousesQuery = useQuery({ queryKey: ['warehouses', 'code-lookup'], queryFn: () => getWarehouses({ includeInactive: true }), enabled: open && !warehouseId })
  const queryClient = useQueryClient()
  useEffect(() => { if (!open) return; setErrors({}); if (!warehouseId) setForm({ ...emptyForm }) }, [open, warehouseId])
  useEffect(() => { if (open && warehouseId && details.data) setForm({ code: details.data.code, name: details.data.name, address: details.data.address || '' }) }, [details.data, open, warehouseId])
  const save = useMutation({ mutationFn: (input: WarehouseInput) => warehouseId ? updateWarehouse(warehouseId, input) : createWarehouse(input), onSuccess: (saved) => { toast.success(warehouseId ? 'แก้ไขคลังเรียบร้อยแล้ว' : 'เพิ่มคลังเรียบร้อยแล้ว'); void queryClient.invalidateQueries({ queryKey: ['warehouses'] }); if (warehouseId) queryClient.setQueryData(['warehouse', warehouseId], saved); onClose() } })
  const set = (key: keyof WarehouseInput, value: string) => setForm((old) => ({ ...old, [key]: value }))
  const submit = () => { const next: typeof errors = {}; if (!form.code.trim()) next.code = 'กรุณากรอกรหัสคลัง'; else if (form.code.trim().length > 30) next.code = 'รหัสต้องยาวไม่เกิน 30 ตัวอักษร'; if (!form.name.trim()) next.name = 'กรุณากรอกชื่อคลัง'; else if (form.name.trim().length > 200) next.name = 'ชื่อต้องยาวไม่เกิน 200 ตัวอักษร'; setErrors(next); if (!Object.keys(next).length) save.mutate({ ...form, code: form.code.trim().toUpperCase(), name: form.name.trim() }) }
  const generateCode = () => { const used = new Set((warehousesQuery.data ?? []).map((warehouse) => warehouse.code.trim().toUpperCase())); let sequence = 1; let code = `WH-${String(sequence).padStart(3, '0')}`; while (used.has(code)) { sequence += 1; code = `WH-${String(sequence).padStart(3, '0')}` } set('code', code); setErrors((old) => ({ ...old, code: undefined })) }
  const close = () => { save.reset(); onClose() }
  return <ConfirmModal open={open} title={warehouseId ? 'แก้ไขคลัง' : 'เพิ่มคลัง'} description="กรอกข้อมูลคลังสำหรับสาขาปัจจุบัน" onClose={close} size="medium" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button onClick={submit} disabled={save.isPending || Boolean(warehouseId && details.isPending)} title={warehouseId && details.isPending ? 'กำลังโหลดข้อมูลเดิม' : undefined}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button></>}>{details.isError ? <InlineError error={details.error} /> : <form className="management-form master-form" onSubmit={(e) => { e.preventDefault(); submit() }}><section><h3>ข้อมูลคลัง</h3><div className="form-grid"><Field label="รหัสคลัง *" error={errors.code}><div className="field-input-with-action"><Input value={form.code} maxLength={30} onChange={(e) => set('code', e.target.value)} /><Button type="button" size="sm" variant="secondary" onClick={generateCode} disabled={Boolean(warehouseId) || warehousesQuery.isPending} title={warehouseId ? 'สร้างรหัสอัตโนมัติเฉพาะคลังใหม่' : warehousesQuery.isPending ? 'กำลังตรวจสอบรหัสที่มีอยู่' : 'สร้างรหัสคลังที่ยังไม่ซ้ำ'}><WandSparkles aria-hidden="true" /> สร้างรหัส</Button></div></Field><Field label="ชื่อคลัง *" error={errors.name}><Input value={form.name} maxLength={200} onChange={(e) => set('name', e.target.value)} /></Field><Field label="ที่อยู่" wide><Textarea rows={3} value={form.address || ''} maxLength={500} onChange={(e) => set('address', e.target.value)} /></Field></div></section>{save.isError ? <InlineError error={save.error} /> : null}</form>}</ConfirmModal>
}
