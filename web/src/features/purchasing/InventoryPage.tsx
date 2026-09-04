import { useMutation, useQuery } from '@tanstack/react-query'
import { Boxes, CheckCircle2, Clock3, RefreshCw, Search } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { getWarehouses } from '../../api/masterData'
import { pendingCommand, stockCommand, stockDetail, stockIssue, stockItems, stockOpening, type IssueInput, type StockDetail, type StockMovement } from '../../api/purchasing'
import { ManagementTable } from '../../components/ManagementTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { useSession } from '../../lib/session'
import { Field, InlineError, QueryState } from '../master-data/MasterDataCommon'
import { dateTime, money, usePurchasingRefresh } from './PurchasingPage'

export function InventoryPage() {
  const [q, setQ] = useState(''); const [selected, setSelected] = useState<string | null>(null)
  const query = useQuery({ queryKey: ['inventory', q], queryFn: () => stockItems(q) })
  const { session } = useSession(); const manager = Boolean(session?.user.canSeeCost)
  return <AppShell title="สต็อก FIFO"><section className="management-heading"><div><span className="page-eyebrow">INVENTORY</span><h2>สต็อกและต้นทุน FIFO</h2><p>รับล็อตเก่าก่อน เบิกล็อตเก่าก่อน · จำนวนพร้อมใช้ = คงเหลือ − จองแล้ว</p></div><Button variant="outline" onClick={() => void query.refetch()}><RefreshCw />โหลดใหม่</Button></section>
    <Card className="purchase-filters"><div className="input-with-icon"><Search /><Input aria-label="ค้นหาสต็อก" placeholder="ค้นหารหัส / ชื่อสินค้า" value={q} onChange={e => setQ(e.target.value)} /></div><span className="section-help">สูงสุด 200 ผลค้นหา · กดรายละเอียดเพื่อดูล็อตและประวัติ</span></Card>
    <QueryState query={query} loadingTitle="กำลังโหลดสต็อก" emptyTitle="ยังไม่มีสินค้าอะไหล่ที่ตรงกับการค้นหา" emptyReason="เพิ่มสินค้าจากเมนูสินค้า หรือลองเปลี่ยนคำค้น" onRetry={() => void query.refetch()}>
      <Card className="management-table-card"><ManagementTable data={query.data ?? []} sortScope="loaded" columns={[
        { id: 'item', header: 'สินค้า', value: (item) => item.name, size: 260, render: (item) => <><strong>{item.name}</strong><small className="purchase-sub">{item.code} · {item.unit}</small></> },
        { id: 'fifo', header: 'การใช้ FIFO', value: (item) => item.stockManaged ? 'ใช้ FIFO แล้ว' : 'รอตั้งยอดยกมา', size: 190, render: (item) => <><span className={`purchase-status purchase-status--${item.stockManaged ? 'approved' : 'pending'}`}>{item.stockManaged ? <CheckCircle2 size={14} /> : <Clock3 size={14} />}{item.stockManaged ? 'ใช้ FIFO แล้ว' : 'รอตั้งยอดยกมา'}</span></> },
        { id: 'onHand', header: 'คงเหลือ', value: (item) => item.onHand, size: 120, render: (item) => <>{item.onHand}</> },
        { id: 'reserved', header: 'จอง', value: (item) => item.reserved, size: 120, render: (item) => <>{item.reserved}</> },
        { id: 'available', header: 'พร้อมใช้', value: (item) => item.available, size: 120, render: (item) => <><strong>{item.available}</strong></> },
        { id: 'onOrder', header: 'รอรับ', value: (item) => item.onOrder, size: 120, render: (item) => <>{item.onOrder}</> },
        { id: 'damaged', header: 'ชำรุด', value: (item) => item.damaged, size: 120, render: (item) => <>{item.damaged}</> },
        { id: 'value', header: 'มูลค่า FIFO', value: (item) => item.value, hidden: !manager, render: (item) => <><span className="money">{money(item.value)}</span></> },
        { id: 'actions', header: '', render: (item) => <><Button variant="outline" size="sm" onClick={() => setSelected(item.id)}>รายละเอียด</Button></> },
      ]} /></Card>
    </QueryState>{selected && <StockModal key={selected} id={selected} onClose={() => setSelected(null)} />}
  </AppShell>
}

