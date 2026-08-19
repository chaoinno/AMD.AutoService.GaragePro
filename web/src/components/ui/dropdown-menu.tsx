import {
  createContext,
  type ComponentProps,
  type ReactNode,
  useContext,
  useEffect,
  useRef,
  useState,
} from 'react'
import { cn } from '../../lib/utils'

type DropdownContextValue = { open: boolean; setOpen: (open: boolean) => void }
const DropdownContext = createContext<DropdownContextValue | null>(null)

function useDropdown() {
  const context = useContext(DropdownContext)
  if (!context) throw new Error('DropdownMenu components must be used inside DropdownMenu')
  return context
}

export function DropdownMenu({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const close = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    const escape = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', escape)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', escape)
    }
  }, [open])

  return (
    <DropdownContext.Provider value={{ open, setOpen }}>
      <div data-slot="dropdown-menu" className="ui-dropdown" ref={rootRef}>
        {children}
      </div>
    </DropdownContext.Provider>
  )
}

export function DropdownMenuTrigger({ onClick, ...props }: ComponentProps<'button'>) {
  const { open, setOpen } = useDropdown()
  return (
    <button
      data-slot="dropdown-menu-trigger"
      type="button"
      aria-haspopup="menu"
      aria-expanded={open}
      onClick={(event) => {
        onClick?.(event)
        if (!event.defaultPrevented) setOpen(!open)
      }}
      {...props}
    />
  )
}

export function DropdownMenuContent({ className, ...props }: ComponentProps<'div'>) {
  const { open } = useDropdown()
  if (!open) return null
  return <div data-slot="dropdown-menu-content" role="menu" className={cn('ui-dropdown__content', className)} {...props} />
}

export function DropdownMenuItem({ className, onClick, ...props }: ComponentProps<'button'>) {
  const { setOpen } = useDropdown()
  return (
    <button
      data-slot="dropdown-menu-item"
      type="button"
      role="menuitem"
      className={cn('ui-dropdown__item', className)}
      onClick={(event) => {
        onClick?.(event)
        if (!event.defaultPrevented) setOpen(false)
      }}
      {...props}
    />
  )
}

export function DropdownMenuLabel({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="dropdown-menu-label" className={cn('ui-dropdown__label', className)} {...props} />
}

export function DropdownMenuSeparator({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="dropdown-menu-separator" role="separator" className={cn('ui-dropdown__separator', className)} {...props} />
}
