import { apiRequest } from './client'
import type { LoginResult, MeResult, SwitchableBranch } from './types'

export function login(userName: string, password: string) {
  return apiRequest<LoginResult>('/api/v1/auth/login', {
    method: 'POST',
    body: JSON.stringify({ userName, password }),
    skipUnauthorizedRedirect: true,
  })
}

export function getMe() {
  return apiRequest<MeResult>('/api/v1/auth/me')
}

/** สาขาที่ผู้ดูแลระบบสลับไปได้ — บัญชีผู้ดูแลระบบชื่อเดียวกันในสาขาอื่น (ไม่ใช่ admin ได้ 403) */
export function getSwitchableBranches() {
  return apiRequest<SwitchableBranch[]>('/api/v1/auth/switchable-branches')
}

/** สลับสาขา — คืน token ใหม่ในนามบัญชีของสาขาที่เลือก รูปเดียวกับ login */
export function switchBranch(branchId: number) {
  return apiRequest<LoginResult>('/api/v1/auth/switch-branch', {
    method: 'POST',
    body: JSON.stringify({ branchId }),
  })
}
