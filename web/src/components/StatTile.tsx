import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Card } from './ui/card'

export function StatTile({ icon: Icon, label, value, hint, tone = 'default' }: {
  icon: LucideIcon
  label: string
  value: ReactNode
  hint?: string
  tone?: 'default' | 'warning' | 'danger'
}) {
  return (
    <Card className="stat-tile">
      <span className={`stat-tile__icon stat-tile__icon--${tone}`} aria-hidden="true"><Icon /></span>
      <div className="stat-tile__body">
        <span className="stat-tile__label">{label}</span>
        <span className="stat-tile__value">{value}</span>
        {hint ? <small className="stat-tile__hint">{hint}</small> : null}
      </div>
    </Card>
  )
}
