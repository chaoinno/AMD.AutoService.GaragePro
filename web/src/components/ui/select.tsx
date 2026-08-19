import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'

export function Select({ className, ...props }: ComponentProps<'select'>) {
  return <select data-slot="select" className={cn('ui-select', className)} {...props} />
}
