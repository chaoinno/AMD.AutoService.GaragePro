import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, CheckCircle2, CircleOff, ClipboardList, Clock3, FileSpreadsheet, LoaderCircle, PackageCheck, Plus, Printer, RefreshCw, Search, Send, ShoppingCart, SquarePen, Trash2, Truck, Undo2, type LucideIcon } from 'lucide-react'
import { useState } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { PurchaseApprovalPin } from './PurchaseApprovalPin'
import { PurchaseProgress } from './PurchaseProgress'
import { PurchaseFinancialSummary, PurchaseVatSelector, purchaseTotals, vatLabel } from './PurchaseFinancialSummary'
import { PurchaseItemPicker } from './PurchaseItemPicker'
import { PurchasePartModal } from './PurchasePartModal'
import { getSuppliers, getWarehouses } from '../../api/masterData'
import type { Supplier } from '../../api/types'
import { SupplierFormModal } from '../master-data/SupplierPage'
import { approvalThreshold, convertPurchase, pendingCommand, purchase, purchaseAction, purchases, receipts, receive, savePurchase, stockCommand, type Purchase, type PurchaseInput, type PurchaseKind, type ReceiptInput } from '../../api/purchasing'
import { ManagementTable } from '../../components/ManagementTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button, type ButtonProps } from '../../components/ui/button'
import { Badge } from '../../components/ui/badge'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { StaffAvatar } from '../../components/StaffAvatar'
import { useSession } from '../../lib/session'
import { Field, InlineError, QueryState } from '../master-data/MasterDataCommon'
import './purchasing.css'
import { PurchaseDocumentModal } from './PurchaseDocumentModal'
import { exportPurchases } from './purchaseExport'

export const money = (n: number | null | undefined) => n == null ? '—' : n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
export const dateTime = (s: string) => new Date(s).toLocaleString('th-TH', { dateStyle: 'short', timeStyle: 'short' })
const statuses: Record<string, string> = { draft: 'ฉบับร่าง', pending: 'รออนุมัติ', approved: 'อนุมัติแล้ว', converted: 'แปลงเป็น PO แล้ว', sent: 'ส่งสั่งซื้อแล้ว', partial: 'รับบางส่วน', complete: 'รับครบแล้ว', cancelled: 'ยกเลิก' }
const actions: Record<string, string> = { submit: 'ส่งขออนุมัติ', approve: 'อนุมัติ', return: 'ส่งกลับแก้ไข', send: 'ส่งสั่งซื้อ', cancel: 'ยกเลิกเอกสาร' }
const actionAppearance: Record<string, { variant: ButtonProps['variant']; icon: LucideIcon }> = {
  submit: { variant: 'default', icon: Send },
  approve: { variant: 'success', icon: CheckCircle2 },
  return: { variant: 'warning', icon: Undo2 },
  send: { variant: 'default', icon: Truck },
  cancel: { variant: 'destructive-outline', icon: CircleOff },
  convert: { variant: 'default', icon: ShoppingCart },
}
const confirmationLabels: Record<string, string> = { submit: 'ยืนยันส่งขออนุมัติ', approve: 'ยืนยันอนุมัติรายการ', return: 'ยืนยันส่งกลับแก้ไข', send: 'ยืนยันส่งสั่งซื้อ', cancel: 'ยืนยันยกเลิกเอกสาร', convert: 'ยืนยันสร้าง PO' }
const confirmationDescriptions: Record<string, string> = {
  submit: 'ส่งเอกสารให้ผู้มีสิทธิ์พิจารณาอนุมัติ',
  send: 'เมื่อยืนยัน สถานะจะเปลี่ยนเป็น “ส่งสั่งซื้อแล้ว”',
  return: 'ส่งเอกสารกลับเพื่อแก้ไขตามเหตุผลที่ระบุ',
  cancel: 'เมื่อยืนยัน เอกสารนี้จะเปลี่ยนเป็นสถานะ “ยกเลิก”',
  convert: 'สร้างใบสั่งซื้อจากรายการที่ PR อนุมัติแล้ว',
}
function PurchaseActionIcon({ action }: { action: string }) {
  const Icon = actionAppearance[action]?.icon
  return Icon ? <Icon aria-hidden="true" data-icon="inline-start" /> : null
}
export function PurchaseStatus({ status }: { status: string }) {
  const Icon = status === 'cancelled' ? CircleOff : ['approved', 'complete', 'converted'].includes(status) ? CheckCircle2 : status === 'sent' ? Send : Clock3
  return <span className={`purchase-status purchase-status--${status}`}><Icon size={14} />{statuses[status] || status}</span>
}
export function usePurchasingRefresh() {
  const client = useQueryClient()
  return async (doc?: Purchase) => {
    if (doc) client.setQueryData(['purchase', doc.kind, doc.id], doc)
    await Promise.all(['purchases', 'purchase-count', 'purchase', 'receipts', 'inventory', 'stock-detail', 'catalog', 'catalog-management', 'catalog-management-item'].map(key => client.invalidateQueries({ queryKey: [key] })))
  }
}

export function PurchasingPage() {
  const routeKind = useParams().kind?.toUpperCase()
  if (routeKind !== 'PR' && routeKind !== 'PO') return <Navigate to="/purchasing/pr" replace />
  return <PurchasingWorkspace key={routeKind} kind={routeKind} />
}

