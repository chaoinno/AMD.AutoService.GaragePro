import type { ApiError, Envelope } from './types'
import { stashLoginNotice } from '../lib/loginNotice'
import { clearStoredSession, getAccessToken } from '../lib/session'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

/**
 * CustomEvent บน `window` ที่ยิงทุกครั้งที่ API ตอบ error เรื่องแพ็กเกจ (ทดลองใช้หมดอายุ / ฟีเจอร์ล็อก / ถึงลิมิต)
 * ให้ชั้น UI กลาง (แถบ/หน้าต่างทดลองใช้ใน AppShell) อธิบายได้โดยไม่ต้องดักทุก mutation — `detail` คือ ApiError
 */
export const PLAN_ERROR_EVENT = 'garagepro-plan-error'

const PLAN_ERROR_CODES: ReadonlySet<string> = new Set(['TRIAL_EXPIRED', 'PLAN_FEATURE_LOCKED', 'PLAN_LIMIT_REACHED'])

const defaultHeaders: Record<string, string> = {
  'X-Client-Source': 'web',
}

type ApiRequestInit = RequestInit & {
  skipUnauthorizedRedirect?: boolean
}

function makeApiError(
  messageTh: string,
  code: string,
  traceId: string,
  status?: number,
  extra?: Pick<ApiError, 'field' | 'details' | 'retryAfterSeconds'>,
): ApiError {
  const error = new Error(messageTh) as ApiError
  error.name = 'ApiError'
  error.messageTh = messageTh
  error.code = code
  error.traceId = traceId
  error.status = status
  error.field = extra?.field
  error.details = extra?.details
  error.retryAfterSeconds = extra?.retryAfterSeconds
  if (PLAN_ERROR_CODES.has(code)) dispatchPlanError(error)
  return error
}

function dispatchPlanError(error: ApiError) {
  // โมดูลนี้ถูก import ตอน prerender/SSR ด้วย ซึ่งไม่มี window
  if (typeof window === 'undefined' || typeof window.dispatchEvent !== 'function') return
  window.dispatchEvent(new CustomEvent(PLAN_ERROR_EVENT, { detail: error }))
}

/** header Retry-After (วินาที หรือ HTTP-date) → วินาที — เฉพาะ 429 เพื่อให้หน้าจอบอกได้ว่าลองใหม่ได้เมื่อไหร่ */
function parseRetryAfter(response: Response): number | undefined {
  if (response.status !== 429) return undefined
  const value = response.headers.get('retry-after')?.trim()
  if (!value) return undefined
  if (/^\d+$/.test(value)) return Number(value)
  const at = Date.parse(value)
  if (Number.isNaN(at)) return undefined
  return Math.max(0, Math.ceil((at - Date.now()) / 1000))
}

async function readEnvelope<T>(response: Response): Promise<Envelope<T> | null> {
  try {
    const envelope = (await response.json()) as Envelope<T> | null
    return envelope && typeof envelope === 'object' ? envelope : null
  } catch {
    return null
  }
}

/**
 * 401 = เซสชันใช้ไม่ได้แล้ว (หมดอายุ/ถูกเพิกถอน/แพ็กเกจเปลี่ยน) — อ่านเหตุผลจาก envelope ก่อนล้างเซสชัน
 * แล้วฝากไว้ให้หน้า login แสดง · ฝากเฉพาะตอนที่มีเซสชันอยู่จริง ไม่งั้น 401 จากการกรอกรหัสผิดบนฟอร์ม login เอง
 * จะถูกเก็บไว้แล้วโผล่ซ้ำตอนเปิดหน้า login ครั้งถัดไป
 */
function handleUnauthorized(envelope: Envelope<unknown> | null, expectedToken: string | null, redirect: boolean) {
  if (getAccessToken() !== expectedToken) return
  const hadSession = expectedToken !== null
  if (hadSession && envelope?.error) {
    stashLoginNotice({ code: envelope.error.code, messageTh: envelope.error.messageTh, traceId: envelope.traceId })
  }
  clearStoredSession()
  if (redirect && typeof window !== 'undefined' && window.location.pathname !== '/login') {
    window.location.assign('/login')
  }
}

