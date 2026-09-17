import { useQuery } from '@tanstack/react-query'
import {
  CheckCircle2,
  Clock,
  Printer,
  TriangleAlert,
  UserRound,
  Wrench,
  XCircle,
} from 'lucide-react'
import { Fragment, useEffect, useState } from 'react'
import { API_BASE_URL, isApiError, isForbiddenError } from '../../api/client'
import { getQuotation } from '../../api/quotations'
import type { Quotation, QuotationLine } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { MoneySummary } from '../../components/MoneySummary'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { StatusChip } from '../../components/StatusChip'
import { readSession } from '../../lib/session'
import { formatDate, formatDateTime, formatNumber } from '../../lib/format'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'

function getSignatureUrl(path: string) {
  return `${API_BASE_URL}/api/v1/attachments/file?path=${encodeURIComponent(path)}`
}

type QuotationDocumentModalProps = {
  quotationId: string | null
  onClose: () => void
}

/// เอกสารใบเสนอราคาซ้อนบน job card modal — window.print() พิมพ์เฉพาะ .quotation-document-print-area
/// ด้วย body.printing-quotation-document (ดู @media print ใน index.css) ไม่ต้องปิด job card modal ก่อน
export function QuotationDocumentModal({ quotationId, onClose }: QuotationDocumentModalProps) {
  const query = useQuery({
    queryKey: ['quotation', quotationId ?? ''],
    queryFn: () => getQuotation(quotationId!),
    enabled: Boolean(quotationId),
  })

  useEffect(() => {
    if (!quotationId) return
    document.body.classList.add('printing-quotation-document')
    return () => document.body.classList.remove('printing-quotation-document')
  }, [quotationId])

  let title = 'เอกสารใบเสนอราคา'
  let body = null as React.ReactNode

  if (!quotationId) {
    body = null
  } else if (query.isPending) {
    body = (
      <StateBlock
        variant="loading"
        title="กำลังจัดเตรียมเอกสาร"
        reason="ระบบกำลังโหลดข้อมูลฉบับล่าสุดเพื่อจัดวางเอกสารสำหรับพิมพ์"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void query.refetch()}
      >
        <SkeletonRows count={5} />
      </StateBlock>
    )
  } else if (query.isError) {
    const error = query.error
    const notFound = isApiError(error) && error.status === 404
    const forbidden = isForbiddenError(error)
    body = (
      <StateBlock
        variant={forbidden ? 'forbidden' : notFound ? 'empty' : 'error'}
        title={
          forbidden
            ? 'ไม่มีสิทธิ์ดูเอกสารนี้'
            : notFound
              ? 'ไม่พบเอกสารใบเสนอราคา'
              : 'เปิดเอกสารไม่สำเร็จ'
        }
        reason={isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(error) ? error.traceId : undefined}
        actionLabel={notFound ? 'ปิดหน้าต่าง' : 'ลองใหม่'}
        onAction={() => (notFound ? onClose() : void query.refetch())}
      />
    )
  } else if (!query.data) {
    body = (
      <StateBlock
        variant="empty"
        title="เอกสารไม่มีข้อมูล"
        reason="บริการตอบกลับสำเร็จแต่ไม่มีข้อมูลสำหรับจัดทำเอกสาร"
        traceId="ไม่พบ traceId จากข้อมูลว่าง"
        actionLabel="ปิดหน้าต่าง"
        onAction={onClose}
      />
    )
  } else {
    const quotation = query.data
    title = `เอกสารใบเสนอราคา ${quotation.code}`
    body = (
      <div className="document-modal-body">
        <div className="job-card-panel-actions print-hidden">
          <Button onClick={() => window.print()}>
            <Printer aria-hidden="true" /> พิมพ์
          </Button>
        </div>
        <div className="quotation-document-print-area">
          <QuotationDocument quotation={quotation} />
        </div>
      </div>
    )
  }

  return (
    <ConfirmModal open={quotationId !== null} title={title} onClose={onClose} size="xlarge">
      {body}
    </ConfirmModal>
  )
}

