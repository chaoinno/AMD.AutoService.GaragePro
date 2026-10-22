import assert from 'node:assert/strict'
import test from 'node:test'
import {
  accountNoForCopy,
  addDaysToDateInput,
  amountForCopy,
  cannotSubmitReason,
  compareAmount,
  composePaidAt,
  currentPeriodDays,
  formatFileSize,
  formFieldForError,
  hasErrors,
  isStateChangedError,
  isUnknownOutcomeError,
  missingFieldLabels,
  monthOptionLabel,
  packageDaysText,
  packagePlanLabel,
  packageStatusLabel,
  packageUrgency,
  parseAmountInput,
  paymentStatusDetail,
  paymentStatusMeta,
  pickDroppedFile,
  previewUsableText,
  priceAsAmountInput,
  priceBreakdown,
  rejectReasonText,
  remainingFraction,
  renewStepStates,
  retryAfterText,
  slipContentType,
  slipFileError,
  thaiNowInputs,
  validatePaymentForm,
  vatBreakdownText,
  vatLabel,
  vatPercentText,
} from '../src/features/plan/packageUx.ts'

// 2026-10-21 14:30 เวลาไทย
const now = new Date('2026-10-21T07:30:00Z')
const fmt = (value) => `[${value}]`
const slip = (overrides = {}) => ({ name: 'slip.jpg', type: 'image/jpeg', size: 180_000, ...overrides })
const validInput = (overrides = {}) => ({
  monthCount: 3,
  amountText: '1,500.00',
  paidDate: '2026-10-21',
  paidTime: '14:20',
  slip: slip(),
  note: '',
  ...overrides,
})
const context = { now, maxPastDays: 60, monthOptions: [1, 2, 3, 6, 12] }

test('thai date/time inputs ignore the machine time zone and paidAt carries the +07:00 offset', () => {
  assert.deepEqual(thaiNowInputs(now), { date: '2026-10-21', time: '14:30' })
  // 23:30 UTC = วันถัดไปตามเวลาไทย
  assert.deepEqual(thaiNowInputs(new Date('2026-10-21T17:30:00Z')), { date: '2026-10-22', time: '00:30' })

  assert.equal(composePaidAt('2026-10-21', '14:30'), '2026-10-21T14:30:00+07:00')
  assert.equal(composePaidAt('2026-10-21', '09:05:07'), '2026-10-21T09:05:07+07:00')
  assert.equal(Date.parse(composePaidAt('2026-10-21', '14:30')), Date.parse('2026-10-21T07:30:00Z'))
  assert.equal(composePaidAt('2026-02-30', '10:00'), null, 'a date that does not exist')
  assert.equal(composePaidAt('2026-10-21', '24:00'), null)
  assert.equal(composePaidAt('', '10:00'), null)
  assert.equal(composePaidAt('2026-10-21', ''), null)

  assert.equal(addDaysToDateInput('2026-10-21', -60), '2026-08-22')
  assert.equal(addDaysToDateInput('2024-03-01', -1), '2024-02-29')
})

test('amount accepts commas and up to 2 decimals, rejects zero/negative/too large like the server', () => {
  assert.deepEqual(parseAmountInput('1,500'), { ok: true, value: 1500, normalized: '1500.00' })
  assert.deepEqual(parseAmountInput(' 590.5 '), { ok: true, value: 590.5, normalized: '590.50' })
  assert.deepEqual(parseAmountInput('999,999.99'), { ok: true, value: 999999.99, normalized: '999999.99' })
  assert.deepEqual(parseAmountInput(''), { ok: false, error: 'required' })
  assert.deepEqual(parseAmountInput('12.345'), { ok: false, error: 'format' })
  assert.deepEqual(parseAmountInput('-5'), { ok: false, error: 'format' })
  assert.deepEqual(parseAmountInput('abc'), { ok: false, error: 'format' })
  assert.deepEqual(parseAmountInput('0.00'), { ok: false, error: 'not_positive' })
  assert.deepEqual(parseAmountInput('1000000'), { ok: false, error: 'too_large' })
})

