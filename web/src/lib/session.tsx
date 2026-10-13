import {
  createContext,
  type ReactNode,
  useCallback,
  useContext,
  useMemo,
  useSyncExternalStore,
} from 'react'
import type { AuthUser, LoginResult } from '../api/types'

const SESSION_KEY = 'garagepro.web.session'
const SESSION_EVENT = 'garagepro-session-change'

export type StoredSession = LoginResult & { stage: 'active' }

// ทุกจุดที่แตะ window/localStorage ต้องผ่าน guard นี้ — โมดูลนี้ถูก import ตอน prerender/SSR ของหน้าสาธารณะด้วย
function hasWindow(): boolean {
  return typeof window !== 'undefined'
}

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

/** ตีความค่าดิบจาก localStorage — null ถ้ารูปแบบผิดหรือหมดอายุ (ไม่แตะ storage เอง) */
function parseStoredSession(raw: string | null): StoredSession | null {
  if (!raw) return null
  try {
    const session: unknown = JSON.parse(raw)
    if (!isStoredSession(session)) return null
    const expiresAt = new Date(session.expiresAt).getTime()
    if (!Number.isNaN(expiresAt) && expiresAt <= Date.now()) return null
    return session
  } catch {
    return null
  }
}

function readRaw(): string | null {
  if (!hasWindow()) return null
  try {
    return window.localStorage.getItem(SESSION_KEY)
  } catch {
    return null
  }
}

function removeRaw() {
  if (!hasWindow()) return
  try {
    window.localStorage.removeItem(SESSION_KEY)
  } catch {
    // storage ถูกบล็อก — ถือว่าไม่มีเซสชันอยู่แล้ว
  }
}

export function readSession(): StoredSession | null {
  const raw = readRaw()
  const session = parseStoredSession(raw)
  // ค่าที่เสีย/หมดอายุลบทิ้งทันที ให้ snapshot ของ React เห็น "ไม่มีเซสชัน" ตรงกันในรอบถัดไป
  if (raw !== null && session === null) removeRaw()
  return session
}

function notifySessionChange() {
  if (!hasWindow()) return
  window.dispatchEvent(new Event(SESSION_EVENT))
}

function writeSession(session: StoredSession) {
  if (!hasWindow()) return
  window.localStorage.setItem(SESSION_KEY, JSON.stringify(session))
  notifySessionChange()
}

export function saveSession(result: LoginResult): StoredSession {
  const session: StoredSession = { ...result, stage: 'active' }
  writeSession(session)
  return session
}

/**
 * อัปเดตเฉพาะข้อมูลผู้ใช้ในเซสชัน (เช่น plan/workspace ที่อ่านสดจาก /auth/me) โดยไม่ต้อง login ใหม่
 * ไม่มีเซสชันคืน null และไม่เขียนอะไร
 */
export function updateSessionUser(patch: Partial<AuthUser>): StoredSession | null {
  const current = readSession()
  if (!current) return null
  const next: StoredSession = { ...current, user: { ...current.user, ...patch } }
  writeSession(next)
  return next
}

export function clearStoredSession() {
  removeRaw()
  notifySessionChange()
}

// ---- external store สำหรับ React ----------------------------------------------------------------
// useSyncExternalStore ต้องการให้ getSnapshot คืน object ตัวเดิมตราบใดที่ store ไม่เปลี่ยน (ไม่งั้น render วน
// และเตือน) แต่ JSON.parse ให้ object ใหม่ทุกครั้ง จึง cache ผลตามสตริงดิบใน localStorage แทน
let snapshotRaw: string | null | undefined
let snapshotSession: StoredSession | null = null

function getSnapshot(): StoredSession | null {
  const raw = readRaw()
  if (raw === snapshotRaw) return snapshotSession
  snapshotRaw = raw
  snapshotSession = parseStoredSession(raw)
  if (raw !== null && snapshotSession === null) removeRaw()
  return snapshotSession
}

// prerender/SSR ไม่มี localStorage — ถือว่าไม่มีเซสชันเสมอ แล้วค่อย reconcile หลัง hydrate
// (บนเบราว์เซอร์ปกติ createRoot ไม่เรียกตัวนี้ จึงไม่มีจังหวะที่หน้าเด้งไป /login ทั้งที่มีเซสชัน)
function getServerSnapshot(): StoredSession | null {
  return null
}

function subscribe(onStoreChange: () => void): () => void {
  if (!hasWindow()) return () => {}
  window.addEventListener('storage', onStoreChange)
  window.addEventListener(SESSION_EVENT, onStoreChange)
  return () => {
    window.removeEventListener('storage', onStoreChange)
    window.removeEventListener(SESSION_EVENT, onStoreChange)
  }
}

type SessionContextValue = {
  session: StoredSession | null
  refreshSession: () => void
}

const SessionContext = createContext<SessionContextValue | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const session = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)
  // readSession() ก่อน เพื่อให้เซสชันที่หมดอายุระหว่างเปิดหน้าถูกลบจริง แล้วค่อยบอกทุก subscriber ให้อ่านใหม่
  const refreshSession = useCallback(() => {
    readSession()
    notifySessionChange()
  }, [])

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