function PurchasingWorkspace({ kind }: { kind: PurchaseKind }) {
  const [q, setQ] = useState(''); const [status, setStatus] = useState(''); const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<{ kind: PurchaseKind; id: string } | null>(null)
  const [creating, setCreating] = useState(false)
  const navigate = useNavigate()
  // PO's "ทุกสถานะ" (status === '') hides complete/cancelled by default — those are done, not worklist items.
  // Typing a search keyword looks across every status instead, since the user is hunting for a specific document.
  // Picking any explicit status (including "รับครบแล้ว"/"ยกเลิก") always wins regardless of keyword.
  const defaultsToOpen = kind === 'PO' && status === ''
  const effectiveStatus = defaultsToOpen && !q.trim() ? 'open' : status
  const query = useQuery({ queryKey: ['purchases', kind, q, effectiveStatus, page], queryFn: () => purchases(kind, q, effectiveStatus, page) })
  const { session } = useSession()
  const allowed = ['manager', 'office'].includes(session?.user.role.toLowerCase() || '')
  const excel = useMutation({
    mutationFn: () => exportPurchases(kind, q, effectiveStatus, session?.branchName || ''),
    onSuccess: () => toast.success('ส่งออก Excel เรียบร้อยแล้ว'),
    onError: (error: Error) => toast.error(error.message || 'ส่งออกไม่สำเร็จ กรุณาลองใหม่'),
  })
  const copy = kind === 'PR'
    ? { title: 'รายการใบขอซื้อ', description: 'สร้างและติดตามคำขอซื้อก่อนส่งอนุมัติ', create: 'สร้างใบขอซื้อ' }
    : { title: 'รายการใบสั่งซื้อ', description: 'จัดการคำสั่งซื้อและติดตามการรับสินค้าเข้าคลัง', create: 'สร้างใบสั่งซื้อ' }
  return <AppShell title={kind === 'PR' ? 'ใบขอซื้อ (PR)' : 'ใบสั่งซื้อ (PO)'}><section className="purchase-page-heading">
    <span className={`purchase-page-heading__icon purchase-page-heading__icon--${kind.toLowerCase()}`} aria-hidden="true">{kind === 'PR' ? <ClipboardList /> : <ShoppingCart />}</span>
    <div className="purchase-page-heading__copy"><h2>{copy.title}</h2><p>{copy.description}</p></div>
    <div className="purchase-list-actions"><Button variant="outline" disabled={!allowed || excel.isPending || !query.data?.totalItems} title="ส่งออกทุกรายการตามตัวกรอง รวมทุกหน้า" onClick={() => excel.mutate()}><FileSpreadsheet aria-hidden="true" />{excel.isPending ? 'กำลังส่งออก…' : 'ส่งออก Excel'}</Button><Button disabled={!allowed} title={!allowed ? 'สำหรับผู้จัดการหรือธุรการจัดซื้อ' : undefined} onClick={() => setCreating(true)}><Plus />{copy.create}</Button></div>
  </section>
    <Card className="purchase-filters"><div className="input-with-icon"><Search /><Input aria-label="ค้นหาเอกสารจัดซื้อ" placeholder="ค้นหาเลขเอกสาร / ซัพพลายเออร์" value={q} onChange={e => { setQ(e.target.value); setPage(1) }} /></div><Select aria-label="สถานะเอกสาร" value={status} onChange={e => { setStatus(e.target.value); setPage(1) }}><option value="">ทุกสถานะ</option>{Object.entries(statuses).filter(([key]) => kind === 'PR' ? !['converted', 'sent', 'partial', 'complete'].includes(key) : key !== 'converted').map(([key, label]) => <option value={key} key={key}>{label}</option>)}</Select><Button variant="outline" onClick={() => void query.refetch()}><RefreshCw />โหลดใหม่</Button></Card>
    {defaultsToOpen && (q.trim()
      ? <p className="section-help">กำลังค้นหาในทุกสถานะ (รวมที่รับครบแล้ว/ยกเลิก) เพราะมีคำค้นหาอยู่</p>
      : <p className="section-help">แสดงเฉพาะเอกสารที่ยังไม่รับครบ/ยังไม่ยกเลิก — พิมพ์ค้นหาหรือเลือกสถานะเพื่อดูรายการที่ปิดแล้ว</p>)}
    <QueryState query={query} loadingTitle={`กำลังโหลด${copy.title}`} emptyTitle={`ยังไม่มี${copy.title.replace('รายการ', '')}`} emptyReason={`กด “${copy.create}” เพื่อเริ่มต้น`} onRetry={() => void query.refetch()}>
      <Card className="management-table-card purchase-compact"><ManagementTable data={query.data?.items ?? []} columns={[
        { id: 'sequence', header: 'ลำดับ', size: 65, value: (doc) => (page - 1) * (query.data?.pageSize ?? 25) + (query.data?.items.indexOf(doc) ?? 0) + 1, render: (doc) => <>{(page - 1) * (query.data?.pageSize ?? 25) + (query.data?.items.indexOf(doc) ?? 0) + 1}</> },
        { id: 'number', header: 'เลขเอกสาร', value: (doc) => doc.number, render: (doc) => <><strong>{doc.number}</strong><small className="purchase-sub">{doc.lines.length} รายการ</small></> },
        { id: 'date', header: 'วันที่ / ผู้สร้าง', value: (doc) => new Date(doc.createdAt).getTime(), size: 240, render: (doc) => <>{dateTime(doc.createdAt)}<small className="purchase-sub">{doc.createdByName}</small></> },
        { id: 'supplier', header: 'ซัพพลายเออร์ / คลัง', value: (doc) => [doc.supplierName, doc.warehouseName].filter(Boolean).join(' '), size: 270, render: (doc) => <>{doc.supplierName || 'ยังไม่ระบุซัพพลายเออร์'}<small className="purchase-sub">{doc.warehouseName}</small></> },
        { id: 'status', header: 'สถานะ', value: (doc) => statuses[doc.status] || doc.status, render: (doc) => <><PurchaseStatus status={doc.status} /></> },
        { id: 'total', header: 'ยอดสุทธิ', value: (doc) => doc.total, render: (doc) => <><span className="money">{money(doc.total)}</span><small className="purchase-sub">{vatLabel(doc.hasVat, doc.vatRate)}</small></> },
        { id: 'actions', header: '', render: (doc) => <Button variant="outline" size="sm" onClick={() => setSelected({ kind, id: doc.id })}><ClipboardList aria-hidden="true" /> เปิดเอกสาร</Button> },
      ]} />{query.data?.items.length === 0 && <p className="purchase-empty">ยังไม่มีเอกสารที่ตรงกับการค้นหา กด “สร้าง {kind}” เพื่อเริ่มต้น</p>}</Card>
      <div className="purchase-pagination"><span>{query.data?.totalItems ?? 0} เอกสาร · หน้า {page}</span><Button variant="outline" disabled={page === 1} title={page === 1 ? 'อยู่หน้าแรกแล้ว' : undefined} onClick={() => setPage(p => p - 1)}>ก่อนหน้า</Button><Button variant="outline" disabled={page >= (query.data?.totalPages || 1)} title={page >= (query.data?.totalPages || 1) ? 'ไม่มีหน้าถัดไป' : undefined} onClick={() => setPage(p => p + 1)}>ถัดไป</Button></div>
    </QueryState>
    {creating && <PurchaseEditor kind={kind} onClose={() => setCreating(false)} onSaved={doc => { setCreating(false); setSelected({ kind: doc.kind, id: doc.id }) }} />}
    {selected && <PurchaseDetail key={`${selected.kind}-${selected.id}`} {...selected} onClose={() => setSelected(null)} onConverted={() => navigate('/purchasing/po')} />}
  </AppShell>
}

