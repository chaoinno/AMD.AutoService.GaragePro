import { useMutation } from '@tanstack/react-query'
import {
  CalendarCheck,
  Check,
  CircleAlert,
  CircleCheck,
  ExternalLink,
  FileText,
  Hourglass,
  ImageUp,
  Info,
  Landmark,
  Lock,
  Send,
  ShieldCheck,
  TriangleAlert,
  Upload,
  X,
} from 'lucide-react'
import { type DragEvent, type FormEvent, type ReactNode, useEffect, useMemo, useRef, useState } from 'react'
import { toast } from 'sonner'
import { isApiError, isRateLimitedError } from '../../api/client'
import { submitPackagePayment, type PackageMonthOption, type PackageOverview } from '../../api/package'
import type { ApiError } from '../../api/types'
import { Money } from '../../components/Money'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Textarea } from '../../components/ui/textarea'
import { formatDateOnly, formatMoney } from '../../lib/format'
import { CopyButton } from './PackageParts'
import {
  accountNoForCopy,
  addDaysToDateInput,
  amountForCopy,
  cannotSubmitReason,
  compareAmount,
  composePaidAt,
  DEFAULT_PAID_AT_MAX_PAST_DAYS,
  formatFileSize,
  formFieldForError,
  hasErrors,
  isImageSlipType,
  isStateChangedError,
  isUnknownOutcomeError,
  missingFieldLabels,
  monthOptionLabel,
  NOTE_MAX_LENGTH,
  parseAmountInput,
  PAYMENT_FIELD_ORDER,
  pickDroppedFile,
  priceBreakdown,
  renewStepStates,
  retryAfterText,
  SLIP_ACCEPT,
  slipContentType,
  slipFileError,
  thaiNowInputs,
  validatePaymentForm,
  vatBreakdownText,
  vatLabel,
  type PaymentFormErrors,
  type PaymentFormField,
  type PriceBreakdown,
  type RenewStepState,
} from './packageUx'
import { LINE_OA_ID, LINE_OA_URL } from './trialUx'

const monthInputId = (months: number) => `package-month-${months}`

/** ยอดรวม VAT ที่เติมให้ในช่องยอดเงิน แสดงแบบมีจุลภาค ("12,836.79") · ส่งจริงผ่าน parseAmountInput ซึ่งรับจุลภาคได้ */
function amountPrefill(option: PackageMonthOption | undefined): string {
  const total = priceBreakdown(option)?.total
  return total ? formatMoney(total) : ''
}
const SLIP_INPUT_ID = 'package-slip'
const SLIP_BUTTON_ID = 'package-slip-button'

/**
 * ต่ออายุใน 3 ขั้น (docs/17 §5.1 · §6.1 · §6.3): เลือกระยะเวลา → โอนเงิน → แนบสลิป + สรุปยอดข้างฟอร์ม
 *
 * แสดงทุกบทบาทเพื่อให้เห็นราคาและบัญชีรับเงิน · ส่งได้เฉพาะเมื่อ server ตอบ `canSubmit: true`
 * (ไม่ได้ = ขั้นที่ 3 และปุ่มส่งแสดงเหตุผลจาก server แทนช่องกรอก)
 *
 * requestId: สร้างครั้งเดียวต่อคำขอ · ส่งซ้ำหลังเน็ตหลุด (ไม่รู้ผล) ใช้ค่าเดิมเสมอแม้แก้ฟอร์ม เพราะ server อาจบันทึกไปแล้ว
 * (ส่งซ้ำได้แถวเดิม ไม่อัปโหลดซ้ำ ไม่ส่ง LINE ซ้ำ) · สร้างใหม่เฉพาะเมื่อแก้ฟอร์มหลัง server ตอบผลชัดเจนแล้ว
 */
