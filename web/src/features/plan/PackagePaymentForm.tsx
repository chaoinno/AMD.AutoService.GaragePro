import { useMutation } from '@tanstack/react-query'
import { CircleAlert, CircleCheck, FileText, ImagePlus, Info, Send, TriangleAlert, Upload, X } from 'lucide-react'
import { type FormEvent, useEffect, useMemo, useRef, useState } from 'react'
import { toast } from 'sonner'
import { isApiError, isRateLimitedError } from '../../api/client'
import { submitPackagePayment, type PackageOverview } from '../../api/package'
import type { ApiError } from '../../api/types'
import { Money } from '../../components/Money'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Textarea } from '../../components/ui/textarea'
import { formatDateOnly, formatMoney } from '../../lib/format'
import {
  addDaysToDateInput,
  compareAmount,
  composePaidAt,
  DEFAULT_PAID_AT_MAX_PAST_DAYS,
  formatFileSize,
  formFieldForError,
  hasErrors,
  isImageSlipType,
  isStateChangedError,
  isUnknownOutcomeError,
  monthOptionLabel,
  NOTE_MAX_LENGTH,
  parseAmountInput,
  previewUsableText,
  priceAsAmountInput,
  retryAfterText,
  SLIP_ACCEPT,
  slipContentType,
  thaiNowInputs,
  validatePaymentForm,
  type PaymentFormErrors,
  type PaymentFormField,
} from './packageUx'
import { LINE_OA_ID } from './trialUx'

const FIELD_IDS: Record<PaymentFormField, string> = {
  monthCount: 'package-month-0',
  amount: 'package-amount',
  paidAt: 'package-paid-date',
  // ปุ่มเลือก/เปลี่ยนไฟล์ (input file ซ่อนอยู่ โฟกัสไปที่นั่นไม่มีประโยชน์)
  slip: 'package-slip-button',
  note: 'package-note',
}
const SLIP_INPUT_ID = 'package-slip'

const FIELD_ORDER: PaymentFormField[] = ['monthCount', 'amount', 'paidAt', 'slip', 'note']

/**
 * ฟอร์มแจ้งชำระเงิน (docs/17 §5.1 · §6.1): แสดงเฉพาะเมื่อ server ตอบ `canSubmit: true`
 *
 * requestId: สร้างครั้งเดียวต่อคำขอ · ส่งซ้ำหลังเน็ตหลุด (ไม่รู้ผล) ใช้ค่าเดิมเสมอแม้แก้ฟอร์ม เพราะ server อาจบันทึกไปแล้ว
 * (ส่งซ้ำได้แถวเดิม ไม่อัปโหลดซ้ำ ไม่ส่ง LINE ซ้ำ) · สร้างใหม่เฉพาะเมื่อแก้ฟอร์มหลัง server ตอบผลชัดเจนแล้ว
 */