test('expected amount comparison works in satang and treats a missing price as "ask the team"', () => {
  assert.deepEqual(compareAmount(undefined, 1500), { kind: 'no_price' })
  assert.deepEqual(compareAmount(1770, null), { kind: 'no_amount', price: 1770 })
  assert.deepEqual(compareAmount(590.3, 590.1 + 0.2), { kind: 'match', price: 590.3 })
  assert.deepEqual(compareAmount(1770, 1500), { kind: 'mismatch', price: 1770, diff: -270 })
  assert.equal(priceAsAmountInput(590), '590.00')
  assert.equal(priceAsAmountInput(undefined), '')
})

test('slip must be one png/jpeg/webp/pdf file up to 15 MB, falling back to the extension when type is empty', () => {
  assert.equal(slipFileError(slip()), null)
  assert.equal(slipFileError(slip({ name: 'a.pdf', type: 'application/pdf' })), null)
  assert.equal(slipContentType(slip({ name: 'SLIP.WEBP', type: '' })), 'image/webp')
  assert.equal(slipContentType(slip({ name: 'slip.heic', type: 'image/heic' })), null)
  assert.equal(slipFileError(null), 'แนบสลิปการโอน 1 ไฟล์')
  assert.equal(slipFileError(slip({ name: 'a.gif', type: 'image/gif' })), 'รองรับเฉพาะไฟล์ PNG, JPEG, WebP หรือ PDF')
  assert.match(slipFileError(slip({ size: 0 })), /ว่างเปล่า/)
  assert.equal(slipFileError(slip({ size: 15 * 1024 * 1024 })), null, 'exactly 15 MB is allowed')
  assert.match(slipFileError(slip({ size: 15 * 1024 * 1024 + 1 })), /เกิน 15 MB/)
  assert.equal(formatFileSize(183422), '179 KB')
  assert.equal(formatFileSize(2.5 * 1024 * 1024), '2.5 MB')
})

test('form validation returns Thai messages per field and passes a valid form', () => {
  assert.deepEqual(validatePaymentForm(validInput(), context), {})
  assert.equal(hasErrors({}), false)

  const empty = validatePaymentForm(
    { monthCount: null, amountText: '', paidDate: '', paidTime: '', slip: null, note: '' },
    context,
  )
  assert.deepEqual(Object.keys(empty).sort(), ['amount', 'monthCount', 'paidAt', 'slip'])
  assert.equal(hasErrors(empty), true)
  assert.equal(empty.monthCount, 'เลือกจำนวนเดือนที่ชำระ')
  assert.equal(empty.paidAt, 'กรอกวันที่และเวลาที่โอน')

  assert.equal(validatePaymentForm(validInput({ monthCount: 4 }), context).monthCount, 'เลือกจำนวนเดือนที่ชำระ')
  assert.match(validatePaymentForm(validInput({ note: 'x'.repeat(501) }), context).note, /500/)
})

test('paidAt: not in the future beyond 5 minutes, not older than paidAtMaxPastDays, Buddhist years rejected', () => {
  // ตอนนี้ 14:30 · 14:34 ยังอยู่ใน tolerance · 14:36 เป็นอนาคต
  assert.equal(validatePaymentForm(validInput({ paidTime: '14:34' }), context).paidAt, undefined)
  assert.equal(validatePaymentForm(validInput({ paidTime: '14:36' }), context).paidAt, 'วันเวลาที่โอนต้องไม่เป็นเวลาในอนาคต')
  assert.equal(validatePaymentForm(validInput({ paidDate: '2026-10-22', paidTime: '00:01' }), context).paidAt, 'วันเวลาที่โอนต้องไม่เป็นเวลาในอนาคต')

  assert.equal(validatePaymentForm(validInput({ paidDate: '2026-08-22', paidTime: '08:00' }), context).paidAt, undefined)
  assert.equal(validatePaymentForm(validInput({ paidDate: '2026-08-21', paidTime: '23:59' }), context).paidAt, 'แจ้งชำระย้อนหลังได้ไม่เกิน 60 วัน')
  assert.equal(
    validatePaymentForm(validInput({ paidDate: '2026-10-10' }), { ...context, maxPastDays: 7 }).paidAt,
    'แจ้งชำระย้อนหลังได้ไม่เกิน 7 วัน',
  )
  // server ไม่ส่งค่า → 60 วัน
  assert.equal(
    validatePaymentForm(validInput({ paidDate: '2026-08-01' }), { ...context, maxPastDays: undefined }).paidAt,
    'แจ้งชำระย้อนหลังได้ไม่เกิน 60 วัน',
  )
  assert.match(validatePaymentForm(validInput({ paidDate: '2569-10-21' }), context).paidAt, /ค\.ศ\./)
})