export function PackageRenewForm({
  overview,
  onChanged,
}: {
  overview: PackageOverview
  /** ให้หน้าโหลดภาพรวม/ประวัติใหม่ (ส่งสำเร็จ หรือสถานะฝั่ง server อาจเปลี่ยนแล้ว) */
  onChanged: () => void
}) {
  const canSubmit = overview.canSubmit
  const blockedReason = cannotSubmitReason(overview)
  const options = useMemo(() => overview.monthOptions ?? [], [overview.monthOptions])
  const monthValues = useMemo(() => options.map((option) => option.months), [options])
  const maxPastDays = overview.paidAtMaxPastDays && overview.paidAtMaxPastDays > 0
    ? overview.paidAtMaxPastDays
    : DEFAULT_PAID_AT_MAX_PAST_DAYS

  // มีตัวเลือกเดียว = เลือกให้เลย · หลายตัวเลือกต้องให้ผู้ใช้เลือกเอง (เรื่องเงิน ไม่เดาแทน)
  const onlyOption = options.length === 1 ? options[0] : undefined
  const [monthCount, setMonthCount] = useState<number | null>(() => onlyOption?.months ?? null)
  const [amountText, setAmountText] = useState(() => amountPrefill(onlyOption))
  const [amountTouched, setAmountTouched] = useState(false)
  const [paidDate, setPaidDate] = useState(() => thaiNowInputs().date)
  const [paidTime, setPaidTime] = useState('')
  const [slip, setSlip] = useState<File | null>(null)
  const [note, setNote] = useState('')
  const [dragActive, setDragActive] = useState(false)
  const [dropNotice, setDropNotice] = useState<string | null>(null)
  const [submitAttempted, setSubmitAttempted] = useState(false)
  const [serverErrors, setServerErrors] = useState<PaymentFormErrors>({})
  const [generalError, setGeneralError] = useState<ApiError | null>(null)
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())
  // true = server ตอบผลชัดเจนแล้ว (สำเร็จหรือปฏิเสธ) → แก้ฟอร์มครั้งถัดไปเป็นคำขอใหม่
  const answeredRef = useRef(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  // ลากไฟล์พลาดเป้าแล้วปล่อยบนหน้า: เบราว์เซอร์จะเปิดไฟล์แทนหน้านี้ (ข้อมูลที่กรอกหาย) จึงกันไว้ทั้งหน้า
  useEffect(() => {
    if (!canSubmit) return
    const guard = (event: globalThis.DragEvent) => {
      if (event.dataTransfer?.types.includes('Files')) event.preventDefault()
    }
    window.addEventListener('dragover', guard)
    window.addEventListener('drop', guard)
    return () => {
      window.removeEventListener('dragover', guard)
      window.removeEventListener('drop', guard)
    }
  }, [canSubmit])

  const slipPreviewUrl = useObjectUrl(slip && isImageSlipType(slipContentType(slip)) ? slip : null)
  const now = thaiNowInputs()
  const selectedOption = options.find((option) => option.months === monthCount)
  const selectedPrice = priceBreakdown(selectedOption)

  const formInput = { monthCount, amountText, paidDate, paidTime, slip, note }
  const liveErrors = validatePaymentForm(formInput, { now: new Date(), maxPastDays, monthOptions: monthValues })
  const clientErrors: PaymentFormErrors = submitAttempted ? liveErrors : {}
  // ไฟล์ผิดชนิด/ใหญ่เกิน บอกทันทีที่เลือก ไม่ต้องรอกดส่ง
  const slipPickError = slip ? slipFileError(slip) : null
  const errorFor = (field: PaymentFormField) =>
    clientErrors[field] ?? serverErrors[field] ?? (field === 'slip' ? slipPickError ?? undefined : undefined)

  const parsedAmount = parseAmountInput(amountText)
  const liveSteps = renewStepStates({
    monthChosen: monthCount !== null,
    slipAttached: Boolean(slip) && !slipPickError,
    detailsReady: parsedAmount.ok && !liveErrors.paidAt && !liveErrors.note,
  })
  // ส่งไม่ได้ = ขั้นตอนเป็นข้อมูลอ้างอิง (ราคา/บัญชี) ไม่มีขั้นไหนรอให้ทำ
  const steps: readonly [RenewStepState, RenewStepState, RenewStepState] = canSubmit ? liveSteps : ['todo', 'todo', 'todo']
  const missing = missingFieldLabels(liveErrors)

  const mutation = useMutation({
    mutationFn: submitPackagePayment,
    onSuccess: () => {
      answeredRef.current = false
      toast.success('ส่งแจ้งชำระเงินแล้ว ทีมงานจะตรวจสลิปแล้วแจ้งผลในระบบ')
      setRequestId(crypto.randomUUID())
      resetForm()
      onChanged()
    },
    onError: (error) => {
      if (!isApiError(error)) {
        answeredRef.current = true
        setGeneralError(null)
        toast.error('ส่งแจ้งชำระไม่สำเร็จ')
        return
      }
      if (isUnknownOutcomeError(error.code)) {
        // ไม่รู้ว่าบันทึกไปแล้วหรือยัง: เก็บ requestId เดิม และให้หน้าโหลดสถานะใหม่ (ถ้าบันทึกแล้วจะเห็นรายการรอตรวจ)
        answeredRef.current = false
        setGeneralError(error)
        onChanged()
        return
      }
      answeredRef.current = true
      const field = formFieldForError(error)
      if (field) {
        setServerErrors((current) => ({ ...current, [field]: error.messageTh }))
        setGeneralError(null)
        focusField(field)
        return
      }
      setGeneralError(error)
      // สถานะของอู่ฝั่ง server เปลี่ยนไปแล้ว (มีรายการรอตรวจ · ไม่ใช่ผู้จัดการ · ยังไม่ตั้งบัญชี · ต่ออายุไม่ได้)
      // → โหลดภาพรวมใหม่ ฟอร์มจะแสดงเหตุผลจาก server แทนช่องกรอก · แจ้งด้วย toast เพราะช่องกรอกจะหายไป
      if (isStateChangedError(error.code)) {
        toast.error(error.messageTh)
        onChanged()
      }
    },
  })

  const focusField = (field: PaymentFormField) => {
    const id = field === 'monthCount'
      ? monthInputId(monthCount ?? options[0]?.months ?? 0)
      : field === 'slip'
        ? SLIP_BUTTON_ID
        : field === 'paidAt'
          ? 'package-paid-date'
          : `package-${field}`
    document.getElementById(id)?.focus()
  }

  /** ทุกการแก้ฟอร์มผ่านที่นี่: ล้าง error ของช่องนั้น · หมุน requestId ถ้า server ตอบคำขอเดิมไปแล้ว */
  const edited = (field: PaymentFormField) => {
    setServerErrors((current) => {
      if (!current[field]) return current
      const next = { ...current }
      delete next[field]
      return next
    })
    if (answeredRef.current) {
      answeredRef.current = false
      setRequestId(crypto.randomUUID())
      setGeneralError(null)
    }
  }

  const resetForm = () => {
    setMonthCount(onlyOption?.months ?? null)
    setAmountText(amountPrefill(onlyOption))
    setAmountTouched(false)
    setPaidDate(thaiNowInputs().date)
    setPaidTime('')
    setSlip(null)
    setNote('')
    setDropNotice(null)
    setSubmitAttempted(false)
    setServerErrors({})
    setGeneralError(null)
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  const chooseMonth = (months: number) => {
    edited('monthCount')
    setMonthCount(months)
    // เติมยอดรวม VAT ให้ จนกว่าผู้ใช้จะพิมพ์ยอดเอง (ยอดไม่ตรงราคายังส่งได้ ทีมงานตรวจเอง)
    if (!amountTouched) {
      setAmountText(amountPrefill(options.find((item) => item.months === months)))
    }
  }

  const chooseSlip = (file: File | undefined) => {
    edited('slip')
    setSlip(file ?? null)
  }

  const onDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    setDragActive(false)
    if (!canSubmit) return
    const { file, ignored } = pickDroppedFile(Array.from(event.dataTransfer.files))
    if (!file) return
    chooseSlip(file)
    if (fileInputRef.current) fileInputRef.current.value = ''
    setDropNotice(ignored > 0 ? `แนบได้ครั้งละ 1 ไฟล์ ระบบใช้ไฟล์ "${file.name}"` : null)
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (mutation.isPending || !canSubmit) return
    setSubmitAttempted(true)
    const errors = validatePaymentForm(formInput, { now: new Date(), maxPastDays, monthOptions: monthValues })
    if (hasErrors(errors)) {
      const first = PAYMENT_FIELD_ORDER.find((field) => errors[field])
      if (first) focusField(first)
      return
    }
    const amount = parseAmountInput(amountText)
    const paidAt = composePaidAt(paidDate, paidTime)
    if (!amount.ok || !paidAt || !slip || monthCount === null) return
    setGeneralError(null)
    mutation.mutate({ requestId, paidAt, amount: amount.normalized, monthCount, note, slip })
  }

  const unknownOutcome = Boolean(generalError && isUnknownOutcomeError(generalError.code))
  const vatRate = overview.vatRate
  const pendingExists = overview.cannotSubmitCode === 'pending_exists'
  const expired = overview.readOnly || (typeof overview.daysLeft === 'number' && overview.daysLeft < 0)
  // [BIZ] docs/17 §4: ต่อก่อนหมดนับจากวันหมดเดิม · หมดแล้วนับจากวันที่ทีมงานอนุมัติ
  const durationHint = `${typeof vatRate === 'number' ? 'ราคารวม VAT แล้ว · ' : ''}${
    expired ? 'หมดอายุแล้ว วันใช้งานใหม่นับจากวันที่ทีมงานอนุมัติ' : 'นับต่อจากวันหมดเดิม ไม่เสียวันที่เหลือ'
  }`

  const head = (
    <header className="package-section-head">
      <div>
        <p className="package-section-head__eyebrow">ต่ออายุแพ็กเกจ</p>
        <h2 id="package-renew-title">ต่ออายุใน 3 ขั้น</h2>
        <p>เลือกระยะเวลา โอนเงินเข้าบัญชีบริษัท แล้วแนบสลิป ทีมงานตรวจสลิปแล้วต่ออายุให้ ผลแจ้งในกระดิ่งแจ้งเตือนและอีเมลเจ้าของอู่</p>
      </div>
    </header>
  )

  // มีรายการรอตรวจ: ต่ออายุซ้ำไม่ได้จนกว่าทีมงานตรวจเสร็จ · ย่อเหลือเหตุผลจาก server (รายละเอียดอยู่ในการ์ดรอตรวจด้านบน)
  if (pendingExists) {
    return (
      <section className="package-renew" aria-labelledby="package-renew-title">
        {head}
        <BlockedNotice overview={overview} reason={blockedReason ?? ''} pending />
      </section>
    )
  }

  return (
    <section className="package-renew" aria-labelledby="package-renew-title">
      {head}

      <form
        className="package-renew__layout"
        onSubmit={submit}
        noValidate
        aria-labelledby="package-renew-title"
        aria-busy={mutation.isPending}
      >
        <ol className="package-steps">
          <RenewStep number={1} state={steps[0]} title="เลือกระยะเวลา" hint={durationHint}>
            {options.length ? (
              <fieldset className="package-durations">
                <legend className="sr-only">ระยะเวลาที่ต่ออายุ</legend>
                {options.map((option) => (
                  <DurationOption
                    key={option.months}
                    option={option}
                    vatRate={vatRate}
                    checked={option.months === monthCount}
                    onChoose={() => chooseMonth(option.months)}
                  />
                ))}
              </fieldset>
            ) : (
              <p className="package-muted">ยังไม่มีตัวเลือกระยะเวลา สอบถามทีมงานได้ทาง LINE {LINE_OA_ID}</p>
            )}
            {errorFor('monthCount') ? <small className="package-error" role="alert">{errorFor('monthCount')}</small> : null}
          </RenewStep>

          <RenewStep number={2} state={steps[1]} title="โอนเงินเข้าบัญชีบริษัท" hint="โอนผ่านแอปธนาคารใดก็ได้ แล้วเก็บสลิปไว้แนบในขั้นถัดไป">
            <BankCard overview={overview} selectedMonths={monthCount} selectedPrice={selectedPrice} />
          </RenewStep>

          <RenewStep number={3} state={steps[2]} title="แนบสลิปและวันเวลาที่โอน" hint="กรอกตามสลิปการโอน เวลาเป็นเวลาไทย">
            {canSubmit ? (
              <div className="package-slip-step">
                <div
                  className={`package-drop${dragActive ? ' package-drop--active' : ''}${slip ? ' package-drop--filled' : ''}${errorFor('slip') ? ' package-drop--invalid' : ''}`}
                  onDragEnter={(event) => {
                    event.preventDefault()
                    setDragActive(true)
                  }}
                  onDragOver={(event) => {
                    event.preventDefault()
                    event.dataTransfer.dropEffect = 'copy'
                    if (!dragActive) setDragActive(true)
                  }}
                  onDragLeave={(event) => {
                    // ออกจากกรอบจริง ไม่ใช่แค่เลื่อนผ่านลูกข้างใน
                    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragActive(false)
                  }}
                  onDrop={onDrop}
                >
                  <input
                    ref={fileInputRef}
                    id={SLIP_INPUT_ID}
                    className="package-slip-input"
                    type="file"
                    tabIndex={-1}
                    aria-hidden="true"
                    accept={SLIP_ACCEPT}
                    onChange={(event) => {
                      setDropNotice(null)
                      chooseSlip(event.target.files?.[0])
                    }}
                  />
                  {slip ? (
                    <div className="package-drop__picked">
                      {slipPreviewUrl ? (
                        <img className="package-drop__preview" src={slipPreviewUrl} alt="ตัวอย่างสลิปที่เลือก" />
                      ) : (
                        <span className="package-drop__preview package-drop__preview--file" aria-hidden="true">
                          <FileText /> PDF
                        </span>
                      )}
                      <span className="package-drop__meta">
                        <span className="package-drop__kind">
                          {slipPickError ? <TriangleAlert aria-hidden="true" /> : <CircleCheck aria-hidden="true" />}
                          {slipPickError ? 'ไฟล์นี้ใช้ไม่ได้' : 'แนบสลิปแล้ว'}
                        </span>
                        <strong title={slip.name}>{slip.name}</strong>
                        <small>{isImageSlipType(slipContentType(slip)) ? 'รูปภาพ' : slipContentType(slip) === 'application/pdf' ? 'ไฟล์ PDF' : 'ไม่รู้จักชนิดไฟล์'} · {formatFileSize(slip.size)}</small>
                        <span className="package-drop__actions">
                          <Button
                            id={SLIP_BUTTON_ID}
                            variant="outline"
                            size="sm"
                            onClick={() => fileInputRef.current?.click()}
                          >
                            <Upload aria-hidden="true" /> เปลี่ยนไฟล์
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => {
                              setDropNotice(null)
                              chooseSlip(undefined)
                              if (fileInputRef.current) fileInputRef.current.value = ''
                            }}
                          >
                            <X aria-hidden="true" /> เอาออก
                          </Button>
                        </span>
                      </span>
                    </div>
                  ) : (
                    <button
                      id={SLIP_BUTTON_ID}
                      type="button"
                      className="package-drop__empty"
                      aria-describedby="package-drop-help"
                      onClick={() => fileInputRef.current?.click()}
                    >
                      <span className="package-drop__icon" aria-hidden="true"><ImageUp /></span>
                      <strong>{dragActive ? 'ปล่อยไฟล์เพื่อแนบสลิป' : 'ลากสลิปมาวางที่นี่ หรือกดเพื่อเลือกไฟล์'}</strong>
                      <small id="package-drop-help">PNG, JPEG, WebP หรือ PDF ไม่เกิน 15 MB · 1 ไฟล์</small>
                    </button>
                  )}
                </div>
                {dropNotice ? <p className="package-hint package-hint--warn"><Info aria-hidden="true" /> {dropNotice}</p> : null}
                {errorFor('slip') ? <small className="package-error" role="alert">{errorFor('slip')}</small> : null}

                <div className="package-details">
                  <div className="package-field">
                    <label htmlFor="package-paid-date">วันที่โอน</label>
                    <Input
                      id="package-paid-date"
                      type="date"
                      min={addDaysToDateInput(now.date, -maxPastDays)}
                      max={now.date}
                      value={paidDate}
                      aria-invalid={Boolean(errorFor('paidAt'))}
                      aria-describedby="package-paid-help"
                      onChange={(event) => {
                        edited('paidAt')
                        setPaidDate(event.target.value)
                      }}
                    />
                  </div>
                  <div className="package-field">
                    <label htmlFor="package-paid-time">เวลาที่โอน</label>
                    <Input
                      id="package-paid-time"
                      type="time"
                      max={paidDate === now.date ? now.time : undefined}
                      value={paidTime}
                      aria-invalid={Boolean(errorFor('paidAt'))}
                      aria-describedby="package-paid-help"
                      onChange={(event) => {
                        edited('paidAt')
                        setPaidTime(event.target.value)
                      }}
                    />
                  </div>
                  <p id="package-paid-help" className="package-hint package-details__wide">
                    ย้อนหลังได้ไม่เกิน {maxPastDays} วัน · ปีเป็น ค.ศ. ตามปฏิทินของเบราว์เซอร์
                  </p>
                  {errorFor('paidAt') ? <small className="package-error package-details__wide" role="alert">{errorFor('paidAt')}</small> : null}
                  <div className="package-field package-field--amount package-details__wide">
                    <label htmlFor="package-amount">ยอดเงินที่โอน (บาท)</label>
                    <Input
                      id="package-amount"
                      className="money package-amount"
                      inputMode="decimal"
                      autoComplete="off"
                      placeholder="0.00"
                      value={amountText}
                      aria-invalid={Boolean(errorFor('amount'))}
                      aria-describedby="package-amount-help"
                      onChange={(event) => {
                        edited('amount')
                        setAmountTouched(true)
                        setAmountText(event.target.value)
                      }}
                      onBlur={() => {
                        if (parsedAmount.ok) setAmountText(formatMoney(parsedAmount.value))
                      }}
                    />
                    <div id="package-amount-help">
                      <AmountHint
                        comparison={compareAmount(selectedPrice?.total, parsedAmount.ok ? parsedAmount.value : null)}
                        months={monthCount}
                        vatIncluded={Boolean(selectedPrice && typeof selectedPrice.vat === 'number')}
                      />
                    </div>
                    {errorFor('amount') ? <small className="package-error" role="alert">{errorFor('amount')}</small> : null}
                  </div>
                  <div className="package-field package-details__wide">
                    <label htmlFor="package-note">หมายเหตุถึงทีมงาน (ไม่บังคับ)</label>
                    <Textarea
                      id="package-note"
                      rows={2}
                      maxLength={NOTE_MAX_LENGTH}
                      placeholder="เช่น โอนจากบัญชีบริษัท"
                      value={note}
                      aria-invalid={Boolean(errorFor('note'))}
                      onChange={(event) => {
                        edited('note')
                        setNote(event.target.value)
                      }}
                    />
                    <p className="package-hint package-hint--counter">{note.length}/{NOTE_MAX_LENGTH}</p>
                    {errorFor('note') ? <small className="package-error" role="alert">{errorFor('note')}</small> : null}
                  </div>
                </div>
              </div>
            ) : (
              <BlockedNotice overview={overview} reason={blockedReason ?? ''} pending={pendingExists} />
            )}
          </RenewStep>
        </ol>

        <aside className="package-summary" aria-labelledby="package-summary-title">
          <h3 id="package-summary-title">สรุปยอดชำระ</h3>
          <SummaryRows option={selectedOption} price={selectedPrice} vatRate={vatRate} />
          <div className="package-summary__total">
            <span>ยอดที่ต้องโอน</span>
            {selectedPrice ? (
              <strong><Money value={selectedPrice.total} /> <small>บาท</small></strong>
            ) : (
              <strong className="package-summary__placeholder">{selectedOption ? 'ยังไม่ตั้งราคา' : 'เลือกระยะเวลาก่อน'}</strong>
            )}
          </div>
          <div className="package-summary__until">
            <CalendarCheck aria-hidden="true" />
            <span>
              <small>ถ้าทีมงานอนุมัติวันนี้ ใช้งานได้ถึง</small>
              <strong>{selectedOption?.previewLastUsableDate ? formatDateOnly(selectedOption.previewLastUsableDate) : 'เลือกระยะเวลาในขั้นที่ 1'}</strong>
            </span>
          </div>

          <div className="package-summary__action">
            {generalError ? <SubmitErrorAlert error={generalError} unknownOutcome={unknownOutcome} /> : null}
            <Button type="submit" size="lg" className="package-summary__submit" disabled={!canSubmit || mutation.isPending}>
              <Send aria-hidden="true" />
              {mutation.isPending ? 'กำลังส่ง…' : unknownOutcome ? 'ส่งคำขอเดิมอีกครั้ง' : 'ส่งแจ้งชำระเงิน'}
            </Button>
            {mutation.isPending ? (
              <p className="package-summary__state" role="status">กำลังอัปโหลดสลิปและส่งแจ้งชำระ…</p>
            ) : !canSubmit ? (
              // [UI] ปุ่มที่ปิดต้องบอกเหตุผลเป็นข้อความ (เหตุผลจาก server ตรงตัว)
              <p className="package-summary__state package-summary__state--blocked">
                {pendingExists ? <Hourglass aria-hidden="true" /> : <Lock aria-hidden="true" />} {blockedReason}
              </p>
            ) : missing.length ? (
              <p className="package-summary__state">
                <Info aria-hidden="true" /> ยังขาด: {missing.join(' · ')}
              </p>
            ) : (
              <p className="package-summary__state package-summary__state--ready">
                <CircleCheck aria-hidden="true" /> ข้อมูลครบ พร้อมส่งแจ้งชำระ
              </p>
            )}
          </div>
          <p className="package-summary__assure">
            <ShieldCheck aria-hidden="true" /> ทีมงานตรวจสลิปแล้วต่ออายุให้ทันทีที่อนุมัติ ไม่ต้องเข้าสู่ระบบใหม่
          </p>
        </aside>
      </form>
    </section>
  )
}

