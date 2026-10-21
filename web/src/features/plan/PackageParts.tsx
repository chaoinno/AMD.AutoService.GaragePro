import { useQuery } from '@tanstack/react-query'
import { Ban, CircleAlert, CircleCheck, CircleHelp, CircleX, ExternalLink, FileText, Hourglass, Loader2, type LucideIcon } from 'lucide-react'
import { useEffect, useState } from 'react'
import { isApiError } from '../../api/client'
import { downloadPackageSlip, type PackagePayment } from '../../api/package'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { formatFileSize, isImageSlipType, paymentStatusMeta, type PaymentStatusMeta } from './packageUx'

const STATUS_ICONS: Record<PaymentStatusMeta['status'], LucideIcon> = {
  pending: Hourglass,
  approved: CircleCheck,
  rejected: CircleX,
  cancelled: Ban,
  unknown: CircleHelp,
}

/** [UI] สถานะรายการแจ้งชำระ: สี + ไอคอน + ข้อความเสมอ */
export function PackagePaymentStatusChip({ status }: { status: string | undefined }) {
  const meta = paymentStatusMeta(status)
  const Icon = STATUS_ICONS[meta.status]
  return (
    <Badge className={`status-chip package-status package-status--${meta.status}`}>
      <Icon className="package-status__icon" aria-hidden="true" />
      <span>{meta.label}</span>
    </Badge>
  )
}

export const PACKAGE_SLIP_QUERY_KEY = 'package-slip'

/**
 * ดูสลิปของรายการแจ้งชำระ (เฉพาะผู้จัดการ): GET .../slip ต้องมี Bearer token จึงดาวน์โหลดเป็น blob แล้วใช้ object URL
 * (ห้ามใส่ path ตรงใน <img src>) · คืน URL เมื่อปิดหน้าต่าง
 */
export function PackageSlipDialog({ payment, onClose }: { payment: PackagePayment; onClose: () => void }) {
  const query = useQuery({
    queryKey: [PACKAGE_SLIP_QUERY_KEY, payment.id],
    queryFn: () => downloadPackageSlip(payment.id),
    staleTime: 5 * 60_000,
    retry: false,
  })
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!query.data) {
      setUrl(null)
      return
    }
    const next = URL.createObjectURL(query.data)
    setUrl(next)
    return () => URL.revokeObjectURL(next)
  }, [query.data])

  const contentType = query.data?.type || payment.slip?.contentType || ''
  const isImage = isImageSlipType(contentType)

  return (
    <ConfirmModal
      open
      size="large"
      title={`สลิปรายการ ${payment.shortId}`}
      description={payment.slip ? `${payment.slip.contentType} · ${formatFileSize(payment.slip.sizeBytes)}` : 'ไฟล์สลิปที่แนบตอนแจ้งชำระ'}
      onClose={onClose}
      footer={
        <div className="package-dialog-footer">
          {url ? (
            <a className="ui-button ui-button--outline ui-button--size-default" href={url} target="_blank" rel="noopener noreferrer">
              เปิดในแท็บใหม่ <ExternalLink aria-hidden="true" />
            </a>
          ) : null}
          <Button variant="ghost" onClick={onClose}>ปิด</Button>
        </div>
      }
    >
      {query.isPending ? (
        <p className="package-slip-viewer__state" role="status">
          <Loader2 className="spin" aria-hidden="true" /> กำลังโหลดสลิป…
        </p>
      ) : query.isError ? (
        <Alert variant="destructive">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>{isApiError(query.error) ? query.error.messageTh : 'โหลดสลิปไม่สำเร็จ'}</AlertTitle>
            <AlertDescription>
              รหัสติดตาม (traceId): {isApiError(query.error) ? query.error.traceId : 'ไม่พบรหัสติดตาม'}{' '}
              <Button variant="link" size="sm" onClick={() => void query.refetch()}>ลองใหม่</Button>
            </AlertDescription>
          </div>
        </Alert>
      ) : url && isImage ? (
        <div className="package-slip-viewer">
          <img src={url} alt={`สลิปการโอนของรายการ ${payment.shortId}`} />
        </div>
      ) : url ? (
        <div className="package-slip-viewer package-slip-viewer--file">
          <FileText aria-hidden="true" />
          <span>ไฟล์ PDF แสดงในหน้าต่างนี้ไม่ได้ กด "เปิดในแท็บใหม่" เพื่อดูสลิป</span>
        </div>
      ) : null}
    </ConfirmModal>
  )
}
