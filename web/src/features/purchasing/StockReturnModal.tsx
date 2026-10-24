import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { createReturn, getWithdrawal, pendingCommand, RETURN_REASONS, stockCommand, type ReturnInput, type StockReturn } from '../../api/purchasing'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { useSession } from '../../lib/session'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import './purchasing.css'

type StockReturnModalProps = {
  operationId: string
  onClose: () => void
  onCreated: (returned: StockReturn) => void
}

/// คืนอะไหล่จากใบเบิก WD- ของงาน — ของกลับเข้าล็อตเดิมที่ต้นทุนเดิม (server คำนวณ) เฉพาะของสภาพดี
/// จำนวนที่คืนได้ต่อรายการ = เบิกในใบนี้ − คืนไปแล้ว (มาจากใบเบิกที่ server ส่งมา) และ API ตรวจซ้ำตอนบันทึก
export function StockReturnModal({ operationId, onClose, onCreated }: StockReturnModalProps) {
  const queryClient = useQueryClient()
  const { session } = useSession()
  const key = `stock-return:${session?.user.shardKey}:${session?.branchId}:${operationId}`
  const pending = pendingCommand<ReturnInput>(key)
  const [reasonCode, setReasonCode] = useState(pending?.reasonCode || '')
  const [note, setNote] = useState(pending?.note || '')
  const [quantities, setQuantities] = useState<Record<string, string>>(
    () => Object.fromEntries((pending?.lines ?? []).map(l => [l.catalogItemId, String(l.quantity)])))
  const [requestId] = useState(pending?.requestId || crypto.randomUUID())

  const withdrawal = useQuery({ queryKey: ['stock-withdrawal', operationId], queryFn: () => getWithdrawal(operationId) })
  const lines = (withdrawal.data?.lines ?? []).map(l => ({ ...l, returnable: l.quantity - (l.returnedQuantity ?? 0) }))
  const qtyOf = (id: string) => { const n = Number(quantities[id]); return Number.isInteger(n) && n > 0 ? n : 0 }
  const selected = lines.filter(l => qtyOf(l.catalogItemId) > 0)
  const overLine = lines.find(l => qtyOf(l.catalogItemId) > l.returnable)

  const save = useMutation({
    mutationFn: (input: ReturnInput) => stockCommand(key, input, (i) => createReturn(operationId, i)),
    onSuccess: (returned) => {
      for (const k of ['stock-withdrawal', 'job-withdrawals', 'job-withdrawal-plan', 'inventory', 'stock-detail', 'purchase-count'])
        void queryClient.invalidateQueries({ queryKey: [k] })
      onCreated(returned)
    },
  })
  const uncertain = Boolean(pending)

  const blockedReason = !withdrawal.data ? ''
    : !lines.some(l => l.returnable > 0) ? 'ใบเบิกนี้คืนครบทุกรายการแล้ว'
    : !reasonCode ? 'เลือกเหตุผลการคืน'
    : reasonCode === 'other' && !note.trim() ? 'เหตุผล "อื่นๆ" ต้องระบุหมายเหตุ'
    : overLine ? `คืน ${overLine.code} ได้ไม่เกิน ${overLine.returnable} ${overLine.unit}`
    : !selected.length ? 'ใส่จำนวนที่คืนอย่างน้อย 1 รายการ'
    : ''

  const submit = () => {
    if (blockedReason && !uncertain) return
    save.mutate(pending ?? {
      requestId, reasonCode, note: note.trim() || undefined,
      lines: selected.map(l => ({ catalogItemId: l.catalogItemId, quantity: qtyOf(l.catalogItemId) })),
    })
  }

  return (
    <ConfirmModal
      open
      title={`คืนสินค้า · ${withdrawal.data?.documentNumber ?? 'ใบเบิก'}`}
      description="ของที่คืนกลับเข้าล็อตเดิมที่ต้นทุนเดิม และกลับไปเป็นยอดคงเหลือให้เบิกของงาน · คืนได้เฉพาะของสภาพดี ก่อนออกใบเสร็จเท่านั้น"
      size="xlarge"
      onClose={() => { if (!save.isPending) onClose() }}
      footer={<>
        <Button variant="ghost" disabled={save.isPending} onClick={onClose}>ยกเลิก</Button>
        <Button type="submit" form="stock-return-form" disabled={save.isPending || (Boolean(blockedReason) && !uncertain)} title={blockedReason || undefined}>
          {save.isPending ? 'กำลังบันทึก…' : uncertain ? 'ตรวจสอบ / ส่งคำขอเดิมซ้ำ' : 'ยืนยันคืนสินค้า'}
        </Button>
      </>}
    >
      {uncertain && <p role="status">คำขอคืนก่อนหน้ายังไม่ทราบผล ส่งคำขอเดิมซ้ำได้โดยไม่คืนซ้ำ</p>}
      <form id="stock-return-form" className="management-form" onSubmit={e => { e.preventDefault(); submit() }}>
        <fieldset disabled={save.isPending || uncertain} className="purchase-fieldset">
          <div className="form-grid">
            <Field label="เหตุผลการคืน *">
              <Select required value={reasonCode} onChange={e => setReasonCode(e.target.value)}>
                <option value="">เลือกเหตุผล</option>
                {RETURN_REASONS.map(r => <option key={r.code} value={r.code}>{r.label}</option>)}
              </Select>
            </Field>
            <Field label={reasonCode === 'other' ? 'หมายเหตุ *' : 'หมายเหตุ'} wide>
              <Textarea maxLength={500} value={note} onChange={e => setNote(e.target.value)} placeholder="รายละเอียดเพิ่มเติม (ถ้ามี)" />
            </Field>
          </div>
        </fieldset>

        {withdrawal.isPending ? <p role="status" className="section-help">กำลังโหลดใบเบิก…</p>
          : withdrawal.isError ? <div><InlineError error={withdrawal.error} /><Button variant="outline" size="sm" onClick={() => void withdrawal.refetch()}>ลองใหม่</Button></div>
          : (
            <div className="purchase-table-scroll">
              <table className="master-table purchase-lines">
                <thead><tr><th>สินค้า</th><th>เบิก</th><th>คืนแล้ว</th><th>คืนได้</th><th>คืนครั้งนี้</th></tr></thead>
                <tbody>
                  {lines.map(line => (
                    <tr key={line.catalogItemId}>
                      <td><strong>{line.name}</strong><small className="purchase-sub">{line.code} · {line.unit}</small></td>
                      <td>{line.quantity}</td>
                      <td>{line.returnedQuantity ?? 0}</td>
                      <td>{line.returnable}</td>
                      <td>
                        {line.returnable > 0 ? (
                          <Input aria-label={`จำนวนคืน ${line.name} (สูงสุด ${line.returnable})`} type="number" min="0" max={line.returnable} step="1"
                            disabled={save.isPending || uncertain} value={quantities[line.catalogItemId] ?? ''}
                            onChange={e => setQuantities(old => ({ ...old, [line.catalogItemId]: e.target.value }))} />
                        ) : '-'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        {blockedReason && <p role="status" className="field-error">{blockedReason}</p>}
        {save.isError && <InlineError error={save.error} />}
      </form>
    </ConfirmModal>
  )
}
