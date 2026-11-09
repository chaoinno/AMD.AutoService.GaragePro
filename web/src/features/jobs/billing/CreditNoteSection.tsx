import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Printer, Undo2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../../api/client'
import {
  getCreditNoteState,
  recordCreditNoteRefund,
  REFUND_METHODS,
  REFUND_STATUS_LABEL,
  type CreditNote,
} from '../../../api/creditNotes'
import type { PaymentMethod } from '../../../api/pos'
import type { Job } from '../../../api/types'
import { ConfirmModal } from '../../../components/ConfirmModal'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { formatDateTime, formatMoney } from '../../../lib/format'
import { Field, InlineError } from '../../master-data/MasterDataCommon'
import { PlanInterestActions } from '../../plan/PlanLockedPanel'
import { BillingDocRow } from './BillingDocRow'
import { CreditNoteDocumentModal } from './CreditNoteDocument'
import { CreditNoteIssueModal } from './CreditNoteIssueModal'

/// แถวใบลดหนี้ในการ์ด "เอกสาร & ส่งมอบรถ" (docs/20 §2.5) — แถวหลัก 1 แถว + แถวละใบที่ออกแล้ว
/// [BIZ] ออก/คืนเงิน = ผู้จัดการ (server คำนวณ canIssue/canRefund และเหตุผลให้) · ล็อกตามแพ็กเกจเดียวกับใบกำกับภาษี
export function CreditNoteRows({ job, receiptIssued, locked }: { job: Job; receiptIssued: boolean; locked: boolean }) {
  const queryClient = useQueryClient()
  const [issueOpen, setIssueOpen] = useState(false)
  const [printing, setPrinting] = useState<CreditNote | null>(null)
  const [refunding, setRefunding] = useState<CreditNote | null>(null)

  const query = useQuery({
    queryKey: ['credit-notes', job.jobId],
    queryFn: () => getCreditNoteState(job.jobId),
    enabled: receiptIssued && !locked,
  })
  const state = query.data

  if (locked) {
    return (
      <BillingDocRow title="ใบลดหนี้" done={false} locked status="ใบลดหนี้เปิดในแพ็กเกจเต็มพร้อมใบกำกับภาษี">
        <PlanInterestActions feature="taxInvoice" />
      </BillingDocRow>
    )
  }

  const issued = state?.issued ?? []
  const source = state?.sourceDocumentNo
    ? `อ้างอิง${state.sourceType === 'taxInvoice' ? 'ใบกำกับภาษี' : 'ใบเสร็จ'} ${state.sourceDocumentNo}`
    : null
  const status = !receiptIssued
    ? 'ออกได้หลังออกใบเสร็จแล้ว'
    : query.isPending
      ? 'กำลังตรวจสอบ…'
      : query.isError
        ? isApiError(query.error) ? query.error.messageTh : 'ตรวจสอบสถานะใบลดหนี้ไม่สำเร็จ'
        : [
            source,
            issued.length > 0
              ? `ออกแล้ว ${issued.length} ใบ · ลดได้อีก ${formatMoney(state!.remainingNetAmount)} บาท (ก่อน VAT)`
              : null,
            state!.blockedReasonTh || (issued.length === 0 ? 'ยังไม่มีใบลดหนี้' : null),
          ].filter(Boolean).join(' · ')

  return (
    <>
      <BillingDocRow title="ใบลดหนี้" done={false} status={status}>
        <Button
          size="sm"
          variant="outline"
          disabled={!state?.canIssue}
          onClick={() => setIssueOpen(true)}
        >
          ออกใบลดหนี้
        </Button>
      </BillingDocRow>

      {issued.map((note) => (
        <BillingDocRow
          key={note.id}
          title={`ใบลดหนี้ ${note.documentNo}`}
          done={note.refundStatus === 'full'}
          status={[
            formatDateTime(note.issuedAt),
            `${formatMoney(note.totalAmount)} บาท`,
            REFUND_STATUS_LABEL[note.refundStatus]
              + (note.refundStatus === 'partial' ? ` (คืนแล้ว ${formatMoney(note.refundedAmount)})` : ''),
            note.refundStatus !== 'full' && !state?.canRefund ? 'ผู้จัดการเป็นผู้บันทึกคืนเงิน' : null,
          ].filter(Boolean).join(' · ')}
        >
          <Button variant="outline" size="sm" onClick={() => setPrinting(note)}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
          {note.refundStatus !== 'full' && state?.canRefund ? (
            <Button variant="outline" size="sm" onClick={() => setRefunding(note)}>
              <Undo2 aria-hidden="true" /> บันทึกคืนเงิน
            </Button>
          ) : null}
        </BillingDocRow>
      ))}

      {state ? (
        <CreditNoteIssueModal
          open={issueOpen}
          job={job}
          state={state}
          onClose={() => setIssueOpen(false)}
          onIssued={(note) => {
            setIssueOpen(false)
            void queryClient.invalidateQueries({ queryKey: ['credit-notes', job.jobId] })
            // docs/20 Q9: ใบลดหนี้อ้างใบเสร็จทำให้ออกใบกำกับไม่ได้อีก
            void queryClient.invalidateQueries({ queryKey: ['tax-invoice', job.jobId] })
            setPrinting(note)
          }}
        />
      ) : null}
      <CreditNoteDocumentModal note={printing} onClose={() => setPrinting(null)} />
      <RefundModal
        note={refunding}
        onClose={() => setRefunding(null)}
        onDone={() => {
          setRefunding(null)
          void queryClient.invalidateQueries({ queryKey: ['credit-notes', job.jobId] })
        }}
      />
    </>
  )
}

