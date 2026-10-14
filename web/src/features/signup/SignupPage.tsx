import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Check, CircleAlert, Eye, EyeOff, LoaderCircle, ShieldCheck } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { toast } from 'sonner'
import type { z } from 'zod'
import { isApiError, isRateLimitedError } from '../../api/client'
import { createSignupWorkspace, getSignupConfig, getSignupProvinces, resendSignupEmail, signupWithGoogle, startSignupEmail, verifySignupEmail, type SignupConfig, type SignupResult, type SignupTicket } from '../../api/signup'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'
import { usePageMeta } from '../../lib/seo'
import { saveSession } from '../../lib/session'
import { ContactActions } from '../landing/ContactActions'
import { DoneStep } from './DoneStep'
import { GoogleIdentityButton } from './GoogleIdentityButton'
import { SignupError } from './SignupError'
import { initialSignupFlow, persistSignupFlow, resendSecondsRemaining, restoreSignupFlow, SAMPLE_CREDENTIALS_KEY, signupReducer, type SignupAction, type SignupFlow } from './signupFlow'
import { accountSchema, consentSchema, otpCodeOnlySchema, otpWithPasswordSchema, SIGNUP_PASSWORD_HINT, workspaceSchema, type AccountValues, type OtpValues, type WorkspaceValues } from './signupSchema'
import { useTurnstile } from './useTurnstile'
import './signup.css'

const HEADLINE = 'ทดลองใช้ ServicePro ฟรี 30 วัน'
type StepProps = { onError: (error: unknown) => void; onEdit: () => void; requestId: () => string }