// ---- ขั้นตอน ------------------------------------------------------------------------------------------

const STEP_STATE_TEXT: Record<RenewStepState, string> = {
  done: 'เสร็จแล้ว',
  current: 'ขั้นปัจจุบัน',
  todo: 'ยังไม่ถึง',
}

function RenewStep({
  number,
  state,
  title,
  hint,
  children,
}: {
  number: number
  state: RenewStepState
  title: string
  hint: string
  children: ReactNode
}) {
  return (
    <li className={`package-step package-step--${state}`}>
      <span className="package-step__marker" aria-hidden="true">
        {state === 'done' ? <Check /> : number}
      </span>
      <div className="package-step__card">
        <header className="package-step__head">
          <h3>
            <span className="package-step__number">ขั้นที่ {number}</span>
            {title}
            <span className="sr-only"> ({STEP_STATE_TEXT[state]})</span>
          </h3>
          <p>{hint}</p>
        </header>
        {children}
      </div>
    </li>
  )
}

/** การ์ดตัวเลือกระยะเวลา: radio จริง (ลูกศรเลื่อนตัวเลือกได้ตามมาตรฐานเบราว์เซอร์) ห่อด้วย label ทั้งการ์ด */
function DurationOption({
  option,
  vatRate,
  checked,
  onChoose,
}: {
  option: PackageMonthOption
  vatRate: number | undefined
  checked: boolean
  onChoose: () => void
}) {
  const price = priceBreakdown(option)
  const breakdown = vatBreakdownText(price, vatRate, formatMoney)
  return (
    <label className={`package-duration${checked ? ' package-duration--checked' : ''}`}>
      <input
        id={monthInputId(option.months)}
        className="package-duration__input"
        type="radio"
        name="package-month"
        value={option.months}
        checked={checked}
        onChange={onChoose}
      />
      <span className="package-duration__radio" aria-hidden="true">{checked ? <Check /> : null}</span>
      <span className="package-duration__main">
        <strong>{monthOptionLabel(option.months)}</strong>
        <small>
          <CalendarCheck aria-hidden="true" />
          {option.previewLastUsableDate ? `ใช้ได้ถึง ${formatDateOnly(option.previewLastUsableDate)}` : 'วันหมดอายุคำนวณตอนอนุมัติ'}
        </small>
      </span>
      <span className="package-duration__price">
        {price ? (
          <>
            <span className="package-duration__total"><Money value={price.total} /> <small>บาท</small></span>
            {breakdown ? <small className="package-duration__vat">{breakdown}</small> : null}
          </>
        ) : (
          <span className="package-duration__noprice">ยังไม่ตั้งราคา</span>
        )}
      </span>
    </label>
  )
}

