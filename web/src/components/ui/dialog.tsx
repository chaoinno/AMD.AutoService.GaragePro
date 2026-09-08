import {
  createContext,
  type ComponentProps,
  type KeyboardEvent,
  type ReactNode,
  useContext,
  useEffect,
  useId,
  useRef,
} from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { cn } from '../../lib/utils'
import { Button } from './button'

type DialogContextValue = {
  open: boolean
  onOpenChange: (open: boolean) => void
  titleId: string
  descriptionId: string
}

const DialogContext = createContext<DialogContextValue | null>(null)

function useDialogContext() {
  const context = useContext(DialogContext)
  if (!context) throw new Error('Dialog components must be used inside Dialog')
  return context
}

export function Dialog({
  open,
  onOpenChange,
  children,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  children: ReactNode
}) {
  const baseId = useId()
  return (
    <DialogContext.Provider
      value={{
        open,
        onOpenChange,
        titleId: `${baseId}-title`,
        descriptionId: `${baseId}-description`,
      }}
    >
      {children}
    </DialogContext.Provider>
  )
}

export function DialogTrigger({ onClick, ...props }: ComponentProps<'button'>) {
  const { onOpenChange } = useDialogContext()
  return (
    <button
      data-slot="dialog-trigger"
      type="button"
      onClick={(event) => {
        onClick?.(event)
        if (!event.defaultPrevented) onOpenChange(true)
      }}
      {...props}
    />
  )
}

export function DialogContent({ className, children, ...props }: ComponentProps<'div'>) {
  const { open, onOpenChange, titleId, descriptionId } = useDialogContext()
  const contentRef = useRef<HTMLDivElement>(null)
  const previousFocus = useRef<HTMLElement | null>(null)
  const onOpenChangeRef = useRef(onOpenChange)

  useEffect(() => {
    onOpenChangeRef.current = onOpenChange
  }, [onOpenChange])
  const onOpenChangeRef = useRef(onOpenChange)
  onOpenChangeRef.current = onOpenChange

  useEffect(() => {
    if (!open) return
    previousFocus.current = document.activeElement as HTMLElement | null
    document.body.classList.add('modal-open')
    const frame = window.requestAnimationFrame(() => {
      const first = contentRef.current?.querySelector<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])',
      )
      ;(first ?? contentRef.current)?.focus()
    })
    const handleEscape = (event: globalThis.KeyboardEvent) => {
      if (event.key === 'Escape') onOpenChangeRef.currentRef.current(false)
    }
    document.addEventListener('keydown', handleEscape)
    return () => {
      window.cancelAnimationFrame(frame)
      document.removeEventListener('keydown', handleEscape)
      document.body.classList.remove('modal-open')
      previousFocus.current?.focus()
    }
  }, [open])

  if (!open) return null

  const trapFocus = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Tab') return
    const elements = Array.from(
      contentRef.current?.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])',
      ) ?? [],
    )
    if (!elements.length) {
      event.preventDefault()
      return
    }
    const first = elements[0]
    const last = elements[elements.length - 1]
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last?.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first?.focus()
    }
  }

  return createPortal(
    <div
      data-slot="dialog-overlay"
      className="ui-dialog__overlay"
      role="presentation"
      onMouseDown={() => onOpenChange(false)}
    >
      <div
        data-slot="dialog-content"
        ref={contentRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descriptionId}
        tabIndex={-1}
        className={cn('ui-dialog__content', className)}
        onKeyDown={trapFocus}
        onMouseDown={(event) => event.stopPropagation()}
        {...props}
      >
        {children}
        <Button
          className="ui-dialog__close"
          variant="ghost"
          size="icon"
          aria-label="ปิดหน้าต่าง"
          onClick={() => onOpenChange(false)}
        >
          <X aria-hidden="true" />
        </Button>
      </div>
    </div>,
    document.body,
  )
}

export function DialogHeader({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="dialog-header" className={cn('ui-dialog__header', className)} {...props} />
}

export function DialogFooter({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="dialog-footer" className={cn('ui-dialog__footer', className)} {...props} />
}

export function DialogTitle({ className, ...props }: ComponentProps<'h2'>) {
  const { titleId } = useDialogContext()
  return <h2 id={titleId} data-slot="dialog-title" className={cn('ui-dialog__title', className)} {...props} />
}

export function DialogDescription({ className, ...props }: ComponentProps<'p'>) {
  const { descriptionId } = useDialogContext()
  return (
    <p
      id={descriptionId}
      data-slot="dialog-description"
      className={cn('ui-dialog__description', className)}
      {...props}
    />
  )
}

export function DialogClose({ onClick, ...props }: ComponentProps<'button'>) {
  const { onOpenChange } = useDialogContext()
  return (
    <button
      data-slot="dialog-close"
      type="button"
      onClick={(event) => {
        onClick?.(event)
        if (!event.defaultPrevented) onOpenChange(false)
      }}
      {...props}
    />
  )
}
