import { cva, type VariantProps } from 'class-variance-authority'
import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'

export const buttonVariants = cva('ui-button', {
  variants: {
    variant: {
      default: 'ui-button--default',
      destructive: 'ui-button--destructive',
      outline: 'ui-button--outline',
      secondary: 'ui-button--secondary',
      ghost: 'ui-button--ghost',
      soft: 'ui-button--soft',
      link: 'ui-button--link',
    },
    size: {
      default: 'ui-button--size-default',
      sm: 'ui-button--size-sm',
      lg: 'ui-button--size-lg',
      icon: 'ui-button--size-icon',
    },
  },
  defaultVariants: { variant: 'default', size: 'default' },
})

export type ButtonProps = ComponentProps<'button'> & VariantProps<typeof buttonVariants>

export function Button({ className, variant, size, type = 'button', ...props }: ButtonProps) {
  return (
    <button
      data-slot="button"
      type={type}
      className={cn(buttonVariants({ variant, size }), className)}
      {...props}
    />
  )
}