function BankCard({
  overview,
  selectedMonths,
  selectedPrice,
}: {
  overview: PackageOverview
  selectedMonths: number | null
  selectedPrice: PriceBreakdown | null
}) {
  const account = overview.paymentAccount
  const hasAccount = Boolean(account && account.accountName && (account.accountNo || account.promptPayId))
  if (!hasAccount || !account) {
    return (
      <Alert variant="warning">
        <CircleAlert aria-hidden="true" />
        <div>
          <AlertTitle>ยังไม่ได้ตั้งบัญชีรับเงิน</AlertTitle>
          <AlertDescription>
            ติดต่อทีมงานทาง <a href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">LINE {LINE_OA_ID}</a> เพื่อสอบถามช่องทางชำระค่าแพ็กเกจ
          </AlertDescription>
        </div>
      </Alert>
    )
  }
  return (
    <div className="package-bank">
      <div className="package-bank__account">
        <span className="package-bank__chip">
          <Landmark aria-hidden="true" /> {account.bankName || 'บัญชีรับเงิน'}
        </span>
        {account.accountNo ? (
          <div className="package-bank__row">
            <span className="package-bank__field">
              <small>เลขที่บัญชี</small>
              <strong className="package-bank__number">{account.accountNo}</strong>
            </span>
            <CopyButton value={accountNoForCopy(account.accountNo)} label="เลขที่บัญชี" />
          </div>
        ) : null}
        {account.promptPayId ? (
          <div className="package-bank__row">
            <span className="package-bank__field">
              <small>พร้อมเพย์</small>
              <strong className="package-bank__number">{account.promptPayId}</strong>
            </span>
            <CopyButton value={accountNoForCopy(account.promptPayId)} label="เลขพร้อมเพย์" />
          </div>
        ) : null}
        <span className="package-bank__field">
          <small>ชื่อบัญชี</small>
          <strong className="package-bank__name">{account.accountName}</strong>
        </span>
      </div>
      <div className={`package-bank__amount${selectedPrice ? '' : ' package-bank__amount--empty'}`}>
        <span className="package-bank__field">
          <small>
            ยอดที่ต้องโอน{selectedPrice && typeof selectedPrice.vat === 'number' ? ' (รวม VAT)' : ''}
            {selectedMonths !== null ? ` · ${monthOptionLabel(selectedMonths)}` : ''}
          </small>
          {selectedPrice ? (
            <strong className="package-bank__total"><Money value={selectedPrice.total} /> <small>บาท</small></strong>
          ) : (
            <strong className="package-bank__pick">
              {selectedMonths === null ? 'เลือกระยะเวลาในขั้นที่ 1 ก่อน' : `ยังไม่ตั้งราคา สอบถามยอดที่ LINE ${LINE_OA_ID}`}
            </strong>
          )}
        </span>
        {selectedPrice ? <CopyButton value={amountForCopy(selectedPrice.total)} label="ยอดเงิน">คัดลอกยอด</CopyButton> : null}
      </div>
    </div>
  )
}

