import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, Clock3, PenLine, Phone, UserRound, Wrench, XCircle } from 'lucide-react'
import { useRef, useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { uploadAttachment } from '../../api/attachments'
import { isApiError } from '../../api/client'
import { transitionJob } from '../../api/jobs'
import { decideQuotationLine, getQuotation, signQuotation } from '../../api/quotations'
import type { Job, Quotation, QuotationLine } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { Button } from '../../components/ui/button'
import { Select } from '../../components/ui/select'
import { SignaturePad, type SignaturePadHandle } from '../../components/ui/signature-pad'
import { Textarea } from '../../components/ui/textarea'
import { formatDateTime } from '../../lib/format'
import { useSession } from '../../lib/session'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import { advanceJobToWaitApprove } from '../jobs/advanceJobStatus'
import { BEFORE_APPROVED } from './useActForCustomer'
import { REJECT_REASONS, VERBAL_APPROVAL_SIGNATURE_PATH } from './customerApproval'
import './customerApproval.css'

/** ข้อความเดียวกับหน้าเซ็นบนมือถือ (signature_page.dart) — ลูกค้ายินยอมด้วยถ้อยคำเดียวกันไม่ว่าเซ็นจากไหน */
const CONSENT_TEXT =
  'ข้าพเจ้าได้ตรวจสอบรายการซ่อมและราคาตามใบเสนอราคานี้แล้ว ' +
  'และยินยอมให้อู่ดำเนินการซ่อมเฉพาะรายการที่ข้าพเจ้าอนุมัติไว้'

const VERBAL_CHANNELS = ['โทรศัพท์', 'LINE / ข้อความ', 'แจ้งด้วยตนเองที่อู่ (ไม่ได้เซ็น)'] as const

type ConfirmMethod = 'signature' | 'verbal'

/** สถานะจ๊อบที่ต้องไต่ขึ้นไป "รออนุมัติ" ก่อน — transition เหล่านั้นเปิดเฉพาะ Office/Manager */
const NEEDS_ADVANCE = ['waitinspect', 'waitquote']

type Props = {
  quotationId: string | null
  job: Job
  onClose: () => void
}

/**
 * ดำเนินการแทนลูกค้าจากเว็บ — ขั้นเดียวกับหน้าอนุมัติบนมือถือ (approval_page.dart + signature_page.dart)
 * ตัดสินใจรายบรรทัด → ยืนยันด้วยลายเซ็นบนหน้าจอ หรือบันทึกว่าลูกค้าแจ้งอนุมัติทางช่องทางอื่น → ขยับจ๊อบเป็น "อนุมัติแล้ว"
 *
 * [BIZ] ไม่ข้าม guard ใดๆ — ใช้ endpoint decide/sign/transition ตัวเดียวกับมือถือ และ `WaitApprove→Approved`
 * คำนวณจาก QuotationApproval จริงเสมอ (JobService.ComputeGuardAsync) จึงไม่ส่ง reason
 */
export function CustomerApprovalModal({ quotationId, job, onClose }: Props) {
  if (!quotationId) return null
  return <CustomerApprovalContent key={quotationId} quotationId={quotationId} job={job} onClose={onClose} />
}

function CustomerApprovalContent({ quotationId, job, onClose }: Props & { quotationId: string }) {
  const queryClient = useQueryClient()
  const { session } = useSession()
  const quotationKey = ['quotation', quotationId] as const

  const query = useQuery({ queryKey: quotationKey, queryFn: () => getQuotation(quotationId) })

  const [rejectingLineId, setRejectingLineId] = useState<string | null>(null)
  const [method, setMethod] = useState<ConfirmMethod>('signature')
  const [channel, setChannel] = useState<string>(VERBAL_CHANNELS[0])
  const [verbalNote, setVerbalNote] = useState('')
  const signatureRef = useRef<SignaturePadHandle | null>(null)
  const [hasSignature, setHasSignature] = useState(false)

  const setQuotation = (q: Quotation) => queryClient.setQueryData(quotationKey, q)

  const decideMutation = useMutation({
    mutationFn: (input: { lineId: string; decision: 'Approved' | 'Rejected'; rejectReason: string | null }) =>
      decideQuotationLine(quotationId, input.lineId, { decision: input.decision, rejectReason: input.rejectReason }),
    onSuccess: (q) => {
      setQuotation(q)
      setRejectingLineId(null)
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกการตัดสินใจไม่สำเร็จ'),
  })

  const approveAllPendingMutation = useMutation({
    mutationFn: async (lines: QuotationLine[]) => {
      let latest: Quotation | null = null
      for (const line of lines) {
        latest = await decideQuotationLine(quotationId, line.id, { decision: 'Approved', rejectReason: null })
      }
      return latest
    },
    onSuccess: (q) => { if (q) setQuotation(q) },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'อนุมัติรายการไม่สำเร็จ'),
    // บางบรรทัดอาจสำเร็จไปแล้วก่อนเจอ error — โหลดใหม่ให้เห็นสถานะจริง
    onSettled: () => void queryClient.invalidateQueries({ queryKey: quotationKey }),
  })

  const submitMutation = useMutation({
    mutationFn: async (q: Quotation) => {
      // [BIZ] จ๊อบมีใบเสนอราคาได้หลายใบ — ใบที่สองที่อนุมัติระหว่างซ่อมไม่ต้อง (และห้าม) ดันสถานะจ๊อบย้อนกลับ
      const moveJob = BEFORE_APPROVED.includes(job.status)
      if (moveJob) await advanceJobToWaitApprove(job.jobId, job.status)

      // ลูกค้าอาจเซ็นจากมือถือไปแล้วแต่จ๊อบขยับตามไม่ได้ — เซ็นซ้ำจะถูกปฏิเสธ จึงข้ามไปดันสถานะจ๊อบอย่างเดียว
      // ต้องเช็คแบบ truthy: API ตัดฟิลด์ที่เป็น null ทิ้ง (WhenWritingNull ใน Program.cs) ค่าที่ได้จริงคือ undefined
      // เคยเขียน `=== null` แล้วข้ามการเซ็นทุกครั้ง → transition โดน JOB_GUARD_NOT_SATISFIED
      if (!q.approval) {
        const userName = session?.user.displayName ?? 'ไม่ระบุชื่อ'
        let signatureImagePath: string
        let consentText: string

        if (method === 'signature') {
          const blob = await signatureRef.current?.toBlob()
          if (!blob) throw new Error('กรุณาให้ลูกค้าเซ็นในกรอบก่อน')
          const file = new File([blob], `sig-${q.jobNo}-v${q.version}.png`, { type: 'image/png' })
          const attachment = await uploadAttachment({ jobId: q.jobId, kind: 'signature', entityId: q.id, file })
          signatureImagePath = attachment.relativePath
          consentText = CONSENT_TEXT
        } else {
          signatureImagePath = VERBAL_APPROVAL_SIGNATURE_PATH
          consentText =
            `ลูกค้าแจ้งอนุมัติทาง${channel} — ไม่มีลายเซ็น · บันทึกแทนโดย ${userName} · ${verbalNote.trim()}`
        }

        await signQuotation(q.id, {
          signatureImagePath,
          consentText,
          deviceInfo: `เว็บ · ${method === 'signature' ? 'เซ็นบนหน้าจอ' : 'บันทึกแทนลูกค้า'} · ${userName}`,
          witnessEmployeeId: session?.user.userId ?? 0,
          witnessEmployeeName: userName,
        })
      }

      if (moveJob) await transitionJob(job.jobId, { toStatus: 'approved' })
      return moveJob
    },
    onSuccess: (movedJob) => {
      toast.success(movedJob
        ? 'บันทึกการอนุมัติของลูกค้าแล้ว — จ๊อบเปลี่ยนเป็น "อนุมัติแล้ว"'
        : 'บันทึกการอนุมัติของลูกค้าแล้ว — รายการที่อนุมัติรวมเข้ายอดชำระของจ๊อบนี้')
      onClose()
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : error instanceof Error ? error.message : 'บันทึกการอนุมัติไม่สำเร็จ')
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: quotationKey })
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
    },
  })

  const q = query.data
  const signed = Boolean(q?.approval)
  const openForDecision = q?.status === 'sent' || q?.status === 'partial'
  const readOnly = signed || !openForDecision
  const busy = decideMutation.isPending || approveAllPendingMutation.isPending || submitMutation.isPending

  const pendingLines = q?.lines.filter((l) => l.approvalStatus === 'pending') ?? []
  const approvedCount = q?.lines.filter((l) => l.approvalStatus === 'approved').length ?? 0
  const role = session?.user.role.toLowerCase() ?? ''
  const canAdvanceJob = !NEEDS_ADVANCE.includes(job.status) || ['office', 'manager'].includes(role)

  // [UI] ปุ่มที่ปิดต้องบอกเหตุผล — เรียงตามสิ่งที่ต้องทำก่อน/หลัง เหมือน StickyActionBar ของมือถือ
  let blockedReason: string | null = null
  if (!q) blockedReason = 'กำลังโหลดใบเสนอราคา'
  else if (!canAdvanceJob) blockedReason = 'จ๊อบยังไม่ถึงขั้น "รออนุมัติ" — ต้องให้ผู้จัดการหรือธุรการดำเนินการ'
  else if (!signed && !openForDecision) blockedReason = `ใบเสนอราคาสถานะ "${q.statusLabelTh}" — รับการอนุมัติไม่ได้`
  else if (!signed && q.isExpired) blockedReason = 'ใบเสนอราคาหมดอายุ — ต้องออกฉบับแก้ไขก่อนให้ลูกค้าอนุมัติ'
  else if (!signed && pendingLines.length > 0) blockedReason = `ยังเหลือ ${pendingLines.length} รายการที่ยังไม่ได้ตัดสินใจ`
  else if (!signed && approvedCount === 0) blockedReason = 'ยังไม่มีรายการที่อนุมัติ — ต้องอนุมัติอย่างน้อย 1 รายการ (ถ้าไม่อนุมัติทั้งหมดให้ออกฉบับแก้ไขหรือปิดงาน)'
  else if (!signed && method === 'signature' && !hasSignature) blockedReason = 'ให้ลูกค้าเซ็นในกรอบลายเซ็นก่อน'
  else if (!signed && method === 'verbal' && !verbalNote.trim()) blockedReason = 'ระบุรายละเอียดการแจ้งอนุมัติ (ชื่อผู้แจ้ง/เวลา) ก่อน'
  else if (busy) blockedReason = 'กำลังบันทึก กรุณารอสักครู่'

  const customerLines = q?.lines.filter((l) => l.source === 'customer') ?? []
  const technicianLines = q?.lines.filter((l) => l.source === 'technician') ?? []

  const renderGroup = (title: string, tone: 'customer' | 'technician', lines: QuotationLine[]) =>
    lines.length ? (
      <section className={`customer-approval__group customer-approval__group--${tone}`}>
        <h3>
          {tone === 'customer' ? <UserRound aria-hidden="true" /> : <Wrench aria-hidden="true" />}
          {title} <small>{lines.length} รายการ</small>
        </h3>
        <ul>
          {lines.map((line) => (
            <LineRow
              key={line.id}
              line={line}
              readOnly={readOnly}
              disabled={busy}
              rejecting={rejectingLineId === line.id}
              onApprove={() => decideMutation.mutate({ lineId: line.id, decision: 'Approved', rejectReason: null })}
              onStartReject={() => setRejectingLineId(line.id)}
              onCancelReject={() => setRejectingLineId(null)}
              onReject={(reason) => decideMutation.mutate({ lineId: line.id, decision: 'Rejected', rejectReason: reason })}
            />
          ))}
        </ul>
      </section>
    ) : null

  return (
    <ConfirmModal
      open
      size="large"
      title={q ? `ดำเนินการแทนลูกค้า · ${q.code} (v${q.version})` : 'ดำเนินการแทนลูกค้า'}
      description="บันทึกการตัดสินใจของลูกค้ารายรายการ แล้วยืนยันด้วยลายเซ็นหรือบันทึกช่องทางที่ลูกค้าแจ้งอนุมัติ — ขั้นตอนเดียวกับหน้าอนุมัติบนมือถือ"
      onClose={onClose}
      footer={
        <>
          {blockedReason && q ? <span className="customer-approval__blocked">{blockedReason}</span> : null}
          <Button variant="ghost" onClick={onClose} disabled={submitMutation.isPending}>ปิด</Button>
          <Button disabled={Boolean(blockedReason)} onClick={() => q && submitMutation.mutate(q)}>
            {submitMutation.isPending
              ? 'กำลังบันทึก…'
              : signed ? 'ยืนยันและเปลี่ยนจ๊อบเป็น "อนุมัติแล้ว"' : 'ยืนยันการอนุมัติของลูกค้า'}
          </Button>
        </>
      }
    >
      {query.isPending ? (
        <p className="form-message">กำลังโหลดใบเสนอราคา…</p>
      ) : query.isError ? (
        <div className="customer-approval__stack">
          <InlineError error={query.error} />
          <Button variant="outline" size="sm" onClick={() => void query.refetch()}>ลองใหม่</Button>
        </div>
      ) : q ? (
        <div className="customer-approval__stack">
          {q.revisionReason ? (
            <div className="customer-approval__banner customer-approval__banner--warn" role="status">
              <strong>ใบเสนอราคาฉบับแก้ไข (เวอร์ชัน {q.version})</strong>
              <p>{q.revisionReason} — การอนุมัติในเวอร์ชันก่อนหน้าเป็นโมฆะ ต้องตัดสินใจใหม่ทุกรายการ</p>
            </div>
          ) : null}
          {signed && q.approval ? (
            <div className="customer-approval__banner" role="status">
              <strong><CheckCircle2 aria-hidden="true" /> ลูกค้ายืนยันแล้ว</strong>
              <p>
                เมื่อ {formatDateTime(q.approval.signedAt)} · รับรองโดย {q.approval.witnessEmployeeName} · ผูกกับเวอร์ชันที่{' '}
                {q.approval.quotationVersion} — เหลือแค่เปลี่ยนสถานะจ๊อบเป็น "อนุมัติแล้ว"
              </p>
            </div>
          ) : null}
          {!signed && q.isExpired ? (
            <div className="customer-approval__banner customer-approval__banner--error" role="alert">
              <strong>ใบเสนอราคานี้หมดอายุแล้ว</strong>
              <p>ออกฉบับแก้ไขก่อนให้ลูกค้าอนุมัติ</p>
            </div>
          ) : null}

          <div className="customer-approval__toolbar">
            <span>
              ลูกค้า <strong>{q.customer.name}</strong> · {q.vehicle.registration}
            </span>
            {!readOnly && pendingLines.length > 0 ? (
              <Button
                size="sm"
                variant="outline"
                disabled={busy}
                onClick={() => approveAllPendingMutation.mutate(pendingLines)}
              >
                {approveAllPendingMutation.isPending ? 'กำลังอนุมัติ…' : `อนุมัติที่เหลือทั้งหมด (${pendingLines.length})`}
              </Button>
            ) : null}
          </div>

          {renderGroup('รายการที่ลูกค้าขอ', 'customer', customerLines)}
          {renderGroup('รายการที่ช่างแนะนำ', 'technician', technicianLines)}

          <div className="customer-approval__total">
            <span>อนุมัติ {approvedCount} จาก {q.lines.length} รายการ · ยอดที่ต้องชำระ (รวม VAT)</span>
            <Money value={q.totals.approved?.grandTotal ?? 0} />
          </div>

          {!signed ? (
            <section className="customer-approval__confirm">
              <h3>วิธียืนยันของลูกค้า</h3>
              <div className="customer-approval__methods" role="radiogroup" aria-label="วิธียืนยันของลูกค้า">
                <MethodOption
                  checked={method === 'signature'}
                  onSelect={() => setMethod('signature')}
                  icon={<PenLine aria-hidden="true" />}
                  title="ลูกค้าเซ็นบนหน้าจอนี้"
                  detail="ลูกค้าอยู่ที่อู่ — ใช้เมาส์ ปากกา หรือนิ้วบนจอสัมผัส"
                />
                <MethodOption
                  checked={method === 'verbal'}
                  onSelect={() => setMethod('verbal')}
                  icon={<Phone aria-hidden="true" />}
                  title="ลูกค้าแจ้งอนุมัติทางช่องทางอื่น"
                  detail="ไม่มีลายเซ็น — พนักงานบันทึกแทนและชื่อผู้บันทึกจะอยู่ในเอกสาร"
                />
              </div>

              {method === 'signature' ? (
                <div className="customer-approval__signature">
                  <p className="customer-approval__consent">{CONSENT_TEXT}</p>
                  <SignaturePad handleRef={(h) => { signatureRef.current = h }} onChange={setHasSignature} />
                  <Button
                    size="sm"
                    variant="ghost"
                    disabled={!hasSignature || busy}
                    onClick={() => signatureRef.current?.clear()}
                  >
                    ล้างลายเซ็น
                  </Button>
                </div>
              ) : (
                <div className="customer-approval__verbal">
                  <Field label="ช่องทางที่ลูกค้าแจ้ง *">
                    <Select value={channel} onChange={(e) => setChannel(e.target.value)}>
                      {VERBAL_CHANNELS.map((c) => <option key={c} value={c}>{c}</option>)}
                    </Select>
                  </Field>
                  <Field label="รายละเอียด (ชื่อผู้แจ้ง เวลา หรือเลขที่ข้อความ) *">
                    <Textarea
                      rows={2}
                      maxLength={300}
                      value={verbalNote}
                      onChange={(e) => setVerbalNote(e.target.value)}
                      placeholder="เช่น คุณสมชาย (เจ้าของรถ) โทรแจ้ง 14:20 น."
                    />
                  </Field>
                </div>
              )}
            </section>
          ) : null}

          {submitMutation.isError ? <InlineError error={submitMutation.error} /> : null}
        </div>
      ) : null}
    </ConfirmModal>
  )
}

