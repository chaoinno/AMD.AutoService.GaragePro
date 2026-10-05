import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowLeft, Ban, Barcode, CircleAlert, CircleCheck, LoaderCircle, Minus, Package, Plus, Search,
  Trash2, TriangleAlert, UserRound, Wallet, X,
} from 'lucide-react'
import { useCallback, useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { searchCatalog } from '../../api/catalog'
import { getCustomers } from '../../api/customerVehicles'
import { getWarehouses } from '../../api/masterData'
import {
  addSaleLine, cancelSale, deleteSaleLine, promotions, updateSale, updateSaleLine,
  type BillDiscountType, type Promotion, type Sale, type SaleLine, type UpdateSaleInput,
} from '../../api/sales'
import type { CatalogItem } from '../../api/types'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { MoneyInput } from '../../components/MoneyInput'
import { Button } from '../../components/ui/button'
import { Combobox } from '../../components/ui/combobox'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { formatDateTime, formatMoney, formatNumber } from '../../lib/format'
import { SaleCheckoutModal } from './SaleCheckoutModal'
import { ErrorAlert } from './saleFormat'
import { saleKey, useApplySale } from './useSale'

const LINE_SAVE_DELAY = 400
const HEADER_SAVE_DELAY = 500

function headerFromSale(sale: Sale): UpdateSaleInput {
  return {
    warehouseId: sale.warehouseId,
    legacyCustomerId: sale.legacyCustomerId ?? null,
    customerName: sale.customerName ?? null,
    customerPhone: sale.customerPhone ?? null,
    billDiscountType: sale.billDiscountType ?? 'none',
    billDiscountValue: sale.billDiscountValue ?? 0,
    billPromotionId: sale.billPromotionId ?? null,
    vatIncluded: sale.vatIncluded,
  }
}

export function promotionLabel(promotion: Pick<Promotion, 'name' | 'kind' | 'value' | 'maxAmount'>) {
  // ตัวเลือกใน select แคบ — ย่อค่าให้สั้น (ทศนิยมเฉพาะเมื่อมี) ชื่อโปรต้องอ่านออกก่อน
  const value = promotion.kind === 'percent'
    ? `−${formatNumber(promotion.value)}%${promotion.maxAmount ? ` ไม่เกิน ${formatNumber(promotion.maxAmount)}` : ''}`
    : `−${formatNumber(promotion.value)}฿`
  return `${promotion.name} ${value}`
}

/// งานที่รอส่งอยู่ (debounce) หรือกำลังส่ง — ปุ่มชำระเงินต้องรอให้ว่างก่อน ไม่งั้นยอดที่ส่งไปชำระ
/// อาจเป็นยอดก่อนการแก้ครั้งล่าสุด แล้วโดน SALE_PAYMENT_MISMATCH
function useSaveTracker() {
  const [pending, setPending] = useState(0)
  const track = useCallback(async <T,>(work: () => Promise<T>) => {
    setPending((n) => n + 1)
    try { return await work() } finally { setPending((n) => Math.max(0, n - 1)) }
  }, [])
  return { pending, track }
}

export function SaleEditor({ sale }: { sale: Sale }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const applySale = useApplySale()
  const { pending, track } = useSaveTracker()
  const [scheduled, setScheduled] = useState(0)
  const [saveError, setSaveError] = useState<unknown>(null)
  const [checkoutOpen, setCheckoutOpen] = useState(false)
  const [confirmCancel, setConfirmCancel] = useState(false)

  /// ส่งคำสั่งแก้บิล → วางผลลง cache · ถ้าล้มเหลวให้โหลดบิลจริงกลับมาทับค่าบนจอ (กันจอค้างค่าที่ server ไม่รับ)
  const save = useCallback((work: () => Promise<Sale>) => track(async () => {
    try {
      const next = await work()
      setSaveError(null)
      applySale(next)
      return next
    } catch (error) {
      setSaveError(error)
      void queryClient.invalidateQueries({ queryKey: saleKey(sale.id) })
      return null
    }
  }), [applySale, queryClient, sale.id, track])

  const onScheduleChange = useCallback((delta: number) => setScheduled((n) => Math.max(0, n + delta)), [])

  const cancel = useMutation({
    mutationFn: () => cancelSale(sale.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['sales'] })
      void queryClient.invalidateQueries({ queryKey: ['sale-draft-count'] })
      queryClient.removeQueries({ queryKey: saleKey(sale.id) })
      toast.success('ยกเลิกบิลร่างแล้ว')
      navigate('/sales')
    },
  })

  const busy = pending > 0 || scheduled > 0
  const shortLines = sale.lines.filter((line) => line.quantity > (line.availableInWarehouse ?? 0))
  const checkoutBlockedReason = sale.lines.length === 0
    ? 'เพิ่มสินค้าอย่างน้อย 1 รายการก่อนรับชำระเงิน'
    : busy
      ? 'กำลังบันทึกการแก้ไขล่าสุด รอสักครู่'
      : shortLines.length
        ? `${shortLines[0]?.name} ในคลังที่เลือกมีไม่พอ — ลดจำนวนหรือเปลี่ยนคลังก่อน`
        : sale.totalAmount <= 0
          ? 'ยอดสุทธิเป็น 0 บาท ชำระเงินไม่ได้ — ตรวจส่วนลด/โปรโมชัน'
          : null

  return (
    <AppShell title="ขายสินค้า · บิลร่าง">
      <section className="sale-editor-heading">
        <Button variant="ghost" size="icon" aria-label="กลับรายการบิล" onClick={() => navigate('/sales')}>
          <ArrowLeft aria-hidden="true" />
        </Button>
        <div className="sale-editor-heading__copy">
          <span className="sale-editor-heading__eyebrow">บิลขายหน้าร้าน · ฉบับร่าง</span>
          <h2>{sale.customerName || 'ลูกค้าทั่วไป'}</h2>
          <p>เปิดบิล {formatDateTime(sale.createdAt)} · ยังไม่ตัดสต็อกจนกว่าจะรับชำระเงิน</p>
        </div>
        <SaveIndicator busy={busy} failed={Boolean(saveError)} />
        <Button variant="outline" onClick={() => setConfirmCancel(true)} disabled={cancel.isPending}>
          <Ban aria-hidden="true" /> ยกเลิกบิลร่าง
        </Button>
      </section>

      {saveError ? (
        <div className="sale-save-error">
          <ErrorAlert title="บันทึกการแก้ไขไม่สำเร็จ — หน้าจอแสดงค่าล่าสุดที่ระบบบันทึกไว้แล้ว" error={saveError} />
          <Button variant="ghost" size="icon" aria-label="ปิดข้อความ" onClick={() => setSaveError(null)}><X aria-hidden="true" /></Button>
        </div>
      ) : null}

      <div className="sale-editor-grid">
        <ProductPanel sale={sale} save={save} />
        <LinesPanel sale={sale} save={save} onScheduleChange={onScheduleChange} />
        <SummaryPanel
          sale={sale}
          save={save}
          onScheduleChange={onScheduleChange}
          checkoutBlockedReason={checkoutBlockedReason}
          onCheckout={() => setCheckoutOpen(true)}
        />
      </div>

      <SaleCheckoutModal open={checkoutOpen} sale={sale} onClose={() => setCheckoutOpen(false)} />

      <ConfirmModal
        open={confirmCancel}
        size="small"
        title="ยกเลิกบิลร่างนี้?"
        description="บิลร่างยังไม่ได้ตัดสต็อกหรือรับเงิน การยกเลิกจึงไม่กระทบสต็อก แต่จะแก้ไขบิลนี้ต่อไม่ได้อีก"
        onClose={() => { setConfirmCancel(false); cancel.reset() }}
        footer={(
          <>
            <Button variant="outline" onClick={() => setConfirmCancel(false)}>กลับไปแก้ไขบิล</Button>
            <Button variant="destructive" disabled={cancel.isPending} onClick={() => cancel.mutate()}>
              {cancel.isPending ? <LoaderCircle className="spin" aria-hidden="true" /> : <Ban aria-hidden="true" />}
              ยกเลิกบิลร่าง
            </Button>
          </>
        )}
      >
        {cancel.isError ? <ErrorAlert title="ยกเลิกบิลร่างไม่สำเร็จ" error={cancel.error} /> : null}
      </ConfirmModal>
    </AppShell>
  )
}

