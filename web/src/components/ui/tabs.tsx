import { createContext, type ComponentProps, type ReactNode, useContext } from 'react'
import { cn } from '../../lib/utils'

type TabsContextValue = { value: string; onValueChange: (value: string) => void }
const TabsContext = createContext<TabsContextValue | null>(null)

function useTabs() {
  const context = useContext(TabsContext)
  if (!context) throw new Error('Tabs components must be used inside Tabs')
  return context
}

export function Tabs({
  value,
  onValueChange,
  className,
  children,
  ...props
}: ComponentProps<'div'> & {
  value: string
  onValueChange: (value: string) => void
  children: ReactNode
}) {
  return (
    <TabsContext.Provider value={{ value, onValueChange }}>
      <div data-slot="tabs" className={cn('ui-tabs', className)} {...props}>
        {children}
      </div>
    </TabsContext.Provider>
  )
}

export function TabsList({ className, ...props }: ComponentProps<'div'>) {
  return <div data-slot="tabs-list" role="tablist" className={cn('ui-tabs__list', className)} {...props} />
}

export function TabsTrigger({ value, className, ...props }: ComponentProps<'button'> & { value: string }) {
  const tabs = useTabs()
  const active = tabs.value === value
  return (
    <button
      data-slot="tabs-trigger"
      role="tab"
      type="button"
      aria-selected={active}
      data-state={active ? 'active' : 'inactive'}
      className={cn('ui-tabs__trigger', className)}
      onClick={() => tabs.onValueChange(value)}
      {...props}
    />
  )
}

export function TabsContent({ value, className, ...props }: ComponentProps<'div'> & { value: string }) {
  const tabs = useTabs()
  if (tabs.value !== value) return null
  return <div data-slot="tabs-content" role="tabpanel" className={cn('ui-tabs__content', className)} {...props} />
}
