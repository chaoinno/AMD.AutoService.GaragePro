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
import {
  formatFileSize,
  isImageSlipType,
  paymentRefText,
  paymentStatusMeta,
  slipSource,
  type PaymentStatusMeta,
  type SlipSource,
} from './packageUx'

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
 * สลิปของรายการแจ้งชำระ 2 แบบ (docs/17 §5.1 · §8.7.1)
 * · ไฟล์ของเรา (selfserve · เฉพาะผู้จัดการ): GET .../slip ต้องมี Bearer token จึงดาวน์โหลดเป็น blob แล้วใช้ object URL
 *   (ห้ามใส่ path ตรงใน <img src>) · key เดียวกันระหว่างรูปย่อกับหน้าต่างดูสลิป เปิดหน้าต่างแล้วไม่โหลดซ้ำ · คืน URL เมื่อเลิกใช้
 * · ลิงก์รูปภายนอก (garagepro): ไม่อยู่หลัง auth ของเรา ใช้ <img src> ตรงได้ ไม่ยิง API · ผ่าน `slipSource` แล้วเป็น https เท่านั้น
 */
export function useSlipBlob(payment: PackagePayment, enabled = true) {
  const source: SlipSource = slipSource(payment.slip)
  const query = useQuery({
    queryKey: [PACKAGE_SLIP_QUERY_KEY, payment.id],
    queryFn: () => downloadPackageSlip(payment.id),
    enabled: enabled && source.kind === 'download',
    staleTime: 5 * 60_000,
    retry: false,
  })
  const [blobUrl, setBlobUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!query.data) {
      setBlobUrl(null)
      return
    }
    const next = URL.createObjectURL(query.data)
    setBlobUrl(next)
    return () => URL.revokeObjectURL(next)
  }, [query.data])
  if (source.kind === 'external') {
    // ลิงก์ของระบบเดิมเป็นรูปเสมอ (ไม่มี contentType ให้ตรวจ) · รูปเปิดไม่ได้ให้ component จัดการด้วย onError
    return { source, external: true, url: source.url, isImage: true, pending: false, error: null as unknown, refetch: () => {} }
  }
  const contentType = query.data?.type || payment.slip?.contentType || ''
  return {
    source,
    external: false,
    url: blobUrl,
    isImage: isImageSlipType(contentType),
    pending: source.kind === 'download' && query.isPending,
    error: query.isError ? query.error : null,
    refetch: () => void query.refetch(),
  }
}

function slipDescription(payment: PackagePayment, external: boolean): string {
  if (external) return 'รูปสลิปที่แนบตอนแจ้งชำระ (เก็บในระบบ GaragePro)'
  const slip = payment.slip
  const parts = [slip?.contentType, typeof slip?.sizeBytes === 'number' ? formatFileSize(slip.sizeBytes) : null].filter(Boolean)
  return parts.length ? parts.join(' · ') : 'ไฟล์สลิปที่แนบตอนแจ้งชำระ'
}

/** รูปย่อสลิปที่กดเปิดหน้าต่างดูสลิปได้ · PDF/โหลดไม่สำเร็จแสดงเป็นไอคอนไฟล์ (ข้อความอยู่ใน aria-label ของปุ่ม) */
export function PackageSlipThumb({ payment, onOpen }: { payment: PackagePayment; onOpen: () => void }) {
  const { url, isImage, pending, error, external } = useSlipBlob(payment)
  const [broken, setBroken] = useState(false)
  const size = typeof payment.slip?.sizeBytes === 'number' ? ` (${formatFileSize(payment.slip.sizeBytes)})` : ''
  const label = `ดูสลิปรายการ ${paymentRefText(payment)}${size}`
  return (
    <button type="button" className="package-slip-thumb" onClick={onOpen} aria-label={label} title={label}>
      {url && isImage && !broken ? (
        <img
          src={url}
          alt=""
          referrerPolicy={external ? 'no-referrer' : undefined}
          onError={() => setBroken(true)}
        />
      ) : pending ? (
        <Loader2 className="spin" aria-hidden="true" />
      ) : error || broken ? (
        <CircleAlert aria-hidden="true" />
      ) : (
        <span className="package-slip-thumb__file"><FileText aria-hidden="true" /> PDF</span>
      )}
    </button>
  )
}

/** หน้าต่างดูสลิปของรายการแจ้งชำระ */
export function PackageSlipDialog({ payment, onClose }: { payment: PackagePayment; onClose: () => void }) {
  const { url, isImage, pending, error, external, refetch } = useSlipBlob(payment)
  const [broken, setBroken] = useState(false)
  const ref = paymentRefText(payment)

  return (
    <ConfirmModal
      open
      size="large"
      title={`สลิปรายการ ${ref}`}
      description={slipDescription(payment, external)}
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
      {pending ? (
        <p className="package-slip-viewer__state" role="status">
          <Loader2 className="spin" aria-hidden="true" /> กำลังโหลดสลิป…
        </p>
      ) : error ? (
        <Alert variant="destructive">
          <CircleAlert aria-hidden="true" />
          <div>
            <AlertTitle>{isApiError(error) ? error.messageTh : 'โหลดสลิปไม่สำเร็จ'}</AlertTitle>
            <AlertDescription>
              รหัสติดตาม (traceId): {isApiError(error) ? error.traceId : 'ไม่พบรหัสติดตาม'}{' '}
              <Button variant="link" size="sm" onClick={refetch}>ลองใหม่</Button>
            </AlertDescription>
          </div>
        </Alert>
      ) : url && isImage && broken ? (
        // ลิงก์ภายนอกไม่ใช่คำขอ API จึงไม่มี traceId · ทางต่อคือเปิดลิงก์ตรงในแท็บใหม่
        <div className="package-slip-viewer package-slip-viewer--file" role="status">
          <CircleAlert aria-hidden="true" />
          <span>แสดงรูปสลิปในหน้าต่างนี้ไม่ได้ กด "เปิดในแท็บใหม่" เพื่อดูสลิป ถ้ายังเปิดไม่ได้ ติดต่อทีมงานทาง LINE</span>
        </div>
      ) : url && isImage ? (
        <div className="package-slip-viewer">
          <img
            src={url}
            alt={`สลิปการโอนของรายการ ${ref}`}
            referrerPolicy={external ? 'no-referrer' : undefined}
            onError={() => setBroken(true)}
          />
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