function SaveIndicator({ busy, failed }: { busy: boolean; failed: boolean }) {
  if (busy) return <span className="sale-save-indicator"><LoaderCircle className="spin" aria-hidden="true" /> กำลังบันทึก…</span>
  if (failed) return <span className="sale-save-indicator sale-save-indicator--error"><CircleAlert aria-hidden="true" /> บันทึกล่าสุดไม่สำเร็จ</span>
  return <span className="sale-save-indicator sale-save-indicator--ok"><CircleCheck aria-hidden="true" /> บันทึกแล้ว</span>
}

type SaveFn = (work: () => Promise<Sale>) => Promise<Sale | null>

function PanelHeading({ step, title, hint }: { step: string; title: string; hint?: string }) {
  return (
    <div className="panel-heading">
      <div>
        <span className="panel-heading__step">{step}</span>
        <h3>{title}</h3>
      </div>
      {hint ? <span className="panel-heading__hint">{hint}</span> : null}
    </div>
  )
}

// ────────────────────────────── 01 สินค้า ──────────────────────────────

function ProductPanel({ sale, save }: { sale: Sale; save: SaveFn }) {
  const queryClient = useQueryClient()
  const inputRef = useRef<HTMLInputElement>(null)
  const [text, setText] = useState('')
  const [notice, setNotice] = useState<string | null>(null)
  const [addingId, setAddingId] = useState<string | null>(null)
  const term = text.trim()

  const results = useQuery({
    queryKey: ['catalog', term],
    queryFn: () => searchCatalog(term),
    enabled: term.length >= 2,
  })
  // ขายได้เฉพาะอะไหล่ที่ใช้สต็อก — ค่าแรงไม่มีทางผ่าน backend (SALE_ITEM_NOT_STOCKED) จึงไม่แสดงให้เสียเวลา
  const parts = (results.data ?? []).filter((item) => item.type === 'part' && item.id)
  const qtyInBill = new Map(sale.lines.map((line) => [line.catalogItemId, line.quantity]))

  useEffect(() => { inputRef.current?.focus() }, [])

  const add = async (item: CatalogItem) => {
    if (!item.id) return
    setAddingId(item.id)
    const next = await save(() => addSaleLine(sale.id, { catalogItemId: item.id!, quantity: 1, discountPercent: 0, promotionId: null }))
    setAddingId(null)
    if (next) setNotice(`เพิ่ม ${item.code} ${item.name} แล้ว`)
    return next
  }

  /// [UI] เครื่องสแกนบาร์โค้ดพิมพ์รหัสแล้วกด Enter เหมือนคีย์บอร์ด — รหัสตรงเป๊ะ = เพิ่มทันทีแล้วล้างช่องรอสแกนตัวถัดไป
  const onKeyDown = async (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Escape') { setText(''); setNotice(null); return }
    if (event.key !== 'Enter' || !term) return
    event.preventDefault()
    const list = results.data && results.isSuccess && term.length >= 2
      ? results.data
      : await queryClient.fetchQuery({ queryKey: ['catalog', term], queryFn: () => searchCatalog(term) }).catch(() => [])
    const exact = list.find((item) => item.type === 'part' && item.id && item.code.toLowerCase() === term.toLowerCase())
    if (!exact) { setNotice(`ไม่พบรหัส "${term}" ตรงตัว — เลือกสินค้าจากรายการด้านล่าง`); return }
    const next = await add(exact)
    if (next) setText('')
    inputRef.current?.focus()
  }

  return (
    <section className="sale-panel sale-product-panel" aria-label="ค้นหาสินค้า">
      <PanelHeading step="01" title="สินค้า" hint="อะไหล่ที่ใช้สต็อก" />
      <div className="sale-product-search">
        <div className="input-with-icon">
          <Search aria-hidden="true" />
          <Input
            ref={inputRef}
            aria-label="ค้นหาสินค้า"
            className="sale-product-search__input"
            type="search"
            autoComplete="off"
            placeholder="สแกนบาร์โค้ด หรือพิมพ์รหัส/ชื่อ"
            value={text}
            onChange={(event) => { setText(event.target.value); setNotice(null) }}
            onKeyDown={(event) => void onKeyDown(event)}
          />
        </div>
        <p className="sale-hint"><Barcode aria-hidden="true" /> พิมพ์รหัสตรงตัวแล้วกด Enter = เพิ่มลงบิลทันที</p>
        {notice ? <p className="sale-hint sale-hint--notice" role="status">{notice}</p> : null}
      </div>

      <div className="sale-product-results">
        {term.length < 2 ? (
          <div className="catalog-prompt">
            <span className="catalog-prompt__icon" aria-hidden="true"><Package /></span>
            <strong>ค้นหาสินค้าเพื่อเพิ่มลงบิล</strong>
            <p>พิมพ์อย่างน้อย 2 ตัวอักษร</p>
          </div>
        ) : results.isPending ? (
          <div className="catalog-loading"><span className="spinner" aria-hidden="true" /> กำลังค้นหา…</div>
        ) : results.isError ? (
          <div className="sale-inline-state sale-inline-state--error">
            <CircleAlert aria-hidden="true" />
            <span>{isApiError(results.error) ? results.error.messageTh : 'ค้นหาสินค้าไม่สำเร็จ'}</span>
            <small className="trace-id">traceId: {isApiError(results.error) ? results.error.traceId : 'ไม่มี'}</small>
            <Button size="sm" variant="outline" onClick={() => void results.refetch()}>ลองใหม่</Button>
          </div>
        ) : parts.length === 0 ? (
          <div className="sale-inline-state">
            <Search aria-hidden="true" />
            <span>ไม่พบอะไหล่ "{term}"</span>
            <small>ค่าแรงและสินค้าที่ไม่ใช้สต็อกขายหน้าร้านไม่ได้</small>
          </div>
        ) : (
          parts.map((item) => {
            const out = item.available <= 0
            const inBill = qtyInBill.get(item.id!)
            return (
              <button
                type="button"
                key={item.id}
                className="sale-product-card"
                disabled={out || addingId !== null}
                aria-label={out ? `${item.name} หมดสต็อก เพิ่มไม่ได้` : `เพิ่ม ${item.name} ลงบิล`}
                onClick={() => void add(item)}
              >
                <span className="sale-product-card__top">
                  <span className="sale-product-card__code">{item.code}</span>
                  {inBill ? <span className="sale-product-card__inbill">ในบิล ×{inBill}</span> : null}
                </span>
                <strong className="sale-product-card__name">{item.name}</strong>
                <span className="sale-product-card__meta">
                  <Money value={item.price} suffix=" บาท" />
                  <span className={out ? 'stock-out' : item.available <= 3 ? 'stock-low' : 'stock-ok'}>
                    {out ? <><Ban aria-hidden="true" /> หมดสต็อก</> : `พร้อมขาย ${formatNumber(item.available)} ${item.unit}`}
                  </span>
                </span>
                {addingId === item.id ? <span className="sale-product-card__adding"><LoaderCircle className="spin" aria-hidden="true" /> กำลังเพิ่ม…</span> : null}
              </button>
            )
          })
        )}
      </div>
    </section>
  )
}

