import { formatMoney } from '../lib/format'

type MoneyProps = {
  value: number | null | undefined
  prefix?: string
  suffix?: string
  className?: string
}

export function Money({ value, prefix, suffix, className = '' }: MoneyProps) {
  return (
    <span className={`money ${className}`.trim()}>
      {prefix}
      {formatMoney(value)}
      {suffix}
    </span>
  )
}