function SupplierSelect({ value, onChange, onAdd, selectedSupplier }: { value: string; onChange: (value: string) => void; onAdd: (name: string) => void; selectedSupplier: Supplier | null }) {
  const [q, setQ] = useState('')
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const query = useQuery({ queryKey: ['suppliers', 'purchase-picker', q], queryFn: () => getSuppliers({ keyword: q, pageSize: 100 }) })
  const items = Array.isArray(query.data) ? query.data : query.data?.items || []
  return <div className="purchase-supplier-picker">
    <Field label="ซัพพลายเออร์ *"><Input aria-label="ค้นหาซัพพลายเออร์" placeholder="พิมพ์ค้นหาซัพพลายเออร์" value={q} onChange={e => setQ(e.target.value)} /><Select required value={value} onChange={e => onChange(e.target.value)} aria-label="เลือกซัพพลายเออร์"><option value="">เลือกซัพพลายเออร์</option>{value && !items.some(x => x.id === value) && <option value={value}>{selectedSupplier?.id === value ? `${selectedSupplier.code} · ${selectedSupplier.name}` : 'ซัพพลายเออร์ที่เลือกไว้'}</option>}{items.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select>{query.isError && <InlineError error={query.error} />}</Field>
    {query.isSuccess && !items.length && <p className="section-help" role="status">{q ? 'ไม่พบซัพพลายเออร์ที่ค้นหา' : 'ยังไม่มีซัพพลายเออร์ในระบบ'}</p>}
    <Button type="button" variant="outline" size="sm" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มซัพพลายเออร์ได้' : undefined} onClick={() => onAdd(q.trim())}><Plus aria-hidden="true" />เพิ่มซัพพลายเออร์ใหม่</Button>
    {!canManage && <p className="section-help">ให้ผู้จัดการสาขาเพิ่มซัพพลายเออร์ก่อน แล้วค้นหาเพื่อเลือกใช้งาน</p>}
  </div>
}

function PurchaseEditor({ kind, doc, onClose, onSaved }: { kind: PurchaseKind; doc?: Purchase; onClose: () => void; onSaved: (doc: Purchase) => void }) {
  const { session } = useSession()
  const canCreatePart = ['manager', 'office'].includes(session?.user.role.toLowerCase() || '')
  const [newPart, setNewPart] = useState<{ rowId: string; name: string } | null>(null)
  const [warehouseId, setWarehouse] = useState(doc?.warehouseId || '')
  const [supplierId, setSupplier] = useState(doc?.supplierId || '')
  const [newSupplierName, setNewSupplierName] = useState<string | null>(null)
  const [addedSupplier, setAddedSupplier] = useState<Supplier | null>(null)
  const [requiredDate, setDate] = useState(doc?.requiredDate?.slice(0, 10) || '')
  const [note, setNote] = useState(doc?.note || ''); const [terms, setTerms] = useState(doc?.paymentTerms || '')
  const [hasVat, setHasVat] = useState(doc?.hasVat ?? false)
  const [error, setError] = useState('')
  const [rowsToAdd, setRowsToAdd] = useState('1')
  const blankRow = () => ({ rowId: crypto.randomUUID(), catalogItemId: '', code: '', name: '', unit: '', quantity: '1', unitCost: '0', receivedQuantity: 0 })
  const [lines, setLines] = useState(() => doc?.lines.length ? doc.lines.map(x => ({ rowId: crypto.randomUUID(), catalogItemId: x.catalogItemId, code: x.code, name: x.name, unit: x.unit, quantity: String(x.quantity), unitCost: String(x.unitCost), receivedQuantity: x.receivedGood + x.receivedDamaged })) : [blankRow()])
  const warehouses = useQuery({ queryKey: ['warehouses', 'purchase-picker'], queryFn: () => getWarehouses() })
  const totals = purchaseTotals(lines.map(line => ({ quantity: Number(line.quantity), unitCost: Number(line.unitCost) })), hasVat, doc?.vatRate)
  const refresh = usePurchasingRefresh()
  const save = useMutation({ mutationFn: (input: PurchaseInput) => savePurchase(kind, doc?.id || null, input), onSuccess: async saved => { await refresh(saved); toast.success('บันทึกเอกสารแล้ว'); onSaved(saved) } })
  const submit = () => {
    if (!lines.length) { setError('กรุณาเพิ่มสินค้าอย่างน้อย 1 รายการ'); return }
    if (lines.some(x => !x.catalogItemId)) { setError('กรุณาเลือกสินค้าหรือเพิ่มอะไหล่ใหม่ให้ครบทุกแถว หรือลบแถวที่ไม่ใช้'); return }
    if (new Set(lines.map(x => x.catalogItemId)).size !== lines.length) { setError('มีสินค้าซ้ำ กรุณารวมจำนวนในแถวเดียวกัน'); return }
    if (lines.some(x => !x.quantity || !x.unitCost || !Number.isInteger(Number(x.quantity)) || Number(x.quantity) <= 0 || Number(x.unitCost) < 0)) { setError('กรุณาระบุจำนวนเต็มบวกและราคาที่ถูกต้อง'); return }
    setError(''); save.mutate({ warehouseId, supplierId: supplierId || null, requiredDate: requiredDate || null, note, paymentTerms: terms, hasVat, version: doc?.version, lines: lines.map(x => ({ catalogItemId: x.catalogItemId, quantity: Number(x.quantity), unitCost: Number(x.unitCost) })) })
  }
  if (newSupplierName !== null) return <SupplierFormModal key="purchase-editor-new-supplier" open supplierId={null} initialName={newSupplierName} onClose={() => setNewSupplierName(null)} onSaved={saved => { setSupplier(saved.id); setAddedSupplier(saved) }} />
  if (newPart) return <PurchasePartModal initialName={newPart.name} onClose={() => setNewPart(null)} onSaved={(item, unitCost) => {
    setLines(old => old.map(row => row.rowId === newPart.rowId ? { ...row, catalogItemId: item.id, code: item.code, name: item.name, unit: item.unit, unitCost: String(unitCost) } : row))
    setError('')
    setNewPart(null)
  }} />
  return <ConfirmModal open title={doc ? `แก้ไข ${doc.number}` : `สร้าง${kind === 'PR' ? 'ใบขอซื้อ (PR)' : 'ใบสั่งซื้อ (PO)'}`} description={doc ? "รายการที่เพิ่ม แก้จำนวน แก้ราคา ลบ หรือเปลี่ยน VAT ต้องส่งขออนุมัติใหม่" : "ออกเลขเอกสารอัตโนมัติเมื่อบันทึก · จำนวนสินค้าเป็นจำนวนเต็ม"} size="xlarge" onClose={() => { if (!save.isPending) onClose() }} footer={<><Button variant="ghost" onClick={onClose} disabled={save.isPending}>ยกเลิก</Button><Button form="purchase-editor" type="submit" disabled={save.isPending}>{save.isPending ? 'กำลังบันทึก…' : doc ? 'บันทึกการแก้ไข' : 'บันทึกฉบับร่าง'}</Button></>}>
    <form id="purchase-editor" className="management-form" onSubmit={e => { e.preventDefault(); submit() }}><fieldset disabled={save.isPending} className="purchase-fieldset"><div className="form-grid">
      <Field label="คลังรับสินค้า *"><Select required value={warehouseId} onChange={e => setWarehouse(e.target.value)}><option value="">เลือกคลัง</option>{warehouses.data?.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select>{warehouses.isError && <InlineError error={warehouses.error} />}</Field>
      <Field label="วันที่ต้องการสินค้า"><Input type="date" value={requiredDate} onChange={e => setDate(e.target.value)} /></Field>
      {kind === 'PO' && <><SupplierSelect value={supplierId} onChange={setSupplier} onAdd={setNewSupplierName} selectedSupplier={addedSupplier} /><Field label="เงื่อนไขชำระเงิน"><Input maxLength={300} value={terms} onChange={e => setTerms(e.target.value)} /></Field></>}
      <Field label="หมายเหตุ / เหตุผลขอซื้อ" wide><Textarea rows={2} placeholder="ระบุเหตุผลขอซื้อหรือรายละเอียดเพิ่มเติม (ถ้ามี)" maxLength={1000} value={note} onChange={e => setNote(e.target.value)} /></Field></div>
      <div className="purchase-entry-toolbar">
        <div className="purchase-entry-toolbar__copy"><div className="purchase-entry-toolbar__title"><h3>รายการสินค้า</h3><Badge variant="secondary">{lines.length} แถว</Badge></div><p>ค้นหาด้วยรหัสหรือชื่อสินค้า · หากไม่พบรายการ สามารถเพิ่มอะไหล่ใหม่จากผลการค้นหาได้</p></div>
        <div className="purchase-entry-toolbar__controls"><Field label="จำนวนแถวที่เพิ่ม"><Input id="purchase-row-count" type="number" min="1" max="50" step="1" value={rowsToAdd} onChange={e => setRowsToAdd(e.target.value)} /></Field><Button type="button" variant="outline" onClick={() => { const count = Number(rowsToAdd); if (!Number.isInteger(count) || count < 1 || count > 50) { setError('เพิ่มได้ครั้งละ 1–50 แถว'); return } setLines(old => [...old, ...Array.from({ length: count }, blankRow)]); setError('') }}><Plus aria-hidden="true" data-icon="inline-start" /> เพิ่มแถว</Button></div>
      </div>
      {lines.some(line => line.receivedQuantity > 0) && <p className="section-help">รายการที่รับสินค้าแล้วเปลี่ยนสินค้าและลบไม่ได้ · จำนวนต้องไม่น้อยกว่ายอดที่รับแล้ว</p>}
      <div className="purchase-table-scroll"><table className="master-table purchase-lines purchase-entry-lines"><thead><tr><th>ลำดับ</th><th>สินค้า</th><th>หน่วย</th><th>จำนวน</th><th>ราคา/หน่วย</th><th>รวม</th><th /></tr></thead><tbody>{lines.map((line, i) => <tr key={line.rowId}><td className="purchase-entry-sequence">{i + 1}</td><td>{line.receivedQuantity > 0 ? <><strong>{line.name}</strong><small className="purchase-sub">{line.code}</small></> : <><PurchaseItemPicker rowId={line.rowId} label={line.catalogItemId ? `${line.code} · ${line.name}` : ''} excluded={lines.filter((_, n) => n !== i).map(x => x.catalogItemId)} onCreate={canCreatePart ? name => setNewPart({ rowId: line.rowId, name }) : undefined} onSelect={item => setLines(old => old.map(row => row.rowId === line.rowId ? { ...row, catalogItemId: item.id, code: item.code, name: item.name, unit: item.unit, unitCost: String(item.cost ?? 0) } : row))} /></>}</td><td className="purchase-entry-unit">{line.unit || '—'}</td><td><Input aria-label={`จำนวน ${line.name}`} required type="number" min={Math.max(1, line.receivedQuantity)} max="1000000" step="1" value={line.quantity} onChange={e => setLines(old => old.map((x, n) => n === i ? { ...x, quantity: e.target.value } : x))} /></td><td><Input aria-label={`ราคา ${line.name}`} required type="number" min="0" max="100000000" step="0.01" value={line.unitCost} onChange={e => setLines(old => old.map((x, n) => n === i ? { ...x, unitCost: e.target.value } : x))} /></td><td className="money">{money(Number(line.quantity) * Number(line.unitCost))}</td><td><Button variant="ghost" size="icon" disabled={line.receivedQuantity > 0} title={line.receivedQuantity > 0 ? 'รายการนี้รับสินค้าแล้ว ไม่สามารถลบได้' : undefined} aria-label={`ลบ ${line.name}`} onClick={() => setLines(old => old.filter((_, n) => n !== i))}><Trash2 /></Button></td></tr>)}</tbody></table></div>
      <div className="purchase-financials"><PurchaseVatSelector value={hasVat} onChange={setHasVat} /><PurchaseFinancialSummary {...totals} hasVat={hasVat} vatRate={doc?.vatRate} /></div>
      {error && <p role="alert" className="field-error">{error}</p>}{save.isError && <InlineError error={save.error} />}</fieldset></form>
  </ConfirmModal>
}

function PurchaseDetail({ kind, id, onClose, onConverted }: { kind: PurchaseKind; id: string; onClose: () => void; onConverted: (doc: Purchase) => void }) {
  const query = useQuery({ queryKey: ['purchase', kind, id], queryFn: () => purchase(kind, id), refetchOnWindowFocus: false })
  const history = useQuery({ queryKey: ['receipts', id], queryFn: () => receipts(id), enabled: kind === 'PO' })
  const policy = useQuery({ queryKey: ['purchase-policy'], queryFn: approvalThreshold })
  const { session } = useSession(); const manager = Boolean(session?.user.canSeeCost)
  const [editing, setEditing] = useState(false); const [receiving, setReceiving] = useState(false)
  const [printing, setPrinting] = useState(false)
  const [sourceReview, setSourceReview] = useState(false)
  const [action, setAction] = useState(''); const [reason, setReason] = useState(''); const [supplier, setSupplier] = useState('')
  const [newSupplierName, setNewSupplierName] = useState<string | null>(null)
  const [addedSupplier, setAddedSupplier] = useState<Supplier | null>(null)
  const refresh = usePurchasingRefresh(); const doc = query.data
  const hasPendingReceipt = Boolean(pendingCommand(`purchase-receipt:${session?.user.shardKey}:${session?.branchId}:${id}`))
  const [pinCode, setPinCode] = useState('')
  const mutate = useMutation({ mutationFn: () => action === 'convert' ? convertPurchase(doc!, supplier) : purchaseAction(doc!, action, reason, pinCode), onSuccess: async saved => { await refresh(saved); setAction(''); setReason(''); setPinCode(''); toast.success('ดำเนินการสำเร็จ'); if (action === 'convert') onConverted(saved) } })
  if (newSupplierName !== null) return <SupplierFormModal key="purchase-convert-new-supplier" open supplierId={null} initialName={newSupplierName} onClose={() => setNewSupplierName(null)} onSaved={saved => { setSupplier(saved.id); setAddedSupplier(saved) }} />
  if (sourceReview && doc?.sourceRequestId) return <PurchaseDetail kind="PR" id={doc.sourceRequestId} onClose={() => setSourceReview(false)} onConverted={() => setSourceReview(false)} />
  if (editing && doc) return <PurchaseEditor kind={kind} doc={doc} onClose={() => setEditing(false)} onSaved={() => setEditing(false)} />
  if (receiving && doc) return <ReceiptForm doc={doc} onClose={() => setReceiving(false)} />
  if (printing && doc) return <PurchaseDocumentModal kind={kind} id={id} onClose={() => setPrinting(false)} />
  if (action === 'approve' && doc) {
    const closeConfirmation = () => { if (!mutate.isPending) { setAction(''); setPinCode(''); mutate.reset() } }
    return <ConfirmModal key="purchase-approval-confirmation" open
      title={doc.number} description={kind === 'PR' ? 'อนุมัติใบขอซื้อ (PR)' : 'อนุมัติใบสั่งซื้อ (PO)'}
      onClose={closeConfirmation} footer={<>
        <Button variant="ghost" disabled={mutate.isPending} onClick={closeConfirmation}><ArrowLeft aria-hidden="true" />กลับ</Button>
        <Button type="submit" form="purchase-approval-confirmation" variant="success" disabled={mutate.isPending || pinCode.length !== 4}>
          {mutate.isPending ? <LoaderCircle className="animate-spin" aria-hidden="true" /> : <PurchaseActionIcon action="approve" />}
          {mutate.isPending ? 'กำลังอนุมัติ…' : confirmationLabels.approve}
        </Button>
      </>}>
      <form id="purchase-approval-confirmation" className="purchase-approval-confirmation" onSubmit={event => { event.preventDefault(); if (!mutate.isPending && /^[0-9]{4}$/.test(pinCode)) mutate.mutate() }}>
        <PurchaseApprovalPin value={pinCode} onChange={value => { setPinCode(value); if (mutate.isError) mutate.reset() }}
          branchName={session?.branchName || 'สาขาปัจจุบัน'} count={doc.approvalChanges?.length ?? doc.lines.length}
          taxChange={Boolean(doc.taxApprovalChange)} total={doc.total} disabled={mutate.isPending} invalid={mutate.isError} errorMessage={mutate.error?.message} />
      </form>
    </ConfirmModal>
  }
  if (action && doc) {
    const closeConfirmation = () => { if (!mutate.isPending) { setAction(''); setReason(''); mutate.reset() } }
    const formId = `purchase-${action}-confirmation`
    return <ConfirmModal key={formId} open
      title={doc.number} description={kind === 'PR' ? 'ใบขอซื้อ (PR)' : 'ใบสั่งซื้อ (PO)'}
      onClose={closeConfirmation} footer={<>
        <Button variant="ghost" disabled={mutate.isPending} onClick={closeConfirmation}><ArrowLeft aria-hidden="true" />กลับ</Button>
        <Button type="submit" form={formId} variant={action === 'cancel' ? 'destructive' : actionAppearance[action]?.variant} disabled={mutate.isPending}>
          {mutate.isPending ? <LoaderCircle className="animate-spin" aria-hidden="true" /> : <PurchaseActionIcon action={action} />}
          {mutate.isPending ? 'กำลังดำเนินการ…' : confirmationLabels[action]}
        </Button>
      </>}>
      <form id={formId} className="purchase-action-confirmation" onSubmit={event => { event.preventDefault(); if (!mutate.isPending) mutate.mutate() }}>
        <section className="purchase-action-card" data-action={action} aria-labelledby="purchase-action-title">
          <span className="purchase-action-card__seal"><PurchaseActionIcon action={action} /></span>
          <div className="purchase-action-card__heading"><h3 id="purchase-action-title">{confirmationLabels[action]}</h3><p>{confirmationDescriptions[action]}</p></div>
          {action === 'send' && <dl className="purchase-action-card__facts"><div><dt>ซัพพลายเออร์</dt><dd>{doc.supplierName || 'ยังไม่ระบุ'}</dd></div><div><dt>คลังรับสินค้า</dt><dd>{doc.warehouseName}</dd></div></dl>}
          <div className="purchase-action-card__summary"><span>รายการสินค้า</span><strong>{doc.lines.length} รายการ · {vatLabel(doc.hasVat, doc.vatRate)}</strong></div>
          <div className="purchase-action-card__financials"><PurchaseFinancialSummary {...doc} /></div>
          {action === 'convert' && <fieldset className="purchase-fieldset" disabled={mutate.isPending}><SupplierSelect value={supplier} onChange={setSupplier} onAdd={setNewSupplierName} selectedSupplier={addedSupplier} /></fieldset>}
          {['cancel', 'return'].includes(action) && <Field label="เหตุผล *"><Textarea required maxLength={1000} value={reason} disabled={mutate.isPending} onChange={event => { setReason(event.target.value); if (mutate.isError) mutate.reset() }} /></Field>}
          <p className="purchase-action-card__audit">ระบบจะบันทึกผู้ดำเนินการและเวลาของรายการนี้</p>
          {mutate.isError && <InlineError error={mutate.error} />}
        </section>
      </form>
    </ConfirmModal>
  }
  const chooseAction = (next: string) => { setAction(next); setReason(''); setPinCode(''); mutate.reset() }
  const canApprove = manager || (kind === 'PO' && policy.data !== undefined && (doc?.total || 0) <= policy.data)
  const choices = doc ? [doc.status === 'draft' ? 'submit' : '', doc.status === 'pending' && canApprove ? 'approve' : '', doc.status === 'pending' && canApprove ? 'return' : '', kind === 'PO' && doc.status === 'approved' ? 'send' : '', !['cancelled', 'complete', 'converted'].includes(doc.status) ? 'cancel' : ''].filter(Boolean) : []
  return <ConfirmModal open title={doc?.number || 'รายละเอียดเอกสาร'} description={<span className={`purchase-document-type purchase-document-type--${kind.toLowerCase()}`}>{kind === 'PR' ? 'ใบขอซื้อ' : 'ใบสั่งซื้อและประวัติรับสินค้า'}</span>} size="xlarge" onClose={() => { if (!mutate.isPending) onClose() }} footer={<><Button variant="outline" onClick={() => { setAction(''); setPinCode(''); setPrinting(true) }} disabled={!doc || mutate.isPending}><Printer aria-hidden="true" /> พิมพ์ {kind}</Button><Button variant="outline" onClick={onClose} disabled={mutate.isPending}>ปิด</Button></>}>
    <QueryState query={query} loadingTitle="กำลังโหลดเอกสาร" emptyTitle="ไม่พบเอกสาร" emptyReason="กรุณาโหลดใหม่" onRetry={() => void query.refetch()}>{doc && <div className="purchase-detail purchase-compact">
      <header className="purchase-summary">
        <div className="purchase-summary__facts">
          <PurchaseStatus status={doc.status} />
          <span><small>คลังรับสินค้า</small><strong>{doc.warehouseName}</strong></span>
          <span><small>ซัพพลายเออร์</small><strong>{doc.supplierName || 'ยังไม่ระบุ'}</strong></span>
          <span><small>วันที่ต้องการ</small><strong>{doc.requiredDate ? new Date(doc.requiredDate).toLocaleDateString('th-TH') : 'ยังไม่ระบุ'}</strong></span>
        </div>
        <div className="purchase-summary__people">
          <PurchaseActor label="ผู้สร้าง" name={doc.createdByName} timestamp={doc.createdAt} />
          <PurchaseActor label={doc.approvalChanges?.length || doc.taxApprovalChange ? "อนุมัติล่าสุด" : "ผู้อนุมัติ"} name={doc.approvedByName} timestamp={doc.approvedAt} />
        </div>
      </header>
      <PurchaseProgress doc={doc} />
      {doc.sourceRequestId && <div className="purchase-source"><p className="section-help">PO นี้รับผลอนุมัติจาก PR แล้ว · รายการที่แก้ไขต้องขออนุมัติใหม่ก่อนส่งสั่งซื้อ</p><Button variant="outline" size="sm" disabled={mutate.isPending} onClick={() => { setAction(''); setPinCode(''); setSourceReview(true) }}>เปิด PR ต้นทาง</Button></div>}
      <p>{doc.note || 'ไม่มีหมายเหตุ'}{doc.paymentTerms && ` · ชำระเงิน: ${doc.paymentTerms}`}</p>{doc.cancelReason && <p>เหตุผลยกเลิก: {doc.cancelReason}</p>}
      <div className="purchase-table-scroll"><ManagementTable data={doc.lines} columns={[
        { id: 'sequence', header: 'ลำดับ', size: 65, value: (line) => doc.lines.indexOf(line) + 1, render: (line) => <>{doc.lines.indexOf(line) + 1}</> },
        { id: 'item', header: 'สินค้า', value: (line) => line.name, size: 260, render: (line) => <>{line.name}<small className="purchase-sub">{line.code} · {line.unit}</small></> },
        { id: 'approval', header: 'การอนุมัติ', size: 150, value: (line) => line.approvalStatus, render: (line) => <PurchaseStatus status={line.approvalStatus || 'pending'} /> },
        { id: 'quantity', header: 'สั่งซื้อ', value: (line) => line.quantity, render: (line) => <>{line.quantity}</> },
        { id: 'cost', header: 'ราคา/หน่วย', value: (line) => line.unitCost, render: (line) => <><span className="money">{money(line.unitCost)}</span></> },
        { id: 'good', header: 'รับดี', value: (line) => line.receivedGood, render: (line) => <>{line.receivedGood}</> },
        { id: 'damaged', header: 'ชำรุด', value: (line) => line.receivedDamaged, render: (line) => <>{line.receivedDamaged}</> },
        { id: 'outstanding', header: 'ค้างรับ', value: (line) => line.outstanding, render: (line) => <>{line.outstanding}</> },
      ]} /></div>
      {Boolean(doc.approvalChanges?.length) && <section className="purchase-changes"><h3>รายการที่ขออนุมัติ {doc.approvalChanges.length} รายการ</h3><p className="section-help">รายการเดิมที่ไม่เปลี่ยนแปลงคงผลอนุมัติไว้</p>{doc.approvalChanges.map(change => <div key={change.catalogItemId} className="purchase-changes__row"><span><strong>{change.name}</strong><small>{change.code} · {change.change === 'added' ? 'เพิ่มใหม่' : change.change === 'removed' ? 'ลบรายการ' : 'แก้ไขรายการ'}</small></span><span>{change.previousQuantity != null ? <><del>{change.previousQuantity} × {money(change.previousUnitCost)} บาท</del><br /></> : null}{change.quantity != null ? <strong>{change.quantity} × {money(change.unitCost)} บาท</strong> : <strong>นำออกจากเอกสาร</strong>}</span></div>)}</section>}
      {doc.taxApprovalChange && <section className="purchase-changes"><h3>การเปลี่ยน VAT ที่ขออนุมัติ</h3><div className="purchase-changes__row"><span>ภาษีมูลค่าเพิ่ม<small>รายการสินค้าเดิมคงผลอนุมัติไว้</small></span><span><del>{vatLabel(doc.taxApprovalChange.previousHasVat, doc.taxApprovalChange.previousVatRate)}</del><br /><strong>{vatLabel(doc.hasVat, doc.vatRate)}</strong></span></div></section>}
      <div className="purchase-financials purchase-financials--detail"><p className="section-help">ราคาต่อหน่วยเป็นราคาก่อน VAT · {vatLabel(doc.hasVat, doc.vatRate)}</p><PurchaseFinancialSummary {...doc} /></div>
      {kind === 'PO' && policy.data !== undefined && <p className="section-help">วงเงินอนุมัติธุรการ: {money(policy.data)} บาท · ยอดเกินวงเงินต้องให้ผู้จัดการอนุมัติ</p>}
      {doc.status === 'pending' && !canApprove && <p className="section-help">รอผู้จัดการสาขาอนุมัติเอกสารนี้</p>}
      <div className="purchase-actions purchase-document-actions">{!['complete', 'cancelled'].includes(doc.status) && <Button variant="outline" disabled={mutate.isPending || hasPendingReceipt} onClick={() => { setAction(''); setPinCode(''); setEditing(true) }}><SquarePen aria-hidden="true" data-icon="inline-start" />แก้ไขรายการ</Button>}{choices.filter(a => a !== 'cancel').map(a => <Button key={a} data-action={a} variant={actionAppearance[a]?.variant} disabled={mutate.isPending} onClick={() => chooseAction(a)}><PurchaseActionIcon action={a} />{actions[a]}</Button>)}{kind === 'PR' && doc.status === 'approved' && <Button disabled={mutate.isPending} onClick={() => chooseAction('convert')}><ShoppingCart aria-hidden="true" data-icon="inline-start" />สร้าง PO จาก PR</Button>}{kind === 'PO' && (['sent', 'partial'].includes(doc.status) || hasPendingReceipt) && <Button disabled={mutate.isPending} onClick={() => setReceiving(true)}><PackageCheck aria-hidden="true" data-icon="inline-start" />{hasPendingReceipt ? 'ตรวจสอบคำขอรับสินค้าที่ค้าง' : 'รับสินค้าเข้าคลัง'}</Button>}{choices.includes('cancel') && <Button data-action="cancel" variant="destructive-outline" disabled={mutate.isPending} onClick={() => chooseAction('cancel')}><PurchaseActionIcon action="cancel" />ยกเลิกเอกสาร</Button>}</div>
      {kind === 'PO' && <section><h3>ประวัติรับสินค้า</h3>{history.isError ? <InlineError error={history.error} /> : history.isPending ? <p>กำลังโหลดประวัติ…</p> : !history.data?.length ? <p className="section-help">ยังไม่มีใบรับสินค้า</p> : history.data.map(grn => <div key={grn.id} className="purchase-receipt"><strong>{grn.number}</strong><span>ใบส่งของ {grn.deliveryNumber} · {dateTime(grn.receivedAt)} · {grn.receivedByName}</span><span>ของดี {grn.lines.reduce((s, l) => s + l.goodQuantity, 0)} · ชำรุด {grn.lines.reduce((s, l) => s + l.damagedQuantity, 0)}</span></div>)}</section>}
    </div>}</QueryState>
  </ConfirmModal>
}

function PurchaseActor({ label, name, timestamp }: { label: string; name: string | null; timestamp: string | null }) {
  const displayName = name || 'รอการอนุมัติ'
  return <div className={`purchase-actor ${name ? '' : 'purchase-actor--pending'}`}>
    {name ? <StaffAvatar className="purchase-actor__avatar" name={name} /> : <span className="purchase-actor__avatar purchase-actor__avatar--empty"><Clock3 /></span>}
    <span><small>{label}</small><strong>{displayName}</strong>{timestamp ? <time dateTime={timestamp}>{dateTime(timestamp)}</time> : <time>ยังไม่มีผู้ดำเนินการ</time>}</span>
  </div>
}

function ReceiptForm({ doc, onClose }: { doc: Purchase; onClose: () => void }) {
  const { session } = useSession(); const manager = Boolean(session?.user.canSeeCost)
  const key = `purchase-receipt:${session?.user.shardKey}:${session?.branchId}:${doc.id}`
  const [input, setInput] = useState<ReceiptInput>(() => pendingCommand<ReceiptInput>(key) || { requestId: crypto.randomUUID(), deliveryNumber: '', lines: doc.lines.filter(x => x.outstanding > 0).map(x => ({ purchaseLineId: x.id, goodQuantity: 0, damagedQuantity: 0, unitCost: x.unitCost, note: '' })) })
  const refresh = usePurchasingRefresh(); const [error, setError] = useState('')
  const save = useMutation({ mutationFn: (payload: ReceiptInput) => stockCommand(key, payload, body => receive(doc.id, body)), onSuccess: async () => { await refresh(); toast.success('รับสินค้าเข้าคลังเรียบร้อยแล้ว'); onClose() } })
  const uncertain = Boolean(pendingCommand(key)); const locked = uncertain || save.isPending
  const change = (i: number, patch: Partial<ReceiptInput['lines'][number]>) => setInput(old => ({ ...old, lines: old.lines.map((x, n) => n === i ? { ...x, ...patch } : x) }))
  const submit = () => {
    const lines = input.lines.filter(x => x.goodQuantity + x.damagedQuantity > 0)
    if (!lines.length) { setError('กรุณาระบุจำนวนที่รับจริงอย่างน้อย 1 รายการ'); return }
    setError('')
    // Persist and retry the exact same payload; zero-quantity rows are harmless in the form, but excluded from API.
    const payload = pendingCommand<ReceiptInput>(key) || { ...input, lines }
    save.mutate(payload)
  }
  return <ConfirmModal open title={`รับสินค้า ${doc.number}`} description="ระบุจำนวนรับจริง · ของชำรุดไม่นับเป็นพร้อมใช้ · รับครบรวมของชำรุดจะปิด PO" size="xlarge" onClose={() => { if (!save.isPending) onClose() }} footer={<><Button variant="ghost" onClick={onClose} disabled={save.isPending}>กลับ</Button><Button form="receipt-form" type="submit" disabled={save.isPending}>{save.isPending ? 'กำลังรับสินค้า…' : uncertain ? 'ตรวจสอบ / ส่งคำขอเดิมซ้ำ' : 'ยืนยันรับสินค้า'}</Button></>}>
    {uncertain && <p role="status">มีคำขอรับสินค้าที่ยังไม่ทราบผล กดส่งคำขอเดิมซ้ำเพื่อยืนยันโดยไม่เพิ่มสต็อกซ้ำ</p>}
    <form id="receipt-form" onSubmit={e => { e.preventDefault(); submit() }}><fieldset className="purchase-fieldset" disabled={locked}><Field label="เลขใบส่งของ *"><Input required maxLength={100} value={input.deliveryNumber} onChange={e => setInput(old => ({ ...old, deliveryNumber: e.target.value }))} /></Field><div className="purchase-table-scroll"><table className="master-table purchase-lines"><thead><tr><th>สินค้า / ค้างรับ</th><th>ของดี</th><th>ชำรุด</th><th>ราคา/หน่วย</th><th>หมายเหตุ</th></tr></thead><tbody>{input.lines.map((line, i) => { const source = doc.lines.find(x => x.id === line.purchaseLineId)!; return <tr key={line.purchaseLineId}><td>{source.name}<small className="purchase-sub">ค้างรับ {source.outstanding} {source.unit}</small></td><td><Input aria-label={`ของดี ${source.name}`} type="number" min="0" max={source.outstanding} step="1" value={line.goodQuantity} onChange={e => change(i, { goodQuantity: Number(e.target.value) })} /></td><td><Input aria-label={`ชำรุด ${source.name}`} type="number" min="0" max={source.outstanding} step="1" value={line.damagedQuantity} onChange={e => change(i, { damagedQuantity: Number(e.target.value) })} /></td><td><Input aria-label={`ราคาที่รับ ${source.name}`} title={!manager ? 'การเปลี่ยนราคาต้องให้ผู้จัดการยืนยัน' : undefined} readOnly={!manager} type="number" min="0" step="0.01" value={line.unitCost} onChange={e => change(i, { unitCost: Number(e.target.value) })} /></td><td><Input aria-label={`หมายเหตุ ${source.name}`} required={line.damagedQuantity > 0 || line.unitCost !== source.unitCost} maxLength={1000} value={line.note} onChange={e => change(i, { note: e.target.value })} /></td></tr> })}</tbody></table></div></fieldset>{error && <p role="alert" className="field-error">{error}</p>}{save.isError && <InlineError error={save.error} />}</form>
  </ConfirmModal>
}