// ────────────────────────────── 02 รายการขาย ──────────────────────────────

function LinesPanel({ sale, save, onScheduleChange }: { sale: Sale; save: SaveFn; onScheduleChange: (delta: number) => void }) {
  const linePromotions = useQuery({ queryKey: ['promotions', 'applicable', 'line'], queryFn: () => promotions('line') })
  const itemCount = sale.lines.reduce((sum, line) => sum + line.quantity, 0)

  return (
    <section className="sale-panel sale-lines-panel" aria-label="รายการขาย">
      <PanelHeading step="02" title="รายการขาย" hint={sale.lines.length ? `${sale.lines.length} รายการ · ${formatNumber(itemCount)} ชิ้น` : undefined} />
      {sale.lines.length === 0 ? (
        <div className="line-empty">
          <span aria-hidden="true"><Plus /></span>
          <strong>ยังไม่มีสินค้าในบิล</strong>
          <p>สแกนบาร์โค้ด หรือค้นหาสินค้าจากพาเนลซ้ายแล้วกดที่การ์ดเพื่อเพิ่ม · เพิ่มสินค้าเดิมซ้ำจะบวกจำนวนเข้าบรรทัดเดิม</p>
        </div>
      ) : (
        <ol className="sale-lines">
          {sale.lines.map((line, index) => (
            <SaleLineRow
              key={line.id}
              index={index + 1}
              saleId={sale.id}
              line={line}
              promotions={linePromotions.data ?? []}
              save={save}
              onScheduleChange={onScheduleChange}
            />
          ))}
        </ol>
      )}
    </section>
  )
}