test('only network/invalid-response errors keep the requestId; server field errors map back to form fields', () => {
  assert.equal(isUnknownOutcomeError('NETWORK_ERROR'), true)
  assert.equal(isUnknownOutcomeError('INVALID_RESPONSE'), true)
  assert.equal(isUnknownOutcomeError('PACKAGE_PAYMENT_VALIDATION'), false)
  assert.equal(isUnknownOutcomeError('PACKAGE_PAYMENT_PENDING_EXISTS'), false)
  assert.equal(isUnknownOutcomeError(undefined), false)

  assert.equal(formFieldForError({ code: 'PACKAGE_PAYMENT_VALIDATION', field: 'paidAt', status: 422 }), 'paidAt')
  assert.equal(formFieldForError({ code: 'PACKAGE_PAYMENT_VALIDATION', field: 'Amount', status: 422 }), 'amount')
  assert.equal(formFieldForError({ code: 'PACKAGE_PAYMENT_VALIDATION', field: 'monthCount' }), 'monthCount')
  assert.equal(formFieldForError({ code: 'PACKAGE_PAYMENT_VALIDATION', field: 'requestId' }), null)
  assert.equal(formFieldForError({ code: 'ATTACHMENT_TYPE_INVALID', status: 422 }), 'slip')
  assert.equal(formFieldForError({ code: 'ATTACHMENT_TOO_LARGE', status: 413 }), 'slip')
  assert.equal(formFieldForError({ code: 'HTTP_413', status: 413 }), 'slip', 'nginx 413 without an envelope')
  assert.equal(formFieldForError({ code: 'PACKAGE_PAYMENT_PENDING_EXISTS', status: 409 }), null)

  assert.equal(isStateChangedError('PACKAGE_PAYMENT_PENDING_EXISTS'), true)
  assert.equal(isStateChangedError('PACKAGE_NOT_RENEWABLE'), true)
  assert.equal(isStateChangedError('PACKAGE_PAYMENT_UNAVAILABLE'), true)
  assert.equal(isStateChangedError('PACKAGE_PAYMENT_FORBIDDEN'), true)
  assert.equal(isStateChangedError('PACKAGE_SLIP_UPLOAD_FAILED'), false)
  assert.equal(isStateChangedError(undefined), false)

  assert.equal(retryAfterText(undefined), null)
  assert.equal(retryAfterText(45), 'ลองใหม่ได้ในอีก 45 วินาที')
  assert.equal(retryAfterText(600), 'ลองใหม่ได้ในอีกประมาณ 10 นาที')
})