export function SignupPage() {
  usePageMeta('/signup')
  const navigate = useNavigate()
  const config = useQuery({ queryKey: ['signup', 'config'], queryFn: getSignupConfig, retry: false })
  const [flow, setFlow] = useState<SignupFlow>(initialSignupFlow)
  const [restored, setRestored] = useState(false)
  const [error, setError] = useState<unknown>()
  const [disabled, setDisabled] = useState(false)
  const [result, setResult] = useState<SignupResult>()
  // [SECURITY] รหัสผ่านของขั้นแรก ใช้ส่งคู่กับ OTP ตอนยืนยันอีเมล — อยู่ใน state ของหน้านี้เท่านั้น ไม่ลง sessionStorage
  // (signupFlow เก็บเฉพาะฟิลด์ที่ allowlist) · โหลดหน้าใหม่ = หาย → ขั้น OTP ให้กรอกเอง
  const [password, setPassword] = useState<string>()
  const requestRef = useRef<string | undefined>(undefined)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const send = (action: SignupAction) => setFlow((current) => signupReducer(current, action))
  useEffect(() => {
    const draft = restoreSignupFlow()
    requestRef.current = draft.requestId
    setFlow(draft)
    setRestored(true)
  }, [])
  useEffect(() => { if (restored) persistSignupFlow(flow) }, [flow, restored])
  useEffect(() => { if (restored) headingRef.current?.focus() }, [flow.step, restored])

  function onEdit() {
    requestRef.current = crypto.randomUUID()
    send({ type: 'edited', requestId: requestRef.current })
    setError(undefined)
  }
  function requestId() {
    if (!requestRef.current) {
      requestRef.current = crypto.randomUUID()
      send({ type: 'edited', requestId: requestRef.current })
    }
    return requestRef.current
  }
  function onError(next: unknown) {
    setError(next)
    if (isApiError(next) && next.code === 'SIGNUP_TOKEN_INVALID') {
      requestRef.current = undefined
      setPassword(undefined)
      send({ type: 'restart' })
    }
    if (isApiError(next) && next.code === 'SIGNUP_DISABLED') setDisabled(true)
  }
  function verified(ticket: SignupTicket) {
    requestRef.current = undefined
    setPassword(undefined)
    setError(undefined)
    send({ type: 'verified', ...ticket })
  }
  function completed(next: SignupResult) {
    // [SECURITY] saveSession spreads its input into localStorage. Keep one-time technician passwords out.
    const { workspaceNo, sampleTechnicians, ...session } = next
    saveSession(session)
    send({ type: 'completed' })
    persistSignupFlow({ ...initialSignupFlow, step: 'done' })
    setError(undefined)
    setResult(next)
    if (window.matchMedia('(min-width: 1024px)').matches) {
      try {
        if (sampleTechnicians?.length) window.sessionStorage.setItem(SAMPLE_CREDENTIALS_KEY, JSON.stringify({ workspaceNo, technicians: sampleTechnicians }))
        else window.sessionStorage.removeItem(SAMPLE_CREDENTIALS_KEY)
      } catch {
        // Keep the credentials reviewable when the browser cannot store the desktop handoff.
        toast.error('เก็บรหัสช่างไม่ได้ กรุณาคัดลอกรหัสก่อนเข้าระบบงาน', { description: 'รหัสติดตาม (traceId): ไม่ใช่คำขอ API' })
        return
      }
      toast.success('เปิดอู่สำเร็จ')
      navigate('/jobs?welcome=1', { replace: true })
    }
  }

  if (config.data?.enabled === false || disabled) return (
    <section className="signup-closed landing-container">
      <p className="landing-eyebrow">ทดลองใช้ฟรี 30 วัน</p>
      <h1>{HEADLINE} เปิดรับสมัครเร็วๆ นี้</h1>
      <p className="landing-body">โปรแกรมสำหรับอู่บริการและอู่เซอร์วิส ทดลองใช้ 30 วันโดยไม่ต้องใส่บัตรเครดิต</p>
      <h2 className="landing-h2">สอบถามทีมงานระหว่างรอเปิดรับสมัคร</h2>
      <p className="landing-body">ติดต่อผ่าน LINE โทรศัพท์ หรือฝากข้อมูลเพื่อขอ Demo ได้</p>
      <SignupError error={error} />
      <ContactActions />
    </section>
  )

  const stepIndex = flow.step === 'account' ? 0 : flow.step === 'otp' ? 1 : 2
  return (
    <section className="signup-section" aria-label="สมัครทดลองใช้ ServicePro">
      <div className={`signup-container${flow.step === 'done' ? ' signup-container--done' : ''}`}>
        {flow.step !== 'done' ? <div className="signup-intro">
          <p className="landing-eyebrow"><ShieldCheck size={18} aria-hidden="true" /> เริ่มต้นใช้งานง่าย</p>
          <h1>{HEADLINE}</h1>
          <p>ไม่ต้องใส่บัตรเครดิต · ตั้งค่าเสร็จในไม่กี่นาที</p>
          <ul><li><Check size={18} aria-hidden="true" /> จัดการงานตั้งแต่รับรถจนส่งมอบ</li><li><Check size={18} aria-hidden="true" /> เชื่อมงานออฟฟิศกับแอปช่าง</li><li><Check size={18} aria-hidden="true" /> มีข้อมูลตัวอย่างให้เริ่มลองได้ทันที</li></ul>
        </div> : null}
        <div className="signup-card">
          {flow.step === 'done' && result ? <DoneStep result={result} /> : (
            <>
              <ol className="signup-progress" aria-label="ขั้นตอนสมัคร">
                {['บัญชี', 'ยืนยันอีเมล', 'ตั้งค่าอู่'].map((label, index) => <li key={label} aria-current={index === stepIndex ? 'step' : undefined}><span>{index < stepIndex ? <Check size={16} aria-hidden="true" /> : index + 1}</span>{label}</li>)}
              </ol>
              <h2 ref={headingRef} tabIndex={-1}>{flow.step === 'account' ? 'สร้างบัญชีเจ้าของอู่' : flow.step === 'otp' ? 'ยืนยันอีเมลของคุณ' : 'บอกเราเกี่ยวกับอู่ของคุณ'}</h2>
              <SignupError error={config.error ?? error} restartNotice={isApiError(error) && error.code === 'SIGNUP_TOKEN_INVALID'} />
              {config.isError ? <button className="signup-secondary" type="button" onClick={() => void config.refetch()}>โหลดการสมัครอีกครั้ง</button> : !config.data || !restored ? <p className="signup-hint" role="status"><LoaderCircle size={18} className="spin" aria-hidden="true" /> กำลังโหลดช่องทางสมัคร…</p> : flow.step === 'account' ? (
                <AccountStep config={config.data} email={flow.email} onError={onError} onEdit={onEdit} requestId={requestId} onStarted={(email, resendAfterSeconds, startedPassword) => { setError(undefined); setPassword(startedPassword); send({ type: 'emailStarted', email, resendAfterSeconds, now: Date.now() }) }} onVerified={verified} />
              ) : flow.step === 'otp' ? (
                <OtpStep flow={flow} password={password} onError={onError} onVerified={verified} onResend={(seconds) => send({ type: 'resendScheduled', resendAfterSeconds: seconds, now: Date.now() })} onBack={() => { requestRef.current = undefined; setPassword(undefined); setError(undefined); send({ type: 'changeEmail' }) }} />
              ) : flow.step === 'workspace' ? (
                <WorkspaceStep signupToken={flow.signupToken ?? ''} onError={onError} onEdit={onEdit} requestId={requestId} onCompleted={completed} />
              ) : null}
              <p className="signup-footer">มีบัญชีแล้ว? <Link to="/login" state={{ fromLanding: true }}>เข้าสู่ระบบ</Link></p>
            </>
          )}
        </div>
      </div>
    </section>
  )
}

