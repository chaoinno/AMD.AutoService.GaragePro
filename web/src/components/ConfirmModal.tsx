import type { ReactNode } from 'react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from './ui/dialog'

type ConfirmModalProps = {
  open: boolean
  title: string
  description?: string
  onClose: () => void
  children?: ReactNode
  footer?: ReactNode
  size?: 'small' | 'medium' | 'large' | 'xlarge'
}

export function ConfirmModal({
  open,
  title,
  description,
  onClose,
  children,
  footer,
  size = 'medium',
}: ConfirmModalProps) {
  return (
    <Dialog open={open} onOpenChange={(nextOpen) => { if (!nextOpen) onClose() }}>
      <DialogContent className={`ui-dialog__content--${size}`}>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description || 'รายละเอียดการดำเนินการ'}</DialogDescription>
        </DialogHeader>
        {children ? <div className="ui-dialog__body">{children}</div> : null}
        {footer ? <DialogFooter>{footer}</DialogFooter> : null}
      </DialogContent>
    </Dialog>
  )
}