function StockModal({ id, onClose }: { id: string; onClose: () => void }) {
  const query = useQuery({ queryKey: ['stock-detail', id], queryFn: () => stockDetail(id) })
  const [mode, setMode] = useState<'opening' | 'issue' | null>(null)
  const { session } = useSession(); const manager = Boolean(session?.user.canSeeCost)
  const hasPendingIssue = Boolean(pendingCommand(`stock-issue:${session?.user.shardKey}:${session?.branchId}:${id}`))
  if (mode && query.data) return <StockForm mode={mode} detail={query.data} onClose={() => setMode(null)} />
  return <ConfirmModal open title={query.data?.item.name || 'รายละเอียดสต็อก'} description="ล็อตเรียงตามเวลารับเข้าระบบ · ประวัติแสดงยอดของดีรวมทั้งสาขา" size="xlarge" onClose={onClose} footer={<Button variant="outline" onClick={onClose}>ปิด</Button>}>
    <QueryState query={query} loadingTitle="กำลังโหลดล็อตและประวัติ" emptyTitle="ไม่พบสินค้า" emptyReason="ลองโหลดข้อมูลใหม่" onRetry={() => void query.refetch()}>{query.data && <div className="purchase-detail">
      <div className="purchase-summary"><span><Boxes size={18} />{query.data.item.code}</span><span>คงเหลือ {query.data.item.onHand}</span><span>จอง {query.data.item.reserved}</span><strong>พร้อมใช้ {query.data.item.available}</strong><span>ชำรุด {query.data.item.damaged}</span></div>
      <div className="purchase-actions">{!query.data.item.stockManaged ? <><Button disabled={!manager} title={!manager ? 'เฉพาะผู้จัดการตั้งยอดยกมาได้' : undefined} onClick={() => setMode('opening')}>ตั้งยอดยกมา FIFO</Button><p className="section-help">ยืนยันจำนวนเดิม คลังที่เก็บ และต้นทุนล็อตยกมาก่อนใช้ FIFO</p></> : <Button disabled={query.data.item.available <= 0 && !hasPendingIssue} title={query.data.item.available <= 0 && !hasPendingIssue ? 'ไม่มีจำนวนพร้อมใช้ให้เบิก' : undefined} onClick={() => setMode('issue')}>{hasPendingIssue ? 'ตรวจสอบคำขอเบิกที่ค้าง' : 'เบิกสินค้า FIFO'}</Button>}<Button variant="outline" onClick={() => void query.refetch()}>โหลดล่าสุด</Button></div>
      <section><h3>ล็อตสินค้า</h3><div className="purchase-table-scroll"><ManagementTable data={query.data.lots} columns={[
        { id: 'document', header: 'เอกสารรับ / เวลา', value: (lot) => new Date(lot.receivedAt).getTime(), size: 260, render: (lot) => <>{lot.documentNumber}<small className="purchase-sub">{dateTime(lot.receivedAt)}</small></> },
        { id: 'warehouse', header: 'คลัง', value: (lot) => lot.warehouseName, render: (lot) => <>{lot.warehouseName}</> },
        { id: 'received', header: 'รับเข้า', value: (lot) => lot.receivedQuantity, render: (lot) => <>{lot.receivedQuantity}</> },
        { id: 'remaining', header: 'คงเหลือ', value: (lot) => lot.remainingQuantity, render: (lot) => <>{lot.remainingQuantity}</> },
        { id: 'cost', header: 'ต้นทุน/หน่วย', value: (lot) => lot.unitCost, hidden: !manager, render: (lot) => <><span className="money">{money(lot.unitCost)}</span></> },
        { id: 'value', header: 'มูลค่าคงเหลือ', value: (lot) => lot.value, hidden: !manager, render: (lot) => <><span className="money">{money(lot.value)}</span></> },
      ]} />{!query.data.lots.length && <p className="purchase-empty">ยังไม่มีล็อตของดี</p>}</div></section>
      <section><h3>ประวัติการเคลื่อนไหว (ล่าสุด 200 รายการ)</h3><div className="purchase-table-scroll"><ManagementTable data={query.data.movements} columns={[
        { id: 'document', header: 'เอกสาร / เวลา', value: (m) => new Date(m.occurredAt).getTime(), size: 240, render: (m) => <>{m.documentNumber}<small className="purchase-sub">{dateTime(m.occurredAt)}</small></> },
        { id: 'type', header: 'ประเภท', value: (m) => ({ opening: 'ยอดยกมา', receipt: 'รับสินค้า', issue: 'เบิก FIFO' }[m.type] || m.type), render: (m) => <>{{ opening: 'ยอดยกมา', receipt: 'รับสินค้า', issue: 'เบิก FIFO' }[m.type] || m.type}</> },
        { id: 'quantity', header: 'เปลี่ยนแปลง', value: (m) => m.quantity, render: (m) => <>{m.quantity > 0 ? '+' : ''}{m.quantity}</> },
        { id: 'damaged', header: 'ชำรุดเพิ่ม', value: (m) => m.damagedQuantity, render: (m) => <>{m.damagedQuantity}</> },
        { id: 'balance', header: 'ก่อน → หลัง', value: (m) => m.balanceAfter, render: (m) => <>{m.balanceBefore} → {m.balanceAfter}</> },
        { id: 'cost', header: 'ต้นทุน/หน่วย', value: (m) => m.unitCost, hidden: !manager, render: (m) => <><span className="money">{money(m.unitCost)}</span></> },
        { id: 'reason', header: 'เหตุผล / ผู้ดำเนินการ', value: (m) => [m.reason, m.performedByName].filter(Boolean).join(' '), size: 260, render: (m) => <>{m.reason}<small className="purchase-sub">{m.performedByName}</small></> },
      ]} />{!query.data.movements.length && <p className="purchase-empty">ยังไม่มีประวัติการเคลื่อนไหว</p>}</div></section>
    </div>}</QueryState>
  </ConfirmModal>
}