function QuotationDocument({ quotation }: { quotation: Quotation }) {
  const customerLines = quotation.lines.filter((line) => line.source === 'customer')
  const technicianLines = quotation.lines.filter((line) => line.source === 'technician')
  const showApproval = Boolean(quotation.approval)

  return (
    <article className="quotation-document">
      <header className="document-header">
        <div className="document-branch">
          <div className="document-brand-mark" aria-hidden="true">GP</div>
          <div>
            <h1>{quotation.branch.name}</h1>
            <p>{quotation.branch.address || 'ไม่ระบุที่อยู่'}</p>
            <p>
              โทร {quotation.branch.phone || 'ไม่ระบุ'} · เลขประจำตัวผู้เสียภาษี{' '}
              {quotation.branch.taxId || 'ไม่ระบุ'}
            </p>
          </div>
        </div>
        <div className="document-title">
          <span>เอกสารเสนอราคา</span>
          <h2>ใบเสนอราคา</h2>
          <strong>{quotation.code}</strong>
          <div>
            <span className="version-chip">v{quotation.version}</span>
            <StatusChip status={quotation.status} label={quotation.statusLabelTh} />
          </div>
        </div>
      </header>

      <section className="document-info-grid">
        <InfoBlock title="ข้อมูลลูกค้า">
          <InfoRow label="ชื่อ" value={quotation.customer.name} strong />
          <InfoRow label="โทร" value={quotation.customer.phone || 'ไม่ระบุ'} />
          <InfoRow label="เลขผู้เสียภาษี" value={quotation.customer.taxId || 'ไม่ระบุ'} />
          <InfoRow label="ที่อยู่" value={quotation.customer.address || 'ไม่ระบุ'} />
        </InfoBlock>
        <InfoBlock title="ข้อมูลรถและงานซ่อม">
          <div className="document-info-columns">
            <div>
              <InfoRow label="ทะเบียน" value={quotation.vehicle.registration} strong />
              <InfoRow label="รุ่น" value={quotation.vehicle.model || 'ไม่ระบุ'} />
              <InfoRow label="เลขตัวถัง" value={quotation.vehicle.vin || 'ไม่ระบุ'} />
              <InfoRow
                label="เลขไมล์"
                value={
                  quotation.vehicle.mileage === null || quotation.vehicle.mileage === undefined
                    ? 'ไม่ระบุ'
                    : `${formatNumber(quotation.vehicle.mileage)} กม.`
                }
              />
            </div>
            <div>
              <InfoRow label="เลขงาน" value={quotation.jobNo} strong />
              <InfoRow label="วันที่ออก" value={formatDate(quotation.createdAt)} />
              <InfoRow label="วันหมดอายุ" value={formatDate(quotation.validUntil)} />
            </div>
          </div>
        </InfoBlock>
      </section>

      {quotation.revisionReason ? (
        <section className="revision-notice">
          <TriangleAlert className="revision-notice__icon" aria-hidden="true" />
          <div>
            <strong>ใบเสนอราคาฉบับแก้ไข</strong>
            <p>เหตุผล: {quotation.revisionReason}</p>
            <p className="revision-notice__void">การอนุมัติในเวอร์ชันก่อนหน้าเป็นโมฆะ</p>
          </div>
        </section>
      ) : null}

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th className="document-col-sequence">ลำดับ</th>
              <th className="document-col-code">รหัส</th>
              <th>รายการ</th>
              <th className="document-number">จำนวน</th>
              <th>หน่วย</th>
              <th className="document-number">ราคา/หน่วย</th>
              <th className="document-number">ส่วนลด</th>
              <th className="document-number">ยอดสุทธิ</th>
              {showApproval ? <th className="document-col-approval">ผลอนุมัติ</th> : null}
            </tr>
          </thead>
          <tbody>
            <DocumentLineGroup
              title="รายการที่ลูกค้าขอ"
              tone="customer"
              lines={customerLines}
              showApproval={showApproval}
            />
            <DocumentLineGroup
              title="รายการที่ช่างแนะนำ"
              tone="technician"
              lines={technicianLines}
              showApproval={showApproval}
            />
          </tbody>
        </table>
      </section>

      <section className="document-summary-section">
        <div className="document-notes">
          <strong>รายละเอียดเพิ่มเติม</strong>
          <p>
            ราคาอะไหล่และค่าแรงตามรายการข้างต้นมีผลถึงวันที่ {formatDate(quotation.validUntil)}
          </p>
          <p>กรุณาตรวจสอบรายการก่อนอนุมัติ งานจะเริ่มดำเนินการตามรายการที่ได้รับอนุมัติเท่านั้น</p>
        </div>
        <MoneySummary totals={quotation.totals} showCost={false} showApproved className="document-money-summary" />
      </section>

      {quotation.approval ? (
        <section className="approval-block">
          <header>
            <span className="approval-block__check" aria-hidden="true"><CheckCircle2 /></span>
            <div>
              <strong>การอนุมัติจากลูกค้า</strong>
              <p>บันทึกการยืนยันตัวตนและความยินยอมสำหรับเอกสารฉบับนี้</p>
            </div>
          </header>
          <div className="approval-block__content">
            <div className="signature-image-box">
              <SignatureImage
                path={quotation.approval.signatureImagePath}
                customerName={quotation.customer.name}
              />
              <small>ลายเซ็นลูกค้า</small>
            </div>
            <dl className="approval-details">
              <div>
                <dt>ชื่อลูกค้า</dt>
                <dd>{quotation.customer.name}</dd>
              </div>
              <div>
                <dt>วันเวลาที่เซ็น</dt>
                <dd>{formatDateTime(quotation.approval.signedAt)}</dd>
              </div>
              <div>
                <dt>พนักงานผู้รับรอง</dt>
                <dd>{quotation.approval.witnessEmployeeName || 'ไม่ระบุ'}</dd>
              </div>
            </dl>
            <div className="approval-consent">
              <strong>ข้อความยินยอม</strong>
              <p>
                {quotation.approval.consentText ||
                  'ข้าพเจ้ายืนยันว่าได้ตรวจสอบและยินยอมให้ดำเนินการตามรายการที่อนุมัติในใบเสนอราคานี้'}
              </p>
              <p className="approval-version-binding">
                ลายเซ็นนี้ผูกกับใบเสนอราคาเวอร์ชันที่ {quotation.approval.quotationVersion}
              </p>
            </div>
          </div>
        </section>
      ) : null}

      <footer className="document-footer">
        <div className="signature-slots">
          <div className="signature-slot">
            <span />
            <strong>ผู้เสนอราคา</strong>
            <small>{quotation.createdByUserName}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
          <div className="signature-slot">
            <span />
            <strong>ผู้อนุมัติ</strong>
            <small>{quotation.customer.name}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
        </div>
        <div className="document-terms">
          <strong>เงื่อนไข</strong>
          <ol>
            <li>ราคาอาจเปลี่ยนแปลงหากพบความเสียหายเพิ่มเติม โดยจะแจ้งให้ลูกค้าทราบก่อนดำเนินการ</li>
            <li>อะไหล่ที่สั่งเฉพาะรายการอาจมีเงื่อนไขการคืนสินค้าตามผู้จำหน่าย</li>
            <li>ใบเสนอราคานี้ใช้ได้ถึงวันที่ {formatDate(quotation.validUntil)}</li>
          </ol>
        </div>
        <div className="document-footer__meta">
          <span>{quotation.branch.name}</span>
          <span>
            {quotation.code} · v{quotation.version}
          </span>
        </div>
      </footer>
    </article>
  )
}

