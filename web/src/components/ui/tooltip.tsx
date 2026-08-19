import type { ComponentProps, ReactNode } from 'react'
import { cn } from '../../lib/utils'

export function TooltipProvider({ children }: { children: ReactNode }) {
  return children
}

export function Tooltip({ content, children }: { content: ReactNode; children: ReactNode }) {
  return (
    <span data-slot="tooltip" className="ui-tooltip">
      {children}
      <span role="tooltip" className="ui-tooltip__content">
        {content}
      </span>
    </span>
  )
}

export function TooltipContent({ className, ...props }: ComponentProps<'span'>) {
  return <span role="tooltip" className={cn('ui-tooltip__content', className)} {...props} />
}

export function TooltipTrigger({ className, ...props }: ComponentProps<'span'>) {
  return <span className={cn('ui-tooltip__trigger', className)} {...props} />
}