function RefundModal({ note, onClose, onDone }: { note: CreditNote | null; onClose: () => void; onDone: () => void }) {
  const remaining = note ? Math.round((note.totalAmount - note.refundedAmount) * 100) / 100 : 0
  const [method, setMethod] = useState<PaymentMethod>('cash')
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  // RequestId เดิมใช้ซ้ำเมื่อเน็ตหลุด (server ไม่บันทึกซ้ำ) · server ปฏิเสธ (4xx) = ไม่ได้บันทึก สร้างใหม่ได้
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())

  useEffect(() => {
    if (!note) return
    setMethod('cash')
    setAmount(String(remaining))
    setReference('')
    setRequestId(crypto.randomUUID())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [note?.id])

  const value = Number(amount)
  const error = !amount.trim() || !Number.isFinite(value) || value <= 0
    ? 'กรุณาระบุยอดคืนเงินมากกว่าศูนย์'
    : value > remaining
      ? `ยอดคืนเงินเกินยอดที่ยังค้างคืน (${formatMoney(remaining)} บาท)`
      : null

  const mutation = useMutation({
    mutationFn: () => recordCreditNoteRefund(note!.id, {
      requestId, method, amount: Math.round(value * 100) / 100, reference: reference.trim() || null,
    }),
    onSuccess: (updated) => {
      toast.success(`บันทึกคืนเงินตามใบลดหนี้ ${updated.documentNo} แล้ว`)
      onDone()
    },
    onError: (e) => {
      if (isApiError(e) && e.status && e.status < 500) setRequestId(crypto.randomUUID())
      toast.error(isApiError(e) ? e.messageTh : 'บันทึกคืนเงินไม่สำเร็จ')
    },
  })

  return (
    <ConfirmModal
      open={note !== null}
      title={note ? `บันทึกคืนเงิน · ${note.documentNo}` : 'บันทึกคืนเงิน'}
      description={note ? `ยอดใบลดหนี้ ${formatMoney(note.totalAmount)} บาท · ค้างคืน ${formatMoney(remaining)} บาท` : undefined}
      onClose={onClose}
      footer={
        <div className="billing-issue-footer">
          {error ? <p className="billing-issue-footer__reason">{error}</p> : null}
          <Button variant="outline" onClick={onClose}>ยกเลิก</Button>
          <Button disabled={Boolean(error) || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึกคืนเงิน'}
          </Button>
        </div>
      }
    >
      <section className="form-grid">
        <Field label="ช่องทางคืนเงิน">
          <Select value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
            {REFUND_METHODS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
          </Select>
        </Field>
        <Field label="ยอดคืนเงิน (บาท)">
          <Input className="money" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} />
        </Field>
        <Field label="เลขอ้างอิง (ถ้ามี)" wide>
          <Input maxLength={200} value={reference} onChange={(e) => setReference(e.target.value)} />
        </Field>
      </section>
      <p className="form-message">บันทึกแล้วลบไม่ได้ (เป็นหลักฐานเงินออก) · คืนได้หลายครั้งรวมไม่เกินยอดใบลดหนี้</p>
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
    </ConfirmModal>
  )
}