function InfoBlock({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="document-info-block">
      <h3>{title}</h3>
      {children}
    </section>
  )
}

function InfoRow({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="document-info-row">
      <span>{label}</span>
      {strong ? <strong>{value}</strong> : <p>{value}</p>}
    </div>
  )
}

/**
 * รูปลายเซ็นต้องดึงผ่าน fetch ไม่ใช่ <img src> ตรงๆ
 * เพราะ endpoint ไฟล์แนบต้องใช้ Bearer token ซึ่ง <img> แนบ header ไม่ได้
 * จึงโหลดเป็น blob แล้วค่อยสร้าง object URL ให้ <img>
 */
function SignatureImage({ path, customerName }: { path: string; customerName: string }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (!path) return

    let revoked = false
    let created: string | null = null

    const session = readSession()
    const token = session?.accessToken

    fetch(getSignatureUrl(path), {
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    })
      .then((response) => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return response.blob()
      })
      .then((blob) => {
        if (revoked) return
        created = URL.createObjectURL(blob)
        setObjectUrl(created)
      })
      .catch(() => setFailed(true))

    return () => {
      revoked = true
      if (created) URL.revokeObjectURL(created)
    }
  }, [path])

  if (!path) return <span>ไม่มีรูปลายเซ็น</span>
  if (failed) return <span>ไม่สามารถโหลดรูปลายเซ็นได้</span>
  if (!objectUrl) return <span>กำลังโหลดลายเซ็น…</span>

  return <img src={objectUrl} alt={`ลายเซ็นของ ${customerName}`} />
}

