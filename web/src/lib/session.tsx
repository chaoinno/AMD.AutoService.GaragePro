import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from 'react'
import type { LoginResult } from '../api/types'

const SESSION_KEY = 'garagepro.web.session'
const SESSION_EVENT = 'garagepro-session-change'

export type StoredSession = LoginResult & { stage: 'active' }

function isStoredSession(value: unknown): value is StoredSession {
  if (!value || typeof value !== 'object') return false
  const session = value as Partial<StoredSession>
  return (
    session.stage === 'active' &&
    typeof session.accessToken === 'string' &&
    typeof session.expiresAt === 'string' &&
    typeof session.user === 'object' &&
    typeof session.branchId === 'number' &&
    typeof session.branchName === 'string'
  )
}

export function readSession(): StoredSession | null {
  try {
    const raw = window.localStorage.getItem(SESSION_KEY)
    if (!raw) return null
    const session: unknown = JSON.parse(raw)
    if (!isStoredSession(session)) {
      window.localStorage.removeItem(SESSION_KEY)
      return null
    }
    const expiresAt = new Date(session.expiresAt).getTime()
    if (!Number.isNaN(expiresAt) && expiresAt <= Date.now()) {
      window.localStorage.removeItem(SESSION_KEY)
      return null
    }
    return session
  } catch {
    window.localStorage.removeItem(SESSION_KEY)
    return null
  }
}

function notifySessionChange() {
  window.dispatchEvent(new Event(SESSION_EVENT))
}

export function saveSession(result: LoginResult): StoredSession {
  const session: StoredSession = { ...result, stage: 'active' }
  window.localStorage.setItem(SESSION_KEY, JSON.stringify(session))
  notifySessionChange()
  return session
}

export function clearStoredSession() {
  window.localStorage.removeItem(SESSION_KEY)
  notifySessionChange()
}

type SessionContextValue = {
  session: StoredSession | null
  refreshSession: () => void
}

const SessionContext = createContext<SessionContextValue | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<StoredSession | null>(() => readSession())
  const refreshSession = useCallback(() => setSession(readSession()), [])

  useEffect(() => {
    window.addEventListener('storage', refreshSession)
    window.addEventListener(SESSION_EVENT, refreshSession)
    return () => {
      window.removeEventListener('storage', refreshSession)
      window.removeEventListener(SESSION_EVENT, refreshSession)
    }
  }, [refreshSession])

  const value = useMemo(() => ({ session, refreshSession }), [refreshSession, session])
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession() {
  const context = useContext(SessionContext)
  if (!context) throw new Error('useSession must be used inside SessionProvider')
  return context
}

export function getAccessToken(): string | null {
  return readSession()?.accessToken ?? null
}