test('payment status meta: label for each status, unknown values do not crash, detail lines per status', () => {
  assert.deepEqual(paymentStatusMeta('pending'), { status: 'pending', label: 'รอตรวจสลิป' })
  assert.deepEqual(paymentStatusMeta('approved'), { status: 'approved', label: 'อนุมัติแล้ว' })
  assert.deepEqual(paymentStatusMeta('rejected'), { status: 'rejected', label: 'ปฏิเสธ' })
  assert.deepEqual(paymentStatusMeta('cancelled'), { status: 'cancelled', label: 'ยกเลิกแล้ว' })
  assert.deepEqual(paymentStatusMeta('Approved'), { status: 'approved', label: 'อนุมัติแล้ว' })
  assert.deepEqual(paymentStatusMeta('weird'), { status: 'unknown', label: 'ไม่ทราบสถานะ' })
  assert.deepEqual(paymentStatusMeta(undefined), { status: 'unknown', label: 'ไม่ทราบสถานะ' })

  assert.equal(paymentStatusDetail({ status: 'approved', periodLastUsableDate: '2027-02-13' }, fmt), 'ใช้งานได้ถึง [2027-02-13]')
  assert.equal(paymentStatusDetail({ status: 'approved' }, fmt), null, 'approved without the period field (dropped null)')
  assert.equal(paymentStatusDetail({ status: 'rejected', rejectReason: 'amount_mismatch' }, fmt), 'เหตุผล: ยอดเงินไม่ตรงกับแพ็กเกจ')
  assert.equal(
    paymentStatusDetail({ status: 'rejected', rejectReason: 'other', rejectReasonTh: 'ข้อความจาก server' }, fmt),
    'เหตุผล: ข้อความจาก server',
  )
  assert.equal(paymentStatusDetail({ status: 'pending' }, fmt), 'ทีมงานกำลังตรวจสลิป')
  assert.equal(paymentStatusDetail({ status: 'cancelled' }, fmt), null)
  assert.equal(rejectReasonText({ rejectReason: 'new_reason_code' }), 'ไม่ระบุเหตุผล')
  assert.equal(rejectReasonText({}), 'ไม่ระบุเหตุผล')
})

test('current package: plan labels, days text for positive/zero/negative, urgency by plan threshold', () => {
  assert.equal(packagePlanLabel('trial'), 'ทดลองใช้')
  assert.equal(packagePlanLabel('paid'), 'แพ็กเกจที่ชำระเงินแล้ว')
  assert.equal(packagePlanLabel('converted'), 'ย้ายไประบบ GaragePro แล้ว')
  assert.equal(packagePlanLabel('nope'), 'ไม่ทราบแพ็กเกจ')

  assert.equal(packageDaysText(23), 'เหลือ 23 วัน')
  assert.equal(packageDaysText(0), 'วันนี้เป็นวันสุดท้าย')
  assert.equal(packageDaysText(-3), 'หมดอายุแล้ว 3 วัน')
  assert.equal(packageDaysText(undefined), null)

  assert.equal(packageUrgency({ plan: 'paid', daysLeft: 30, readOnly: false }, 14), 'ok')
  assert.equal(packageUrgency({ plan: 'paid', daysLeft: 14, readOnly: false }, 14), 'warn')
  assert.equal(packageUrgency({ plan: 'trial', daysLeft: 14, readOnly: false }, 7), 'ok')
  assert.equal(packageUrgency({ plan: 'trial', daysLeft: -1, readOnly: false }, 7), 'expired')
  assert.equal(packageUrgency({ plan: 'paid', daysLeft: 20, readOnly: true }, 14), 'expired')
  assert.equal(packageUrgency({ plan: 'paid', readOnly: false }, 14), 'none')
  assert.equal(packageUrgency({ plan: 'suspended', readOnly: false }, 7), 'expired')
})

test('cannot-submit reason prefers the server text, falls back per code, and is null when allowed', () => {
  assert.equal(cannotSubmitReason({ canSubmit: true }), null)
  assert.equal(
    cannotSubmitReason({ canSubmit: false, cannotSubmitCode: 'pending_exists', cannotSubmitReasonTh: 'ข้อความ server' }),
    'ข้อความ server',
  )
  assert.equal(cannotSubmitReason({ canSubmit: false, cannotSubmitCode: 'not_manager' }), 'เฉพาะผู้จัดการอู่แจ้งชำระเงินได้')
  assert.match(cannotSubmitReason({ canSubmit: false, cannotSubmitCode: 'unavailable' }), /LINE/)
  assert.equal(cannotSubmitReason({ canSubmit: false }), 'ยังแจ้งชำระเงินไม่ได้ในตอนนี้')
})

test('month labels and the "if approved today" preview use the server date-only string', () => {
  assert.equal(monthOptionLabel(1), '1 เดือน')
  assert.equal(monthOptionLabel(12), '12 เดือน (1 ปี)')
  assert.equal(previewUsableText({ previewLastUsableDate: '2026-12-13' }, fmt), 'ถ้าทีมงานอนุมัติวันนี้ ใช้งานได้ถึง [2026-12-13]')
  assert.equal(previewUsableText({}, fmt), null)
  assert.equal(previewUsableText(undefined, fmt), null)
})