function AccountStep({ config, email, onStarted, onVerified, onError, onEdit, requestId }: StepProps & { config: SignupConfig; email: string; onStarted: (email: string, seconds: number, password: string) => void; onVerified: (ticket: SignupTicket) => void }) {
  const [showPassword, setShowPassword] = useState(false)
  const form = useForm<AccountValues>({ resolver: zodResolver(accountSchema), defaultValues: { email, password: '', consent: false, website: '' } })
  const turnstile = useTurnstile(config.turnstileSiteKey ?? import.meta.env.VITE_TURNSTILE_SITE_KEY ?? '')
  const pendingRef = useRef(false)
  function failed(error: unknown) {
    onError(error)
    if (isApiError(error) && ['email', 'password', 'consent', 'website'].includes(error.field ?? '')) form.setError(error.field as keyof AccountValues, { message: error.messageTh }, { shouldFocus: true })
  }
  const start = useMutation({ mutationFn: startSignupEmail, onSuccess: (data, input) => { form.resetField('password'); onStarted(input.email, data.resendAfterSeconds, input.password) }, onError: failed, onSettled: () => { pendingRef.current = false; turnstile.reset() } })
  const google = useMutation({ mutationFn: signupWithGoogle, onSuccess: onVerified, onError: failed, onSettled: () => { pendingRef.current = false; turnstile.reset() } })
  const pending = start.isPending || google.isPending
  const botReason = turnstile.available && !turnstile.token ? 'กรุณายืนยันว่าคุณไม่ใช่บอทก่อนดำเนินการ' : undefined
  const reason = pending ? 'กำลังส่งข้อมูล กรุณารอสักครู่…' : botReason
  const consent = form.watch('consent')
  const googleEnabled = config.googleEnabled && Boolean(import.meta.env.VITE_GOOGLE_CLIENT_ID)
  const errors = form.formState.errors
  function googleCredential(idToken: string) {
    if (pendingRef.current || botReason) return
    if (!consentSchema.safeParse(form.getValues('consent')).success) {
      form.setError('consent', { message: 'กรุณายอมรับข้อกำหนดและนโยบายความเป็นส่วนตัว' }, { shouldFocus: true })
      return
    }
    pendingRef.current = true
    google.mutate({ idToken, turnstileToken: turnstile.token, website: form.getValues('website') })
  }
  return (
    <form className="signup-form" noValidate onChange={onEdit} onSubmit={form.handleSubmit((values) => {
      if (pendingRef.current || reason || !config.emailEnabled) return
      pendingRef.current = true
      start.mutate({ ...values, consent: true, requestId: requestId(), turnstileToken: turnstile.token })
    })}>
      {googleEnabled ? <GoogleIdentityButton onCredential={googleCredential} disabledReason={reason ?? (!consent ? 'ยอมรับข้อกำหนดและนโยบายความเป็นส่วนตัวด้านล่างเพื่อสมัครด้วย Google' : undefined)} /> : null}
      {googleEnabled && config.emailEnabled ? <div className="signup-divider">หรือ</div> : null}
      {config.emailEnabled ? <>
        <div className="form-field"><Label htmlFor="signup-email">อีเมล *</Label><Input id="signup-email" type="email" autoComplete="email" placeholder="owner@gmail.com" aria-invalid={Boolean(errors.email)} aria-describedby={errors.email ? 'signup-email-error' : undefined} {...form.register('email')} />{errors.email ? <p id="signup-email-error" className="field-error">{errors.email.message}</p> : null}</div>
        <div className="form-field"><Label htmlFor="signup-password">รหัสผ่าน *</Label><div className="signup-password"><Input id="signup-password" type={showPassword ? 'text' : 'password'} autoComplete="new-password" aria-invalid={Boolean(errors.password)} aria-describedby="signup-password-help" {...form.register('password')} /><button type="button" aria-label={showPassword ? 'ซ่อนรหัสผ่าน' : 'แสดงรหัสผ่าน'} onClick={() => setShowPassword((value) => !value)}>{showPassword ? <EyeOff size={20} aria-hidden="true" /> : <Eye size={20} aria-hidden="true" />}</button></div><p id="signup-password-help" className={errors.password ? 'field-error' : 'signup-hint'}>{errors.password?.message ?? 'อย่างน้อย 8 ตัวอักษร มีทั้งตัวอักษรและตัวเลข'}</p></div>
      </> : null}
      {config.emailEnabled || googleEnabled ? <>
        <div><label className="signup-check" htmlFor="signup-consent"><input id="signup-consent" type="checkbox" aria-invalid={Boolean(errors.consent)} aria-describedby={errors.consent ? 'signup-consent-error' : undefined} {...form.register('consent')} /><span>ยอมรับ <Link to="/terms" target="_blank" rel="noopener noreferrer">ข้อกำหนดการใช้งาน</Link> และ <Link to="/privacy" target="_blank" rel="noopener noreferrer">นโยบายความเป็นส่วนตัว</Link></span></label>{errors.consent ? <p id="signup-consent-error" className="field-error">{errors.consent.message}</p> : null}</div>
        {turnstile.available ? <div ref={turnstile.containerRef} className="signup-turnstile" /> : null}
        {turnstile.error ? <p className="field-error"><CircleAlert size={16} aria-hidden="true" /> โหลดการยืนยันบอทไม่สำเร็จ <button type="button" className="signup-text-button" onClick={turnstile.reset}>ลองใหม่</button><br />รหัสติดตาม (traceId): ไม่ใช่คำขอ API</p> : null}
        <div className="landing-honeypot" aria-hidden="true"><label htmlFor="signup-website">Website</label><input id="signup-website" tabIndex={-1} autoComplete="off" {...form.register('website')} /></div>
        {reason ? <p className="signup-hint" role="status">{reason}</p> : null}
        {config.emailEnabled ? <button className="landing-btn landing-btn--primary signup-submit" type="submit" disabled={Boolean(reason)}>{pending ? <><LoaderCircle size={18} className="spin" aria-hidden="true" /> กำลังส่งข้อมูล…</> : 'รับรหัสยืนยันทางอีเมล'}</button> : null}
      </> : <p className="signup-hint">ยังไม่มีช่องทางสมัครที่พร้อมใช้งาน กรุณา <Link to="/contact">ติดต่อทีมงาน</Link></p>}
    </form>
  )
}

