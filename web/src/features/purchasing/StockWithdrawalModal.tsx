import { useMutation, useQuery } from '@tanstack/react-query'
import { Trash2 } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { getCatalogItems } from '../../api/catalog'
import { getWarehouses } from '../../api/masterData'
import { createWithdrawal, pendingCommand, stockCommand, type Withdrawal, type WithdrawalInput } from '../../api/purchasing'
import { getQuotation, getQuotations } from '../../api/quotations'
import { getStaffs } from '../../api/staffs'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Combobox } from '../../components/ui/combobox'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { useSession } from '../../lib/session'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import './purchasing.css'

type StockWithdrawalModalProps = {
  jobId: string | null
  jobNo?: string
  onClose: () => void
  onCreated: (withdrawal: Withdrawal) => void
}

type Line = { catalogItemId: string; code: string; name: string; unit: string; available: number; quantity: string }

/// สร้างใบเบิกสินค้าหลายรายการในเอกสารเดียว — ใช้ทั้งจากหน้าสต็อกและจากขั้น "เบิกสินค้า" ใน JobCardModal
/// เมื่อผูก jobId แล้วเลือกจะบันทึกอ้างอิงงานไว้ในทุกบรรทัดของใบเบิก (ดู StockMovement.JobId)
export function StockWithdrawalModal({ jobId, jobNo, onClose, onCreated }: StockWithdrawalModalProps) {
  const { session } = useSession()
  const key = `stock-withdrawal:${session?.user.shardKey}:${session?.branchId}:${jobId ?? 'adhoc'}`
  const pending = pendingCommand<WithdrawalInput>(key)

  const [warehouseId, setWarehouseId] = useState(pending?.warehouseId || '')
  const [requesterStaffId, setRequesterStaffId] = useState(pending?.requesterStaffId ? String(pending.requesterStaffId) : '')
  const [requesterLabel, setRequesterLabel] = useState('')
  const [reason, setReason] = useState(pending?.reason || '')
  const [staffQuery, setStaffQuery] = useState('')
  const [itemQuery, setItemQuery] = useState('')
  const [lines, setLines] = useState<Line[]>([])
  const [error, setError] = useState('')

  const warehouses = useQuery({ queryKey: ['warehouses', 'withdrawal-picker'], queryFn: () => getWarehouses() })
  const staffs = useQuery({ queryKey: ['staffs', 'withdrawal-picker', staffQuery], queryFn: () => getStaffs({ keyword: staffQuery, pageSize: 50 }) })
  const catalog = useQuery({ queryKey: ['catalog', 'withdrawal-picker', itemQuery], queryFn: () => getCatalogItems({ type: 'part', keyword: itemQuery, pageSize: 100 }) })

  const save = useMutation({
    mutationFn: (input: WithdrawalInput) => stockCommand(key, input, createWithdrawal),
    onSuccess: onCreated,
  })
  const uncertain = Boolean(pending)

  // เตรียมรายการสินค้า (ไม่รวมค่าแรง) จากใบเสนอราคาที่อนุมัติแล้วของ job นี้ให้อัตโนมัติ — ผู้ใช้แก้จำนวน/ลบได้ก่อนบันทึก
  const jobQuotations = useQuery({
    queryKey: ['job-quotations', jobId ?? ''],
    queryFn: () => getQuotations('', jobId!),
    enabled: Boolean(jobId) && !uncertain,
  })
  const approvedQuotationId = jobQuotations.data?.find(q => q.status === 'approved')?.id ?? null
  const approvedQuotation = useQuery({
    queryKey: ['quotation', approvedQuotationId ?? ''],
    queryFn: () => getQuotation(approvedQuotationId!),
    enabled: Boolean(approvedQuotationId),
  })
  const [prefilling, setPrefilling] = useState(false)
  const [skippedAdHocCount, setSkippedAdHocCount] = useState(0)
  const prefilledRef = useRef(false)

  useEffect(() => {
    if (prefilledRef.current || uncertain || !approvedQuotation.data) return
    prefilledRef.current = true
    const approvedPartLines = approvedQuotation.data.lines.filter(l => l.type === 'part' && l.approvalStatus === 'approved')
    // [BIZ] รายการนอกแคตตาล็อกไม่มี catalogItemId ให้เบิกจากสต็อกได้ — ข้ามอย่างชัดเจนแทนยิงค้นหาแล้วปล่อยให้หายเงียบๆ
    // เหมือนก่อนหน้านี้ (docs/07-quotation-adhoc-line.md)
    const partLines = approvedPartLines.filter(l => !l.isAdHoc)
    setSkippedAdHocCount(approvedPartLines.length - partLines.length)
    if (!partLines.length) return
    setPrefilling(true)
    void (async () => {
      const resolved: Line[] = []
      for (const partLine of partLines) {
        try {
          const found = await getCatalogItems({ type: 'part', keyword: partLine.catalogCode, pageSize: 5 })
          const item = found.items.find(x => x.code === partLine.catalogCode)
          if (item) resolved.push({ catalogItemId: item.id, code: item.code, name: item.name, unit: item.unit, available: item.available, quantity: String(Math.max(1, Math.round(partLine.quantity))) })
        } catch { /* ข้ามรายการที่ค้นหาไม่สำเร็จ — ผู้ใช้เพิ่มเองได้จากช่องค้นหาด้านล่าง */ }
      }
      setLines(old => [...old, ...resolved.filter(r => !old.some(o => o.catalogItemId === r.catalogItemId))])
      setPrefilling(false)
    })()
  }, [approvedQuotation.data, uncertain])

  const submit = () => {
    if (!warehouseId) { setError('กรุณาเลือกคลังที่เบิก'); return }
    if (!requesterStaffId) { setError('กรุณาเลือกผู้เบิก'); return }
    if (!reason.trim()) { setError('กรุณาระบุเหตุผลการเบิก'); return }
    if (!lines.length) { setError('กรุณาเพิ่มสินค้าอย่างน้อย 1 รายการ'); return }
    if (lines.some(x => !x.quantity || !Number.isInteger(Number(x.quantity)) || Number(x.quantity) <= 0)) {
      setError('กรุณาระบุจำนวนเบิกเป็นจำนวนเต็มบวก'); return
    }
    setError('')
    const payload = pending || {
      requestId: crypto.randomUUID(),
      warehouseId,
      jobId,
      requesterStaffId: Number(requesterStaffId),
      reason: reason.trim(),
      lines: lines.map(x => ({ catalogItemId: x.catalogItemId, quantity: Number(x.quantity) })),
    }
    save.mutate(payload)
  }

  return (
    <ConfirmModal
      open
      title={jobNo ? `สร้างใบเบิกสินค้า · งาน ${jobNo}` : 'สร้างใบเบิกสินค้า'}
      description="เบิกได้หลายรายการในใบเดียว ระบบตัดล็อต FIFO เก่าก่อนให้อัตโนมัติ"
      size="xlarge"
      onClose={() => { if (!save.isPending) onClose() }}
      footer={<>
        <Button variant="ghost" disabled={save.isPending} onClick={onClose}>ยกเลิก</Button>
        <Button form="stock-withdrawal-form" type="submit" disabled={save.isPending}>
          {save.isPending ? 'กำลังบันทึก…' : uncertain ? 'ตรวจสอบ / ส่งคำขอเดิมซ้ำ' : 'สร้างใบเบิก'}
        </Button>
      </>}
    >
      {uncertain && <p role="status">มีคำขอเบิกก่อนหน้ายังไม่ทราบผล กดส่งคำขอเดิมซ้ำเพื่อยืนยันโดยไม่เบิกซ้ำ</p>}
      <form id="stock-withdrawal-form" className="management-form" onSubmit={e => { e.preventDefault(); submit() }}>
        <fieldset disabled={save.isPending || uncertain} className="purchase-fieldset">
          <div className="form-grid">
            <Field label="คลังที่เบิก *">
              <Select required value={warehouseId} onChange={e => setWarehouseId(e.target.value)}>
                <option value="">เลือกคลัง</option>
                {warehouses.data?.map(w => <option key={w.id} value={w.id}>{w.code} · {w.name}</option>)}
              </Select>
              {warehouses.isError && <InlineError error={warehouses.error} />}
            </Field>
            <Field label="ผู้เบิก *">
              <Combobox
                ariaLabel="ค้นหาและเลือกผู้เบิก"
                placeholder="พิมพ์ค้นหาชื่อหรือรหัสพนักงาน"
                query={staffQuery}
                onQueryChange={setStaffQuery}
                selectedLabel={requesterLabel}
                loading={staffs.isFetching}
                loadingLabel="กำลังค้นหาพนักงาน…"
                emptyLabel={staffQuery ? 'ไม่พบพนักงานที่ค้นหา' : 'พิมพ์เพื่อค้นหาพนักงาน'}
                options={(staffs.data?.items ?? []).map(s => ({ value: String(s.id), label: `${s.code} · ${s.fullName}` }))}
                onSelect={option => { setRequesterStaffId(option.value); setRequesterLabel(option.label) }}
              />
              {staffs.isError && <InlineError error={staffs.error} />}
            </Field>
            <Field label="เหตุผลการเบิก *" wide>
              <Textarea required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} placeholder={jobNo ? `เช่น เบิกอะไหล่ให้งาน ${jobNo}` : 'เช่น เบิกใช้งานซ่อมทั่วไป'} />
            </Field>
          </div>

          {prefilling && <p role="status" className="section-help">กำลังดึงรายการสินค้าจากใบเสนอราคาที่อนุมัติแล้วให้อัตโนมัติ…</p>}
          {skippedAdHocCount > 0 && (
            <p role="status" className="section-help">
              มี {skippedAdHocCount} รายการนอกแคตตาล็อกในใบเสนอราคานี้ — เป็นของซื้อนอกที่ไม่มีในคลัง จึงไม่ดึงมาเป็นรายการเบิกให้อัตโนมัติ
              (เบิกได้ปกติถ้ามีสินค้าที่ใกล้เคียงในคลังจริง ค้นหาเพิ่มเองได้ด้านล่าง)
            </p>
          )}

          <section className="purchase-picker">
            <Field label="ค้นหาสินค้าเพื่อเพิ่มรายการ">
              <Combobox
                ariaLabel="ค้นหาและเพิ่มสินค้าในใบเบิก"
                placeholder="พิมพ์รหัสหรือชื่อสินค้า"
                query={itemQuery}
                onQueryChange={setItemQuery}
                loading={catalog.isFetching}
                loadingLabel="กำลังค้นหาสินค้า…"
                emptyLabel="ไม่พบสินค้าที่ค้นหา (สูงสุด 100 ผลค้นหา)"
                options={(catalog.data?.items ?? [])
                  .filter(x => !lines.some(l => l.catalogItemId === x.id))
                  .map(x => ({ value: x.id, label: `${x.code} · ${x.name}`, description: `พร้อมใช้ ${x.available} ${x.unit}` }))}
                onSelect={option => {
                  const item = catalog.data?.items.find(x => x.id === option.value)
                  if (item) setLines(old => [...old, { catalogItemId: item.id, code: item.code, name: item.name, unit: item.unit, available: item.available, quantity: '1' }])
                }}
              />
            </Field>
            {catalog.isError && <InlineError error={catalog.error} />}
          </section>

          <div className="purchase-table-scroll">
            <table className="master-table purchase-lines">
              <thead><tr><th>สินค้า</th><th>พร้อมใช้</th><th>จำนวนเบิก</th><th /></tr></thead>
              <tbody>
                {lines.map((line, i) => (
                  <tr key={line.catalogItemId}>
                    <td><strong>{line.name}</strong><small className="purchase-sub">{line.code} · {line.unit}</small></td>
                    <td>{line.available}</td>
                    <td>
                      <Input
                        aria-label={`จำนวนเบิก ${line.name}`}
                        required
                        type="number"
                        min="1"
                        max={Math.max(1, line.available)}
                        step="1"
                        value={line.quantity}
                        onChange={e => setLines(old => old.map((x, n) => n === i ? { ...x, quantity: e.target.value } : x))}
                      />
                    </td>
                    <td>
                      <Button variant="ghost" size="icon" aria-label={`ลบ ${line.name}`} onClick={() => setLines(old => old.filter((_, n) => n !== i))}>
                        <Trash2 />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {Boolean(lines.length) && (
            <p className="purchase-total">
              จำนวนรวม <strong>{lines.reduce((sum, x) => sum + (Number(x.quantity) || 0), 0)}</strong> ชิ้น จาก {lines.length} รายการ
            </p>
          )}
          {error && <p role="alert" className="field-error">{error}</p>}
          {save.isError && <InlineError error={save.error} />}
        </fieldset>
      </form>
    </ConfirmModal>
  )
}