function DocumentLineGroup({
  title,
  tone,
  lines,
  showApproval,
}: {
  title: string
  tone: 'customer' | 'technician'
  lines: QuotationLine[]
  showApproval: boolean
}) {
  const columnCount = showApproval ? 9 : 8

  return (
    <Fragment>
      <tr className={`document-group-row document-group-row--${tone}`}>
        <td colSpan={columnCount}>
          <span aria-hidden="true">{tone === 'customer' ? <UserRound /> : <Wrench />}</span>
          <strong>{title}</strong>
          <small>{lines.length} รายการ</small>
        </td>
      </tr>
      {lines.length ? (
        lines.map((line) => {
          const rejected = showApproval && line.approvalStatus === 'rejected'
          return (
            <tr className={rejected ? 'document-line--rejected' : undefined} key={line.id}>
              <td className="document-center money">{line.sequence}</td>
              <td className="document-code">{line.catalogCode || '—'}</td>
              <td>
                <strong>{line.name}</strong>
                {line.note ? <small className="document-line-note">{line.note}</small> : null}
              </td>
              <td className={`document-number money ${rejected ? 'strike-value' : ''}`}>
                {formatNumber(line.quantity)}
              </td>
              <td>{line.unit}</td>
              <td className={`document-number money ${rejected ? 'strike-value' : ''}`}>
                <Money value={line.unitPrice} />
              </td>
              <td className={`document-number money ${rejected ? 'strike-value' : ''}`}>
                <Money value={line.discountAmount + line.promotionAmount} />
              </td>
              <td className={`document-number money ${rejected ? 'strike-value' : ''}`}>
                <Money value={line.netAmount} />
              </td>
              {showApproval ? (
                <td>
                  <ApprovalResult line={line} />
                </td>
              ) : null}
            </tr>
          )
        })
      ) : (
        <tr className="document-group-empty">
          <td colSpan={columnCount}>ไม่มีรายการในกลุ่มนี้</td>
        </tr>
      )}
    </Fragment>
  )
}

function ApprovalResult({ line }: { line: QuotationLine }) {
  if (line.approvalStatus === 'approved') {
    return (
      <Badge className="approval-result approval-result--approved">
        <CheckCircle2 aria-hidden="true" /> อนุมัติ
      </Badge>
    )
  }
  if (line.approvalStatus === 'rejected') {
    return (
      <Badge className="approval-result approval-result--rejected">
        <span>
          <XCircle aria-hidden="true" /> ไม่อนุมัติ
        </span>
        <small>{line.rejectReason || 'ไม่ระบุเหตุผล'}</small>
      </Badge>
    )
  }
  return (
    <Badge className="approval-result approval-result--pending">
      <Clock aria-hidden="true" /> รออนุมัติ
    </Badge>
  )
}