function SaleLineRow({ index, saleId, line, promotions: options, save, onScheduleChange }: {
  index: number
  saleId: string
  line: SaleLine
  promotions: Promotion[]
  save: SaveFn
  onScheduleChange: (delta: number) => void
}) {
  const [quantity, setQuantity] = useState(String(line.quantity))
  const [discount, setDiscount] = useState(String(line.discountPercent))
  const [promotionId, setPromotionId] = useState(line.promotionId ?? '')
  const [deleting, setDeleting] = useState(false)
  const timer = useRef<number | null>(null)
  const inFlight = useRef(false)

  // รับค่าที่ server คำนวณ/บันทึกแล้วกลับมาแสดง เฉพาะตอนที่ไม่มีการแก้ค้างอยู่ ไม่งั้นตัวเลขที่กำลังพิมพ์จะเด้งกลับ
  useEffect(() => {
    if (timer.current !== null || inFlight.current) return
    setQuantity(String(line.quantity))
    setDiscount(String(line.discountPercent))
    setPromotionId(line.promotionId ?? '')
  }, [line.quantity, line.discountPercent, line.promotionId])

  useEffect(() => () => { if (timer.current !== null) { window.clearTimeout(timer.current); onScheduleChange(-1) } }, [onScheduleChange])

  const qtyValue = Number(quantity)
  const discountValue = Number(discount)
  const qtyError = !Number.isInteger(qtyValue) || qtyValue < 1 ? 'จำนวนต้องเป็นจำนวนเต็มตั้งแต่ 1' : null
  const discountError = discount === '' || Number.isNaN(discountValue) || discountValue < 0 || discountValue > 100 ? 'ส่วนลด 0–100%' : null

  const schedule = (next: { quantity: string; discount: string; promotionId: string }, delay = LINE_SAVE_DELAY) => {
    const q = Number(next.quantity); const d = Number(next.discount)
    if (timer.current !== null) { window.clearTimeout(timer.current); timer.current = null; onScheduleChange(-1) }
    if (!Number.isInteger(q) || q < 1 || next.discount === '' || Number.isNaN(d) || d < 0 || d > 100) return
    onScheduleChange(1)
    timer.current = window.setTimeout(() => {
      timer.current = null
      onScheduleChange(-1)
      inFlight.current = true
      void save(() => updateSaleLine(saleId, line.id, { quantity: q, discountPercent: d, promotionId: next.promotionId || null }))
        .finally(() => { inFlight.current = false })
    }, delay)
  }

  const setQty = (value: string) => { setQuantity(value); schedule({ quantity: value, discount, promotionId }) }
  const step = (delta: number) => setQty(String(Math.max(1, (Number.isInteger(qtyValue) ? qtyValue : line.quantity) + delta)))

  const remove = async () => {
    if (timer.current !== null) { window.clearTimeout(timer.current); timer.current = null; onScheduleChange(-1) }
    setDeleting(true)
    const next = await save(() => deleteSaleLine(saleId, line.id))
    if (!next) setDeleting(false)
  }

  const available = line.availableInWarehouse ?? 0
  const short = line.quantity > available
  const selectedPromoMissing = line.promotionId && !options.some((p) => p.id === line.promotionId)

  return (
    <li className={`sale-line${short ? ' sale-line--short' : ''}`}>
      <div className="sale-line__title">
        <span className="line-sequence">{index}</span>
        <span className="sale-line__name">
          <strong>{line.name}</strong>
          <small>{line.code} · <Money value={line.unitPrice} /> / {line.unit}</small>
        </span>
        <span className="sale-line__net">
          <small>สุทธิ</small>
          <Money value={line.netAmount} />
        </span>
        <Button
          variant="ghost"
          size="icon"
          className="icon-button icon-button--danger"
          aria-label={`ลบ ${line.name} ออกจากบิล`}
          disabled={deleting}
          onClick={() => void remove()}
        >
          {deleting ? <LoaderCircle className="spin" aria-hidden="true" /> : <Trash2 aria-hidden="true" />}
        </Button>
      </div>

      <div className="sale-line__fields">
        <div className="sale-field">
          <span id={`qty-${line.id}`}>จำนวน</span>
          <div className="sale-stepper" role="group" aria-labelledby={`qty-${line.id}`}>
            <button type="button" aria-label="ลดจำนวน" disabled={qtyValue <= 1} onClick={() => step(-1)}><Minus aria-hidden="true" /></button>
            <input
              aria-label={`จำนวน ${line.name}`}
              inputMode="numeric"
              value={quantity}
              aria-invalid={Boolean(qtyError)}
              onChange={(event) => setQty(event.target.value.replace(/[^\d]/g, ''))}
              onFocus={(event) => event.currentTarget.select()}
            />
            <button type="button" aria-label="เพิ่มจำนวน" onClick={() => step(1)}><Plus aria-hidden="true" /></button>
          </div>
        </div>
        <label className="sale-field">
          <span>ส่วนลด %</span>
          <Input
            className="money"
            inputMode="decimal"
            value={discount}
            aria-invalid={Boolean(discountError)}
            onFocus={(event) => event.currentTarget.select()}
            onChange={(event) => {
              const value = event.target.value
              if (!/^\d*(\.\d{0,2})?$/.test(value)) return
              setDiscount(value)
              schedule({ quantity, discount: value, promotionId })
            }}
          />
        </label>
        <label className="sale-field sale-field--wide">
          <span>โปรโมชัน</span>
          <Select
            value={promotionId}
            onChange={(event) => {
              setPromotionId(event.target.value)
              schedule({ quantity, discount, promotionId: event.target.value }, 0)
            }}
          >
            <option value="">ไม่ใช้โปรโมชัน</option>
            {selectedPromoMissing ? <option value={line.promotionId!}>{line.promotionName || line.promotionCode} (ใช้ไม่ได้แล้ว)</option> : null}
            {options.map((promotion) => <option key={promotion.id} value={promotion.id}>{promotionLabel(promotion)}</option>)}
          </Select>
        </label>
      </div>

      {qtyError || discountError ? (
        <p className="sale-line__error" role="alert"><CircleAlert aria-hidden="true" /> {qtyError ?? discountError} — ยังไม่บันทึกจนกว่าจะแก้ให้ถูก</p>
      ) : null}
      {line.discountAmount > 0 || line.promotionAmount > 0 ? (
        <p className="sale-line__breakdown">
          <span>ราคารวม <Money value={line.unitPrice * line.quantity} /></span>
          {line.discountAmount > 0 ? <span>ส่วนลด <Money value={line.discountAmount} prefix="−" /></span> : null}
          {line.promotionAmount > 0 ? <span>{line.promotionName || 'โปรโมชัน'} <Money value={line.promotionAmount} prefix="−" /></span> : null}
        </p>
      ) : null}
      {short ? (
        <p className="sale-line__stock" role="alert">
          <TriangleAlert aria-hidden="true" />
          {available > 0 ? `คลังที่เลือกมีพร้อมขายเพียง ${formatNumber(available)} ${line.unit}` : 'คลังที่เลือกไม่มีสินค้านี้พร้อมขาย'} — ลดจำนวนหรือเปลี่ยนคลังก่อนชำระเงิน
        </p>
      ) : null}
    </li>
  )
}

