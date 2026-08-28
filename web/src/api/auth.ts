import { apiRequest } from './client'
import type { LoginResult, MeResult } from './types'

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