function MethodOption({ checked, onSelect, icon, title, detail }: {
  checked: boolean
  onSelect: () => void
  icon: ReactNode
  title: string
  detail: string
}) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={checked}
      className={`customer-approval__method${checked ? ' customer-approval__method--active' : ''}`}
      onClick={onSelect}
    >
      {icon}
      <span>
        <strong>{title}</strong>
        <small>{detail}</small>
      </span>
    </button>
  )
}

function LineRow({ line, readOnly, disabled, rejecting, onApprove, onStartReject, onCancelReject, onReject }: {
  line: QuotationLine
  readOnly: boolean
  disabled: boolean
  rejecting: boolean
  onApprove: () => void
  onStartReject: () => void
  onCancelReject: () => void
  onReject: (reason: string) => void
}) {
  return (
    <li className={`customer-approval__line customer-approval__line--${line.approvalStatus}`}>
      <div className="customer-approval__line-main">
        <div>
          <strong>{line.name}</strong>
          <small>
            {line.type === 'labor' ? 'ค่าแรง' : 'อะไหล่'} · {line.quantity} {line.unit}
            {line.assignedTechnicianName ? ` · ${line.assignedTechnicianName}` : ''}
          </small>
        </div>
        <Money value={line.netAmount} />
      </div>
      <div className="customer-approval__line-actions">
        <DecisionBadge line={line} />
        {!readOnly && !rejecting ? (
          <div className="customer-approval__decide">
            <Button
              size="sm"
              variant={line.approvalStatus === 'approved' ? 'default' : 'outline'}
              disabled={disabled || line.approvalStatus === 'approved'}
              onClick={onApprove}
            >
              <CheckCircle2 aria-hidden="true" /> อนุมัติ
            </Button>
            <Button
              size="sm"
              variant={line.approvalStatus === 'rejected' ? 'destructive' : 'outline'}
              disabled={disabled}
              onClick={onStartReject}
            >
              <XCircle aria-hidden="true" /> {line.approvalStatus === 'rejected' ? 'เปลี่ยนเหตุผล' : 'ไม่อนุมัติ'}
            </Button>
          </div>
        ) : null}
      </div>
      {rejecting ? (
        <div className="customer-approval__reasons" role="group" aria-label={`เหตุผลที่ไม่อนุมัติ ${line.name}`}>
          <span>เหตุผลที่ไม่อนุมัติ (เลือก 1 ข้อ):</span>
          {REJECT_REASONS.map((reason) => (
            <Button key={reason} size="sm" variant="outline" disabled={disabled} onClick={() => onReject(reason)}>
              {reason}
            </Button>
          ))}
          <Button size="sm" variant="ghost" disabled={disabled} onClick={onCancelReject}>ยกเลิก</Button>
        </div>
      ) : null}
    </li>
  )
}

function DecisionBadge({ line }: { line: QuotationLine }) {
  if (line.approvalStatus === 'approved') {
    return <span className="customer-approval__badge customer-approval__badge--approved"><CheckCircle2 aria-hidden="true" /> ลูกค้าอนุมัติ</span>
  }
  if (line.approvalStatus === 'rejected') {
    return (
      <span className="customer-approval__badge customer-approval__badge--rejected">
        <XCircle aria-hidden="true" /> ไม่อนุมัติ{line.rejectReason ? ` · ${line.rejectReason}` : ''}
      </span>
    )
  }
  return <span className="customer-approval__badge customer-approval__badge--pending"><Clock3 aria-hidden="true" /> ยังไม่ตัดสินใจ</span>
}