function SummaryRows({
  option,
  price,
  vatRate,
}: {
  option: PackageMonthOption | undefined
  price: PriceBreakdown | null
  vatRate: number | undefined
}) {
  const split = price && typeof price.subtotal === 'number' && typeof price.vat === 'number'
  return (
    <dl className="package-summary__rows">
      <div>
        <dt>ระยะเวลา</dt>
        <dd>{option ? monthOptionLabel(option.months) : <span className="package-summary__placeholder">ยังไม่เลือก</span>}</dd>
      </div>
      {split ? (
        <>
          <div>
            <dt>ราคาก่อน VAT</dt>
            <dd><Money value={price.subtotal} /></dd>
          </div>
          <div>
            <dt>{vatLabel(vatRate)}</dt>
            <dd><Money value={price.vat} /></dd>
          </div>
        </>
      ) : typeof vatRate === 'number' && !option ? (
        <>
          <div>
            <dt>ราคาก่อน VAT</dt>
            <dd className="package-summary__placeholder">-</dd>
          </div>
          <div>
            <dt>{vatLabel(vatRate)}</dt>
            <dd className="package-summary__placeholder">-</dd>
          </div>
        </>
      ) : null}
    </dl>
  )
}

/** แจ้งชำระไม่ได้: เหตุผลจาก server ตรงตัว (docs/17 §6.1 ห้ามแต่งเอง) แทนช่องกรอกของขั้นที่ 3 */
function BlockedNotice({ overview, reason, pending }: { overview: PackageOverview; reason: string; pending: boolean }) {
  return (
    <div className={`package-blocked${pending ? ' package-blocked--pending' : ''}`}>
      <span className="package-blocked__icon" aria-hidden="true">{pending ? <Hourglass /> : <Lock />}</span>
      <div>
        <strong role="status">{reason}</strong>
        {pending ? (
          <p>
            ดูรายละเอียดรายการที่รอตรวจด้านบน{' '}
            <a href="#package-pending">ไปที่รายการรอตรวจ</a>
          </p>
        ) : overview.cannotSubmitCode === 'not_manager' ? (
          <p>ดูราคาและบัญชีรับเงินได้ตามขั้นที่ 1 และ 2 แล้วแจ้งผู้จัดการอู่ให้แนบสลิป</p>
        ) : (
          <p>
            สอบถามทีมงานได้ทาง{' '}
            <a href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">LINE {LINE_OA_ID} <ExternalLink aria-hidden="true" /></a>
          </p>
        )}
      </div>
    </div>
  )
}

