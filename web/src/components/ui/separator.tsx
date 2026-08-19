import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'

export function Separator({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="separator" role="separator" className={cn('ui-separator', className)} {...props} />
}
