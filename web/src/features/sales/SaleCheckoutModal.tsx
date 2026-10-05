import { useMutation, useQueryClient } from '@tanstack/react-query'
import { CircleAlert, LoaderCircle, Plus, RotateCcw, Trash2, TriangleAlert, Wallet } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { pendingSaleCheckout, saleCheckoutCommand, type CheckoutInput, type PaymentMethod, type Sale } from '../../api/sales'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { MoneyInput } from '../../components/MoneyInput'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { formatMoney } from '../../lib/format'
import { ErrorAlert, PAYMENT_METHOD_OPTIONS, paymentMethodLabel, toSatang } from './saleFormat'
import { saleKey, useApplySale } from './useSale'

type Row = { key: number; method: PaymentMethod; amount: number; reference: string }

const pendingKey = (saleId: string) => `garagepro.sale-checkout.${saleId}`
const fromSatang = (satang: number) => satang / 100

/// ธนบัตรที่ลูกค้ามักยื่นมา — ปุ่มลัดช่อง "รับเงินมา" ไม่ต้องพิมพ์เอง
function cashSuggestions(amount: number) {
  const notes = [100, 500, 1000]
  const values = new Set<number>([amount])
  notes.forEach((note) => { const up = Math.ceil(amount / note) * note; if (up > amount) values.add(up) })
  return [...values].sort((a, b) => a - b).slice(0, 4)
}

