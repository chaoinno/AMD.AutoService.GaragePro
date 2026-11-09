import { useMutation } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../../api/client'
import {
  CREDIT_NOTE_REASONS,
  issueCreditNote,
  REFUND_METHODS,
  type CreditNote,
  type CreditNoteReason,
  type CreditNoteState,
  type CreditPlanLine,
} from '../../../api/creditNotes'
import type { PaymentMethod } from '../../../api/pos'
import type { Job } from '../../../api/types'
import { ConfirmModal } from '../../../components/ConfirmModal'
import { Money } from '../../../components/Money'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { Textarea } from '../../../components/ui/textarea'
import { formatDateTime, formatMoney, formatNumber } from '../../../lib/format'
import { Field, InlineError } from '../../master-data/MasterDataCommon'

type Mode = 'none' | 'quantity' | 'amount'
type Entry = { mode: Mode; value: string }

const round2 = (n: number) => Math.round(n * 100) / 100

/// ยอดที่ลดของบรรทัด — มิเรอร์ CreditNoteService.BuildLine (ลดจำนวนที่เหลือทั้งหมด = ยอดคงเหลือทั้งหมด ไม่ค้างเศษสตางค์)
/// คืน string = เหตุผลที่ใช้ไม่ได้ · server ตรวจซ้ำเสมอ
export function creditLineAmount(line: CreditPlanLine, entry: Entry): number | string | null {
  if (entry.mode === 'none') return null
  const value = Number(entry.value)
  if (!entry.value.trim() || !Number.isFinite(value) || value <= 0) return `ระบุ${entry.mode === 'quantity' ? 'จำนวน' : 'ยอดเงิน'}ที่ลดของ "${line.description}"`
  if (entry.mode === 'quantity') {
    if (value > line.remainingQuantity) return `จำนวนที่ลดของ "${line.description}" เกินคงเหลือ (${formatNumber(line.remainingQuantity)} ${line.unit})`
    const amount = value === line.remainingQuantity
      ? line.remainingAmount
      : Math.min(line.remainingAmount, round2(line.originalNetAmount * value / line.originalQuantity))
    return amount > 0 ? amount : `ยอดที่ลดของ "${line.description}" ปัดเศษแล้วเป็นศูนย์ ให้ระบุเป็นยอดเงินแทน`
  }
  if (round2(value) !== value) return `ยอดเงินของ "${line.description}" ทศนิยมได้ไม่เกิน 2 ตำแหน่ง`
  if (value > line.remainingAmount) return `ยอดที่ลดของ "${line.description}" เกินคงเหลือ (${formatMoney(line.remainingAmount)} บาท)`
  return value
}