// ราคาจริง (docs/17 §5.5): 3,999 ต่อเดือนก่อน VAT + VAT 7% (server ปัดครึ่งขึ้นต่อรายการ)
const realOptions = [
  { months: 1, subtotal: 3999, vat: 279.93, price: 4278.93 },
  { months: 2, subtotal: 7998, vat: 559.86, price: 8557.86 },
  { months: 3, subtotal: 11997, vat: 839.79, price: 12836.79 },
  { months: 6, subtotal: 23994, vat: 1679.58, price: 25673.58 },
  { months: 12, subtotal: 47988, vat: 3359.16, price: 51347.16 },
]
const money = (value) => value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

test('price breakdown: total incl. VAT is what to transfer, VAT split only when the server sends both parts', () => {
  for (const option of realOptions) {
    const breakdown = priceBreakdown(option)
    assert.deepEqual(breakdown, { total: option.price, subtotal: option.subtotal, vat: option.vat })
    // ยอดที่เติมให้ในช่องยอดเงิน = ยอดรวม VAT และเทียบแล้วตรง
    assert.equal(priceAsAmountInput(breakdown.total), option.price.toFixed(2))
    assert.deepEqual(compareAmount(breakdown.total, option.price), { kind: 'match', price: option.price })
  }
  assert.equal(
    vatBreakdownText(priceBreakdown(realOptions[0]), 0.07, money),
    'ก่อน VAT 3,999.00 + VAT 7% 279.93',
  )
  assert.equal(vatBreakdownText(priceBreakdown(realOptions[4]), 0.07, money), 'ก่อน VAT 47,988.00 + VAT 7% 3,359.16')

  // server ไม่แยก VAT (VatRate ว่าง) → มีแต่ยอดรวม ไม่มีบรรทัดแยก
  assert.deepEqual(priceBreakdown({ months: 1, price: 3999 }), { total: 3999 })
  assert.equal(vatBreakdownText(priceBreakdown({ months: 1, price: 3999 }), undefined, money), null)
  // ส่งมาไม่ครบคู่ = ไม่แยก
  assert.deepEqual(priceBreakdown({ months: 1, price: 4278.93, subtotal: 3999 }), { total: 4278.93 })
  // ยังไม่ตั้งราคา
  assert.equal(priceBreakdown({ months: 1 }), null)
  assert.equal(priceBreakdown(undefined), null)
  assert.equal(priceBreakdown({ months: 1, price: 0 }), null)
  // ไม่มี price แต่มีสองส่วน (นอกสัญญา) รวมเป็นสตางค์
  assert.deepEqual(priceBreakdown({ months: 1, subtotal: 590.1, vat: 0.2 }), { total: 590.3, subtotal: 590.1, vat: 0.2 })
})

test('VAT labels: rate is a fraction, not a percentage', () => {
  assert.equal(vatPercentText(0.07), '7%')
  assert.equal(vatPercentText(0.075), '7.5%')
  assert.equal(vatPercentText(0), '0%')
  assert.equal(vatPercentText(undefined), null)
  assert.equal(vatPercentText(-1), null)
  assert.equal(vatLabel(0.07), 'VAT 7%')
  assert.equal(vatLabel(undefined), 'VAT')
})

test('copy helpers give bank-app friendly values', () => {
  assert.equal(accountNoForCopy('068-8-63750-3'), '0688637503')
  assert.equal(accountNoForCopy(' 0812345678 '), '0812345678')
  assert.equal(accountNoForCopy('ABC'), 'ABC')
  assert.equal(amountForCopy(51347.16), '51347.16')
  assert.equal(amountForCopy(4278.9), '4278.90')
})

