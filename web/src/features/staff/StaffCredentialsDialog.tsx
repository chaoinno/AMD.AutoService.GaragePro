import { Copy, KeyRound, TriangleAlert } from 'lucide-react'
import { useEffect, useState } from 'react'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { copyText } from '../plan/copyText'
import { MobileAppInstall } from '../plan/MobileAppInstall'
import '../plan/plan.css'

export type StaffCredentials = {
  staffName: string
  userName: string
  initialPassword: string
}

/**
 * รหัสเข้าแอปของพนักงานที่เพิ่งสร้าง (อู่ที่สมัครเอง) — server คืนรหัสผ่านครั้งเดียวในคำตอบของการสร้าง
 * [SECURITY] ไม่เก็บลง storage/ไม่ log · ปิดหน้าต่างได้ต่อเมื่อยืนยันว่าจดแล้ว เพราะเปิดดูซ้ำไม่ได้อีก
 */
export function StaffCredentialsDialog({ credentials, onClose }: { credentials: StaffCredentials | null; onClose: () => void }) {
  const [confirmed, setConfirmed] = useState(false)
  const [attemptedClose, setAttemptedClose] = useState(false)
  useEffect(() => {
    setConfirmed(false)
    setAttemptedClose(false)
  }, [credentials])

  // ปุ่ม X / Esc / คลิกนอกหน้าต่างผ่านทางนี้ทั้งหมด — ยังไม่ยืนยันก็ยังไม่ปิด แต่ชี้ไปที่ช่องยืนยันแทน
  const requestClose = () => {
    if (!confirmed) {
      setAttemptedClose(true)
      return
    }
    onClose()
  }

  const closeReason = 'ติ๊ก "คัดลอกหรือจดรหัสผ่านแล้ว" ก่อนปิด รหัสผ่านนี้เปิดดูซ้ำไม่ได้'

  return (
    <ConfirmModal
      open={credentials !== null}
      title="รหัสเข้าแอปของพนักงานใหม่"
      description={credentials ? `ส่งให้ ${credentials.staffName} ใช้เข้าแอป ServicePro บนมือถือ` : undefined}
      onClose={requestClose}
      size="medium"
      footer={
        <div className="staff-credentials__footer">
          {!confirmed ? <span className="staff-credentials__reason">{closeReason}</span> : null}
          <Button disabled={!confirmed} title={!confirmed ? closeReason : undefined} onClick={onClose}>ปิด</Button>
        </div>
      }
    >
      {credentials ? (
        <div className="staff-credentials">
          <Alert variant="warning">
            <TriangleAlert aria-hidden="true" />
            <div>
              <AlertTitle>รหัสผ่านแสดงครั้งเดียว คัดลอกหรือจดไว้ให้ช่างก่อนปิด</AlertTitle>
              <AlertDescription>ถ้าลืม ผู้จัดการตั้งรหัสผ่านใหม่ได้จากหน้าแก้ไขพนักงาน</AlertDescription>
            </div>
          </Alert>

          <dl className="staff-credentials__fields">
            <div>
              <dt>ชื่อผู้ใช้</dt>
              <dd>
                <code>{credentials.userName}</code>
                <Button variant="outline" size="sm" onClick={() => void copyText(credentials.userName, 'ชื่อผู้ใช้')}>
                  <Copy aria-hidden="true" /> คัดลอก
                </Button>
              </dd>
            </div>
            <div>
              <dt>รหัสผ่าน</dt>
              <dd>
                <code>{credentials.initialPassword}</code>
                <Button variant="outline" size="sm" onClick={() => void copyText(credentials.initialPassword, 'รหัสผ่าน')}>
                  <Copy aria-hidden="true" /> คัดลอก
                </Button>
              </dd>
            </div>
          </dl>

          <MobileAppInstall />

          <label className={`staff-credentials__confirm${attemptedClose && !confirmed ? ' staff-credentials__confirm--attention' : ''}`}>
            <input
              type="checkbox"
              checked={confirmed}
              onChange={(event) => setConfirmed(event.target.checked)}
            />
            <KeyRound aria-hidden="true" />
            <span>คัดลอกหรือจดรหัสผ่านแล้ว</span>
          </label>
          {attemptedClose && !confirmed ? (
            <p className="field-error" role="alert">{closeReason}</p>
          ) : null}
        </div>
      ) : null}
    </ConfirmModal>
  )
}
