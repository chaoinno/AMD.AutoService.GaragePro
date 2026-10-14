import { useEffect, useRef, useState } from 'react'
import { loadExternalScript } from './externalScript'

type GoogleIdentity = {
  initialize: (options: { client_id: string; callback: (response: { credential: string }) => void }) => void
  renderButton: (element: HTMLElement, options: { locale: string; text: string; shape: string; width: number }) => void
}
declare global {
  interface Window { google?: { accounts: { id: GoogleIdentity } } }
}

export function useGoogleIdentity(onCredential: (idToken: string) => void, clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID ?? '') {
  const buttonRef = useRef<HTMLDivElement>(null)
  const callbackRef = useRef(onCredential)
  const [ready, setReady] = useState(false)
  const [error, setError] = useState(false)
  const [attempt, setAttempt] = useState(0)
  useEffect(() => { callbackRef.current = onCredential }, [onCredential])
  useEffect(() => {
    if (!clientId) return
    let cancelled = false
    let observer: ResizeObserver | undefined
    setReady(false)
    setError(false)
    void loadExternalScript('https://accounts.google.com/gsi/client').then(() => {
      if (cancelled || !buttonRef.current) return
      const identity = window.google?.accounts.id
      if (!identity) throw new Error('Google Identity unavailable')
      identity.initialize({ client_id: clientId, callback: ({ credential }) => { if (!cancelled && credential) callbackRef.current(credential) } })
      const element = buttonRef.current
      let lastWidth = 0
      const render = () => {
        const width = Math.min(400, Math.floor(element.clientWidth))
        if (width === lastWidth || width <= 0) return
        lastWidth = width
        element.replaceChildren()
        identity.renderButton(element, { locale: 'th', text: 'continue_with', shape: 'pill', width })
      }
      render()
      observer = new ResizeObserver(render)
      observer.observe(element)
      setReady(true)
    }).catch(() => { if (!cancelled) setError(true) })
    return () => { cancelled = true; observer?.disconnect(); buttonRef.current?.replaceChildren() }
  }, [clientId, attempt])
  return { available: Boolean(clientId), buttonRef, ready, error, retry: () => setAttempt((value) => value + 1) }
}