export function PackagePaymentForm({
  overview,
  onChanged,
}: {
  overview: PackageOverview
  /** ให้หน้าโหลดภาพรวม/ประวัติใหม่ (ส่งสำเร็จ หรือสถานะฝั่ง server อาจเปลี่ยนแล้ว) */
  onChanged: () => void
}) {
  const options = useMemo(() => overview.monthOptions ?? [], [overview.monthOptions])
  const monthValues = useMemo(() => options.map((option) => option.months), [options])
  const maxPastDays = overview.paidAtMaxPastDays && overview.paidAtMaxPastDays > 0
    ? overview.paidAtMaxPastDays
    : DEFAULT_PAID_AT_MAX_PAST_DAYS

  // มีตัวเลือกเดียว = เลือกให้เลย · หลายตัวเลือกต้องให้ผู้ใช้เลือกเอง (เรื่องเงิน ไม่เดาแทน)
  const onlyOption = options.length === 1 ? options[0] : undefined
  const [monthCount, setMonthCount] = useState<number | null>(() => onlyOption?.months ?? null)
  const [amountText, setAmountText] = useState(() => priceAsAmountInput(onlyOption?.price))
  const [amountTouched, setAmountTouched] = useState(false)
  const [paidDate, setPaidDate] = useState(() => thaiNowInputs().date)
  const [paidTime, setPaidTime] = useState('')
  const [slip, setSlip] = useState<File | null>(null)
  const [note, setNote] = useState('')
  const [submitAttempted, setSubmitAttempted] = useState(false)
  const [serverErrors, setServerErrors] = useState<PaymentFormErrors>({})
  const [generalError, setGeneralError] = useState<ApiError | null>(null)
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())
  // true = server ตอบผลชัดเจนแล้ว (สำเร็จหรือปฏิเสธ) → แก้ฟอร์มครั้งถัดไปเป็นคำขอใหม่
  const answeredRef = useRef(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const slipPreviewUrl = useObjectUrl(slip && isImageSlipType(slipContentType(slip)) ? slip : null)
  const now = thaiNowInputs()
  const selectedOption = options.find((option) => option.months === monthCount)

  const formInput = { monthCount, amountText, paidDate, paidTime, slip, note }
  const clientErrors: PaymentFormErrors = submitAttempted
    ? validatePaymentForm(formInput, { now: new Date(), maxPastDays, monthOptions: monthValues })
    : {}
  const errorFor = (field: PaymentFormField) => clientErrors[field] ?? serverErrors[field]

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
        document.getElementById(FIELD_IDS[field])?.focus()
        return
      }
      setGeneralError(error)
      // สถานะของอู่ฝั่ง server เปลี่ยนไปแล้ว (มีรายการรอตรวจ · ไม่ใช่ผู้จัดการ · ยังไม่ตั้งบัญชี · ต่ออายุไม่ได้)
      // → โหลดภาพรวมใหม่ ฟอร์มจะถูกแทนด้วยเหตุผลจาก server · แจ้งด้วย toast เพราะฟอร์มนี้จะหายไป
      if (isStateChangedError(error.code)) {
        toast.error(error.messageTh)
        onChanged()
      }
    },
  })

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
    setAmountText(priceAsAmountInput(onlyOption?.price))
    setAmountTouched(false)
    setPaidDate(thaiNowInputs().date)
    setPaidTime('')
    setSlip(null)
    setNote('')
    setSubmitAttempted(false)
    setServerErrors({})
    setGeneralError(null)
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  const chooseMonth = (months: number) => {
    edited('monthCount')
    setMonthCount(months)
    // เติมยอดตามราคามาตรฐานให้ จนกว่าผู้ใช้จะพิมพ์ยอดเอง (ยอดไม่ตรงราคายังส่งได้ ทีมงานตรวจเอง)
    if (!amountTouched) {
      const option = options.find((item) => item.months === months)
      setAmountText(priceAsAmountInput(option?.price))
    }
  }

  const chooseSlip = (file: File | undefined) => {
    edited('slip')
    setSlip(file ?? null)
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (mutation.isPending) return
    setSubmitAttempted(true)
    const errors = validatePaymentForm(formInput, { now: new Date(), maxPastDays, monthOptions: monthValues })
    if (hasErrors(errors)) {
      const first = FIELD_ORDER.find((field) => errors[field])
      if (first) document.getElementById(FIELD_IDS[first])?.focus()
      return
    }
    const amount = parseAmountInput(amountText)
    const paidAt = composePaidAt(paidDate, paidTime)
    if (!amount.ok || !paidAt || !slip || monthCount === null) return
    setGeneralError(null)
    mutation.mutate({ requestId, paidAt, amount: amount.normalized, monthCount, note, slip })
  }

  const parsedAmount = parseAmountInput(amountText)
  const comparison = compareAmount(selectedOption?.price, parsedAmount.ok ? parsedAmount.value : null)
  const preview = previewUsableText(selectedOption, formatDateOnly)
  const unknownOutcome = generalError && isUnknownOutcomeError(generalError.code)

  return (
    <form className="package-form" onSubmit={submit} noValidate aria-describedby="package-form-help">
      <p id="package-form-help" className="package-form__help">
        กรอกตามสลิปการโอน แล้วแนบสลิป 1 ไฟล์ ทีมงานจะตรวจสลิปแล้วต่ออายุให้ ผลการตรวจแจ้งในกระดิ่งแจ้งเตือนและอีเมลเจ้าของอู่
      </p>

      <fieldset className="package-field">
        <legend>จำนวนเดือนที่ชำระ</legend>
        <div className="package-months" role="radiogroup" aria-label="จำนวนเดือนที่ชำระ">
          {options.map((option, index) => {
            const checked = option.months === monthCount
            return (
              <button
                key={option.months}
                id={index === 0 ? FIELD_IDS.monthCount : undefined}
                type="button"
                role="radio"
                aria-checked={checked}
                aria-label={`${monthOptionLabel(option.months)} ${typeof option.price === 'number' ? `ราคา ${formatMoney(option.price)} บาท` : 'ยังไม่ตั้งราคา'}`}
                className={`package-month${checked ? ' package-month--active' : ''}`}
                onClick={() => chooseMonth(option.months)}
              >
                <strong>{monthOptionLabel(option.months)}</strong>
                {typeof option.price === 'number'
                  ? <Money value={option.price} suffix=" บาท" />
                  : <small>ยังไม่ตั้งราคา</small>}
              </button>
            )
          })}
        </div>
        {preview ? <p className="package-field__hint"><Info aria-hidden="true" /> {preview}</p> : null}
        {errorFor('monthCount') ? <small className="field-error" role="alert">{errorFor('monthCount')}</small> : null}
      </fieldset>

      <div className="package-field">
        <label htmlFor={FIELD_IDS.amount}>ยอดเงินที่โอน (บาท)</label>
        <Input
          id={FIELD_IDS.amount}
          className="money package-amount"
          inputMode="decimal"
          autoComplete="off"
          placeholder="0.00"
          value={amountText}
          aria-invalid={Boolean(errorFor('amount'))}
          onChange={(event) => {
            edited('amount')
            setAmountTouched(true)
            setAmountText(event.target.value)
          }}
          onBlur={() => {
            const parsed = parseAmountInput(amountText)
            if (parsed.ok) setAmountText(formatMoney(parsed.value))
          }}
        />
        <AmountHint comparison={comparison} months={monthCount} />
        {errorFor('amount') ? <small className="field-error" role="alert">{errorFor('amount')}</small> : null}
      </div>

      <div className="package-field">
        <span className="package-field__label" id="package-paid-at-label">วันที่และเวลาที่โอน (เวลาไทยตามสลิป)</span>
        <div className="package-datetime" role="group" aria-labelledby="package-paid-at-label">
          <Input
            id={FIELD_IDS.paidAt}
            type="date"
            aria-label="วันที่โอน"
            min={addDaysToDateInput(now.date, -maxPastDays)}
            max={now.date}
            value={paidDate}
            aria-invalid={Boolean(errorFor('paidAt'))}
            onChange={(event) => {
              edited('paidAt')
              setPaidDate(event.target.value)
            }}
          />
          <Input
            id="package-paid-time"
            type="time"
            aria-label="เวลาที่โอน"
            max={paidDate === now.date ? now.time : undefined}
            value={paidTime}
            aria-invalid={Boolean(errorFor('paidAt'))}
            onChange={(event) => {
              edited('paidAt')
              setPaidTime(event.target.value)
            }}
          />
        </div>
        <p className="package-field__hint">ย้อนหลังได้ไม่เกิน {maxPastDays} วัน · ปีเป็น ค.ศ. ตามปฏิทินของเบราว์เซอร์</p>
        {errorFor('paidAt') ? <small className="field-error" role="alert">{errorFor('paidAt')}</small> : null}
      </div>

      <div className="package-field">
        <label htmlFor={SLIP_INPUT_ID}>สลิปการโอน</label>
        <input
          ref={fileInputRef}
          id={SLIP_INPUT_ID}
          className="package-slip-input"
          type="file"
          tabIndex={-1}
          accept={SLIP_ACCEPT}
          aria-invalid={Boolean(errorFor('slip'))}
          onChange={(event) => {
            chooseSlip(event.target.files?.[0])
          }}
        />
        {slip ? (
          <div className="package-slip-picked">
            {slipPreviewUrl
              ? <img src={slipPreviewUrl} alt="ตัวอย่างสลิปที่เลือก" />
              : <span className="package-slip-picked__icon" aria-hidden="true"><FileText /></span>}
            <span className="package-slip-picked__meta">
              <strong>{slip.name}</strong>
              <small>{formatFileSize(slip.size)}</small>
            </span>
            <span className="package-slip-picked__actions">
              <Button id={FIELD_IDS.slip} variant="outline" size="sm" onClick={() => fileInputRef.current?.click()}>
                <Upload aria-hidden="true" /> เปลี่ยนไฟล์
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  chooseSlip(undefined)
                  if (fileInputRef.current) fileInputRef.current.value = ''
                }}
              >
                <X aria-hidden="true" /> เอาออก
              </Button>
            </span>
          </div>
        ) : (
          <button
            id={FIELD_IDS.slip}
            type="button"
            className="package-slip-drop"
            aria-label="เลือกไฟล์สลิป PNG, JPEG, WebP หรือ PDF ไม่เกิน 15 MB"
            onClick={() => fileInputRef.current?.click()}
          >
            <ImagePlus aria-hidden="true" />
            <strong>เลือกไฟล์สลิป</strong>
            <small>PNG, JPEG, WebP หรือ PDF ไม่เกิน 15 MB</small>
          </button>
        )}
        {errorFor('slip') ? <small className="field-error" role="alert">{errorFor('slip')}</small> : null}
      </div>

      <div className="package-field">
        <label htmlFor={FIELD_IDS.note}>หมายเหตุถึงทีมงาน (ไม่บังคับ)</label>
        <Textarea
          id={FIELD_IDS.note}
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
        <p className="package-field__hint package-field__counter">{note.length}/{NOTE_MAX_LENGTH}</p>
        {errorFor('note') ? <small className="field-error" role="alert">{errorFor('note')}</small> : null}
      </div>

      {generalError ? <SubmitErrorAlert error={generalError} unknownOutcome={Boolean(unknownOutcome)} /> : null}

      <div className="package-form__footer">
        {mutation.isPending ? (
          <span className="package-form__status" role="status">กำลังอัปโหลดสลิปและส่งแจ้งชำระ…</span>
        ) : null}
        <Button type="submit" disabled={mutation.isPending}>
          <Send aria-hidden="true" /> {mutation.isPending ? 'กำลังส่ง…' : unknownOutcome ? 'ส่งคำขอเดิมอีกครั้ง' : 'ส่งแจ้งชำระเงิน'}
        </Button>
      </div>
    </form>
  )
}

function AmountHint({ comparison, months }: { comparison: ReturnType<typeof compareAmount>; months: number | null }) {
  if (months === null) {
    return <p className="package-field__hint">เลือกจำนวนเดือนก่อน ระบบจะแสดงราคามาตรฐานให้เทียบ</p>
  }
  switch (comparison.kind) {
    case 'no_price':
      return (
        <p className="package-field__hint">
          <Info aria-hidden="true" /> ยังไม่ได้ตั้งราคามาตรฐานของ {monthOptionLabel(months)} สอบถามยอดได้ที่ LINE {LINE_OA_ID}
        </p>
      )
    case 'no_amount':
      return <p className="package-field__hint">ราคามาตรฐาน <Money value={comparison.price} /> บาท</p>
    case 'match':
      return (
        <p className="package-field__hint package-field__hint--match">
          <CircleCheck aria-hidden="true" /> ตรงกับราคามาตรฐาน <Money value={comparison.price} /> บาท
        </p>
      )
    case 'mismatch':
      return (
        <p className="package-field__hint package-field__hint--mismatch">
          <TriangleAlert aria-hidden="true" /> ไม่ตรงกับราคามาตรฐาน <Money value={comparison.price} /> บาท
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
