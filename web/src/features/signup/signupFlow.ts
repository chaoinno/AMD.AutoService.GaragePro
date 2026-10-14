export const SIGNUP_STORAGE_KEY = 'garagepro.web.signup'
export const SAMPLE_CREDENTIALS_KEY = 'garagepro.web.sampleCredentials'
export type SignupStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>
export type SignupFlow = {
  step: 'account' | 'otp' | 'workspace' | 'done'
  email: string
  ticket: 'idle' | 'sent' | 'verified'
  signupToken?: string
  expiresAt?: string
  requestId?: string
  resendAvailableAt: number
}
export const initialSignupFlow: SignupFlow = { step: 'account', email: '', ticket: 'idle', resendAvailableAt: 0 }
export type SignupAction =
  | { type: 'emailStarted'; email: string; resendAfterSeconds: number; now: number }
  | { type: 'verified'; signupToken: string; expiresAt: string }
  | { type: 'resendScheduled'; resendAfterSeconds: number; now: number }
  | { type: 'edited'; requestId: string }
  | { type: 'changeEmail' }
  | { type: 'restart' }
  | { type: 'completed' }

export function signupReducer(state: SignupFlow, action: SignupAction): SignupFlow {
  switch (action.type) {
    case 'emailStarted':
      if (state.step !== 'account') return state
      return { ...state, step: 'otp', email: action.email, ticket: 'sent', resendAvailableAt: action.now + Math.max(0, action.resendAfterSeconds) * 1000 }
    case 'verified':
      if (state.step !== 'account' && state.step !== 'otp') return state
      return { ...state, step: 'workspace', ticket: 'verified', signupToken: action.signupToken, expiresAt: action.expiresAt, requestId: undefined }
    case 'resendScheduled':
      if (state.step !== 'otp') return state
      return { ...state, resendAvailableAt: action.now + Math.max(0, action.resendAfterSeconds) * 1000 }
    case 'edited':
      return { ...state, requestId: action.requestId }
    case 'changeEmail':
    case 'restart':
      return { ...initialSignupFlow, email: state.email }
    case 'completed':
      return state.step === 'workspace' ? { ...initialSignupFlow, step: 'done' } : state
  }
}

export function resendSecondsRemaining(state: SignupFlow, now: number): number {
  return Math.max(0, Math.ceil((state.resendAvailableAt - now) / 1000))
}

function defaultStorage(): SignupStorage | null {
  try { return typeof window === 'undefined' ? null : window.sessionStorage } catch { return null }
}

export function persistSignupFlow(state: SignupFlow, storage: SignupStorage | null = defaultStorage()): void {
  if (!storage) return
  try {
    if (state.step === 'done') { storage.removeItem(SIGNUP_STORAGE_KEY); return }
    // [SECURITY] Allowlist only flow fields; passwords and Google/Turnstile credentials never enter this draft.
    const { step, email, ticket, signupToken, expiresAt, requestId, resendAvailableAt } = state
    storage.setItem(SIGNUP_STORAGE_KEY, JSON.stringify({ step, email, ticket, signupToken, expiresAt, requestId, resendAvailableAt }))
  } catch { /* Storage may be blocked; the live flow still works. */ }
}

export function restoreSignupFlow(storage: SignupStorage | null = defaultStorage(), now = Date.now()): SignupFlow {
  try {
    const raw = storage?.getItem(SIGNUP_STORAGE_KEY)
    if (!raw) return { ...initialSignupFlow }
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object') return { ...initialSignupFlow }
    const data = value as Partial<SignupFlow>
    const email = typeof data.email === 'string' ? data.email : ''
    const requestId = typeof data.requestId === 'string' ? data.requestId : undefined
    if (data.step === 'workspace' && typeof data.signupToken === 'string' && data.signupToken && typeof data.expiresAt === 'string' && Date.parse(data.expiresAt) > now) {
      return { ...initialSignupFlow, step: 'workspace', email, ticket: 'verified', signupToken: data.signupToken, expiresAt: data.expiresAt, requestId }
    }
    if (data.step === 'otp' && email && typeof data.resendAvailableAt === 'number' && Number.isFinite(data.resendAvailableAt)) {
      return { ...initialSignupFlow, step: 'otp', email, ticket: 'sent', resendAvailableAt: data.resendAvailableAt, requestId }
    }
    return { ...initialSignupFlow, email }
  } catch { return { ...initialSignupFlow } }
}