/// [BIZ] ยอดรวมทุกช่องทางต้องเท่ายอดบิลพอดี (SALE_PAYMENT_MISMATCH) — เงินทอนคิดบนจอเท่านั้น ไม่บันทึกยอดเกิน
/// [BIZ] คำขอที่ไม่ทราบผล (เน็ตหลุด/5xx) ต้องส่งซ้ำด้วย requestId เดิม ห้ามสร้างใหม่ — ไม่งั้นเสี่ยงรับเงินซ้ำ
///       saleCheckoutCommand เก็บคำขอไว้ใน sessionStorage และลบเองเมื่อ server ตอบผลชัดเจน (2xx/4xx)
export function SaleCheckoutModal({ open, sale, onClose }: { open: boolean; sale: Sale; onClose: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const applySale = useApplySale()
  const key = pendingKey(sale.id)
  const [rows, setRows] = useState<Row[]>([])
  const [received, setReceived] = useState(0)
  const [pending, setPending] = useState<CheckoutInput | null>(null)

  const totalSatang = toSatang(sale.totalAmount)

  useEffect(() => {
    if (!open) return
    setRows([{ key: Date.now(), method: 'cash', amount: sale.totalAmount, reference: '' }])
    setReceived(sale.totalAmount)
    setPending(pendingSaleCheckout(key))
    // ตั้งค่าเริ่มต้นเฉพาะตอนเปิด — ยอดบิลเปลี่ยนระหว่างเปิด modal ไม่ได้ (ปุ่มแก้บิลอยู่หลัง modal)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const checkout = useMutation({
    mutationFn: (input: CheckoutInput) => saleCheckoutCommand(sale.id, key, input),
    onSuccess: (completed, input) => {
      const cashPaid = input.payments.filter((p) => p.method === 'cash').reduce((sum, p) => sum + toSatang(p.amount), 0)
      const change = pending ? 0 : Math.max(0, toSatang(received) - cashPaid)
      applySale(completed)
      setPending(null)
      toast.success(`ออกใบเสร็จ ${completed.receiptNo} แล้ว`)
      onClose()
      navigate(`/sales/${completed.id}`, { replace: true, state: { change: fromSatang(change) } })
    },
    onError: (error, input) => {
      // ถือคำขอเดิมไว้ใน state ด้วย ไม่พึ่ง sessionStorage อย่างเดียว (storage อาจใช้ไม่ได้)
      const unknownResult = !isApiError(error) || !error.status || error.status >= 500
      setPending(unknownResult ? input : null)
      // ยอดอาจเปลี่ยนเพราะโปรหมดอายุ/สต็อกไม่พอ — โหลดบิลล่าสุดมาให้แก้ต่อได้ถูกต้อง
      if (isApiError(error) && error.status && error.status < 500) void queryClient.invalidateQueries({ queryKey: saleKey(sale.id) })
    },
  })

  const paidSatang = rows.reduce((sum, row) => sum + toSatang(row.amount), 0)
  const remainingSatang = totalSatang - paidSatang
  const cashSatang = rows.filter((row) => row.method === 'cash').reduce((sum, row) => sum + toSatang(row.amount), 0)
  const changeSatang = toSatang(received) - cashSatang

  const blockedReason = rows.some((row) => toSatang(row.amount) <= 0)
    ? 'ทุกช่องทางต้องมียอดมากกว่า 0 — ลบช่องทางที่ไม่ใช้ออก'
    : remainingSatang > 0
      ? `ยอดชำระยังขาดอีก ${formatMoney(fromSatang(remainingSatang))} บาท`
      : remainingSatang < 0
        ? `ยอดชำระเกินยอดบิล ${formatMoney(fromSatang(-remainingSatang))} บาท — เงินทอนให้ใส่ที่ช่อง "รับเงินสดมา"`
        : cashSatang > 0 && changeSatang < 0
          ? `รับเงินสดมาไม่พอ ขาดอีก ${formatMoney(fromSatang(-changeSatang))} บาท`
          : null

  const updateRow = (rowKey: number, patch: Partial<Row>) => setRows((current) => current.map((row) => (row.key === rowKey ? { ...row, ...patch } : row)))

  const submit = () => {
    if (pending) { checkout.mutate(pending); return }
    if (blockedReason) return
    checkout.mutate({
      requestId: crypto.randomUUID(),
      payments: rows.map((row) => ({
        method: row.method,
        amount: fromSatang(toSatang(row.amount)),
        reference: row.method === 'cash' ? null : row.reference.trim() || null,
      })),
    })
  }

  const discardPending = () => {
    try { sessionStorage.removeItem(key) } catch { /* ไม่มี storage ก็ไม่มีอะไรค้าง */ }
    setPending(null)
    checkout.reset()
  }

  const unknownOutcome = checkout.isError && Boolean(pending)

  return (
    <ConfirmModal
      open={open}
      size="medium"
      title="รับชำระเงิน"
      description={<>ยอดที่ต้องชำระ <Money value={sale.totalAmount} suffix=" บาท" /> · {sale.lines.length} รายการ</>}
      onClose={() => { if (!checkout.isPending) { onClose(); checkout.reset() } }}
      footer={(
        <>
          <Button variant="outline" disabled={checkout.isPending} onClick={() => { onClose(); checkout.reset() }}>กลับไปแก้บิล</Button>
          <Button size="lg" disabled={checkout.isPending || (!pending && Boolean(blockedReason))} onClick={submit}>
            {checkout.isPending ? <LoaderCircle className="spin" aria-hidden="true" /> : pending ? <RotateCcw aria-hidden="true" /> : <Wallet aria-hidden="true" />}
            {checkout.isPending ? 'กำลังบันทึก…' : pending ? 'ส่งคำขอเดิมอีกครั้ง' : 'ยืนยันรับเงินและออกใบเสร็จ'}
          </Button>
        </>
      )}
    >
      <div className="sale-checkout">
        <div className="sale-checkout__total">
          <span>ยอดสุทธิ</span>
          <Money value={sale.totalAmount} suffix=" บาท" />
        </div>

        {pending ? (
          <div className="sale-checkout__pending" role="alert">
            <TriangleAlert aria-hidden="true" />
            <div>
              <strong>{unknownOutcome ? 'ยังไม่ทราบผลการชำระเงิน' : 'มีคำขอชำระเงินครั้งก่อนที่ยังไม่ทราบผล'}</strong>
              <p>
                ระบบอาจบันทึกไปแล้ว — ส่งคำขอเดิมซ้ำได้อย่างปลอดภัย ระบบจะไม่รับเงินซ้ำ
                ({pending.payments.map((p) => `${paymentMethodLabel(p.method)} ${formatMoney(p.amount)}`).join(' + ')})
              </p>
              <Button size="sm" variant="ghost" disabled={checkout.isPending} onClick={discardPending}>ทิ้งคำขอเดิมแล้วกรอกใหม่</Button>
            </div>
          </div>
        ) : (
          <>
            <ol className="sale-pay-rows">
              {rows.map((row) => (
                <li key={row.key} className="sale-pay-row">
                  <label className="sale-field">
                    <span>ช่องทาง</span>
                    <Select value={row.method} onChange={(event) => updateRow(row.key, { method: event.target.value as PaymentMethod })}>
                      {PAYMENT_METHOD_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                    </Select>
                  </label>
                  <label className="sale-field">
                    <span>ยอด (บาท)</span>
                    <MoneyInput value={row.amount} onValueChange={(amount) => updateRow(row.key, { amount })} />
                  </label>
                  {row.method !== 'cash' ? (
                    <label className="sale-field sale-pay-row__ref">
                      <span>เลขอ้างอิง (ไม่บังคับ)</span>
                      <Input value={row.reference} maxLength={100} placeholder="เลขสลิป / 4 หลักท้ายบัตร" onChange={(event) => updateRow(row.key, { reference: event.target.value })} />
                    </label>
                  ) : <span className="sale-pay-row__ref" />}
                  <Button
                    variant="ghost"
                    size="icon"
                    className="icon-button icon-button--danger"
                    aria-label={`ลบช่องทาง ${paymentMethodLabel(row.method)}`}
                    disabled={rows.length === 1}
                    title={rows.length === 1 ? 'ต้องมีอย่างน้อย 1 ช่องทาง' : undefined}
                    onClick={() => setRows((current) => current.filter((r) => r.key !== row.key))}
                  >
                    <Trash2 aria-hidden="true" />
                  </Button>
                </li>
              ))}
            </ol>

            <div className="sale-checkout__split">
              <Button
                variant="outline"
                size="sm"
                disabled={remainingSatang <= 0}
                title={remainingSatang <= 0 ? 'ยอดครบแล้ว — ลดยอดช่องทางเดิมก่อนถ้าจะแบ่งจ่าย' : undefined}
                onClick={() => setRows((current) => [...current, { key: Date.now(), method: 'transfer', amount: fromSatang(remainingSatang), reference: '' }])}
              >
                <Plus aria-hidden="true" /> แบ่งจ่ายอีกช่องทาง
              </Button>
              <span className={remainingSatang === 0 ? 'sale-checkout__balance is-ok' : 'sale-checkout__balance'}>
                {remainingSatang === 0 ? 'ยอดครบพอดี' : remainingSatang > 0 ? <>คงเหลือ <Money value={fromSatang(remainingSatang)} /></> : <>เกิน <Money value={fromSatang(-remainingSatang)} /></>}
              </span>
            </div>

            {cashSatang > 0 ? (
              <div className="sale-cash">
                <label className="sale-field">
                  <span>รับเงินสดมา (บาท)</span>
                  <MoneyInput value={received} onValueChange={setReceived} />
                </label>
                <div className="sale-cash__quick" aria-label="จำนวนเงินที่รับมา">
                  {cashSuggestions(fromSatang(cashSatang)).map((value) => (
                    <button type="button" key={value} className={toSatang(received) === toSatang(value) ? 'is-active' : undefined} onClick={() => setReceived(value)}>
                      {value === fromSatang(cashSatang) ? 'พอดี' : formatMoney(value)}
                    </button>
                  ))}
                </div>
                <div className={`sale-cash__change${changeSatang < 0 ? ' is-short' : ''}`}>
                  <span>{changeSatang < 0 ? 'ยังขาด' : 'เงินทอน'}</span>
                  <Money value={fromSatang(Math.abs(changeSatang))} suffix=" บาท" />
                </div>
              </div>
            ) : null}

            {blockedReason ? <p className="disabled-reason"><CircleAlert aria-hidden="true" /> {blockedReason}</p> : null}
          </>
        )}

        {checkout.isError ? (
          <ErrorAlert
            title={unknownOutcome ? 'เชื่อมต่อไม่สำเร็จ — กด "ส่งคำขอเดิมอีกครั้ง"' : 'รับชำระเงินไม่สำเร็จ — ยังไม่ได้ตัดสต็อกหรือบันทึกเงิน'}
            error={checkout.error}
          />
        ) : null}
      </div>
    </ConfirmModal>
  )
}
