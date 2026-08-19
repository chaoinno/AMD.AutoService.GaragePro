import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation } from '@tanstack/react-query'
import { CircleAlert, Eye, EyeOff, LoaderCircle, LockKeyhole, ShieldCheck, UserRound } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import { z } from 'zod'
import { login } from '../../api/auth'
import { isApiError } from '../../api/client'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { clearStoredSession, savePreShiftSession } from '../../lib/session'

const loginSchema = z.object({
  userName: z.string().trim().min(1, 'กรุณากรอกรหัสพนักงาน'),
  password: z.string().min(1, 'กรุณากรอกรหัสผ่าน'),
})

type LoginForm = z.infer<typeof loginSchema>

export function LoginPage() {
  const navigate = useNavigate()
  const [showPassword, setShowPassword] = useState(false)
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginForm>({ resolver: zodResolver(loginSchema) })

  const mutation = useMutation({
    mutationFn: ({ userName, password }: LoginForm) => {
      clearStoredSession()
      return login(userName.trim(), password)
    },
    onSuccess: (result) => {
      savePreShiftSession(result)
      navigate('/branch', { replace: true })
    },
  })

  return (
    <main className="auth-page auth-page--login">
      <div className="auth-page__ambient auth-page__ambient--one" />
      <div className="auth-page__ambient auth-page__ambient--two" />
      <section className="login-intro" aria-label="ข้อมูลระบบ">
        <div className="auth-brand">
          <span className="auth-brand__mark">GP</span>
          <span>
            <strong>GaragePro</strong>
            <small>ระบบงานบริการ</small>
          </span>
        </div>
        <div className="login-intro__copy">
          <span className="login-intro__eyebrow">
            <ShieldCheck aria-hidden="true" /> ระบบภายในสำหรับพนักงาน
          </span>
          <h1>จัดการงานบริการ<br />ให้ทุกกะทำงานต่อกันได้</h1>
          <p>เข้าสู่ระบบด้วยบัญชีพนักงานเดิม จากนั้นเลือกสาขาและกะที่กำลังปฏิบัติงาน</p>
        </div>
      </section>

      <Card className="login-card">
        <CardHeader>
          <div className="login-card__icon" aria-hidden="true">
            <LockKeyhole />
          </div>
          <CardTitle>เข้าสู่ระบบ</CardTitle>
          <CardDescription>ใช้รหัสพนักงานและรหัสผ่านของ GaragePro</CardDescription>
        </CardHeader>
        <CardContent>
          <form className="auth-form" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
            <div className="form-field">
              <Label htmlFor="userName">รหัสพนักงาน</Label>
              <div className="input-with-icon">
                <UserRound aria-hidden="true" />
                <Input
                  id="userName"
                  autoComplete="username"
                  placeholder="กรอกรหัสพนักงาน"
                  aria-invalid={Boolean(errors.userName)}
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

            {mutation.isError ? (
              <Alert variant="destructive">
                <CircleAlert aria-hidden="true" />
                <div>
                  <AlertTitle>
                    {isApiError(mutation.error)
                      ? mutation.error.messageTh
                      : 'เข้าสู่ระบบไม่สำเร็จ'}
                  </AlertTitle>
                  <AlertDescription>
                    <p>ตรวจสอบข้อมูลแล้วลองเข้าสู่ระบบอีกครั้ง</p>
                    <p className="trace-id">
                      รหัสติดตาม (traceId):{' '}
                      {isApiError(mutation.error) ? mutation.error.traceId : 'ไม่พบรหัสติดตาม'}
                    </p>
                  </AlertDescription>
                </div>
              </Alert>
            ) : null}

            <Button className="auth-submit" size="lg" type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? (
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
          <p className="login-card__notice">
            ระบบนี้เป็นระบบภายในของอู่ การใช้งานทั้งหมดจะถูกบันทึกในประวัติกิจกรรม
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
