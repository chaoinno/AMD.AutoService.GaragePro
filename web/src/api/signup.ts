import { apiRequest } from './client'
import type { LoginResult } from './types'

export type SignupConfig = {
  enabled: boolean
  googleEnabled: boolean
  emailEnabled: boolean
  turnstileSiteKey?: string
}
export type SignupProvince = { id: number; name: string }
export type SignupTicket = { signupToken: string; expiresAt: string }
export type ResendResult = { resendAfterSeconds: number }
export type EmailStartInput = {
  requestId: string
  email: string
  password: string
  turnstileToken: string
  website: string
  consent: true
}
export type WorkspaceInput = {
  requestId: string
  signupToken: string
  garageName: string
  phone: string
  provinceId?: number
  size?: '1-3' | '4-10' | '10+'
  sampleData: boolean
}
export type SampleTechnician = { userName: string; password: string }
export type SignupResult = LoginResult & {
  workspaceNo: string
  sampleTechnicians?: SampleTechnician[]
}

function post<T>(path: string, body: unknown): Promise<T> {
  return apiRequest<T>(`/api/v1/public/signup/${path}`, {
    method: 'POST',
    body: JSON.stringify(body),
    skipUnauthorizedRedirect: true,
  })
}

export function getSignupConfig() {
  return apiRequest<SignupConfig>('/api/v1/public/signup/config', { skipUnauthorizedRedirect: true })
}
export function getSignupProvinces() {
  return apiRequest<SignupProvince[]>('/api/v1/public/signup/provinces', { skipUnauthorizedRedirect: true })
}
export function startSignupEmail(input: EmailStartInput) {
  return post<ResendResult>('email/start', input)
}
export function resendSignupEmail(email: string) {
  return post<ResendResult>('email/resend', { email })
}
/**
 * [SECURITY] ส่งรหัสผ่านเดียวกับ email/start ไปด้วย — server รับ OTP เฉพาะเมื่อรหัสผ่านตรงกับที่ตั้งตอนเริ่มสมัคร
 * (กันคนที่เริ่มสมัครอีเมลเดียวกันทับด้วยรหัสผ่านของตัวเอง) · ไม่ตรง = SIGNUP_PASSWORD_MISMATCH (field `password`)
 */
export function verifySignupEmail(email: string, code: string, password: string) {
  return post<SignupTicket>('email/verify', { email, code, password })
}
export function signupWithGoogle(input: { idToken: string; turnstileToken: string; website: string }) {
  return post<SignupTicket>('google', input)
}
export function createSignupWorkspace(input: WorkspaceInput) {
  return post<SignupResult>('workspace', input)
}
