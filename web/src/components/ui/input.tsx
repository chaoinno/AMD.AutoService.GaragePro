import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'

export function Input({ className, type = 'text', ...props }: ComponentProps<'input'>) {
  return <input data-slot="input" className={cn('ui-input', className)} type={type} {...props} />
}
