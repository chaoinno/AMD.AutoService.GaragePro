import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { LockKeyhole, ShieldCheck } from 'lucide-react'
import { Input } from '../../components/ui/input'

export function PurchaseApprovalPin({ value, onChange, branchName, count, taxChange, total, disabled, invalid, errorMessage }: {
  value: string; onChange: (value: string) => void; branchName: string; count: number; taxChange?: boolean; total?: number; disabled: boolean; invalid: boolean; errorMessage?: string
}) {
  const inputs = useRef<Array<HTMLInputElement | null>>([])
  const timers = useRef<Array<ReturnType<typeof setTimeout> | undefined>>([])
  const pendingFocus = useRef<number | null>(null)
  const [visibleDigits, setVisibleDigits] = useState<Set<number>>(() => new Set())
  const focus = (index: number, select = true) => { inputs.current[index]?.focus(); if (select) inputs.current[index]?.select() }
  useEffect(() => { inputs.current[0]?.focus() }, [])
  useEffect(() => () => { timers.current.forEach(timer => clearTimeout(timer)) }, [])
  useEffect(() => {
    if (invalid || disabled) {
      timers.current.forEach(timer => clearTimeout(timer))
      setVisibleDigits(new Set())
      if (invalid && !disabled) pendingFocus.current = 0
    }
  }, [invalid, disabled])
  // Move only after the controlled value commits, so onFocus sees the new PIN length.
  useLayoutEffect(() => {
    if (pendingFocus.current !== null) {
      const target = pendingFocus.current
      pendingFocus.current = null
      focus(target, false)
    }
  })
  const enter = (next: string, target: number, reveal: number[] = []) => {
    pendingFocus.current = target
    onChange(next)
    timers.current.forEach((timer, index) => { if (index >= next.length) clearTimeout(timer) })
    setVisibleDigits(old => new Set([...old].filter(index => index < next.length).concat(reveal)))
    reveal.forEach(index => {
      clearTimeout(timers.current[index])
      timers.current[index] = setTimeout(() => {
        setVisibleDigits(old => { const hidden = new Set(old); hidden.delete(index); return hidden })
        timers.current[index] = undefined
      }, 1000)
    })
  }
  return <section className="purchase-pin" aria-labelledby="purchase-pin-title">
    <span className="purchase-pin__seal" aria-hidden="true"><ShieldCheck /></span>
    <div className="purchase-pin__heading"><h3 id="purchase-pin-title">ยืนยันการอนุมัติด้วย PIN</h3><p>รหัสประจำสาขา <strong>{branchName}</strong></p></div>
    <div className="purchase-pin__summary"><span>รายการที่ขออนุมัติ</span><strong>{count > 0 ? `${count} รายการ${taxChange ? ' และ VAT' : ''}` : taxChange ? 'VAT ของเอกสาร' : 'ไม่มีรายการแก้ไข'}</strong></div>
    {total !== undefined && <div className="purchase-pin__summary"><span>ยอดสุทธิเอกสาร</span><strong>{total.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} บาท</strong></div>}
    <fieldset className="purchase-pin__fieldset" disabled={disabled} aria-describedby={invalid ? 'purchase-pin-error purchase-pin-help' : 'purchase-pin-help'} data-invalid={invalid || undefined}>
      <legend>กรอก PIN สาขา 4 หลัก</legend>
      <div className="purchase-pin__digits" onPaste={event => {
        const digits = event.clipboardData.getData('text').trim()
        event.preventDefault()
        if (/^[0-9]{1,4}$/.test(digits)) enter(digits, Math.min(digits.length, 3), [...digits].map((_, index) => index))
      }}>
        {[0, 1, 2, 3].map(index => <Input key={index} ref={element => { inputs.current[index] = element }}
          type={visibleDigits.has(index) ? 'text' : 'password'} inputMode="numeric" autoComplete="off" maxLength={1} pattern="[0-9]" required
          data-dialog-autofocus={index === 0 || undefined}
          aria-label={`PIN หลักที่ ${index + 1}`} aria-invalid={invalid} value={value[index] || ''}
          onFocus={event => { if (index > value.length) focus(value.length); else event.target.select() }}
          onChange={event => {
            const digit = event.target.value
            if (!/^[0-9]?$/.test(digit)) return
            if (digit) {
              const next = value.slice(0, index) + digit + value.slice(index + 1)
              enter(next, Math.min(index + 1, 3, next.length), [index])
            } else { enter(value.slice(0, index), index) }
          }}
          onKeyDown={event => {
            if (event.key === 'Backspace') { event.preventDefault(); const target = value[index] ? index : Math.max(0, index - 1); enter(value.slice(0, target), target) }
            if (event.key === 'Delete') { event.preventDefault(); enter(value.slice(0, index), index) }
            if (event.key === 'ArrowLeft') { event.preventDefault(); focus(Math.max(0, index - 1)) }
            if (event.key === 'ArrowRight') { event.preventDefault(); focus(Math.min(3, index + 1, value.length)) }
          }} />)}
      </div>
    </fieldset>
    {invalid && <p id="purchase-pin-error" role="alert" className="purchase-pin__error">{errorMessage || 'ไม่สามารถอนุมัติได้ กรุณาลองอีกครั้ง'}</p>}
    <p id="purchase-pin-help" className="purchase-pin__help"><LockKeyhole aria-hidden="true" /> ใช้ PIN ของสาขานี้เพื่อยืนยันการอนุมัติ</p>
  </section>
}
