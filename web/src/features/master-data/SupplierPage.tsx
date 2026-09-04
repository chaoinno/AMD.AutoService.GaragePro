import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleOff as ToggleLeft, Mail, Pencil, Phone, Plus, Search, UserRound, WandSparkles } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { toast } from 'sonner'
import { getSupplier, getSuppliers, createSupplier, setSupplierStatus, updateSupplier } from '../../api/masterData'
import type { Supplier, SupplierInput } from '../../api/types'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Textarea } from '../../components/ui/textarea'
import { Select } from '../../components/ui/select'
import { useSession } from '../../lib/session'
import { Field, InlineError, PermissionNote, QueryState, StatusBadge } from './MasterDataCommon'

const emptyForm: SupplierInput = { code: '', name: '', contactName: '', phone: '', email: '', address: '', taxId: '', paymentTerms: '', note: '' }

export function SupplierPage() {
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const [keyword, setKeyword] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('active')
  const [formId, setFormId] = useState<string | 'new' | null>(null)
  const [statusTarget, setStatusTarget] = useState<Supplier | null>(null)
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: ['suppliers', keyword, status],
    queryFn: () => getSuppliers({ keyword: keyword || undefined, includeInactive: status !== 'active', page: 1, pageSize: 50 }),
  })
  const suppliers = useMemo(() => {
    const data = query.data
    const items = Array.isArray(data) ? data : data?.items ?? []
    return items.filter((x) => status === 'all' || (status === 'active' ? x.isActive : !x.isActive))
  }, [query.data, status])
  const statusMutation = useMutation({
    mutationFn: (target: Supplier) => setSupplierStatus(target.id, !target.isActive),
    onSuccess: () => { toast.success(statusTarget?.isActive ? 'ปิดใช้งานซัพพลายเออร์แล้ว' : 'เปิดใช้งานซัพพลายเออร์แล้ว'); setStatusTarget(null); void queryClient.invalidateQueries({ queryKey: ['suppliers'] }) },
  })

  return <AppShell title="ซัพพลายเออร์">
    <section className="page-heading"><div><p className="eyebrow">ข้อมูลหลัก / จัดซื้อ</p><h2>จัดการซัพพลายเออร์</h2><p>รายชื่อผู้จำหน่ายอะไหล่และบริการสำหรับสาขาปัจจุบัน</p></div><Button disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มซัพพลายเออร์ได้' : undefined} onClick={() => setFormId('new')}><Plus aria-hidden="true" /> เพิ่มซัพพลายเออร์</Button></section>
    <PermissionNote canManage={canManage} />
    <Card className="management-filters master-filters"><div className="filter-search input-with-icon"><Search aria-hidden="true" /><Input value={keyword} onChange={(event) => setKeyword(event.target.value)} placeholder="ค้นหารหัสหรือชื่อซัพพลายเออร์" aria-label="ค้นหาซัพพลายเออร์" /></div><Select value={status} onChange={(event) => setStatus(event.target.value as typeof status)} aria-label="กรองสถานะ"><option value="active">ใช้งานอยู่</option><option value="inactive">ปิดใช้งาน</option><option value="all">ทั้งหมด</option></Select></Card>
    <QueryState query={{ ...query, data: suppliers }} loadingTitle="กำลังโหลดซัพพลายเออร์" emptyTitle="ยังไม่มีซัพพลายเออร์" emptyReason="เพิ่มผู้จำหน่ายรายแรก หรือเปลี่ยนตัวกรองสถานะเพื่อดูรายการอื่น" onRetry={() => void query.refetch()}>
      <Card className="management-table-card"><table className="master-table"><thead><tr><th>ซัพพลายเออร์</th><th>ผู้ติดต่อ</th><th>เงื่อนไขชำระเงิน</th><th>สถานะ</th><th aria-label="การดำเนินการ" /></tr></thead><tbody>{suppliers.map((item) => <tr key={item.id}><td><div className="master-name"><strong>{item.name}</strong><small>{item.code}</small></div></td><td><div className="master-contact">{item.contactName ? <span><UserRound /> {item.contactName}</span> : null}{item.phone ? <span><Phone /> {item.phone}</span> : null}{item.email ? <span><Mail /> {item.email}</span> : null}{!item.contactName && !item.phone && !item.email ? <span className="muted">ไม่ระบุผู้ติดต่อ</span> : null}</div></td><td>{item.paymentTerms || <span className="muted">ไม่ระบุ</span>}</td><td><StatusBadge isActive={item.isActive} /></td><td><div className="row-actions"><Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขได้' : 'แก้ไขซัพพลายเออร์'} aria-label={`แก้ไข ${item.name}`} onClick={() => setFormId(item.id)}><Pencil /></Button><Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เปลี่ยนสถานะได้' : item.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} aria-label={`${item.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${item.name}`} onClick={() => setStatusTarget(item)}><ToggleLeft /></Button></div></td></tr>)}</tbody></table></Card>
    </QueryState>
    <SupplierFormModal open={formId !== null} supplierId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
    <ConfirmModal open={Boolean(statusTarget)} title={statusTarget?.isActive ? 'ปิดใช้งานซัพพลายเออร์' : 'เปิดใช้งานซัพพลายเออร์'} description="ข้อมูลจะไม่ถูกลบและสามารถเปิดใช้งานกลับมาได้" onClose={() => { setStatusTarget(null); statusMutation.reset() }} footer={<><Button variant="ghost" onClick={() => setStatusTarget(null)}>ยกเลิก</Button><Button variant={statusTarget?.isActive ? 'destructive' : 'default'} disabled={statusMutation.isPending} onClick={() => statusTarget && statusMutation.mutate(statusTarget)}>{statusMutation.isPending ? 'กำลังบันทึก…' : statusTarget?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}</Button></>}>{statusMutation.isError ? <InlineError error={statusMutation.error} /> : null}<p>ซัพพลายเออร์: <strong>{statusTarget?.code} · {statusTarget?.name}</strong></p></ConfirmModal>
  </AppShell>
}

