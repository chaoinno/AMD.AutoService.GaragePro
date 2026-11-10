import { useMutation, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../../api/client'
import { getSuppliers } from '../../../api/masterData'
import {
  CLAIM_WINDOW_MONTHS,
  formatTaxMonth,
  getPurchaseTaxInvoicePrefill,
  recordPurchaseTaxInvoice,
  voidPurchaseTaxInvoice,
  type PurchaseTaxInvoice,
} from '../../../api/taxReports'
import { ConfirmModal } from '../../../components/ConfirmModal'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { Textarea } from '../../../components/ui/textarea'
import { formatMoney } from '../../../lib/format'
import { Field, InlineError } from '../../master-data/MasterDataCommon'

const localIso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const digits = (s: string) => s.replace(/\D/g, '')
const money = (s: string) => {
  const v = Number(s.replace(/,/g, ''))
  return s.trim() && Number.isFinite(v) ? v : NaN
}
const hasMoreThanTwoDecimals = (v: number) => Math.round(v * 100) !== v * 100

export type PurchaseInvoiceTarget = { goodsReceiptId: string | null; claimYear: number; claimMonth: number }

/// บันทึกใบกำกับภาษีซื้อ (docs/20 §4 · Q4/Q5) — จากใบรับสินค้า (เติมยอดให้แก้ได้) หรือบันทึกเอง
/// กติกาเดียวกับ TaxReportService: เลขผู้เสียภาษี 13 หลัก · สาขา 5 หลัก · เดือนที่ใช้สิทธิ์ = เดือนของใบกำกับ..+6 เดือน
export function PurchaseTaxInvoiceModal({ target, onClose, onSaved }: {
  target: PurchaseInvoiceTarget | null
  onClose: () => void
  onSaved: (saved: PurchaseTaxInvoice) => void
}) {
  const open = target !== null
  const goodsReceiptId = target?.goodsReceiptId ?? null
  const today = localIso(new Date())

  const suppliers = useQuery({
    queryKey: ['suppliers', 'tax-picker'],
    queryFn: async () => {
      const r = await getSuppliers({ pageSize: 200 })
      return (Array.isArray(r) ? r : r.items).filter((s) => s.isActive)
    },
    enabled: open,
  })
  const prefill = useQuery({
    queryKey: ['purchase-tax-invoice-prefill', goodsReceiptId],
    queryFn: () => getPurchaseTaxInvoicePrefill(goodsReceiptId!),
    enabled: open && Boolean(goodsReceiptId),
  })

  const [supplierId, setSupplierId] = useState('')
  const [supplierName, setSupplierName] = useState('')
  const [taxId, setTaxId] = useState('')
  const [branchNo, setBranchNo] = useState('00000')
  const [invoiceNo, setInvoiceNo] = useState('')
  const [invoiceDate, setInvoiceDate] = useState(today)
  const [claim, setClaim] = useState('')
  const [net, setNet] = useState('')
  const [vat, setVat] = useState('')
  const [note, setNote] = useState('')
  // RequestId เดิมใช้ซ้ำเมื่อเน็ตหลุด (server ไม่บันทึกซ้ำ) · server ปฏิเสธ (4xx) = ไม่ได้บันทึก สร้างใหม่ได้
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())

  useEffect(() => {
    if (!target) return
    setSupplierId(''); setSupplierName(''); setTaxId(''); setBranchNo('00000'); setInvoiceNo('')
    setInvoiceDate(today); setClaim(''); setNet(''); setVat(''); setNote('')
    setRequestId(crypto.randomUUID())
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target])

  useEffect(() => {
    const p = prefill.data
    if (!p) return
    setSupplierId(p.supplierId ?? '')
    setSupplierName(p.supplierName ?? '')
    setTaxId(p.supplierTaxId ?? '')
    setInvoiceDate(p.receivedDate > today ? today : p.receivedDate)
    setNet(String(p.netAmount))
    setVat(String(p.vatAmount))
    setNote(p.deliveryNumber ? `ใบส่งของ ${p.deliveryNumber}` : '')
  }, [prefill.data, today])

  const chooseSupplier = (id: string) => {
    setSupplierId(id)
    const s = suppliers.data?.find((x) => x.id === id)
    if (s) {
      setSupplierName(s.name)
      setTaxId(digits(s.taxId ?? ''))
    }
  }

  // เดือนที่ใช้สิทธิ์ได้: เดือนของใบกำกับ ถึง +6 เดือน · ค่าเริ่มต้น = เดือนที่ดูรายงานอยู่ถ้าอยู่ในช่วง ไม่งั้นเดือนของใบกำกับ
  const claimOptions = useMemo(() => {
    const m = /^(\d{4})-(\d{2})/.exec(invoiceDate)
    if (!m) return []
    const base = Number(m[1]) * 12 + Number(m[2]) - 1
    return Array.from({ length: CLAIM_WINDOW_MONTHS + 1 }, (_, i) => {
      const n = base + i
      const y = Math.floor(n / 12)
      const mo = (n % 12) + 1
      return { value: `${y}-${mo}`, label: formatTaxMonth(y, mo) + (i === 0 ? ' (เดือนของใบกำกับ)' : '') }
    })
  }, [invoiceDate])
  useEffect(() => {
    if (!target || !claimOptions.length) return
    const wanted = `${target.claimYear}-${target.claimMonth}`
    setClaim((current) => claimOptions.some((o) => o.value === current) ? current
      : claimOptions.some((o) => o.value === wanted) ? wanted : (claimOptions[0]?.value ?? ''))
  }, [claimOptions, target])

  const netValue = money(net)
  const vatValue = money(vat)
  const taxDigits = digits(taxId)
  const error =
    !supplierName.trim() ? 'กรุณาระบุชื่อผู้ขาย'
      : taxDigits.length !== 13 ? 'เลขประจำตัวผู้เสียภาษีของผู้ขายต้องเป็นตัวเลข 13 หลัก'
      : !/^\d{5}$/.test(branchNo.trim()) ? 'สาขาของผู้ขายต้องเป็นตัวเลข 5 หลัก (สำนักงานใหญ่ = 00000)'
      : !invoiceNo.trim() ? 'กรุณาระบุเลขที่ใบกำกับภาษี'
      : !invoiceDate ? 'กรุณาระบุวันที่ตามใบกำกับภาษี'
      : invoiceDate > today ? 'วันที่ใบกำกับภาษีต้องไม่เป็นวันในอนาคต'
      : !claim ? 'กรุณาเลือกเดือนที่ใช้สิทธิ์'
      : !(netValue > 0) || hasMoreThanTwoDecimals(netValue) ? 'มูลค่าก่อนภาษีต้องมากกว่า 0 และมีทศนิยมไม่เกิน 2 ตำแหน่ง'
      : !(vatValue > 0) || hasMoreThanTwoDecimals(vatValue) ? 'ภาษีมูลค่าเพิ่มต้องมากกว่า 0 และมีทศนิยมไม่เกิน 2 ตำแหน่ง'
      : vatValue >= netValue ? 'ภาษีมูลค่าเพิ่มต้องน้อยกว่ามูลค่าก่อนภาษี'
      : null
  const suggestedVat = netValue > 0 ? Math.round(netValue * 7) / 100 : null

  const mutation = useMutation({
    mutationFn: () => {
      const [y = 0, m = 0] = claim.split('-').map(Number)
      return recordPurchaseTaxInvoice({
        requestId,
        supplierId: supplierId || null,
        supplierName: supplierName.trim(),
        supplierTaxId: taxDigits,
        supplierBranchNo: branchNo.trim(),
        invoiceNo: invoiceNo.trim(),
        invoiceDate,
        claimYear: y,
        claimMonth: m,
        netAmount: netValue,
        vatAmount: vatValue,
        goodsReceiptId,
        note: note.trim() || null,
      })
    },
    onSuccess: (saved) => {
      toast.success(`บันทึกใบกำกับภาษีซื้อ ${saved.invoiceNo} แล้ว · ใช้สิทธิ์เดือน ${formatTaxMonth(saved.claimYear, saved.claimMonth)}`)
      onSaved(saved)
    },
    onError: (e) => {
      if (isApiError(e) && e.status && e.status < 500) setRequestId(crypto.randomUUID())
    },
  })

  const loadingPrefill = Boolean(goodsReceiptId) && prefill.isPending

  return (
    <ConfirmModal
      open={open}
      title={prefill.data ? `บันทึกใบกำกับภาษีซื้อ · ${prefill.data.goodsReceiptNumber}` : 'บันทึกใบกำกับภาษีซื้อ'}
      description={prefill.data
        ? `ใบสั่งซื้อ ${prefill.data.purchaseOrderNumber} · ยอดเติมจากใบรับสินค้า (รวมของชำรุด) ตรวจกับใบกำกับจริงและแก้ได้ก่อนบันทึก`
        : 'ใบกำกับที่ได้รับจากผู้ขาย — ค่าใช้จ่ายนอกระบบจัดซื้อก็บันทึกได้'}
      onClose={onClose}
      size="large"
      footer={
        <div className="billing-issue-footer">
          {error && !loadingPrefill ? <p className="billing-issue-footer__reason">{error}</p> : null}
          <Button variant="outline" onClick={onClose}>ยกเลิก</Button>
          <Button disabled={Boolean(error) || loadingPrefill || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึกใบกำกับภาษีซื้อ'}
          </Button>
        </div>
      }
    >
      {prefill.isError ? <InlineError error={prefill.error} /> : null}
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      {loadingPrefill ? <p className="section-help">กำลังเติมข้อมูลจากใบรับสินค้า…</p> : (
        <section className="form-grid">
          <Field label="ซัพพลายเออร์ในระบบ (ไม่บังคับ)" wide>
            <Select value={supplierId} onChange={(e) => chooseSupplier(e.target.value)}>
              <option value="">— ผู้ขายนอกรายชื่อ / กรอกเอง —</option>
              {(suppliers.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.code} · {s.name}</option>)}
            </Select>
          </Field>
          <Field label="ชื่อผู้ขาย" wide>
            <Input value={supplierName} maxLength={200} onChange={(e) => setSupplierName(e.target.value)} />
          </Field>
          <Field label="เลขประจำตัวผู้เสียภาษีผู้ขาย (13 หลัก)">
            <Input inputMode="numeric" value={taxId} maxLength={17} onChange={(e) => setTaxId(e.target.value)} />
          </Field>
          <Field label="สาขาของผู้ขาย (00000 = สำนักงานใหญ่)">
            <Input inputMode="numeric" value={branchNo} maxLength={5} onChange={(e) => setBranchNo(digits(e.target.value))} />
          </Field>
          <Field label="เลขที่ใบกำกับภาษี">
            <Input value={invoiceNo} maxLength={60} onChange={(e) => setInvoiceNo(e.target.value)} />
          </Field>
          <Field label="วันที่ตามใบกำกับภาษี">
            <Input type="date" value={invoiceDate} max={today} onChange={(e) => setInvoiceDate(e.target.value)} />
          </Field>
          <Field label={`เดือนภาษีที่ใช้สิทธิ์ (ได้ไม่เกิน ${CLAIM_WINDOW_MONTHS} เดือนถัดไป)`} wide>
            <Select value={claim} onChange={(e) => setClaim(e.target.value)}>
              {claimOptions.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </Select>
          </Field>
          <Field label="มูลค่าก่อนภาษี (บาท)">
            <Input inputMode="decimal" value={net} onChange={(e) => setNet(e.target.value)} />
          </Field>
          <Field label="ภาษีมูลค่าเพิ่ม (บาท)">
            <Input inputMode="decimal" value={vat} onChange={(e) => setVat(e.target.value)} />
            {suggestedVat !== null && suggestedVat !== vatValue ? (
              <button type="button" className="link-button tax-suggest" onClick={() => setVat(suggestedVat.toFixed(2))}>
                ใช้ 7% = {formatMoney(suggestedVat)}
              </button>
            ) : null}
          </Field>
          <Field label="หมายเหตุ (ไม่บังคับ)" wide>
            <Textarea value={note} maxLength={500} rows={2} onChange={(e) => setNote(e.target.value)} />
          </Field>
          {netValue > 0 && vatValue > 0 ? (
            <p className="tax-modal-total field--wide">รวมตามใบกำกับ <strong className="report-num">{formatMoney(netValue + vatValue)}</strong> บาท</p>
          ) : null}
        </section>
      )}
    </ConfirmModal>
  )
}

/// ยกเลิกใบกำกับภาษีซื้อ — ไม่ลบ เก็บเหตุผลไว้ · ยกเลิกแล้วบันทึกเลขเดิมใหม่ได้
export function VoidPurchaseTaxInvoiceModal({ invoice, onClose, onDone }: {
  invoice: PurchaseTaxInvoice | null
  onClose: () => void
  onDone: () => void
}) {
  const [reason, setReason] = useState('')
  useEffect(() => { setReason('') }, [invoice?.id])
  const mutation = useMutation({
    mutationFn: () => voidPurchaseTaxInvoice(invoice!.id, reason.trim()),
    onSuccess: (x) => {
      toast.success(`ยกเลิกใบกำกับภาษีซื้อ ${x.invoiceNo} แล้ว`)
      onDone()
    },
  })
  const error = reason.trim() ? null : 'กรุณาระบุเหตุผลที่ยกเลิก'
  return (
    <ConfirmModal
      open={invoice !== null}
      title={invoice ? `ยกเลิกใบกำกับภาษีซื้อ ${invoice.invoiceNo}` : 'ยกเลิกใบกำกับภาษีซื้อ'}
      description={invoice ? `${invoice.supplierName} · ภาษี ${formatMoney(invoice.vatAmount)} บาท — ใบนี้จะไม่นับในรายงานภาษีซื้ออีก` : undefined}
      onClose={onClose}
      footer={
        <div className="billing-issue-footer">
          {error ? <p className="billing-issue-footer__reason">{error}</p> : null}
          <Button variant="outline" onClick={onClose}>ไม่ยกเลิก</Button>
          <Button variant="destructive" disabled={Boolean(error) || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังยกเลิก…' : 'ยืนยันยกเลิก'}
          </Button>
        </div>
      }
    >
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      <Field label="เหตุผลที่ยกเลิก" wide>
        <Textarea value={reason} maxLength={500} rows={3} onChange={(e) => setReason(e.target.value)} />
      </Field>
    </ConfirmModal>
  )
}
