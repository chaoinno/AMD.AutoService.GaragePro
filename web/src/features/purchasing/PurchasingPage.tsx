import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, CircleOff, ClipboardList, Clock3, Plus, RefreshCw, Search, Send, ShoppingCart, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Navigate, useNavigate, useParams } from 'react-router'
import { toast } from 'sonner'
import { getCatalogItems } from '../../api/catalog'
import { getSuppliers, getWarehouses } from '../../api/masterData'
import { approvalThreshold, convertPurchase, pendingCommand, purchase, purchaseAction, purchases, receipts, receive, savePurchase, stockCommand, type Purchase, type PurchaseInput, type PurchaseKind, type ReceiptInput } from '../../api/purchasing'
import { ManagementTable } from '../../components/ManagementTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { StaffAvatar } from '../../components/StaffAvatar'
import { useSession } from '../../lib/session'
import { Field, InlineError, QueryState } from '../master-data/MasterDataCommon'
import './purchasing.css'

export const money = (n: number | null | undefined) => n == null ? '—' : n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
export const dateTime = (s: string) => new Date(s).toLocaleString('th-TH', { dateStyle: 'short', timeStyle: 'short' })
const statuses: Record<string, string> = { draft: 'ฉบับร่าง', pending: 'รออนุมัติ', approved: 'อนุมัติแล้ว', converted: 'แปลงเป็น PO แล้ว', sent: 'ส่งสั่งซื้อแล้ว', partial: 'รับบางส่วน', complete: 'รับครบแล้ว', cancelled: 'ยกเลิก' }
const actions: Record<string, string> = { submit: 'ส่งขออนุมัติ', approve: 'อนุมัติ', return: 'ส่งกลับแก้ไข', send: 'ยืนยันส่งสั่งซื้อ', cancel: 'ยกเลิกเอกสาร' }
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
  const query = useQuery({ queryKey: ['purchases', kind, q, status, page], queryFn: () => purchases(kind, q, status, page) })
  const { session } = useSession()
  const allowed = ['manager', 'office'].includes(session?.user.role.toLowerCase() || '')
  const copy = kind === 'PR'
    ? { title: 'รายการใบขอซื้อ', description: 'สร้างและติดตามคำขอซื้อก่อนส่งอนุมัติ', create: 'สร้างใบขอซื้อ' }
    : { title: 'รายการใบสั่งซื้อ', description: 'จัดการคำสั่งซื้อและติดตามการรับสินค้าเข้าคลัง', create: 'สร้างใบสั่งซื้อ' }
  return <AppShell title={kind === 'PR' ? 'ใบขอซื้อ (PR)' : 'ใบสั่งซื้อ (PO)'}><section className="purchase-page-heading">
    <span className={`purchase-page-heading__icon purchase-page-heading__icon--${kind.toLowerCase()}`} aria-hidden="true">{kind === 'PR' ? <ClipboardList /> : <ShoppingCart />}</span>
    <div className="purchase-page-heading__copy"><h2>{copy.title}</h2><p>{copy.description}</p></div>
    <Button disabled={!allowed} title={!allowed ? 'สำหรับผู้จัดการหรือธุรการจัดซื้อ' : undefined} onClick={() => setCreating(true)}><Plus />{copy.create}</Button>
  </section>
    <Card className="purchase-filters"><div className="input-with-icon"><Search /><Input aria-label="ค้นหาเอกสารจัดซื้อ" placeholder="ค้นหาเลขเอกสาร / ซัพพลายเออร์" value={q} onChange={e => { setQ(e.target.value); setPage(1) }} /></div><Select aria-label="สถานะเอกสาร" value={status} onChange={e => { setStatus(e.target.value); setPage(1) }}><option value="">ทุกสถานะ</option>{Object.entries(statuses).filter(([key]) => kind === 'PR' ? !['converted', 'sent', 'partial', 'complete'].includes(key) : key !== 'converted').map(([key, label]) => <option value={key} key={key}>{label}</option>)}</Select><Button variant="outline" onClick={() => void query.refetch()}><RefreshCw />โหลดใหม่</Button></Card>
    <QueryState query={query} loadingTitle={`กำลังโหลด${copy.title}`} emptyTitle={`ยังไม่มี${copy.title.replace('รายการ', '')}`} emptyReason={`กด “${copy.create}” เพื่อเริ่มต้น`} onRetry={() => void query.refetch()}>
      <Card className="management-table-card"><ManagementTable data={query.data?.items ?? []} sortScope="page" columns={[
        { id: 'number', header: 'เลขเอกสาร', value: (doc) => doc.number, render: (doc) => <><strong>{doc.number}</strong><small className="purchase-sub">{doc.lines.length} รายการ</small></> },
        { id: 'date', header: 'วันที่ / ผู้สร้าง', value: (doc) => new Date(doc.createdAt).getTime(), size: 240, render: (doc) => <>{dateTime(doc.createdAt)}<small className="purchase-sub">{doc.createdByName}</small></> },
        { id: 'supplier', header: 'ซัพพลายเออร์ / คลัง', value: (doc) => [doc.supplierName, doc.warehouseName].filter(Boolean).join(' '), size: 270, render: (doc) => <>{doc.supplierName || 'ยังไม่ระบุซัพพลายเออร์'}<small className="purchase-sub">{doc.warehouseName}</small></> },
        { id: 'status', header: 'สถานะ', value: (doc) => statuses[doc.status] || doc.status, render: (doc) => <><PurchaseStatus status={doc.status} /></> },
        { id: 'total', header: 'มูลค่าก่อนภาษี', value: (doc) => doc.total, render: (doc) => <><span className="money">{money(doc.total)}</span></> },
        { id: 'actions', header: '', render: (doc) => <><Button variant="outline" size="sm" onClick={() => setSelected({ kind, id: doc.id })}><ClipboardList aria-hidden="true" /> เปิดเอกสาร</Button></> },
      ]} />{query.data?.items.length === 0 && <p className="purchase-empty">ยังไม่มีเอกสารที่ตรงกับการค้นหา กด “สร้าง {kind}” เพื่อเริ่มต้น</p>}</Card>
      <div className="purchase-pagination"><span>{query.data?.totalItems ?? 0} เอกสาร · หน้า {page}</span><Button variant="outline" disabled={page === 1} title={page === 1 ? 'อยู่หน้าแรกแล้ว' : undefined} onClick={() => setPage(p => p - 1)}>ก่อนหน้า</Button><Button variant="outline" disabled={page >= (query.data?.totalPages || 1)} title={page >= (query.data?.totalPages || 1) ? 'ไม่มีหน้าถัดไป' : undefined} onClick={() => setPage(p => p + 1)}>ถัดไป</Button></div>
    </QueryState>
    {creating && <PurchaseEditor kind={kind} onClose={() => setCreating(false)} onSaved={doc => { setCreating(false); setSelected({ kind: doc.kind, id: doc.id }) }} />}
    {selected && <PurchaseDetail key={`${selected.kind}-${selected.id}`} {...selected} onClose={() => setSelected(null)} onConverted={() => navigate('/purchasing/po')} />}
  </AppShell>
}

