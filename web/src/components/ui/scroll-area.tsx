import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'

export function ScrollArea({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="scroll-area" className={cn('ui-scroll-area', className)} {...props} />
}