export async function apiRequest<T>(path: string, init: ApiRequestInit = {}): Promise<T> {
  let response: Response
  const { skipUnauthorizedRedirect = false, ...requestInit } = init
  const accessToken = getAccessToken()

  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...requestInit,
      headers: {
        ...defaultHeaders,
        ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
        ...(requestInit.body && !(requestInit.body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}),
        ...requestInit.headers,
      },
    })
  } catch {
    throw makeApiError(
      'ไม่สามารถเชื่อมต่อระบบบริการได้',
      'NETWORK_ERROR',
      'ไม่มี traceId เนื่องจากยังติดต่อเซิร์ฟเวอร์ไม่ได้',
    )
  }

  const envelope = await readEnvelope<T>(response)
  if (getAccessToken() !== accessToken) throw makeApiError('บัญชีที่ใช้งานเปลี่ยนแล้ว', 'SESSION_CHANGED', 'session-changed')

  if (response.status === 401) {
    handleUnauthorized(envelope, accessToken, !skipUnauthorizedRedirect)
  }

  if (!envelope) {
    throw makeApiError(
      'ระบบบริการส่งข้อมูลกลับมาในรูปแบบที่ไม่ถูกต้อง',
      'INVALID_RESPONSE',
      response.headers.get('x-trace-id') ?? 'ไม่พบ traceId',
      response.status,
    )
  }

  if (!response.ok || !envelope.success || envelope.data === null) {
    throw makeApiError(
      envelope.error?.messageTh ?? 'ไม่สามารถดำเนินการได้',
      envelope.error?.code ?? `HTTP_${response.status}`,
      envelope.traceId || response.headers.get('x-trace-id') || 'ไม่พบ traceId',
      response.status,
      {
        field: envelope.error?.field,
        details: envelope.error?.details,
        retryAfterSeconds: parseRetryAfter(response),
      },
    )
  }

  return envelope.data
}

export async function apiDownload(path: string): Promise<Blob> {
  const accessToken = getAccessToken()
  let response: Response
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      headers: { ...defaultHeaders, ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) },
    })
  } catch {
    throw makeApiError('ไม่สามารถเชื่อมต่อระบบบริการได้', 'NETWORK_ERROR', 'ไม่มี traceId เนื่องจากยังติดต่อเซิร์ฟเวอร์ไม่ได้')
  }
  if (!response.ok) {
    const envelope = await readEnvelope<never>(response)
    if (getAccessToken() !== accessToken) throw makeApiError('บัญชีที่ใช้งานเปลี่ยนแล้ว', 'SESSION_CHANGED', 'session-changed')
    if (response.status === 401) handleUnauthorized(envelope, accessToken, true)
    throw makeApiError(
      envelope?.error?.messageTh ?? 'ส่งออกข้อมูลไม่สำเร็จ',
      envelope?.error?.code ?? `HTTP_${response.status}`,
      envelope?.traceId || response.headers.get('x-trace-id') || 'ไม่พบ traceId',
      response.status,
      {
        field: envelope?.error?.field,
        details: envelope?.error?.details,
        retryAfterSeconds: parseRetryAfter(response),
      },
    )
  }
  const blob = await response.blob()
  if (getAccessToken() !== accessToken) throw makeApiError('บัญชีที่ใช้งานเปลี่ยนแล้ว', 'SESSION_CHANGED', 'session-changed')
  return blob
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof Error && 'messageTh' in error && 'traceId' in error
}

/** error เรื่องแพ็กเกจ (403/422) — ไม่ใช่ "ไม่มีสิทธิ์ตามบทบาท" ต้องแสดงเป็นหน้า/ข้อความล็อกฟีเจอร์แทน */
export function isPlanError(error: unknown): error is ApiError {
  return isApiError(error) && PLAN_ERROR_CODES.has(error.code)
}

export function isPlanLockedError(error: unknown): error is ApiError {
  return isApiError(error) && error.code === 'PLAN_FEATURE_LOCKED'
}

export function isPlanLimitError(error: unknown): error is ApiError {
  return isApiError(error) && error.code === 'PLAN_LIMIT_REACHED'
}

export function isTrialExpiredError(error: unknown): error is ApiError {
  return isApiError(error) && error.code === 'TRIAL_EXPIRED'
}

/** ถูกจำกัดอัตรา (429) — ดู `retryAfterSeconds` ว่าลองใหม่ได้เมื่อไหร่ */
export function isRateLimitedError(error: unknown): error is ApiError {
  return isApiError(error) && (error.status === 429 || error.code.endsWith('RATE_LIMITED'))
}

export function isForbiddenError(error: unknown): boolean {
  if (!isApiError(error)) return false
  // TRIAL_EXPIRED/PLAN_FEATURE_LOCKED ก็เป็น 403 แต่ไม่ใช่เรื่องสิทธิ์ — ทุกจุดที่เช็ค forbidden จะได้ไม่แสดง
  // "ไม่มีสิทธิ์ดูข้อมูลนี้" ผิดความหมาย (ตัดที่เดียวครอบทุก call site)
  if (PLAN_ERROR_CODES.has(error.code)) return false
  return error.status === 403 || error.code.toUpperCase().includes('FORBIDDEN')
}