function OtpStep({ flow, password, onError, onVerified, onResend, onBack }: { flow: SignupFlow; password?: string; onError: (error: unknown) => void; onVerified: (ticket: SignupTicket) => void; onResend: (seconds: number) => void; onBack: () => void }) {
  // ช่องรหัสผ่านโผล่เมื่อไม่มีรหัสผ่านของขั้นแรกในหน้า (โหลดหน้าใหม่ตรงขั้นนี้) หรือ server ตอบว่าไม่ตรง
  const [askPassword, setAskPassword] = useState(!password)
  const [showPassword, setShowPassword] = useState(false)
  const focusPasswordRef = useRef(false)
  const form = useForm<OtpValues>({ resolver: zodResolver(askPassword ? otpWithPasswordSchema : otpCodeOnlySchema), defaultValues: { code: '', password: '' } })
  const [now, setNow] = useState(Date.now())
  useEffect(() => { const interval = window.setInterval(() => setNow(Date.now()), 1000); return () => window.clearInterval(interval) }, [])
  // ช่องเพิ่งถูก render หลัง setAskPassword — โฟกัสหลัง commit ไม่งั้น setError({ shouldFocus }) หา ref ไม่เจอ
  useEffect(() => { if (askPassword && focusPasswordRef.current) { focusPasswordRef.current = false; form.setFocus('password') } }, [askPassword, form])
  function failed(error: unknown) {
    onError(error)
    if (isApiError(error) && (error.code === 'SIGNUP_PASSWORD_MISMATCH' || error.field === 'password')) {
      // รหัสผ่านที่ถืออยู่ใช้ไม่ได้แล้ว (อาจมีคนเริ่มสมัครอีเมลนี้ใหม่ทับ) — ให้กรอกเองหรือกด "เปลี่ยนอีเมล" เพื่อเริ่มใหม่
      form.setValue('password', '')
      form.setError('password', { message: error.messageTh })
      if (askPassword) form.setFocus('password')
      else { focusPasswordRef.current = true; setAskPassword(true) }
      return
    }
    if (isApiError(error) && (error.field === 'code' || error.code === 'SIGNUP_OTP_INVALID' || error.code === 'SIGNUP_OTP_EXPIRED' || error.code === 'SIGNUP_OTP_LOCKED')) form.setError('code', { message: error.messageTh }, { shouldFocus: true })
    if (isRateLimitedError(error) && error.retryAfterSeconds !== undefined) { setNow(Date.now()); onResend(error.retryAfterSeconds) }
  }
  const verify = useMutation({ mutationFn: (values: OtpValues) => verifySignupEmail(flow.email, values.code, values.password), onSuccess: onVerified, onError: failed })
  const resend = useMutation({ mutationFn: () => resendSignupEmail(flow.email), onSuccess: (data) => { setNow(Date.now()); onResend(data.resendAfterSeconds); form.clearErrors(); onError(undefined); toast.success('ส่งรหัสยืนยันไปที่อีเมลแล้ว') }, onError: failed })
  const pending = verify.isPending || resend.isPending
  const remaining = resendSecondsRemaining(flow, Math.max(now, Date.now()))
  const errors = form.formState.errors
  return (
    <form className="signup-form" noValidate onSubmit={form.handleSubmit((values) => { if (!pending) verify.mutate({ code: values.code, password: askPassword ? values.password : password ?? '' }) })}>
      <p className="signup-hint" role="status">ส่งรหัสยืนยันไปที่อีเมลแล้ว<br /><strong className="signup-email-address">{flow.email}</strong></p>
      <div className="form-field"><Label htmlFor="signup-code">รหัสยืนยัน 6 หลัก</Label><Input id="signup-code" className="signup-otp" inputMode="numeric" autoComplete="one-time-code" maxLength={6} aria-invalid={Boolean(errors.code)} aria-describedby={errors.code ? 'signup-code-error' : undefined} {...form.register('code')} />{errors.code ? <p id="signup-code-error" className="field-error">{errors.code.message}</p> : null}</div>
      {askPassword ? <div className="form-field"><Label htmlFor="signup-otp-password">รหัสผ่าน *</Label><div className="signup-password"><Input id="signup-otp-password" type={showPassword ? 'text' : 'password'} autoComplete="current-password" aria-invalid={Boolean(errors.password)} aria-describedby="signup-otp-password-help" {...form.register('password')} /><button type="button" aria-label={showPassword ? 'ซ่อนรหัสผ่าน' : 'แสดงรหัสผ่าน'} onClick={() => setShowPassword((value) => !value)}>{showPassword ? <EyeOff size={20} aria-hidden="true" /> : <Eye size={20} aria-hidden="true" />}</button></div><p id="signup-otp-password-help" className={errors.password ? 'field-error' : 'signup-hint'}>{errors.password?.message ?? SIGNUP_PASSWORD_HINT}</p></div> : null}
      {pending ? <p className="signup-hint" role="status">กำลัง{verify.isPending ? 'ตรวจสอบรหัส' : 'ส่งรหัสอีกครั้ง'} กรุณารอสักครู่…</p> : null}
      <button type="submit" className="landing-btn landing-btn--primary signup-submit" disabled={pending}>{verify.isPending ? 'กำลังยืนยันอีเมล…' : 'ยืนยันอีเมล'}</button>
      <button type="button" className="signup-secondary" disabled={pending || remaining > 0} onClick={() => resend.mutate()}>ส่งรหัสอีกครั้ง</button>
      {remaining > 0 ? <p className="signup-hint">ส่งรหัสอีกครั้งได้ในอีก {remaining} วินาที</p> : null}
      <button type="button" className="signup-text-button" disabled={pending} onClick={onBack}>เปลี่ยนอีเมล</button>
    </form>
  )
}

