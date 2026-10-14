import { useCallback, useEffect, useRef, useState } from 'react'
import { loadExternalScript } from './externalScript'

type Turnstile = {
  render: (element: HTMLElement, options: { sitekey: string; language: string; size: string; callback: (token: string) => void; 'expired-callback': () => void; 'error-callback': () => void }) => string
  reset: (id: string) => void
  remove: (id: string) => void
}
declare global { interface Window { turnstile?: Turnstile } }

export function useTurnstile(siteKey = '') {
  const containerRef = useRef<HTMLDivElement>(null)
  const widgetRef = useRef<string | undefined>(undefined)
  const [token, setToken] = useState('')
  const [error, setError] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const reset = useCallback(() => {
    setToken('')
    setError(false)
    if (widgetRef.current !== undefined) window.turnstile?.reset(widgetRef.current)
    else setAttempt((value) => value + 1)
  }, [])
  useEffect(() => {
    setToken('')
    setError(false)
    if (!siteKey) return
    let cancelled = false
    void loadExternalScript('https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit').then(() => {
      if (cancelled || !containerRef.current) return
      if (!window.turnstile) throw new Error('Turnstile unavailable')
      widgetRef.current = window.turnstile.render(containerRef.current, {
        sitekey: siteKey, language: 'th', size: 'flexible',
        callback: (value) => { if (!cancelled) { setToken(value); setError(false) } },
        'expired-callback': () => { if (!cancelled) setToken('') },
        'error-callback': () => { if (!cancelled) { setToken(''); setError(true) } },
      })
    }).catch(() => { if (!cancelled) setError(true) })
    return () => {
      cancelled = true
      if (widgetRef.current !== undefined) window.turnstile?.remove(widgetRef.current)
      widgetRef.current = undefined
    }
  }, [siteKey, attempt])
  return { token: siteKey ? token : '', reset, available: Boolean(siteKey), containerRef, error }
}
