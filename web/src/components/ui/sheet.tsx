import type { ComponentProps } from 'react'
import { cn } from '../../lib/utils'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from './dialog'

export const Sheet = Dialog
export const SheetTrigger = DialogTrigger
export const SheetClose = DialogClose
export const SheetHeader = DialogHeader
export const SheetFooter = DialogFooter
export const SheetTitle = DialogTitle
export const SheetDescription = DialogDescription

export function SheetContent({ className, ...props }: ComponentProps<typeof DialogContent>) {
  return <DialogContent className={cn('ui-sheet__content', className)} {...props} />
}
