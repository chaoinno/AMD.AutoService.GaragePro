import { useEffect, useState, type ReactNode } from 'react'

/** Match SSR and the first hydration render before mounting browser-only content. */
export function ClientOnly({ children }: { children: ReactNode }) {
  const [mounted, setMounted] = useState(false)
  useEffect(() => setMounted(true), [])
  return mounted ? children : null
}
