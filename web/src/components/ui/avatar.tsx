import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'

export function Avatar({ className, ...props }: ComponentProps<'span'>) {
  return <span data-slot="avatar" className={cn('ui-avatar', className)} {...props} />
}

export function AvatarImage({ className, ...props }: ComponentProps<'img'>) {
  return <img data-slot="avatar-image" className={cn('ui-avatar__image', className)} {...props} />
}

export function AvatarFallback({ className, ...props }: ComponentProps<'span'>) {
  return <span data-slot="avatar-fallback" className={cn('ui-avatar__fallback', className)} {...props} />
}