function WorkspaceStep({ signupToken, onError, onEdit, requestId, onCompleted }: StepProps & { signupToken: string; onCompleted: (result: SignupResult) => void }) {
  const provinces = useQuery({ queryKey: ['signup', 'provinces'], queryFn: getSignupProvinces, retry: false })
  const form = useForm<WorkspaceValues, unknown, z.output<typeof workspaceSchema>>({ resolver: zodResolver(workspaceSchema), defaultValues: { ownerName: '', garageName: '', phone: '', sampleData: true } })
  const create = useMutation({ mutationFn: createSignupWorkspace, onSuccess: onCompleted, onError: (error) => {
    onError(error)
    if (isApiError(error) && ['ownerName', 'garageName', 'phone', 'provinceId', 'size', 'sampleData'].includes(error.field ?? '')) form.setError(error.field as keyof WorkspaceValues, { message: error.messageTh }, { shouldFocus: true })
  } })
  const errors = form.formState.errors
  return (
    <form className="signup-form" noValidate onChange={onEdit} onSubmit={form.handleSubmit((values) => { if (!create.isPending) create.mutate({ ...values, requestId: requestId(), signupToken }) })}>
      <div className="form-field"><Label htmlFor="signup-owner">ชื่อ-นามสกุลของคุณ *</Label><Input id="signup-owner" autoComplete="name" maxLength={100} placeholder="เช่น สมชาย ใจดี" aria-invalid={Boolean(errors.ownerName)} aria-describedby="signup-owner-help" {...form.register('ownerName')} /><p id="signup-owner-help" className={errors.ownerName ? 'field-error' : 'signup-hint'}>{errors.ownerName?.message ?? 'แสดงบนใบเสนอราคา ใบเสร็จ และประวัติงาน'}</p></div>
      <div className="form-field"><Label htmlFor="signup-garage">ชื่ออู่ *</Label><Input id="signup-garage" autoComplete="organization" maxLength={100} placeholder="เช่น อู่ช่างเอก" aria-invalid={Boolean(errors.garageName)} aria-describedby={errors.garageName ? 'signup-garage-error' : undefined} {...form.register('garageName')} />{errors.garageName ? <p id="signup-garage-error" className="field-error">{errors.garageName.message}</p> : null}</div>
      <div className="form-field"><Label htmlFor="signup-phone">เบอร์โทร *</Label><Input id="signup-phone" type="tel" autoComplete="tel" placeholder="08x-xxx-xxxx หรือ 02-xxx-xxxx" aria-invalid={Boolean(errors.phone)} aria-describedby={errors.phone ? 'signup-phone-error' : undefined} {...form.register('phone')} />{errors.phone ? <p id="signup-phone-error" className="field-error">{errors.phone.message}</p> : null}</div>
      {provinces.data?.length ? <div className="form-field"><Label htmlFor="signup-province">จังหวัด <span className="signup-hint">(ไม่บังคับ)</span></Label><Select id="signup-province" aria-invalid={Boolean(errors.provinceId)} {...form.register('provinceId', { setValueAs: (value: string) => value ? Number(value) : undefined })}><option value="">เลือกจังหวัด</option>{provinces.data.map((province) => <option value={province.id} key={province.id}>{province.name}</option>)}</Select>{errors.provinceId ? <p className="field-error">{errors.provinceId.message}</p> : null}</div> : null}
      {provinces.isError ? <><SignupError error={provinces.error} /><p className="signup-hint">เลือกจังหวัดภายหลังได้ สมัครต่อได้เลย <button type="button" className="signup-text-button" onClick={() => void provinces.refetch()}>โหลดจังหวัดอีกครั้ง</button></p></> : null}
      <fieldset className="signup-size"><legend>ขนาดอู่ <span className="signup-hint">(ไม่บังคับ)</span></legend><Controller name="size" control={form.control} render={({ field }) => <div>{(['1-3', '4-10', '10+'] as const).map((size) => <label key={size}><input type="radio" name={field.name} ref={field.ref} value={size} checked={field.value === size} onBlur={field.onBlur} onChange={() => field.onChange(size)} /><span>{size} ช่าง</span></label>)}</div>} />{errors.size ? <p className="field-error">{errors.size.message}</p> : null}</fieldset>
      <label className="signup-check" htmlFor="signup-sample"><input id="signup-sample" type="checkbox" {...form.register('sampleData')} /><span>ใส่ข้อมูลตัวอย่างให้ (แนะนำ)</span></label>
      {errors.sampleData ? <p className="field-error">{errors.sampleData.message}</p> : null}
      {create.isPending ? <p className="signup-hint" role="status"><LoaderCircle size={18} className="spin" aria-hidden="true" /> กำลังเปิดอู่ของคุณ…</p> : null}
      <button className="landing-btn landing-btn--primary signup-submit" type="submit" disabled={create.isPending}>{create.isPending ? 'กำลังเปิดอู่ของคุณ…' : 'เริ่มใช้งาน ServicePro'}</button>
    </form>
  )
}