function StockForm({ detail, mode, onClose }: { detail: StockDetail; mode: 'opening' | 'issue'; onClose: () => void }) {
  const { session } = useSession(); const key = `stock-issue:${session?.user.shardKey}:${session?.branchId}:${detail.item.id}`
  const [pending] = useState(() => pendingCommand<IssueInput>(key))
  const [warehouse, setWarehouse] = useState(pending?.warehouseId || '')
  const [quantity, setQuantity] = useState(String(pending?.quantity || 1)); const [cost, setCost] = useState('')
  const [reason, setReason] = useState(pending?.reason || ''); const [requestId] = useState(pending?.requestId || crypto.randomUUID())
  const warehouses = useQuery({ queryKey: ['warehouses', 'purchase-picker'], queryFn: () => getWarehouses() })
  const refresh = usePurchasingRefresh()
  const save = useMutation<boolean | StockMovement[], Error, void>({ mutationFn: () => mode === 'opening' ? stockOpening({ catalogItemId: detail.item.id, warehouseId: warehouse, unitCost: Number(cost), expectedOnHand: detail.item.onHand, expectedDamaged: detail.item.damaged, reason }) : stockCommand(key, pendingCommand<IssueInput>(key) || { requestId, catalogItemId: detail.item.id, warehouseId: warehouse, quantity: Number(quantity), reason }, stockIssue), onSuccess: async () => { await refresh(); toast.success(mode === 'opening' ? 'ตั้งยอดยกมา FIFO เรียบร้อยแล้ว' : 'เบิกสินค้า FIFO เรียบร้อยแล้ว'); onClose() } })
  const uncertain = mode === 'issue' && Boolean(pendingCommand(key))
  const balance = detail.lots.filter(x => x.warehouseId === warehouse).reduce((s, x) => s + x.remainingQuantity, 0)
  return <ConfirmModal open title={mode === 'opening' ? `ตั้งยอดยกมา · ${detail.item.name}` : `เบิก FIFO · ${detail.item.name}`} description={mode === 'opening' ? 'นำจำนวนเดิมเข้าสู่ล็อต FIFO ครั้งเดียว โดยยอดรวมสินค้าไม่เพิ่มซ้ำ' : 'ระบบตัดล็อตที่รับก่อนในคลังนี้อัตโนมัติ โดยกันยอดจองของสาขาไว้'} onClose={() => { if (!save.isPending) onClose() }} footer={<><Button variant="ghost" disabled={save.isPending} onClick={onClose}>กลับ</Button><Button type="submit" form="stock-form" disabled={save.isPending}>{save.isPending ? 'กำลังบันทึก…' : uncertain ? 'ตรวจสอบ / ส่งคำขอเดิมซ้ำ' : 'ยืนยันบันทึก'}</Button></>}>
    {uncertain && <p role="status">คำขอเบิกก่อนหน้ายังไม่ทราบผล ส่งคำขอเดิมซ้ำได้โดยไม่ตัดสต็อกซ้ำ</p>}
    <form id="stock-form" onSubmit={e => { e.preventDefault(); save.mutate() }}><fieldset className="purchase-fieldset management-form" disabled={save.isPending || uncertain}>
      <Field label="คลังสินค้า *"><Select required value={warehouse} onChange={e => setWarehouse(e.target.value)}><option value="">เลือกคลัง</option>{warehouses.data?.map(x => <option key={x.id} value={x.id}>{x.code} · {x.name}</option>)}</Select>{warehouses.isError && <InlineError error={warehouses.error} />}</Field>
      {mode === 'opening' ? <><p>ของดีเดิม <strong>{detail.item.onHand}</strong> · ของชำรุดเดิม <strong>{detail.item.damaged}</strong> · จองแล้ว <strong>{detail.item.reserved}</strong></p><Field label="ต้นทุนต่อหน่วยของยอดยกมา (บาท) *"><Input required type="number" min="0" max="100000000" step="0.01" value={cost} onChange={e => setCost(e.target.value)} /></Field><p className="section-help">ต้นทุนนี้ใช้กับจำนวนของดีเดิมทั้งหมด โปรดตรวจสอบก่อนยืนยัน</p></> : <><p>ของดีในคลังนี้ {balance} · พร้อมใช้ทั้งสาขา {detail.item.available}</p><Field label="จำนวนเบิก *"><Input required type="number" min="1" max={Math.max(0, Math.min(balance, detail.item.available))} step="1" value={quantity} onChange={e => setQuantity(e.target.value)} /></Field></>}
      <Field label="เหตุผล / เลขงานอ้างอิง *"><Textarea required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} /></Field>
    </fieldset>{save.isError && <InlineError error={save.error} />}</form>
  </ConfirmModal>
}