function AmountHint({
  comparison,
  months,
  vatIncluded,
}: {
  comparison: ReturnType<typeof compareAmount>
  months: number | null
  vatIncluded: boolean
}) {
  const vatNote = vatIncluded ? ' (รวม VAT)' : ''
  if (months === null) {
    return <p className="package-hint">เลือกระยะเวลาในขั้นที่ 1 ระบบจะเติมยอดที่ต้องโอนให้</p>
  }
  switch (comparison.kind) {
    case 'no_price':
      return (
        <p className="package-hint">
          <Info aria-hidden="true" /> ยังไม่ได้ตั้งราคาของ {monthOptionLabel(months)} สอบถามยอดได้ที่ LINE {LINE_OA_ID}
        </p>
      )
    case 'no_amount':
      return <p className="package-hint">ยอดที่ต้องโอน{vatNote} <Money value={comparison.price} /> บาท</p>
    case 'match':
      return (
        <p className="package-hint package-hint--match">
          <CircleCheck aria-hidden="true" /> ตรงกับยอดที่ต้องโอน{vatNote} <Money value={comparison.price} /> บาท
        </p>
      )
    case 'mismatch':
      return (
        <p className="package-hint package-hint--warn">
          <TriangleAlert aria-hidden="true" /> ไม่ตรงกับยอดที่ต้องโอน <Money value={comparison.price} /> บาท
          (ต่างกัน <Money value={Math.abs(comparison.diff)} /> บาท) ส่งได้ ทีมงานจะตรวจยอดก่อนอนุมัติ
        </p>
      )
  }
}

