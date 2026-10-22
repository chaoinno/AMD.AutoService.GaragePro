import { useQuery } from '@tanstack/react-query'
import {
  Ban,
  Check,
  CircleAlert,
  CircleCheck,
  CircleHelp,
  CircleX,
  Copy,
  ExternalLink,
  FileText,
  Hourglass,
  Loader2,
  type LucideIcon,
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { isApiError } from '../../api/client'
import { downloadPackageSlip, type PackagePayment } from '../../api/package'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Badge } from '../../components/ui/badge'
import { Button, type ButtonProps } from '../../components/ui/button'
import { copyText } from './copyText'
import { formatFileSize, isImageSlipType, paymentStatusMeta, type PaymentStatusMeta } from './packageUx'

export const PAYMENT_STATUS_ICONS: Record<PaymentStatusMeta['status'], LucideIcon> = {
  pending: Hourglass,
  approved: CircleCheck,
  rejected: CircleX,
  cancelled: Ban,
  unknown: CircleHelp,
}

/** [UI] สถานะรายการแจ้งชำระ: สี + ไอคอน + ข้อความเสมอ */
export function PackagePaymentStatusChip({ status }: { status: string | undefined }) {
  const meta = paymentStatusMeta(status)
  const Icon = PAYMENT_STATUS_ICONS[meta.status]
  return (
    <Badge className={`status-chip package-status package-status--${meta.status}`}>
      <Icon className="package-status__icon" aria-hidden="true" />
      <span>{meta.label}</span>
    </Badge>
  )
}

/**
 * ปุ่มคัดลอก: toast + เปลี่ยนเป็น "คัดลอกแล้ว" ชั่วครู่ (เห็นผลตรงจุดที่กด ไม่ต้องมองหา toast)
 * `label` ใช้ในข้อความ toast เช่น "คัดลอกเลขที่บัญชีแล้ว"
 */
export function CopyButton({
  value,
  label,
  children = 'คัดลอก',
  ...props
}: { value: string; label: string; children?: string } & Omit<ButtonProps, 'onClick' | 'children' | 'value'>) {
  const [copied, setCopied] = useState(false)
  const timer = useRef<number | undefined>(undefined)
  useEffect(() => () => window.clearTimeout(timer.current), [])
  return (
    <Button
      variant="outline"
      size="sm"
      {...props}
      className={`package-copy${copied ? ' package-copy--done' : ''} ${props.className ?? ''}`.trim()}
      onClick={() => {
        void copyText(value, label).then((ok) => {
          if (!ok) return
          setCopied(true)
          window.clearTimeout(timer.current)
          timer.current = window.setTimeout(() => setCopied(false), 2000)
        })
      }}
    >
      {copied ? <Check aria-hidden="true" /> : <Copy aria-hidden="true" />}
      <span aria-live="polite">{copied ? 'คัดลอกแล้ว' : children}</span>
      {/* ชื่อปุ่มสำหรับโปรแกรมอ่านหน้าจอบอกว่าคัดลอกอะไร (ปุ่มคัดลอกมีหลายปุ่มบนหน้า) */}
      <span className="sr-only">{label}</span>
    </Button>
  )
}

export const PACKAGE_SLIP_QUERY_KEY = 'package-slip'

/**
 * โหลดสลิป (เฉพาะผู้จัดการ): GET .../slip ต้องมี Bearer token จึงดาวน์โหลดเป็น blob แล้วใช้ object URL
 * (ห้ามใส่ path ตรงใน <img src>) · key เดียวกันระหว่างรูปย่อกับหน้าต่างดูสลิป เปิดหน้าต่างแล้วไม่โหลดซ้ำ
 * คืน URL เมื่อ component เลิกใช้
 */
export function useSlipBlob(payment: PackagePayment, enabled = true) {
  const query = useQuery({
    queryKey: [PACKAGE_SLIP_QUERY_KEY, payment.id],
    queryFn: () => downloadPackageSlip(payment.id),
    enabled: enabled && Boolean(payment.slip),
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
  return { query, url, contentType, isImage: isImageSlipType(contentType) }
}

/** รูปย่อสลิปที่กดเปิดหน้าต่างดูสลิปได้ · PDF/โหลดไม่สำเร็จแสดงเป็นไอคอนไฟล์ (ข้อความอยู่ใน aria-label ของปุ่ม) */
export function PackageSlipThumb({ payment, onOpen }: { payment: PackagePayment; onOpen: () => void }) {
  const { query, url, isImage } = useSlipBlob(payment)
  const label = `ดูสลิปรายการ ${payment.shortId}${payment.slip ? ` (${formatFileSize(payment.slip.sizeBytes)})` : ''}`
  return (
    <button type="button" className="package-slip-thumb" onClick={onOpen} aria-label={label} title={label}>
      {url && isImage ? (
        <img src={url} alt="" />
      ) : query.isPending ? (
        <Loader2 className="spin" aria-hidden="true" />
      ) : query.isError ? (
        <CircleAlert aria-hidden="true" />
      ) : (
        <span className="package-slip-thumb__file"><FileText aria-hidden="true" /> PDF</span>
      )}
    </button>
  )
}

/** หน้าต่างดูสลิปของรายการแจ้งชำระ (เฉพาะผู้จัดการ) */
export function PackageSlipDialog({ payment, onClose }: { payment: PackagePayment; onClose: () => void }) {
  const { query, url, isImage } = useSlipBlob(payment)

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