test('time-left meter: trial uses 30 days, paid uses the approved period that ends at the current end, otherwise unknown', () => {
  assert.equal(currentPeriodDays({ plan: 'trial', endsAt: '2026-11-13T03:00:00Z' }, []), 30)
  const approved = {
    status: 'approved',
    periodStartsAt: '2026-11-13T17:00:00Z',
    periodEndsAt: '2027-02-13T17:00:00Z',
  }
  assert.equal(currentPeriodDays({ plan: 'paid', endsAt: '2027-02-13T17:00:00Z' }, [approved]), 92)
  // ทีมงานแก้วันเองภายหลัง → วันหมดไม่ตรงกับรายการใด → ไม่รู้ความยาวรอบ
  assert.equal(currentPeriodDays({ plan: 'paid', endsAt: '2027-03-01T17:00:00Z' }, [approved]), null)
  assert.equal(currentPeriodDays({ plan: 'paid', endsAt: '2027-02-13T17:00:00Z' }, [{ ...approved, status: 'rejected' }]), null)
  assert.equal(currentPeriodDays({ plan: 'paid', endsAt: '2027-02-13T17:00:00Z' }, undefined), null)
  assert.equal(currentPeriodDays({ plan: 'paid' }, [approved]), null, 'paid without an end date')
  assert.equal(currentPeriodDays({ plan: 'converted', endsAt: '2027-02-13T17:00:00Z' }, [approved]), null)

  assert.equal(remainingFraction(22, 30), 22 / 30)
  assert.equal(remainingFraction(45, 30), 1, 'extended trial is clamped')
  assert.equal(remainingFraction(-3, 30), 0)
  assert.equal(remainingFraction(undefined, 30), null)
  assert.equal(remainingFraction(10, null), null)
})

test('status label on the hero card covers suspended, read-only, days and no expiry', () => {
  assert.equal(packageStatusLabel({ plan: 'trial', readOnly: false, daysLeft: 22 }), 'เหลือ 22 วัน')
  assert.equal(packageStatusLabel({ plan: 'paid', readOnly: true, daysLeft: -5 }), 'อ่านอย่างเดียว · หมดอายุแล้ว 5 วัน')
  assert.equal(packageStatusLabel({ plan: 'paid', readOnly: true }), 'อ่านอย่างเดียว')
  assert.equal(packageStatusLabel({ plan: 'suspended', readOnly: false, daysLeft: 3 }), 'ถูกระงับการใช้งาน')
  assert.equal(packageStatusLabel({ plan: 'paid', readOnly: false }), 'ใช้งานได้ตามปกติ')
})

test('renew steps: the first unfinished step is current; transfer counts as done once a slip is attached', () => {
  assert.deepEqual(renewStepStates({ monthChosen: false, slipAttached: false, detailsReady: false }), ['current', 'todo', 'todo'])
  assert.deepEqual(renewStepStates({ monthChosen: true, slipAttached: false, detailsReady: true }), ['done', 'current', 'todo'])
  assert.deepEqual(renewStepStates({ monthChosen: true, slipAttached: true, detailsReady: false }), ['done', 'done', 'current'])
  assert.deepEqual(renewStepStates({ monthChosen: true, slipAttached: true, detailsReady: true }), ['done', 'done', 'done'])
  // แนบสลิปก่อนเลือกเดือนได้ (ลากวางก่อน) ขั้นที่ 1 ยังเป็นขั้นปัจจุบัน
  assert.deepEqual(renewStepStates({ monthChosen: false, slipAttached: true, detailsReady: true }), ['current', 'done', 'done'])
})

test('dropped files: the first file is used and the rest are reported', () => {
  assert.deepEqual(pickDroppedFile([]), { file: null, ignored: 0 })
  assert.deepEqual(pickDroppedFile(['a']), { file: 'a', ignored: 0 })
  assert.deepEqual(pickDroppedFile(['a', 'b', 'c']), { file: 'a', ignored: 2 })
})

test('missing field labels follow the on-page order (step 1 then step 3)', () => {
  assert.deepEqual(missingFieldLabels({}), [])
  const empty = validatePaymentForm(
    { monthCount: null, amountText: '', paidDate: '', paidTime: '', slip: null, note: '' },
    context,
  )
  assert.deepEqual(missingFieldLabels(empty), ['ระยะเวลา', 'สลิปการโอน', 'วันเวลาที่โอน', 'ยอดเงินที่โอน'])
  assert.deepEqual(missingFieldLabels({ amount: 'x', monthCount: 'y' }), ['ระยะเวลา', 'ยอดเงินที่โอน'])
})