// ────────────────────────────── 03 สรุปและชำระเงิน ──────────────────────────────

function SummaryPanel({ sale, save, onScheduleChange, checkoutBlockedReason, onCheckout }: {
  sale: Sale
  save: SaveFn
  onScheduleChange: (delta: number) => void
  checkoutBlockedReason: string | null
  onCheckout: () => void
}) {
  const [draft, setDraft] = useState<UpdateSaleInput>(() => headerFromSale(sale))
  const timer = useRef<number | null>(null)
  const inFlight = useRef(false)
  const warehouses = useQuery({ queryKey: ['warehouses', 'sale'], queryFn: () => getWarehouses() })
  const billPromotions = useQuery({ queryKey: ['promotions', 'applicable', 'bill'], queryFn: () => promotions('bill') })

  useEffect(() => {
    if (timer.current !== null || inFlight.current) return
    setDraft(headerFromSale(sale))
  }, [sale])

  useEffect(() => () => { if (timer.current !== null) { window.clearTimeout(timer.current); onScheduleChange(-1) } }, [onScheduleChange])

  const push = (patch: Partial<UpdateSaleInput>, delay = HEADER_SAVE_DELAY) => {
    const next = { ...draft, ...patch }
    setDraft(next)
    if (timer.current !== null) { window.clearTimeout(timer.current); timer.current = null; onScheduleChange(-1) }
    if (next.billDiscountType === 'percent' && next.billDiscountValue > 100) return
    onScheduleChange(1)
    timer.current = window.setTimeout(() => {
      timer.current = null
      onScheduleChange(-1)
      inFlight.current = true
      void save(() => updateSale(sale.id, next)).finally(() => { inFlight.current = false })
    }, delay)
  }

  const selectedBillPromo = billPromotions.data?.find((p) => p.id === draft.billPromotionId)
  const billBase = sale.subtotalAmount - sale.billDiscountAmount
  const promoBelowMin = Boolean(sale.billPromotionId && selectedBillPromo?.minSubtotal && billBase < selectedBillPromo.minSubtotal)
  const billPromoMissing = draft.billPromotionId && billPromotions.isSuccess && !selectedBillPromo

  return (
    <aside className="sale-panel sale-summary-panel" aria-label="สรุปและชำระเงิน">
      <div className="sale-summary-panel__sticky">
        <PanelHeading step="03" title="สรุปและชำระเงิน" />

        <div className="sale-summary-section">
          <CustomerPicker draft={draft} onChange={push} />
        </div>

        <div className="sale-summary-section">
          <label className="sale-field">
            <span>ตัดสต็อกจากคลัง</span>
            <Select value={draft.warehouseId} disabled={!warehouses.data} onChange={(event) => push({ warehouseId: event.target.value }, 0)}>
              {!warehouses.data ? <option value={draft.warehouseId}>กำลังโหลดคลัง…</option> : null}
              {warehouses.data?.filter((w) => w.isActive || w.id === draft.warehouseId).map((w) => (
                <option key={w.id} value={w.id}>{w.code} · {w.name}</option>
              ))}
            </Select>
          </label>

          <div className="sale-field">
            <span>ส่วนลดท้ายบิล</span>
            <div className="sale-discount-row">
              <div className="sale-segmented" role="radiogroup" aria-label="รูปแบบส่วนลดท้ายบิล">
                {([['none', 'ไม่ลด'], ['percent', '%'], ['amount', 'บาท']] as [BillDiscountType, string][]).map(([value, label]) => (
                  <button
                    key={value}
                    type="button"
                    role="radio"
                    aria-checked={draft.billDiscountType === value}
                    className={draft.billDiscountType === value ? 'is-active' : undefined}
                    onClick={() => push({ billDiscountType: value, billDiscountValue: value === 'none' ? 0 : draft.billDiscountValue }, 0)}
                  >
                    {label}
                  </button>
                ))}
              </div>
              {draft.billDiscountType !== 'none' ? (
                <MoneyInput value={draft.billDiscountValue} onValueChange={(value) => push({ billDiscountValue: value })} />
              ) : null}
            </div>
            {draft.billDiscountType === 'percent' && draft.billDiscountValue > 100 ? (
              <small className="sale-field__error">ส่วนลดเป็น % ต้องไม่เกิน 100</small>
            ) : null}
          </div>

          <label className="sale-field">
            <span>โปรโมชันท้ายบิล</span>
            <Select value={draft.billPromotionId ?? ''} onChange={(event) => push({ billPromotionId: event.target.value || null }, 0)}>
              <option value="">ไม่ใช้โปรโมชัน</option>
              {billPromoMissing ? <option value={draft.billPromotionId!}>{sale.billPromotionName || sale.billPromotionCode || 'โปรเดิม'} (ใช้ไม่ได้แล้ว)</option> : null}
              {billPromotions.data?.map((promotion) => (
                <option key={promotion.id} value={promotion.id}>
                  {promotionLabel(promotion)}{promotion.minSubtotal ? ` · ขั้นต่ำ ${formatMoney(promotion.minSubtotal)}` : ''}
                </option>
              ))}
            </Select>
            {promoBelowMin ? (
              <small className="sale-field__warn"><TriangleAlert aria-hidden="true" /> ยอดยังไม่ถึงขั้นต่ำ {formatMoney(selectedBillPromo!.minSubtotal)} บาท — โปรนี้ยังไม่ลดให้</small>
            ) : null}
          </label>

          <label className="sale-check">
            <input type="checkbox" checked={draft.vatIncluded} onChange={(event) => push({ vatIncluded: event.target.checked }, 0)} />
            <span>คิดภาษีมูลค่าเพิ่ม {formatNumber(sale.vatRate * 100 || 7)}% (บวกเพิ่มจากราคา)</span>
          </label>
        </div>

        <div className="money-summary sale-money-summary">
          <SummaryRow label="ยอดก่อนส่วนลด" value={sale.grossAmount} />
          <SummaryRow label="ส่วนลดรายการ" value={sale.lineDiscountAmount} deduction />
          <SummaryRow label="โปรโมชันรายการ" value={sale.linePromotionAmount} deduction />
          {sale.lineDiscountAmount > 0 || sale.linePromotionAmount > 0 ? <SummaryRow label="รวมรายการ" value={sale.subtotalAmount} /> : null}
          <SummaryRow label="ส่วนลดท้ายบิล" value={sale.billDiscountAmount} deduction />
          <SummaryRow label={sale.billPromotionName ? `โปร: ${sale.billPromotionName}` : 'โปรโมชันท้ายบิล'} value={sale.billPromotionAmount} deduction />
          <div className="money-summary__divider" />
          <SummaryRow label="ยอดก่อนภาษี" value={sale.netAmount} always />
          <SummaryRow label={sale.vatIncluded ? `ภาษีมูลค่าเพิ่ม ${formatNumber(sale.vatRate * 100)}%` : 'ไม่คิดภาษีมูลค่าเพิ่ม'} value={sale.vatAmount} always />
          <div className="money-summary__total">
            <span>ยอดสุทธิ</span>
            <Money value={sale.totalAmount} />
          </div>
        </div>

        <div className="sale-checkout-area">
          <Button
            size="lg"
            className="sale-checkout-button"
            disabled={Boolean(checkoutBlockedReason)}
            aria-describedby={checkoutBlockedReason ? 'sale-checkout-reason' : undefined}
            onClick={onCheckout}
          >
            <Wallet aria-hidden="true" /> รับชำระเงิน <Money value={sale.totalAmount} suffix=" บาท" />
          </Button>
          {checkoutBlockedReason ? (
            <p className="disabled-reason" id="sale-checkout-reason"><CircleAlert aria-hidden="true" /> {checkoutBlockedReason}</p>
          ) : (
            <p className="sale-hint">ตัดสต็อก FIFO + ออกใบเสร็จ SL- พร้อมกันตอนยืนยันรับเงิน</p>
          )}
        </div>
      </div>
    </aside>
  )
}

