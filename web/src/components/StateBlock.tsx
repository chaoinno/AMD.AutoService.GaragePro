import {
  CircleAlert,
  FileQuestion,
  LoaderCircle,
  LockKeyhole,
  type LucideIcon,
} from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from './ui/button'
import { Skeleton } from './ui/skeleton'

type StateVariant = 'loading' | 'empty' | 'error' | 'forbidden'

const icons: Record<StateVariant, LucideIcon> = {
  loading: LoaderCircle,
  empty: FileQuestion,
  error: CircleAlert,
  forbidden: LockKeyhole,
}

type StateBlockProps = {
  variant: StateVariant
  title: string
  reason: string
  traceId?: string
  actionLabel: string
  onAction: () => void
  children?: ReactNode
}

export function StateBlock({
  variant,
  title,
  reason,
  traceId,
  actionLabel,
  onAction,
  children,
}: StateBlockProps) {
  const Icon = icons[variant]
  return (
    <section className={`state-block state-block--${variant}`} aria-live="polite">
      <div className="state-block__icon" aria-hidden="true">
        <Icon className={variant === 'loading' ? 'spin' : undefined} />
      </div>
      <div className="state-block__content">
        <h2>{title}</h2>
        <p>{reason}</p>
        {children}
        <p className="trace-id">
          รหัสติดตาม (traceId): <span>{traceId || 'ยังไม่มีรหัสติดตาม'}</span>
        </p>
        <Button variant="outline" onClick={onAction}>
          {actionLabel}
        </Button>
      </div>
    </section>
  )
}

export function SkeletonRows({ count = 5 }: { count?: number }) {
  return (
    <div className="skeleton-list" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div className="skeleton-row" key={index}>
          <Skeleton />
          <Skeleton />
          <Skeleton />
        </div>
      ))}
    </div>
  )
}
