import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, Clock, PauseCircle } from 'lucide-react'
import { useEffect, useState } from 'react'
import { getWarehouses } from '../../api/masterData'
import { createWithdrawal, getJobWithdrawalPlan, pendingCommand, stockCommand, type Withdrawal, type WithdrawalInput, type WithdrawalPlanLine } from '../../api/purchasing'
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
  jobId: string
  jobNo?: string
  onClose: () => void
  onCreated: (withdrawal: Withdrawal) => void
}

/// สร้างใบเบิกสินค้าของงาน (ขั้น "เบิกสินค้า" ใน JobCardModal) หลายรายการในเอกสารเดียว
/// [BIZ] เบิกตามรายการที่ลูกค้าอนุมัติเท่านั้น — เพิ่มสินค้าหรือลบแถวไม่ได้ (คำขอผู้ใช้ 2026-10-02)
/// เบิกบางส่วนได้: จำนวนต่อรายการ 0..ยอดที่ยังไม่ได้เบิก (0 = ยังไม่เบิกรอบนี้ แถวยังอยู่ เบิกต่อในใบถัดไป)
/// รายการ/ยอดคงเหลือมาจาก `/withdrawals/by-job/{jobId}/plan` ที่ server คำนวณ (อนุมัติ − เบิกไปแล้ว) และ API ตรวจซ้ำตอนบันทึก
export function StockWithdrawalModal({ jobId, jobNo, onClose, onCreated }: StockWithdrawalModalProps) {
  const queryClient = useQueryClient()
  const { session } = useSession()
  const key = `stock-withdrawal:${session?.user.shardKey}:${session?.branchId}:${jobId}`
  const pending = pendingCommand<WithdrawalInput>(key)

  const [warehouseId, setWarehouseId] = useState(pending?.warehouseId || '')
  const [requesterStaffId, setRequesterStaffId] = useState(pending?.requesterStaffId ? String(pending.requesterStaffId) : '')
  const [requesterLabel, setRequesterLabel] = useState('')
  const [reason, setReason] = useState(pending?.reason || '')
  const [staffQuery, setStaffQuery] = useState('')
  const [error, setError] = useState('')
  const [quantities, setQuantities] = useState<Record<string, string>>({})

  const warehouses = useQuery({ queryKey: ['warehouses', 'withdrawal-picker'], queryFn: () => getWarehouses() })
  const staffs = useQuery({ queryKey: ['staffs', 'withdrawal-picker', staffQuery], queryFn: () => getStaffs({ keyword: staffQuery, pageSize: 50 }) })
  const plan = useQuery({ queryKey: ['job-withdrawal-plan', jobId], queryFn: () => getJobWithdrawalPlan(jobId) })

  const save = useMutation({
    mutationFn: (input: WithdrawalInput) => stockCommand(key, input, createWithdrawal),
    onSuccess: (withdrawal) => {
      void queryClient.invalidateQueries({ queryKey: ['job-withdrawal-plan', jobId] })
      onCreated(withdrawal)
    },
  })
  const uncertain = Boolean(pending)

  const planLines = plan.data?.lines ?? []
  const toWithdraw = planLines.filter(x => x.remainingQuantity > 0)

  // ค่าเริ่มต้น = เบิกเท่าที่ทำได้ทันที (ยอดคงเหลือ แต่ไม่เกินพร้อมใช้) — ตั้งครั้งเดียวต่อรายการ ไม่ทับที่ผู้ใช้แก้แล้ว
  useEffect(() => {
    if (!plan.data) return
    setQuantities(old => {
      const next = { ...old }
      for (const line of plan.data.lines) {
        if (next[line.catalogItemId] === undefined) next[line.catalogItemId] = String(Math.max(0, Math.min(line.remainingQuantity, line.available)))
      }
      return next
    })
  }, [plan.data])

  const qtyOf = (line: WithdrawalPlanLine) => Number(quantities[line.catalogItemId] ?? 0)
  const invalid = toWithdraw.filter(x => {
    const q = qtyOf(x)
    return !Number.isInteger(q) || q < 0 || q > x.remainingQuantity
  })
  const short = toWithdraw.filter(x => qtyOf(x) > x.available)
  const selected = toWithdraw.filter(x => qtyOf(x) > 0)
  const unavailableItems = plan.data?.unavailableItems ?? []
  const adHocCount = plan.data?.adHocCount ?? 0

  // [UI] ปุ่มที่ปิดต้องบอกเหตุผลเสมอ — คำขอที่ยังไม่ทราบผลส่งซ้ำได้เสมอ (server replay ด้วย RequestId เดิม ไม่เบิกซ้ำ)
  const blockedReason = uncertain ? null
    : plan.isPending ? 'กำลังโหลดรายการที่อนุมัติ…'
      : plan.isError ? 'โหลดรายการที่อนุมัติไม่สำเร็จ — กด "ลองใหม่" ก่อน'
        : !toWithdraw.length
          ? planLines.length
            ? 'เบิกครบทุกรายการที่ลูกค้าอนุมัติแล้ว ไม่มีอะไรเหลือให้เบิก'
            : 'งานนี้ไม่มีสินค้า (อะไหล่) ที่ลูกค้าอนุมัติและเซ็นแล้ว จึงยังสร้างใบเบิกไม่ได้'
          : invalid.length
            ? `จำนวนเบิกของ ${invalid.map(x => x.code).join(', ')} ต้องเป็นจำนวนเต็ม 0 ถึงยอดที่ยังไม่ได้เบิก`
            : short.length
              ? `ยอดพร้อมใช้ไม่พอสำหรับ ${short.map(x => x.code).join(', ')} — ลดจำนวนลงเพื่อเบิกเท่าที่มีก่อน ส่วนที่เหลือเบิกต่อในใบถัดไปได้`
              : !selected.length
                ? 'กรุณาระบุจำนวนเบิกอย่างน้อย 1 รายการ'
                : null

  const submit = () => {
    if (blockedReason) return
    if (!warehouseId) { setError('กรุณาเลือกคลังที่เบิก'); return }
    if (!requesterStaffId) { setError('กรุณาเลือกผู้เบิก'); return }
    if (!reason.trim()) { setError('กรุณาระบุเหตุผลการเบิก'); return }
    setError('')
    const payload = pending || {
      requestId: crypto.randomUUID(),
      warehouseId,
      jobId,
      requesterStaffId: Number(requesterStaffId),
      reason: reason.trim(),
      lines: selected.map(x => ({ catalogItemId: x.catalogItemId, quantity: qtyOf(x) })),
    }
    save.mutate(payload)
  }

  return (
    <ConfirmModal
      open
      title={jobNo ? `สร้างใบเบิกสินค้า · งาน ${jobNo}` : 'สร้างใบเบิกสินค้า'}
      description="เบิกตามรายการที่ลูกค้าอนุมัติเท่านั้น เพิ่มสินค้าอื่นไม่ได้ · เบิกบางส่วนได้ ส่วนที่เหลือเบิกต่อในใบถัดไป · ระบบตัดล็อต FIFO เก่าก่อนให้อัตโนมัติ"
      size="xlarge"
      onClose={() => { if (!save.isPending) onClose() }}
      footer={<>
        <Button variant="ghost" disabled={save.isPending} onClick={onClose}>ยกเลิก</Button>
        <Button form="stock-withdrawal-form" type="submit" disabled={save.isPending || Boolean(blockedReason)}>
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
              <Textarea required maxLength={1000} value={reason} onChange={e => setReason(e.target.value)} placeholder={jobNo ? `เช่น เบิกอะไหล่ให้งาน ${jobNo}` : 'เช่น เบิกอะไหล่ให้งานซ่อม'} />
            </Field>
          </div>
        </fieldset>

        <p className="section-help">
          รายการด้านล่างมาจากบรรทัดอะไหล่ที่ลูกค้าอนุมัติในใบเสนอราคาที่เซ็นแล้วทุกใบของงานนี้ หักจำนวนที่เบิกไปแล้วในใบเบิกก่อนหน้า
          — ใส่ 0 ถ้ายังไม่เบิกรายการนั้นรอบนี้ · ถ้าต้องใช้สินค้าเพิ่ม ให้ออกใบเสนอราคาใหม่หรือฉบับแก้ไขแล้วให้ลูกค้าอนุมัติก่อน
        </p>
        {adHocCount > 0 && (
          <p role="status" className="section-help">
            มี {adHocCount} รายการนอกแคตตาล็อกที่ลูกค้าอนุมัติ — เป็นของซื้อนอกที่ไม่มีในคลัง จึงไม่อยู่ในใบเบิก
          </p>
        )}
        {unavailableItems.length > 0 && (
          <p role="status" className="section-help">
            ไม่อยู่ในใบเบิกเพราะไม่พบในแคตตาล็อกหรือยังไม่ได้ตั้งยอด FIFO: {unavailableItems.join(', ')}
          </p>
        )}

        {plan.isPending ? (
          <p role="status" className="section-help">กำลังโหลดรายการที่ลูกค้าอนุมัติ…</p>
        ) : plan.isError ? (
          <div>
            <InlineError error={plan.error} />
            <Button variant="outline" size="sm" onClick={() => void plan.refetch()}>ลองใหม่</Button>
          </div>
        ) : !planLines.length ? (
          <p className="purchase-empty">ไม่มีสินค้า (อะไหล่) ที่ลูกค้าอนุมัติและเซ็นแล้วในงานนี้</p>
        ) : (
          <div className="purchase-table-scroll">
            <table className="master-table purchase-lines">
              <thead><tr><th>สินค้า</th><th>อนุมัติ</th><th>เบิกแล้ว</th><th>คงเหลือ</th><th>เบิกครั้งนี้</th><th>พร้อมใช้</th><th>สถานะ</th></tr></thead>
              <tbody>
                {planLines.map(line => (
                  <tr key={line.catalogItemId}>
                    <td><strong>{line.name}</strong><small className="purchase-sub">{line.code} · {line.unit}</small></td>
                    <td>{line.approvedQuantity}</td>
                    <td>{line.withdrawnQuantity}</td>
                    <td>{line.remainingQuantity}</td>
                    <td>
                      {line.remainingQuantity > 0 ? (
                        <Input
                          aria-label={`จำนวนเบิก ${line.name} (สูงสุด ${line.remainingQuantity})`}
                          type="number"
                          min="0"
                          max={line.remainingQuantity}
                          step="1"
                          disabled={save.isPending || uncertain}
                          value={quantities[line.catalogItemId] ?? ''}
                          onChange={e => setQuantities(old => ({ ...old, [line.catalogItemId]: e.target.value }))}
                        />
                      ) : '—'}
                    </td>
                    <td>{line.available}</td>
                    <td><LineStatus line={line} quantity={qtyOf(line)} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {Boolean(selected.length) && (
          <p className="purchase-total">
            จำนวนรวม <strong>{selected.reduce((sum, x) => sum + qtyOf(x), 0)}</strong> ชิ้น จาก {selected.length} รายการ
          </p>
        )}
        {blockedReason && !plan.isPending && <p role="status" className="field-error">{blockedReason}</p>}
        {error && <p role="alert" className="field-error">{error}</p>}
        {save.isError && <InlineError error={save.error} />}
      </form>
    </ConfirmModal>
  )
}

/// [UI] สถานะต่อบรรทัด สี + ไอคอน + ข้อความ
function LineStatus({ line, quantity }: { line: WithdrawalPlanLine; quantity: number }) {
  if (line.remainingQuantity === 0) {
    return <span className="purchase-status purchase-status--complete"><CheckCircle2 size={14} aria-hidden="true" /> เบิกครบแล้ว</span>
  }
  if (quantity > line.available) {
    return <span className="purchase-status purchase-status--cancelled"><AlertTriangle size={14} aria-hidden="true" /> พร้อมใช้ไม่พอ</span>
  }
  if (quantity <= 0) {
    return <span className="purchase-status"><PauseCircle size={14} aria-hidden="true" /> ยังไม่เบิกรอบนี้</span>
  }
  if (quantity < line.remainingQuantity) {
    return <span className="purchase-status purchase-status--partial"><Clock size={14} aria-hidden="true" /> เบิกบางส่วน (ค้าง {line.remainingQuantity - quantity})</span>
  }
  return <span className="purchase-status purchase-status--sent"><Clock size={14} aria-hidden="true" /> เบิกครบในใบนี้</span>
}