/// ออกใบลดหนี้ (ผู้จัดการ) — เลือกรายการจากบิลที่อ้าง (ห้ามเพิ่มรายการนอกบิล) · เหตุผล · บันทึกคืนเงินพร้อมกันได้
/// [BIZ] ออกแล้วยกเลิก/แก้ไม่ได้ (docs/20 Q8) จึงแสดงยอดสรุปให้ตรวจก่อนยืนยัน
export function CreditNoteIssueModal({
  open, job, state, onClose, onIssued,
}: { open: boolean; job: Job; state: CreditNoteState; onClose: () => void; onIssued: (note: CreditNote) => void }) {
  const [entries, setEntries] = useState<Record<string, Entry>>({})
  const [reason, setReason] = useState<CreditNoteReason | ''>('')
  const [reasonText, setReasonText] = useState('')
  const [refundNow, setRefundNow] = useState(true)
  const [refundMethod, setRefundMethod] = useState<PaymentMethod>('cash')
  const [refundAmount, setRefundAmount] = useState('')
  const [refundReference, setRefundReference] = useState('')
  // RequestId เดิมใช้ซ้ำเมื่อเน็ตหลุด (server คืนใบเดิม ไม่ออกเลขใหม่) · server ปฏิเสธ (4xx) = ไม่ได้ออก สร้างใหม่ได้
  const [ids, setIds] = useState(() => ({ note: crypto.randomUUID(), refund: crypto.randomUUID() }))
  const newIds = () => setIds({ note: crypto.randomUUID(), refund: crypto.randomUUID() })

  useEffect(() => {
    if (!open) return
    setEntries({}); setReason(''); setReasonText('')
    setRefundNow(true); setRefundMethod('cash'); setRefundAmount(''); setRefundReference('')
    newIds()
  }, [open])

  const creditable = state.lines.filter((l) => l.remainingAmount > 0)
  const computed = useMemo(() => creditable.map((line) => ({
    line, result: creditLineAmount(line, entries[line.sourceLineId] ?? { mode: 'none', value: '' }),
  })), [creditable, entries])

  const lineError = computed.find((c) => typeof c.result === 'string')?.result as string | undefined
  const chosen = computed.filter((c) => typeof c.result === 'number') as { line: CreditPlanLine; result: number }[]
  const diff = round2(chosen.reduce((sum, c) => sum + c.result, 0))
  const vat = round2(diff * state.vatRate)
  const total = round2(diff + vat)

  // ยอดคืนเงินตามยอดใบลดหนี้จนกว่าผู้ใช้จะแก้เอง
  const [refundTouched, setRefundTouched] = useState(false)
  useEffect(() => { if (open) setRefundTouched(false) }, [open])
  const refundValue = refundTouched ? Number(refundAmount) : total

  const formError = chosen.length === 0 && !lineError
    ? 'เลือกรายการจากบิลที่จะลดอย่างน้อย 1 รายการ'
    : lineError
      ?? (!reason ? 'เลือกเหตุผลการลดหนี้'
        : reason === 'other' && !reasonText.trim() ? 'เลือก "เหตุอื่น" ต้องระบุรายละเอียด'
          : refundNow && (!Number.isFinite(refundValue) || refundValue <= 0) ? 'ระบุยอดคืนเงินมากกว่าศูนย์ หรือยกเลิกการคืนเงินตอนนี้'
            : refundNow && refundValue > total ? `ยอดคืนเงินเกินยอดใบลดหนี้ (${formatMoney(total)} บาท)`
              : null)

  const mutation = useMutation({
    mutationFn: () => issueCreditNote(job.jobId, {
      requestId: ids.note,
      reason: reason as CreditNoteReason,
      reasonText: reasonText.trim() || null,
      lines: chosen.map(({ line }) => {
        const entry = entries[line.sourceLineId]!
        const value = Number(entry.value)
        return { sourceLineId: line.sourceLineId, quantity: entry.mode === 'quantity' ? value : null, amount: entry.mode === 'amount' ? value : null }
      }),
      refund: refundNow
        ? { requestId: ids.refund, method: refundMethod, amount: round2(refundValue), reference: refundReference.trim() || null }
        : null,
    }),
    onSuccess: (note) => {
      toast.success(`ออกใบลดหนี้ ${note.documentNo} แล้ว`)
      onIssued(note)
    },
    onError: (e) => {
      if (isApiError(e) && e.status && e.status < 500) newIds()
      toast.error(isApiError(e) ? e.messageTh : 'ออกใบลดหนี้ไม่สำเร็จ')
    },
  })

  const setEntry = (id: string, patch: Partial<Entry>) =>
    setEntries((prev) => ({ ...prev, [id]: { ...(prev[id] ?? { mode: 'none', value: '' }), ...patch } }))

  return (
    <ConfirmModal
      open={open}
      title="ออกใบลดหนี้"
      description={state.sourceDocumentNo
        ? `อ้างอิง${state.sourceType === 'taxInvoice' ? 'ใบกำกับภาษี' : 'ใบเสร็จรับเงิน'} ${state.sourceDocumentNo}${state.sourceIssuedAt ? ` ลงวันที่ ${formatDateTime(state.sourceIssuedAt)}` : ''}`
        : undefined}
      onClose={onClose}
      size="xlarge"
      footer={
        <div className="billing-issue-footer">
          {formError ? <p className="billing-issue-footer__reason">{formError}</p> : null}
          <Button variant="outline" onClick={onClose}>ยกเลิก</Button>
          <Button disabled={Boolean(formError) || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังออกใบลดหนี้…' : `ยืนยันออกใบลดหนี้ ${formatMoney(total)} บาท`}
          </Button>
        </div>
      }
    >
      <div className="billing-issue-form">
        <p className="form-message">
          {state.sourceType === 'taxInvoice'
            ? 'งานนี้มีใบกำกับภาษีแล้ว ใบลดหนี้เป็นเอกสารทางภาษีที่อ้างอิงใบกำกับภาษี'
            : 'งานนี้ไม่ได้ออกใบกำกับภาษี ใบลดหนี้อ้างอิงใบเสร็จรับเงินและไม่ใช่เอกสารทางภาษี · ออกแล้วจะออกใบกำกับภาษีของงานนี้ไม่ได้อีก'}
        </p>

        <div className="credit-note-lines">
          <table>
            <thead>
              <tr>
                <th>รายการในบิล</th>
                <th className="document-number">จำนวนเดิม</th>
                <th className="document-number">มูลค่าเดิม</th>
                <th className="document-number">ลดไปแล้ว</th>
                <th className="document-number">คงเหลือ</th>
                <th>ลดครั้งนี้</th>
                <th className="document-number">ยอดที่ลด</th>
              </tr>
            </thead>
            <tbody>
              {state.lines.map((line) => {
                const entry = entries[line.sourceLineId] ?? { mode: 'none' as Mode, value: '' }
                const result = computed.find((c) => c.line.sourceLineId === line.sourceLineId)?.result
                const done = line.remainingAmount <= 0
                return (
                  <tr key={line.sourceLineId} className={done ? 'credit-note-lines__done' : undefined}>
                    <td>
                      <strong>{line.description}</strong>
                      <small>{line.quotationCode}</small>
                    </td>
                    <td className="document-number">{formatNumber(line.originalQuantity)} {line.unit}</td>
                    <td className="document-number">{formatMoney(line.originalNetAmount)}</td>
                    <td className="document-number">{line.creditedAmount > 0 ? formatMoney(line.creditedAmount) : '-'}</td>
                    <td className="document-number">{formatMoney(line.remainingAmount)}</td>
                    <td>
                      {done ? <span className="section-help">ลดครบแล้ว</span> : (
                        <div className="credit-note-lines__input">
                          <Select
                            aria-label={`วิธีลดของ ${line.description}`}
                            value={entry.mode}
                            onChange={(e) => setEntry(line.sourceLineId, { mode: e.target.value as Mode, value: '' })}
                          >
                            <option value="none">ไม่ลด</option>
                            <option value="quantity" disabled={line.remainingQuantity <= 0}>ลดตามจำนวน</option>
                            <option value="amount">ลดเป็นยอดเงิน</option>
                          </Select>
                          {entry.mode !== 'none' ? (
                            <Input
                              aria-label={entry.mode === 'quantity' ? `จำนวนที่ลดของ ${line.description}` : `ยอดที่ลดของ ${line.description}`}
                              className="money"
                              inputMode="decimal"
                              placeholder={entry.mode === 'quantity' ? `≤ ${formatNumber(line.remainingQuantity)}` : `≤ ${formatMoney(line.remainingAmount)}`}
                              value={entry.value}
                              onChange={(e) => setEntry(line.sourceLineId, { value: e.target.value })}
                            />
                          ) : null}
                        </div>
                      )}
                    </td>
                    <td className="document-number">{typeof result === 'number' ? formatMoney(result) : '-'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>

        <section className="form-grid">
          <Field label="เหตุผลการลดหนี้" wide>
            <Select value={reason} onChange={(e) => setReason(e.target.value as CreditNoteReason)}>
              <option value="">— เลือกเหตุผล —</option>
              {CREDIT_NOTE_REASONS.map((r) => <option key={r.value} value={r.value}>{r.label}</option>)}
            </Select>
          </Field>
          <Field label={reason === 'other' ? 'รายละเอียด (บังคับ)' : 'รายละเอียดเพิ่มเติม (ถ้ามี)'} wide>
            <Textarea rows={2} maxLength={500} value={reasonText} onChange={(e) => setReasonText(e.target.value)} />
          </Field>
        </section>

        <div className="credit-note-summary">
          <div className="money-summary">
            <div className="money-summary__row"><span>มูลค่าตามบิลเดิม</span><Money value={state.originalNetAmount} /></div>
            <div className="money-summary__row"><span>มูลค่าที่ถูกต้อง</span><Money value={round2(state.originalNetAmount - state.creditedNetAmount - diff)} /></div>
            <div className="money-summary__row"><span>ผลต่าง (ลดหนี้ครั้งนี้)</span><Money value={diff} /></div>
            <div className="money-summary__row">
              <span>ภาษีมูลค่าเพิ่ม{state.vatRate > 0 ? ` ${formatNumber(state.vatRate * 100)}%` : ' (งานนี้ไม่คิด VAT)'}</span>
              <Money value={vat} />
            </div>
            <div className="money-summary__total"><span>รวมลดหนี้ทั้งสิ้น</span><Money value={total} /></div>
          </div>

          <div className="credit-note-refund">
            <label className="filter-check">
              <input type="checkbox" checked={refundNow} onChange={(e) => setRefundNow(e.target.checked)} />
              บันทึกคืนเงินลูกค้าตอนนี้
            </label>
            {refundNow ? (
              <section className="form-grid">
                <Field label="ช่องทางคืนเงิน">
                  <Select value={refundMethod} onChange={(e) => setRefundMethod(e.target.value as PaymentMethod)}>
                    {REFUND_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                  </Select>
                </Field>
                <Field label="ยอดคืนเงิน (บาท)">
                  <Input
                    className="money"
                    inputMode="decimal"
                    value={refundTouched ? refundAmount : String(total)}
                    onChange={(e) => { setRefundTouched(true); setRefundAmount(e.target.value) }}
                  />
                </Field>
                <Field label="เลขอ้างอิง (ถ้ามี)" wide>
                  <Input maxLength={200} value={refundReference} onChange={(e) => setRefundReference(e.target.value)} />
                </Field>
              </section>
            ) : (
              <p className="form-message">บันทึกคืนเงินภายหลังได้จากแถวใบลดหนี้ (เช่น หักกลบกับงานถัดไป ให้ระบุในรายละเอียดเหตุผล)</p>
            )}
          </div>
        </div>

        <p className="form-message">ออกแล้วยกเลิกหรือแก้ไขไม่ได้ ตรวจรายการและยอดให้ถูกต้องก่อนยืนยัน</p>
        {mutation.isError ? <InlineError error={mutation.error} /> : null}
      </div>
    </ConfirmModal>
  )
}