function SupplierSelect({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  const [q, setQ] = useState('')
  const query = useQuery({ queryKey: ['suppliers', 'purchase-picker', q], queryFn: () => getSuppliers({ keyword: q, pageSize: 100 }) })
  const items = Array.isArray(query.data) ? query.data : query.data?.items || []
  return <Field label="ซัพพลายเออร์ *"><Input aria-label="ค้นหาซัพพลายเออร์" placeholder="พิมพ์ค้นหาซัพพลายเออร์" value={q} onChange={e => setQ(e.target.value)} /><Select required value={value} onChange={e => onChange(e.target.value)} aria-label="เลือกซัพพลายเออร์"><option value="">เลือกซัพพลายเออร์</option>{value && !items.some(x => x.id === value) && <option value={value}>ซัพพลายเออร์ที่เลือกไว้</option>}{items.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select>{query.isError && <InlineError error={query.error} />}</Field>
}

function PurchaseEditor({ kind, doc, onClose, onSaved }: { kind: PurchaseKind; doc?: Purchase; onClose: () => void; onSaved: (doc: Purchase) => void }) {
  const [warehouseId, setWarehouse] = useState(doc?.warehouseId || '')
  const [supplierId, setSupplier] = useState(doc?.supplierId || '')
  const [requiredDate, setDate] = useState(doc?.requiredDate?.slice(0, 10) || '')
  const [note, setNote] = useState(doc?.note || ''); const [terms, setTerms] = useState(doc?.paymentTerms || '')
  const [q, setQ] = useState(''); const [error, setError] = useState('')
  const [lines, setLines] = useState((doc?.lines || []).map(x => ({ catalogItemId: x.catalogItemId, code: x.code, name: x.name, unit: x.unit, quantity: String(x.quantity), unitCost: String(x.unitCost) })))
  const warehouses = useQuery({ queryKey: ['warehouses', 'purchase-picker'], queryFn: () => getWarehouses() })
  const catalog = useQuery({ queryKey: ['catalog', 'purchase-picker', q], queryFn: () => getCatalogItems({ type: 'part', keyword: q, pageSize: 100 }) })
  const refresh = usePurchasingRefresh()
  const save = useMutation({ mutationFn: (input: PurchaseInput) => savePurchase(kind, doc?.id || null, input), onSuccess: async saved => { await refresh(saved); toast.success('บันทึกเอกสารแล้ว'); onSaved(saved) } })
  const lockedLines = Boolean(doc?.sourceRequestId)
  const submit = () => {
    if (!lines.length) { setError('กรุณาเพิ่มสินค้าอย่างน้อย 1 รายการ'); return }
    if (lines.some(x => !x.quantity || !x.unitCost || !Number.isInteger(Number(x.quantity)) || Number(x.quantity) <= 0 || Number(x.unitCost) < 0)) { setError('กรุณาระบุจำนวนเต็มบวกและราคาที่ถูกต้อง'); return }
    setError(''); save.mutate({ warehouseId, supplierId: supplierId || null, requiredDate: requiredDate || null, note, paymentTerms: terms, version: doc?.version, lines: lines.map(x => ({ catalogItemId: x.catalogItemId, quantity: Number(x.quantity), unitCost: Number(x.unitCost) })) })
  }
  return <ConfirmModal open title={doc ? `แก้ไข ${doc.number}` : `สร้าง${kind === 'PR' ? 'ใบขอซื้อ (PR)' : 'ใบสั่งซื้อ (PO)'}`} description="ออกเลขเอกสารอัตโนมัติเมื่อบันทึก · จำนวนสินค้าเป็นจำนวนเต็ม" size="xlarge" onClose={() => { if (!save.isPending) onClose() }} footer={<><Button variant="ghost" onClick={onClose} disabled={save.isPending}>ยกเลิก</Button><Button form="purchase-editor" type="submit" disabled={save.isPending}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกฉบับร่าง'}</Button></>}>
    <form id="purchase-editor" className="management-form" onSubmit={e => { e.preventDefault(); submit() }}><fieldset disabled={save.isPending} className="purchase-fieldset"><div className="form-grid">
      <Field label="คลังรับสินค้า *"><Select required value={warehouseId} onChange={e => setWarehouse(e.target.value)}><option value="">เลือกคลัง</option>{warehouses.data?.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select>{warehouses.isError && <InlineError error={warehouses.error} />}</Field>
      <Field label="วันที่ต้องการสินค้า"><Input type="date" value={requiredDate} onChange={e => setDate(e.target.value)} /></Field>
      {kind === 'PO' && <><SupplierSelect value={supplierId} onChange={setSupplier} /><Field label="เงื่อนไขชำระเงิน"><Input maxLength={300} value={terms} onChange={e => setTerms(e.target.value)} /></Field></>}
      <Field label="หมายเหตุ / เหตุผลขอซื้อ" wide><Textarea maxLength={1000} value={note} onChange={e => setNote(e.target.value)} /></Field></div>
      {!lockedLines && <section className="purchase-picker"><Field label="ค้นหาสินค้าเพื่อเพิ่มรายการ"><Input placeholder="รหัสหรือชื่อสินค้า" value={q} onChange={e => setQ(e.target.value)} /></Field><Select aria-label="เพิ่มสินค้าในเอกสาร" value="" onChange={e => { const item = catalog.data?.items.find(x => x.id === e.target.value); if (item && !lines.some(x => x.catalogItemId === item.id)) setLines(old => [...old, { catalogItemId: item.id, code: item.code, name: item.name, unit: item.unit, quantity: '1', unitCost: String(item.cost ?? 0) }]) }}><option value="">{catalog.isPending ? 'กำลังค้นหาสินค้า…' : 'เลือกสินค้าเพื่อเพิ่ม (สูงสุด 100 ผลค้นหา)'}</option>{catalog.data?.items.filter(x => !lines.some(l => l.catalogItemId === x.id)).map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select>{catalog.isError && <InlineError error={catalog.error} />}</section>}
      {lockedLines && <p className="section-help">รายการและจำนวนอ้างอิง PR ที่อนุมัติแล้ว สามารถแก้ไขราคาซื้อได้</p>}
      <div className="purchase-table-scroll"><table className="master-table purchase-lines"><thead><tr><th>สินค้า</th><th>จำนวน</th><th>ราคา/หน่วย</th><th>รวม</th><th /></tr></thead><tbody>{lines.map((line, i) => <tr key={line.catalogItemId}><td><strong>{line.name}</strong><small className="purchase-sub">{line.code} · {line.unit}</small></td><td><Input aria-label={`จำนวน ${line.name}`} required type="number" min="1" max="1000000" step="1" readOnly={lockedLines} value={line.quantity} onChange={e => setLines(old => old.map((x, n) => n === i ? { ...x, quantity: e.target.value } : x))} /></td><td><Input aria-label={`ราคา ${line.name}`} required type="number" min="0" max="100000000" step="0.01" value={line.unitCost} onChange={e => setLines(old => old.map((x, n) => n === i ? { ...x, unitCost: e.target.value } : x))} /></td><td className="money">{money(Number(line.quantity) * Number(line.unitCost))}</td><td>{!lockedLines && <Button variant="ghost" size="icon" aria-label={`ลบ ${line.name}`} onClick={() => setLines(old => old.filter((_, n) => n !== i))}><Trash2 /></Button>}</td></tr>)}</tbody></table></div>
      <p className="purchase-total">มูลค่าก่อนภาษี <strong className="money">{money(lines.reduce((sum, x) => sum + Number(x.quantity) * Number(x.unitCost), 0))} บาท</strong></p>
      {error && <p role="alert" className="field-error">{error}</p>}{save.isError && <InlineError error={save.error} />}</fieldset></form>
  </ConfirmModal>
}

function PurchaseDetail({ kind, id, onClose, onConverted }: { kind: PurchaseKind; id: string; onClose: () => void; onConverted: (doc: Purchase) => void }) {
  const query = useQuery({ queryKey: ['purchase', kind, id], queryFn: () => purchase(kind, id), refetchOnWindowFocus: false })
  const history = useQuery({ queryKey: ['receipts', id], queryFn: () => receipts(id), enabled: kind === 'PO' })
  const policy = useQuery({ queryKey: ['purchase-policy'], queryFn: approvalThreshold })
  const { session } = useSession(); const manager = Boolean(session?.user.canSeeCost)
  const [editing, setEditing] = useState(false); const [receiving, setReceiving] = useState(false)
  const [action, setAction] = useState(''); const [reason, setReason] = useState(''); const [supplier, setSupplier] = useState('')
  const refresh = usePurchasingRefresh(); const doc = query.data
  const hasPendingReceipt = Boolean(pendingCommand(`purchase-receipt:${session?.user.shardKey}:${session?.branchId}:${id}`))
  const mutate = useMutation({ mutationFn: () => action === 'convert' ? convertPurchase(doc!, supplier) : purchaseAction(doc!, action, reason), onSuccess: async saved => { await refresh(saved); setAction(''); setReason(''); toast.success('ดำเนินการสำเร็จ'); if (action === 'convert') onConverted(saved) } })
  if (editing && doc) return <PurchaseEditor kind={kind} doc={doc} onClose={() => setEditing(false)} onSaved={() => setEditing(false)} />
  if (receiving && doc) return <ReceiptForm doc={doc} onClose={() => setReceiving(false)} />
  const canApprove = manager || (kind === 'PO' && policy.data !== undefined && (doc?.total || 0) <= policy.data)
  const choices = doc ? [doc.status === 'draft' ? 'submit' : '', doc.status === 'pending' && canApprove ? 'approve' : '', doc.status === 'pending' && canApprove ? 'return' : '', kind === 'PO' && doc.status === 'approved' ? 'send' : '', !['cancelled', 'complete', 'converted'].includes(doc.status) ? 'cancel' : ''].filter(Boolean) : []
  return <ConfirmModal open title={doc?.number || 'รายละเอียดเอกสาร'} description={<span className={`purchase-document-type purchase-document-type--${kind.toLowerCase()}`}>{kind === 'PR' ? 'ใบขอซื้อ' : 'ใบสั่งซื้อและประวัติรับสินค้า'}</span>} size="xlarge" onClose={() => { if (!mutate.isPending) onClose() }} footer={<Button variant="outline" onClick={onClose} disabled={mutate.isPending}>ปิด</Button>}>
    <QueryState query={query} loadingTitle="กำลังโหลดเอกสาร" emptyTitle="ไม่พบเอกสาร" emptyReason="กรุณาโหลดใหม่" onRetry={() => void query.refetch()}>{doc && <div className="purchase-detail">
      <header className="purchase-summary">
        <div className="purchase-summary__facts">
          <PurchaseStatus status={doc.status} />
          <span><small>คลังรับสินค้า</small><strong>{doc.warehouseName}</strong></span>
          <span><small>ซัพพลายเออร์</small><strong>{doc.supplierName || 'ยังไม่ระบุ'}</strong></span>
          <span><small>วันที่ต้องการ</small><strong>{doc.requiredDate ? new Date(doc.requiredDate).toLocaleDateString('th-TH') : 'ยังไม่ระบุ'}</strong></span>
        </div>
        <div className="purchase-summary__people">
          <PurchaseActor label="ผู้สร้าง" name={doc.createdByName} timestamp={doc.createdAt} />
          <PurchaseActor label="ผู้อนุมัติ" name={doc.approvedByName} timestamp={doc.approvedAt} />
        </div>
      </header>
      {doc.sourceRequestId && <p className="section-help">เอกสารนี้สร้างจาก PR ที่อนุมัติแล้ว</p>}
      <p>{doc.note || 'ไม่มีหมายเหตุ'}{doc.paymentTerms && ` · ชำระเงิน: ${doc.paymentTerms}`}</p>{doc.cancelReason && <p>เหตุผลยกเลิก: {doc.cancelReason}</p>}
      <div className="purchase-table-scroll"><ManagementTable data={doc.lines} columns={[
        { id: 'item', header: 'สินค้า', value: (line) => line.name, size: 260, render: (line) => <>{line.name}<small className="purchase-sub">{line.code} · {line.unit}</small></> },
        { id: 'quantity', header: 'สั่งซื้อ', value: (line) => line.quantity, render: (line) => <>{line.quantity}</> },
        { id: 'cost', header: 'ราคา/หน่วย', value: (line) => line.unitCost, render: (line) => <><span className="money">{money(line.unitCost)}</span></> },
        { id: 'good', header: 'รับดี', value: (line) => line.receivedGood, render: (line) => <>{line.receivedGood}</> },
        { id: 'damaged', header: 'ชำรุด', value: (line) => line.receivedDamaged, render: (line) => <>{line.receivedDamaged}</> },
        { id: 'outstanding', header: 'ค้างรับ', value: (line) => line.outstanding, render: (line) => <>{line.outstanding}</> },
      ]} /></div>
      <p className="purchase-total">มูลค่าก่อนภาษี <strong className="money">{money(doc.total)} บาท</strong></p>
      {kind === 'PO' && policy.data !== undefined && <p className="section-help">วงเงินอนุมัติธุรการ: {money(policy.data)} บาท · ยอดเกินวงเงินต้องให้ผู้จัดการอนุมัติ</p>}
      {doc.status === 'pending' && !canApprove && <p className="section-help">รอผู้จัดการสาขาอนุมัติเอกสารนี้</p>}
      <div className="purchase-actions">{doc.status === 'draft' && <Button variant="outline" disabled={mutate.isPending} onClick={() => setEditing(true)}>แก้ไขฉบับร่าง</Button>}{choices.map(a => <Button key={a} variant={a === 'cancel' ? 'ghost' : 'outline'} disabled={mutate.isPending} onClick={() => { setAction(a); setReason(''); mutate.reset() }}>{actions[a]}</Button>)}{kind === 'PR' && doc.status === 'approved' && <Button disabled={mutate.isPending} onClick={() => { setAction('convert'); mutate.reset() }}>สร้าง PO จาก PR</Button>}{kind === 'PO' && (['sent', 'partial'].includes(doc.status) || hasPendingReceipt) && <Button disabled={mutate.isPending} onClick={() => setReceiving(true)}>{hasPendingReceipt ? 'ตรวจสอบคำขอรับสินค้าที่ค้าง' : 'รับสินค้าเข้าคลัง'}</Button>}</div>
      {action && <form className="purchase-confirm" onSubmit={e => { e.preventDefault(); mutate.mutate() }}><strong>{action === 'convert' ? 'สร้าง PO จาก PR ที่อนุมัติแล้ว' : `ยืนยัน${actions[action]} ${doc.number}`}</strong>{action === 'convert' ? <SupplierSelect value={supplier} onChange={setSupplier} /> : ['cancel', 'return'].includes(action) ? <Field label="เหตุผล *"><Textarea required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /></Field> : <p>ระบบจะบันทึกผู้ดำเนินการและเวลาของรายการนี้</p>}{mutate.isError && <InlineError error={mutate.error} />}<div className="purchase-actions"><Button variant="ghost" disabled={mutate.isPending} onClick={() => setAction('')}>กลับ</Button><Button type="submit" disabled={mutate.isPending}>{mutate.isPending ? 'กำลังดำเนินการ…' : 'ยืนยัน'}</Button></div></form>}
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