function SubmitErrorAlert({ error, unknownOutcome }: { error: ApiError; unknownOutcome: boolean }) {
  const retry = isRateLimitedError(error) ? retryAfterText(error.retryAfterSeconds) : null
  const next = isStateChangedError(error.code)
    ? 'ระบบโหลดสถานะล่าสุดของอู่ให้แล้ว ดูรายละเอียดด้านบนของหน้านี้'
    : retry ?? `ลองส่งใหม่อีกครั้ง หรือติดต่อทีมงานทาง LINE ${LINE_OA_ID}`
  return (
    <Alert variant={unknownOutcome ? 'warning' : 'destructive'}>
      <CircleAlert aria-hidden="true" />
      <div>
        <AlertTitle>{unknownOutcome ? 'ยังไม่ทราบว่าส่งแจ้งชำระสำเร็จหรือไม่' : error.messageTh}</AlertTitle>
        <AlertDescription>
          {unknownOutcome
            ? `${error.messageTh} ระบบกำลังตรวจสถานะล่าสุดให้ ถ้ายังไม่เห็นรายการรอตรวจ กด "ส่งคำขอเดิมอีกครั้ง" ระบบจะไม่บันทึกซ้ำ`
            : next}
          {' '}รหัสติดตาม (traceId): {error.traceId || 'ไม่พบรหัสติดตาม'}
        </AlertDescription>
      </div>
    </Alert>
  )
}

/** object URL ของไฟล์ที่เลือก (พรีวิวสลิป) · คืน URL เมื่อเปลี่ยนไฟล์/ออกจากหน้า */
function useObjectUrl(file: File | null): string | null {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!file) {
      setUrl(null)
      return
    }
    const next = URL.createObjectURL(file)
    setUrl(next)
    return () => URL.revokeObjectURL(next)
  }, [file])
  return url
}