function SupplierFormModal({ open, supplierId, onClose }: { open: boolean; supplierId: string | null; onClose: () => void }) {
  const [form, setForm] = useState<SupplierInput>(emptyForm)
  const [errors, setErrors] = useState<Partial<Record<keyof SupplierInput, string>>>({})
  const details = useQuery({ queryKey: ['supplier', supplierId], queryFn: () => getSupplier(supplierId!), enabled: open && Boolean(supplierId) })
  const suppliersForCode = useQuery({ queryKey: ['suppliers', 'code-lookup'], queryFn: () => getSuppliers({ includeInactive: true, page: 1, pageSize: 200 }), enabled: open && !supplierId })
  const queryClient = useQueryClient()
  useEffect(() => { if (!open) return; setErrors({}); if (!supplierId) setForm(emptyForm); else if (details.data) setForm(toInput(details.data)) }, [details.data, open, supplierId])
  const save = useMutation({ mutationFn: (input: SupplierInput) => supplierId ? updateSupplier(supplierId, input) : createSupplier(input), onSuccess: (saved) => { toast.success(supplierId ? 'แก้ไขซัพพลายเออร์เรียบร้อยแล้ว' : 'เพิ่มซัพพลายเออร์เรียบร้อยแล้ว'); void queryClient.invalidateQueries({ queryKey: ['suppliers'] }); if (supplierId) queryClient.setQueryData(['supplier', supplierId], saved); onClose() } })
  const set = (key: keyof SupplierInput, value: string) => setForm((old) => ({ ...old, [key]: value }))
  const submit = () => { const next: typeof errors = {}; if (!form.code.trim()) next.code = 'กรุณากรอกรหัสซัพพลายเออร์'; else if (form.code.trim().length > 30) next.code = 'รหัสต้องยาวไม่เกิน 30 ตัวอักษร'; if (!form.name.trim()) next.name = 'กรุณากรอกชื่อซัพพลายเออร์'; else if (form.name.trim().length > 300) next.name = 'ชื่อต้องยาวไม่เกิน 300 ตัวอักษร'; setErrors(next); if (!Object.keys(next).length) save.mutate({ ...form, code: form.code.trim().toUpperCase(), name: form.name.trim() }) }
  const generateCode = () => { const data = suppliersForCode.data; const existing = Array.isArray(data) ? data : data?.items ?? []; const used = new Set(existing.map((supplier) => supplier.code.trim().toUpperCase())); let sequence = 1; let code = `SUP-${String(sequence).padStart(3, '0')}`; while (used.has(code)) { sequence += 1; code = `SUP-${String(sequence).padStart(3, '0')}` } set('code', code); setErrors((old) => ({ ...old, code: undefined })) }
  const close = () => { save.reset(); onClose() }
  return <ConfirmModal open={open} title={supplierId ? 'แก้ไขซัพพลายเออร์' : 'เพิ่มซัพพลายเออร์'} description="ข้อมูลจะใช้ในงานจัดซื้อและแคตตาล็อกสาขาปัจจุบัน" onClose={close} size="large" footer={<><Button variant="ghost" onClick={close}>ยกเลิก</Button><Button onClick={submit} disabled={save.isPending || Boolean(supplierId && details.isPending)} title={supplierId && details.isPending ? 'กำลังโหลดข้อมูลเดิม' : undefined}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button></>}>{details.isError ? <InlineError error={details.error} /> : <form className="management-form master-form" onSubmit={(event) => { event.preventDefault(); submit() }}><section><h3>ข้อมูลซัพพลายเออร์</h3><div className="form-grid"><Field label="รหัสซัพพลายเออร์ *" error={errors.code}><div className="field-input-with-action"><Input value={form.code} maxLength={30} onChange={(e) => set('code', e.target.value)} /><Button type="button" size="sm" variant="secondary" onClick={generateCode} disabled={Boolean(supplierId) || suppliersForCode.isPending} title={supplierId ? 'สร้างรหัสอัตโนมัติเฉพาะซัพพลายเออร์ใหม่' : suppliersForCode.isPending ? 'กำลังตรวจสอบรหัสที่มีอยู่' : 'สร้างรหัสซัพพลายเออร์ที่ยังไม่ซ้ำ'}><WandSparkles aria-hidden="true" /> สร้างรหัส</Button></div></Field><Field label="ชื่อซัพพลายเออร์ *" error={errors.name}><Input value={form.name} maxLength={300} onChange={(e) => set('name', e.target.value)} /></Field><Field label="ชื่อผู้ติดต่อ"><Input value={form.contactName || ''} maxLength={150} onChange={(e) => set('contactName', e.target.value)} /></Field><Field label="เบอร์โทรศัพท์"><Input value={form.phone || ''} maxLength={30} onChange={(e) => set('phone', e.target.value)} /></Field><Field label="อีเมล"><Input type="email" value={form.email || ''} maxLength={150} onChange={(e) => set('email', e.target.value)} /></Field><Field label="เลขผู้เสียภาษี"><Input value={form.taxId || ''} maxLength={20} onChange={(e) => set('taxId', e.target.value)} /></Field><Field label="เงื่อนไขชำระเงิน"><Input value={form.paymentTerms || ''} maxLength={100} placeholder="เช่น เครดิต 30 วัน" onChange={(e) => set('paymentTerms', e.target.value)} /></Field><Field label="ที่อยู่" wide><Textarea rows={3} value={form.address || ''} maxLength={500} onChange={(e) => set('address', e.target.value)} /></Field><Field label="หมายเหตุ" wide><Textarea rows={2} value={form.note || ''} maxLength={500} onChange={(e) => set('note', e.target.value)} /></Field></div></section>{save.isError ? <InlineError error={save.error} /> : null}</form>}</ConfirmModal>
}

function toInput(item: Supplier): SupplierInput { return { code: item.code, name: item.name, contactName: item.contactName || '', phone: item.phone || '', email: item.email || '', address: item.address || '', taxId: item.taxId || '', paymentTerms: item.paymentTerms || '', note: item.note || '' } }
