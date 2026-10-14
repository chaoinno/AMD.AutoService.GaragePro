import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { CircleAlert, Eye, EyeOff, LoaderCircle, LockKeyhole, UserRound } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link, useNavigate } from 'react-router'
import { z } from 'zod'
import { login, loginWithGoogle } from '../../api/auth'
import { isApiError, isRateLimitedError } from '../../api/client'
import type { LoginResult } from '../../api/types'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { clearStoredSession, saveSession } from '../../lib/session'
import { clearLoginNotice, peekLoginNotice, type LoginNotice } from '../../lib/loginNotice'
import { GoogleIdentityButton } from '../signup/GoogleIdentityButton'

const loginSchema = z.object({
  userName: z.string().trim().min(1, 'กรุณากรอกอีเมลหรือรหัสพนักงาน'),
  password: z.string().min(1, 'กรุณากรอกรหัสผ่าน'),
})

type LoginForm = z.infer<typeof loginSchema>

/**
 * ฟอร์มเข้าสู่ระบบ — ตรรกะ auth (ล้างเซสชันเก่า → login → บันทึก) อยู่ที่นี่ที่เดียว
 * ส่วน LoginDialog แค่ห่อเป็น modal บนหน้าแรก
 */
export function LoginForm({ onPendingChange }: { onPendingChange?: (pending: boolean) => void }) {
  const navigate = useNavigate()
  const [showPassword, setShowPassword] = useState(false)
  const [notice, setNotice] = useState<LoginNotice | null>(null)
  const noticeRead = useRef(false)
  const pendingRef = useRef(false)
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<LoginForm>({ resolver: zodResolver(loginSchema) })

  function success(result: LoginResult) {
    saveSession(result)
    navigate('/jobs', { replace: true })
  }
  function failed(error: unknown) {
    if (isApiError(error) && (error.field === 'userName' || error.field === 'password')) setError(error.field, { message: error.messageTh }, { shouldFocus: true })
  }
  const mutation = useMutation({
    mutationFn: ({ userName, password }: LoginForm) => {
      clearStoredSession()
      return login(userName.trim(), password)
    },
    onSuccess: success,
    onError: failed,
    onSettled: () => { pendingRef.current = false },
  })
  const google = useMutation({
    mutationFn: (idToken: string) => { clearStoredSession(); return loginWithGoogle(idToken) },
    onSuccess: success,
    onError: failed,
    onSettled: () => { pendingRef.current = false },
  })
  const pending = mutation.isPending || google.isPending
  const error = mutation.error ?? google.error

  useEffect(() => {
    onPendingChange?.(pending)
  }, [pending, onPendingChange])
  useEffect(() => {
    // Peek (not take): a 401 mounts this form twice — SPA redirect, then the hard reload — and the
    // second mount is the one the user sees. The notice is cleared when the user submits (below).
    if (noticeRead.current) return
    noticeRead.current = true
    setNotice(peekLoginNotice())
  }, [])

  return (
    <>
      {notice ? <LoginMessage notice={notice} className="login-form-notice" /> : null}
      <form className="auth-form" noValidate onSubmit={handleSubmit((values) => {
        if (pendingRef.current) return
        pendingRef.current = true
        setNotice(null)
        clearLoginNotice()
        google.reset()
        mutation.mutate(values)
      })}>
        <GoogleIdentityButton disabledReason={pending ? 'กำลังเข้าสู่ระบบ กรุณารอสักครู่…' : undefined} onCredential={(idToken) => {
          if (pendingRef.current) return
          pendingRef.current = true
          setNotice(null)
          clearLoginNotice()
          mutation.reset()
          google.mutate(idToken)
        }} />
        <div className="form-field">
          <Label htmlFor="userName">อีเมลหรือรหัสพนักงาน</Label>
          <div className="input-with-icon">
            <UserRound aria-hidden="true" />
            <Input
              id="userName"
              autoComplete="username"
              placeholder="เช่น owner@gmail.com หรือ SP1042.T01"
              aria-invalid={Boolean(errors.userName)}
              data-dialog-autofocus
              {...register('userName')}
            />
          </div>
          {errors.userName ? <p className="field-error">{errors.userName.message}</p> : null}
        </div>

        <div className="form-field">
          <Label htmlFor="password">รหัสผ่าน</Label>
          <div className="input-with-icon input-with-action">
            <LockKeyhole aria-hidden="true" />
            <Input
              id="password"
              type={showPassword ? 'text' : 'password'}
              autoComplete="current-password"
              placeholder="กรอกรหัสผ่าน"
              aria-invalid={Boolean(errors.password)}
              {...register('password')}
            />
            <Button
              type="button"
              variant="ghost"
              size="icon"
              className="input-action"
              aria-label={showPassword ? 'ซ่อนรหัสผ่าน' : 'แสดงรหัสผ่าน'}
              onClick={() => setShowPassword((visible) => !visible)}
            >
              {showPassword ? <EyeOff aria-hidden="true" /> : <Eye aria-hidden="true" />}
            </Button>
          </div>
          {errors.password ? <p className="field-error">{errors.password.message}</p> : null}
        </div>

        {error ? <LoginMessage notice={isApiError(error) ? error : { code: 'UNKNOWN', messageTh: 'เข้าสู่ระบบไม่สำเร็จ' }} retryAfterSeconds={isRateLimitedError(error) ? error.retryAfterSeconds : undefined} /> : null}

        <Button className="auth-submit" size="lg" type="submit" disabled={pending}>
          {pending ? (
            <>
              <LoaderCircle className="spin" aria-hidden="true" /> กำลังเข้าสู่ระบบ
            </>
          ) : (
            <>
              <LockKeyhole aria-hidden="true" /> เข้าสู่ระบบ
            </>
          )}
        </Button>
      </form>
      <p className="login-signup-link"><Link to="/signup">ยังไม่มีบัญชี? ทดลองใช้ฟรี 30 วัน</Link></p>
      <p className="login-dialog__notice">
        ระบบนี้เป็นระบบภายในของอู่ การใช้งานทั้งหมดจะถูกบันทึกในประวัติกิจกรรม
      </p>
    </>
  )
}

function LoginMessage({ notice, className, retryAfterSeconds }: { notice: LoginNotice; className?: string; retryAfterSeconds?: number }) {
  const expired = notice.code === 'TRIAL_EXPIRED'
  return (
    <Alert variant={expired ? 'warning' : 'destructive'} className={className}>
      <CircleAlert aria-hidden="true" />
      <div>
        <AlertTitle>{notice.messageTh}</AlertTitle>
        <AlertDescription>
          {expired ? <p>ติดต่อ <a href="https://line.me/R/ti/p/@garagepro" target="_blank" rel="noopener noreferrer">LINE @garagepro</a> เพื่อสอบถามแพ็กเกจ หรือ <Link to="/signup">สมัครทดลองใช้</Link></p> : <p>ตรวจสอบข้อมูลแล้วลองเข้าสู่ระบบอีกครั้ง</p>}
          {retryAfterSeconds !== undefined ? <p>ลองใหม่ได้ในอีก {retryAfterSeconds} วินาที</p> : null}
          <p className="trace-id">รหัสติดตาม (traceId): {notice.traceId ?? 'ไม่พบรหัสติดตาม'}</p>
        </AlertDescription>
      </div>
    </Alert>
  )
}
