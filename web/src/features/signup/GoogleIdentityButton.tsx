import { CircleAlert } from 'lucide-react'
import { useGoogleIdentity } from './useGoogleIdentity'
import './signup.css'

export function GoogleIdentityButton({ onCredential, disabledReason }: { onCredential: (idToken: string) => void; disabledReason?: string }) {
  const google = useGoogleIdentity(onCredential)
  if (!google.available) return null
  return (
    <div className="signup-google">
      <div ref={google.buttonRef} inert={Boolean(disabledReason)} aria-disabled={Boolean(disabledReason)} />
      {disabledReason ? <p className="signup-hint" role="status">{disabledReason}</p> : !google.ready && !google.error ? <p className="signup-hint" role="status">กำลังโหลดปุ่ม Google…</p> : null}
      {google.error ? <p className="field-error"><CircleAlert size={16} aria-hidden="true" /> โหลดปุ่ม Google ไม่สำเร็จ <button type="button" className="signup-text-button" onClick={google.retry}>ลองใหม่</button><br />รหัสติดตาม (traceId): ไม่ใช่คำขอ API</p> : null}
    </div>
  )
}