function SummaryRow({ label, value, deduction, always }: { label: string; value: number; deduction?: boolean; always?: boolean }) {
  if (!always && !value) return null
  return (
    <div className={`money-summary__row${deduction ? ' money-summary__deduction' : ''}`}>
      <span>{label}</span>
      <Money value={value} prefix={deduction ? '−' : undefined} />
    </div>
  )
}

/// [BIZ] ลูกค้าไม่บังคับ — ค้นลูกค้าในระบบด้วยชื่อ/เบอร์ (อ่านอย่างเดียว snapshot ชื่อ/เบอร์ลงบิล)
/// หรือ walk-in พิมพ์ชื่อ/เบอร์เอง — docs/11 ข้อ 4 · ไม่มีการเขียน legacy Customer
function CustomerPicker({ draft, onChange }: { draft: UpdateSaleInput; onChange: (patch: Partial<UpdateSaleInput>, delay?: number) => void }) {
  const [mode, setMode] = useState<'walkin' | 'search'>(draft.legacyCustomerId ? 'search' : 'walkin')
  const [query, setQuery] = useState('')
  const term = query.trim()
  const customers = useQuery({
    queryKey: ['sale-customers', term],
    queryFn: () => getCustomers({ keyword: term, pageSize: 10 }),
    enabled: mode === 'search' && term.length >= 2,
  })

  return (
    <div className="sale-customer">
      <div className="sale-customer__head">
        <span className="sale-customer__label"><UserRound aria-hidden="true" /> ลูกค้า</span>
        <div className="sale-segmented sale-segmented--small" role="radiogroup" aria-label="ประเภทลูกค้า">
          <button type="button" role="radio" aria-checked={mode === 'walkin'} className={mode === 'walkin' ? 'is-active' : undefined}
            onClick={() => { setMode('walkin'); if (draft.legacyCustomerId) onChange({ legacyCustomerId: null }, 0) }}>
            ทั่วไป
          </button>
          <button type="button" role="radio" aria-checked={mode === 'search'} className={mode === 'search' ? 'is-active' : undefined}
            onClick={() => setMode('search')}>
            ในระบบ
          </button>
        </div>
      </div>

      {mode === 'search' && draft.legacyCustomerId ? (
        <div className="sale-customer__selected">
          <span>
            <strong>{draft.customerName}</strong>
            <small>{draft.customerPhone || 'ไม่มีเบอร์โทร'}</small>
          </span>
          <Button size="sm" variant="ghost" onClick={() => onChange({ legacyCustomerId: null, customerName: null, customerPhone: null }, 0)}>เปลี่ยน</Button>
        </div>
      ) : mode === 'search' ? (
        <Combobox
          id="sale-customer-search"
          ariaLabel="ค้นหาลูกค้าด้วยชื่อหรือเบอร์โทร"
          placeholder="ค้นหาชื่อ / เบอร์โทร"
          query={query}
          onQueryChange={setQuery}
          loading={customers.isFetching}
          emptyLabel={term.length < 2 ? 'พิมพ์อย่างน้อย 2 ตัวอักษร' : 'ไม่พบลูกค้า'}
          options={(customers.data?.items ?? []).map((c) => ({
            value: String(c.id),
            label: c.fullName,
            description: [c.phoneNumber1, c.phoneNumber2].filter(Boolean).join(' · ') || c.code,
          }))}
          onSelect={(option) => {
            const customer = customers.data?.items.find((c) => String(c.id) === option.value)
            if (!customer) return
            setQuery('')
            onChange({ legacyCustomerId: customer.id, customerName: customer.fullName, customerPhone: customer.phoneNumber1 || customer.phoneNumber2 || null }, 0)
          }}
        />
      ) : (
        <div className="sale-customer__walkin">
          <Input aria-label="ชื่อลูกค้า" placeholder="ชื่อ (ไม่บังคับ)" maxLength={200} value={draft.customerName ?? ''}
            onChange={(event) => onChange({ customerName: event.target.value || null })} />
          <Input aria-label="เบอร์โทรลูกค้า" placeholder="เบอร์โทร (ไม่บังคับ)" inputMode="tel" maxLength={30} value={draft.customerPhone ?? ''}
            onChange={(event) => onChange({ customerPhone: event.target.value || null })} />
        </div>
      )}
    </div>
  )
}
