import { useEffect, useRef, useState } from 'react'
import { formatMoney } from '../lib/format'
import { Input } from './ui/input'

type MoneyInputProps = {
  value: number
  disabled?: boolean
  onValueChange: (value: number) => void
}

export function MoneyInput({ value, disabled, onValueChange }: MoneyInputProps) {
  const [displayValue, setDisplayValue] = useState(() => formatMoney(value))
  const focusedRef = useRef(false)

  useEffect(() => {
    if (!focusedRef.current) setDisplayValue(formatMoney(value))
  }, [value])

  return (
    <Input
      className="money"
      type="text"
      inputMode="decimal"
      value={displayValue}
      disabled={disabled}
      onFocus={(event) => {
        const input = event.currentTarget
        focusedRef.current = true
        setDisplayValue(String(value))
        window.requestAnimationFrame(() => input.select())
      }}
      onChange={(event) => {
        const next = event.target.value.replace(/,/g, '')
        if (!/^\d*(\.\d{0,2})?$/.test(next)) return
        setDisplayValue(next)
        if (next !== '' && next !== '.') onValueChange(Number(next))
      }}
      onBlur={() => {
        focusedRef.current = false
        setDisplayValue(formatMoney(value))
      }}
    />
  )
}
