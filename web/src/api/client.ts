import type { ApiError, Envelope } from './types'
import { clearStoredSession, getAccessToken } from '../lib/session'

export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

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
  extra?: Pick<ApiError, 'field' | 'details'>,
): ApiError {
  const error = new Error(messageTh) as ApiError
  error.name = 'ApiError'
  error.messageTh = messageTh
  error.code = code
  error.traceId = traceId
  error.status = status
  error.field = extra?.field
  error.details = extra?.details
  return error
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

  if (response.status === 401) {
    clearStoredSession()
    if (!skipUnauthorizedRedirect && window.location.pathname !== '/login') {
      window.location.assign('/login')
    }
  }

  let envelope: Envelope<T> | null = null
  try {
    envelope = (await response.json()) as Envelope<T>
  } catch {
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
      envelope.error
        ? { field: envelope.error.field, details: envelope.error.details }
        : undefined,
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
  if (response.status === 401) {
    clearStoredSession()
    window.location.assign('/login')
  }
  if (!response.ok) {
    try {
      const envelope = (await response.json()) as Envelope<never>
      throw makeApiError(envelope.error?.messageTh ?? 'ส่งออกข้อมูลไม่สำเร็จ', envelope.error?.code ?? `HTTP_${response.status}`, envelope.traceId, response.status)
    } catch (error) {
      if (isApiError(error)) throw error
      throw makeApiError('ส่งออกข้อมูลไม่สำเร็จ', `HTTP_${response.status}`, response.headers.get('x-trace-id') ?? 'ไม่พบ traceId', response.status)
    }
  }
  return response.blob()
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof Error && 'messageTh' in error && 'traceId' in error
}

export function isForbiddenError(error: unknown): boolean {
  return isApiError(error) &&
    (error.status === 403 || error.code.toUpperCase().includes('FORBIDDEN'))
}
