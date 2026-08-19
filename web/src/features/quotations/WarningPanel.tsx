import { CheckCircle2, CircleAlert, LoaderCircle, TriangleAlert } from 'lucide-react'
import { isApiError } from '../../api/client'
import type { QuotationValidation } from '../../api/types'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { getIssueMessage } from '../../lib/format'

type WarningPanelProps = {
  validation?: QuotationValidation
  pending: boolean
  error: unknown
  onRetry: () => void
}

export function WarningPanel({ validation, pending, error, onRetry }: WarningPanelProps) {
  if (pending) {
    return (
      <Alert className="validation-panel validation-panel--loading" aria-live="polite">
        <LoaderCircle className="spin" aria-hidden="true" />
        <div>
          <AlertTitle>กำลังตรวจสอบความพร้อม</AlertTitle>
          <AlertDescription>ระบบกำลังตรวจรายการ ราคา และข้อมูลที่จำเป็น</AlertDescription>
        </div>
      </Alert>
    )
  }

  if (error) {
    return (
      <Alert variant="destructive" className="validation-panel validation-panel--error">
        <CircleAlert aria-hidden="true" />
        <div>
          <AlertTitle>{isApiError(error) ? error.messageTh : 'ตรวจสอบใบเสนอราคาไม่สำเร็จ'}</AlertTitle>
          <AlertDescription>
            <p>ยังสรุปความพร้อมสำหรับส่งไม่ได้ กรุณาลองตรวจสอบใหม่</p>
            <p className="trace-id">รหัสติดตาม (traceId): {isApiError(error) ? error.traceId : 'ไม่พบรหัสติดตาม'}</p>
            <Button variant="link" onClick={onRetry}>ลองตรวจสอบใหม่</Button>
          </AlertDescription>
        </div>
      </Alert>
    )
  }

  if (!validation) return null

  if (!validation.errors.length && !validation.warnings.length) {
    return (
      <Alert variant="success" className="validation-panel validation-panel--success">
        <CheckCircle2 aria-hidden="true" />
        <div>
          <AlertTitle>พร้อมส่งให้ลูกค้าอนุมัติ</AlertTitle>
          <AlertDescription>ตรวจสอบรายการ ราคา และข้อมูลที่จำเป็นครบถ้วนแล้ว</AlertDescription>
        </div>
      </Alert>
    )
  }

  return (
    <section className="validation-stack" aria-live="polite">
      {validation.errors.length ? (
        <Alert variant="destructive" className="validation-panel validation-panel--error">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>ต้องแก้ไขก่อนส่ง</AlertTitle>
            <AlertDescription>
              <ul>
                {validation.errors.map((issue, index) => (
                  <li key={`${getIssueMessage(issue)}-${index}`}>{getIssueMessage(issue)}</li>
                ))}
              </ul>
            </AlertDescription>
          </div>
        </Alert>
      ) : null}
      {validation.warnings.length ? (
        <Alert variant="warning" className="validation-panel validation-panel--warning">
          <TriangleAlert aria-hidden="true" />
          <div>
            <AlertTitle>คำเตือน</AlertTitle>
            <AlertDescription>
              <ul>
                {validation.warnings.map((issue, index) => (
                  <li key={`${getIssueMessage(issue)}-${index}`}>{getIssueMessage(issue)}</li>
                ))}
              </ul>
            </AlertDescription>
          </div>
        </Alert>
      ) : null}
    </section>
  )
}
