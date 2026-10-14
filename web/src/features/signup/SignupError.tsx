import { CircleAlert } from 'lucide-react'
import { Link } from 'react-router'
import { isApiError, isRateLimitedError } from '../../api/client'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'

export function SignupError({ error, restartNotice = false }: { error: unknown; restartNotice?: boolean }) {
  if (!error) return null
  const apiError = isApiError(error) ? error : undefined
  return (
    <Alert variant={restartNotice ? 'warning' : 'destructive'}>
      <CircleAlert aria-hidden="true" />
      <div>
        <AlertTitle>{apiError?.messageTh ?? 'ดำเนินการไม่สำเร็จ กรุณาลองใหม่'}</AlertTitle>
        <AlertDescription>
          {restartNotice ? <p>กรุณายืนยันบัญชีอีกครั้งเพื่อเริ่มสมัครใหม่</p> : <p>ตรวจสอบข้อมูลแล้วลองอีกครั้ง หรือติดต่อ <a href="https://line.me/R/ti/p/@garagepro" target="_blank" rel="noopener noreferrer">LINE @garagepro</a></p>}
          {isRateLimitedError(error) ? <p>{error.retryAfterSeconds !== undefined ? `ลองใหม่ได้ในอีก ${error.retryAfterSeconds} วินาที` : 'กรุณารอสักครู่แล้วลองใหม่'}</p> : null}
          {apiError?.code === 'SIGNUP_ACCOUNT_EXISTS' ? <p><Link to="/login" state={{ fromLanding: true }}>เข้าสู่ระบบแทน</Link></p> : null}
          <p className="trace-id">รหัสติดตาม (traceId): {apiError?.traceId ?? 'ไม่พบรหัสติดตาม'}</p>
        </AlertDescription>
      </div>
    </Alert>
  )
}
