import { apiRequest } from './client'
import type { LoginResult, MeResult, ShiftOption, ShiftSessionResult } from './types'

export function login(userName: string, password: string) {
  return apiRequest<LoginResult>('/api/v1/auth/login', {
    method: 'POST',
    body: JSON.stringify({ userName, password }),
    skipUnauthorizedRedirect: true,
  })
}

export function getBranchShifts(branchId: number) {
  return apiRequest<ShiftOption[]>(`/api/v1/auth/branches/${branchId}/shifts`)
}

export function openShiftSession(branchId: number, shiftId: string) {
  return apiRequest<ShiftSessionResult>('/api/v1/auth/shift-sessions', {
    method: 'POST',
    body: JSON.stringify({ branchId, shiftId }),
  })
}

export function closeShiftSession(sessionId: string) {
  return apiRequest<boolean>(`/api/v1/auth/shift-sessions/${sessionId}/close`, {
    method: 'POST',
  })
}

export function getMe() {
  return apiRequest<MeResult>('/api/v1/auth/me')
}
