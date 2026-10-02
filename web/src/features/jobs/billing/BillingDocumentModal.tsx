import { useQueries, useQuery } from '@tanstack/react-query'
import { Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { isApiError, isForbiddenError } from '../../../api/client'
import { getPaymentSummary, getTaxInvoiceState, type PaymentSummary, type TaxInvoice } from '../../../api/pos'
import { getQuotation, getQuotations } from '../../../api/quotations'
import type { Job, Quotation } from '../../../api/types'
import { ConfirmModal } from '../../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../../components/StateBlock'
import { Button } from '../../../components/ui/button'
import { Select } from '../../../components/ui/select'
import {
  BillingDocument,
  type BillingDocumentData,
  type BillingGroup,
  type BillingKind,
  type CopyLabel,
} from './BillingDocument'

type Props = { open: boolean; job: Job; kind: BillingKind; onClose: () => void }

const MODAL_TITLE: Record<BillingKind, string> = {
  statement: 'ใบแจ้งยอด',
  receipt: 'ใบเสร็จรับเงิน',
  taxInvoice: 'ใบกำกับภาษี',
}

/// เอกสารการเงินของจ๊อบซ้อนบน job card modal — window.print() พิมพ์เฉพาะ .payment-receipt-print-area
/// ผ่าน body.printing-payment-receipt (ใช้กฎ @media print เดิมของใบเสร็จใน index.css ร่วมกันทั้งสามเอกสาร)
export function BillingDocumentModal({ open, job, kind, onClose }: Props) {
  // [BIZ] เอกสารที่มีเลข (ใบเสร็จ/ใบกำกับ) พิมพ์ซ้ำต้องระบุ "สำเนา" (docs/01-workflow.md §3.9) — ระบบยังไม่นับครั้งที่พิมพ์
  // จึงให้ผู้พิมพ์เลือกเอง ค่าเริ่มต้นเป็นต้นฉบับ
  const [copyLabel, setCopyLabel] = useState<CopyLabel>('original')

  useEffect(() => {
    if (!open) return
    setCopyLabel('original')
    document.body.classList.add('printing-payment-receipt')
    return () => document.body.classList.remove('printing-payment-receipt')
  }, [open])

  const summaryQuery = useQuery({
    queryKey: ['payment-summary', job.jobId],
    queryFn: () => getPaymentSummary(job.jobId),
    enabled: open,
  })
  const listQuery = useQuery({
    queryKey: ['job-quotations', job.jobId],
    queryFn: () => getQuotations('', job.jobId),
    enabled: open && kind !== 'taxInvoice',
  })
  // [BIZ] ยอดที่ต้องชำระ = บรรทัดที่อนุมัติของทุกใบที่ยังไม่ถูกแทนที่ (กฎข้อ 21 · JobQuotations.CombinedApprovedTotals)
  const activeIds = (listQuery.data ?? []).filter((q) => q.status !== 'superseded').map((q) => q.id)
  const quotationQueries = useQueries({
    queries: activeIds.map((id) => ({
      queryKey: ['quotation', id],
      queryFn: () => getQuotation(id),
      enabled: open && kind !== 'taxInvoice',
    })),
  })
  const taxInvoiceQuery = useQuery({
    queryKey: ['tax-invoice', job.jobId],
    queryFn: () => getTaxInvoiceState(job.jobId),
    enabled: open && kind === 'taxInvoice',
  })

  const pending = kind === 'taxInvoice'
    ? taxInvoiceQuery.isPending
    : summaryQuery.isPending || listQuery.isPending || quotationQueries.some((q) => q.isPending)
  const error = kind === 'taxInvoice'
    ? taxInvoiceQuery.error
    : summaryQuery.error ?? listQuery.error ?? quotationQueries.find((q) => q.error)?.error ?? null
  const retry = () => {
    void summaryQuery.refetch()
    void listQuery.refetch()
    void taxInvoiceQuery.refetch()
    quotationQueries.forEach((q) => void q.refetch())
  }

  let data: BillingDocumentData | null = null
  let emptyReason: string | null = null
  if (!pending && !error) {
    if (kind === 'taxInvoice') {
      const invoice = taxInvoiceQuery.data?.issued
      if (invoice) data = fromTaxInvoice(job, invoice)
      else emptyReason = 'งานนี้ยังไม่ได้ออกใบกำกับภาษี'
    } else {
      const quotations = quotationQueries
        .map((q) => q.data)
        .filter((q): q is Quotation => Boolean(q))
        .sort((a, b) => a.version - b.version)
      const summary = summaryQuery.data!
      if (kind === 'receipt' && !summary.receipt) emptyReason = 'งานนี้ยังไม่ได้ออกใบเสร็จ'
      else data = fromQuotations(job, kind, summary, quotations)
    }
  }

  let body: ReactNode
  if (pending) {
    body = (
      <StateBlock
        variant="loading"
        title="กำลังจัดเตรียมเอกสาร"
        reason="ระบบกำลังโหลดข้อมูลล่าสุดเพื่อจัดวางเอกสารสำหรับพิมพ์"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={retry}
      >
        <SkeletonRows count={5} />
      </StateBlock>
    )
  } else if (error) {
    body = (
      <StateBlock
        variant={isForbiddenError(error) ? 'forbidden' : 'error'}
        title={isForbiddenError(error) ? 'ไม่มีสิทธิ์ดูเอกสารนี้' : 'เปิดเอกสารไม่สำเร็จ'}
        reason={isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(error) ? error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={retry}
      />
    )
  } else if (!data) {
    body = <div className="job-detail-empty"><p>{emptyReason}</p></div>
  } else {
    const numbered = kind !== 'statement'
    body = (
      <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden">
          {numbered ? (
            <label className="billing-copy-select">
              <span>พิมพ์เป็น</span>
              <Select value={copyLabel} onChange={(e) => setCopyLabel(e.target.value as CopyLabel)}>
                <option value="original">ต้นฉบับ</option>
                <option value="copy">สำเนา (พิมพ์ซ้ำ)</option>
              </Select>
            </label>
          ) : null}
          <Button onClick={() => window.print()}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
        </div>
        <div className="payment-receipt-print-area">
          <BillingDocument data={data} copyLabel={numbered ? copyLabel : null} />
        </div>
      </div>
    )
  }

  return (
    <ConfirmModal
      open={open}
      title={data?.documentNo ? `${MODAL_TITLE[kind]} ${data.documentNo}` : MODAL_TITLE[kind]}
      onClose={onClose}
      size="xlarge"
    >
      {body}
    </ConfirmModal>
  )
}

function fromQuotations(
  job: Job, kind: 'statement' | 'receipt', summary: PaymentSummary, quotations: Quotation[],
): BillingDocumentData {
  const groups: BillingGroup[] = quotations
    .map((q) => ({
      quotationCode: q.code,
      lines: q.lines
        .filter((l) => l.approvalStatus === 'approved')
        .sort((a, b) => a.sequence - b.sequence)
        .map((l) => ({
          key: l.id,
          description: l.name,
          quantity: l.quantity,
          unit: l.unit,
          unitPrice: l.unitPrice,
          discountAmount: l.discountAmount + l.promotionAmount,
          netAmount: l.netAmount,
        })),
    }))
    .filter((g) => g.lines.length > 0)

  // ข้อมูลร้าน/ลูกค้าเป็นสำเนาที่ใบเสนอราคาเก็บไว้ตอนสร้าง — ใช้ใบล่าสุดที่มีอยู่
  const source = quotations[quotations.length - 1]
  const receipt = kind === 'receipt' ? summary.receipt! : null

  return {
    kind,
    documentNo: receipt?.documentNo ?? null,
    issuedAt: receipt?.issuedAt ?? new Date().toISOString(),
    issuedByName: receipt?.issuedByName ?? null,
    seller: source?.branch ?? { name: job.branchName || 'ไม่ระบุสาขา' },
    buyer: {
      name: source?.customer.name || job.customerName,
      phone: source?.customer.phone,
      address: source?.customer.address,
      taxId: source?.customer.taxId,
    },
    jobNo: job.jobNo,
    vehicleRegistration: job.vehicleRegistration,
    references: summary.quotationCodes.length > 0
      ? [{ label: 'ใบเสนอราคา', value: summary.quotationCodes.join(', ') }]
      : [],
    groups,
    netAmount: receipt?.netAmount ?? summary.netAmount,
    vatAmount: receipt?.vatAmount ?? summary.vatAmount,
    totalAmount: receipt?.totalAmount ?? summary.grandTotal,
    vatRate: summary.vatIncluded ? source?.totals.vatRate ?? null : null,
    payments: summary.payments,
    paidAmount: kind === 'statement' ? summary.paidAmount : null,
    remainingAmount: kind === 'statement' ? summary.remainingAmount : null,
  }
}

function fromTaxInvoice(job: Job, invoice: TaxInvoice): BillingDocumentData {
  const groups = new Map<string, BillingGroup>()
  for (const l of invoice.lines) {
    const group = groups.get(l.quotationCode) ?? { quotationCode: l.quotationCode, lines: [] }
    group.lines.push({
      key: String(l.sequence),
      description: l.description,
      quantity: l.quantity,
      unit: l.unit,
      unitPrice: l.unitPrice,
      discountAmount: l.discountAmount,
      netAmount: l.netAmount,
    })
    groups.set(l.quotationCode, group)
  }

  return {
    kind: 'taxInvoice',
    documentNo: invoice.documentNo,
    issuedAt: invoice.issuedAt,
    issuedByName: invoice.issuedByName,
    seller: invoice.seller,
    buyer: invoice.buyer,
    jobNo: job.jobNo,
    vehicleRegistration: job.vehicleRegistration,
    references: invoice.receiptDocumentNo ? [{ label: 'อ้างอิงใบเสร็จ', value: invoice.receiptDocumentNo }] : [],
    groups: [...groups.values()],
    netAmount: invoice.netAmount,
    vatAmount: invoice.vatAmount,
    totalAmount: invoice.totalAmount,
    vatRate: invoice.vatRate,
    payments: [],
    paidAmount: null,
    remainingAmount: null,
  }
}
