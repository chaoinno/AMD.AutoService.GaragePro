import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import {
  CheckCircle2,
  CircleAlert,
  FileText,
  Info,
  LoaderCircle,
  LockKeyhole,
  Send,
  Trash2,
} from 'lucide-react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { toast } from 'sonner'
import { isApiError, isForbiddenError } from '../../api/client'
import { getTechnicians } from '../../api/catalog'
import { getJob } from '../../api/jobs'
import { advanceJobToWaitApprove } from '../jobs/advanceJobStatus'
import {
  addQuotationLine,
  deleteQuotationLine,
  getQuotation,
  reviseQuotation,
  sendQuotation,
  updateQuotationLine,
  validateQuotation,
} from '../../api/quotations'
import type {
  CatalogItem,
  Quotation,
  QuotationLine,
  UpsertLine,
  UpsertLineSource,
} from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { MoneySummary } from '../../components/MoneySummary'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { StatusChip } from '../../components/StatusChip'
import { formatDate, getIssueMessage } from '../../lib/format'
import { CatalogPanel } from './CatalogPanel'
import { LineEditor } from './LineEditor'
import { WarningPanel } from './WarningPanel'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Label } from '../../components/ui/label'
import { Textarea } from '../../components/ui/textarea'
import { Tooltip } from '../../components/ui/tooltip'
import { useSession } from '../../lib/session'

type EditableLinePatch = Partial<
  Pick<
    QuotationLine,
    | 'quantity'
    | 'unitPrice'
    | 'discountPercent'
    | 'promotion'
    | 'assignedTechnicianId'
    | 'note'
  >
>

function toUpsertLine(line: QuotationLine): UpsertLine {
  const base: UpsertLine = {
    catalogCode: line.catalogCode,
    quantity: line.quantity,
    unitPrice: line.unitPrice,
    discountPercent: line.discountPercent,
    promotion: line.promotion,
    source: line.source === 'customer' ? 'Customer' : 'Technician',
    assignedTechnicianId: line.assignedTechnicianId ?? undefined,
    note: line.note || undefined,
  }
  // [BIZ] รายการนอกแคตตาล็อกต้องแนบชื่อ/หน่วย/ต้นทุนไปทุกครั้งที่ PUT — ไม่งั้นแก้แค่ "จำนวน" ก็ลบชื่อทิ้ง
  // เพราะ backend เก็บ Name/Unit/UnitCost ไว้ที่บรรทัดเอง (ไม่มีแคตตาล็อกให้ snapshot กลับมา) docs/07-quotation-adhoc-line.md
  if (line.isAdHoc) {
    return {
      ...base,
      name: line.name,
      type: line.type,
      unit: line.unit,
      unitCost: line.unitCost ?? undefined,
      standardHours: line.standardHours ?? undefined,
    }
  }
  return base
}

function getCatalogCode(item: CatalogItem) {
  return item.catalogCode || item.code || ''
}

function getCatalogPrice(item: CatalogItem) {
  return item.unitPrice ?? item.price ?? 0
}

type QuotationEditorModalProps = {
  quotationId: string | null
  onClose: () => void
  onOpenDocument: (quotationId: string) => void
  onRevised: (quotationId: string) => void
}

/// จัดการรายละเอียดใบเสนอราคาซ้อนบน job card modal (แท็ป "เสนอราคา/งานซ่อม") — ห้ามเปิดผ่านหน้าเต็มหรือ URL ตรง
export function QuotationEditorModal({
  quotationId,
  onClose,
  onOpenDocument,
  onRevised,
}: QuotationEditorModalProps) {
  const id = quotationId ?? ''
  const queryClient = useQueryClient()
  const { session } = useSession()
  const activeSession = session?.stage === 'active' ? session : null
  const quotationKey = ['quotation', id] as const
  const validationKey = ['quotation-validation', id] as const
  const saveTimers = useRef(new Map<string, number>())
  const validationTimer = useRef<number | null>(null)
  const [pendingSaves, setPendingSaves] = useState(0)
  const [saveError, setSaveError] = useState<unknown>(null)
  const [lineToDelete, setLineToDelete] = useState<QuotationLine | null>(null)
  const [revisionOpen, setRevisionOpen] = useState(false)

  const quotationQuery = useQuery({
    queryKey: quotationKey,
    queryFn: () => getQuotation(id),
    enabled: Boolean(quotationId),
  })

  const techniciansQuery = useQuery({
    queryKey: ['technicians'],
    queryFn: getTechnicians,
    enabled: Boolean(quotationId),
  })

  const validationQuery = useQuery({
    queryKey: validationKey,
    queryFn: () => validateQuotation(id),
    enabled: Boolean(quotationId && quotationQuery.data),
  })

  useEffect(
    () => () => {
      saveTimers.current.forEach((timer) => window.clearTimeout(timer))
      if (validationTimer.current !== null) window.clearTimeout(validationTimer.current)
    },
    [],
  )

  const refreshValidation = useCallback(() => {
    if (validationTimer.current !== null) window.clearTimeout(validationTimer.current)
    validationTimer.current = window.setTimeout(() => {
      void queryClient.invalidateQueries({ queryKey: validationKey })
    }, 650)
  }, [queryClient, validationKey])

  const applyServerQuotation = useCallback(
    (quotation: Quotation) => {
      queryClient.setQueryData(quotationKey, quotation)
      void queryClient.invalidateQueries({ queryKey: validationKey })
    },
    [queryClient, quotationKey, validationKey],
  )

  const patchLine = useCallback(
    (lineId: string, patch: EditableLinePatch) => {
      setSaveError(null)
      let updatedLine: QuotationLine | undefined
      queryClient.setQueryData<Quotation>(quotationKey, (current) => {
        if (!current) return current
        return {
          ...current,
          lines: current.lines.map((line) => {
            if (line.id !== lineId) return line
            updatedLine = { ...line, ...patch }
            return updatedLine
          }),
        }
      })

      if (!updatedLine) return
      const existingTimer = saveTimers.current.get(lineId)
      if (existingTimer) window.clearTimeout(existingTimer)
      const lineSnapshot = updatedLine
      saveTimers.current.set(
        lineId,
        window.setTimeout(() => {
          saveTimers.current.delete(lineId)
          setPendingSaves((count) => count + 1)
          void updateQuotationLine(id, lineId, toUpsertLine(lineSnapshot))
            .then(applyServerQuotation)
            .catch((error: unknown) => {
              setSaveError(error)
              void queryClient.invalidateQueries({ queryKey: quotationKey })
            })
            .finally(() => setPendingSaves((count) => Math.max(0, count - 1)))
        }, 400),
      )
      refreshValidation()
    },
    [applyServerQuotation, id, queryClient, quotationKey, refreshValidation],
  )

  const addMutation = useMutation({
    mutationFn: ({ item, source }: { item: CatalogItem; source: UpsertLineSource }) =>
      addQuotationLine(id, {
        catalogCode: getCatalogCode(item),
        quantity: 1,
        unitPrice: getCatalogPrice(item),
        discountPercent: 0,
        promotion: 0,
        source,
      }),
    onSuccess: (quotation) => {
      applyServerQuotation(quotation)
      toast.success('เพิ่มรายการในใบเสนอราคาแล้ว')
    },
    onError: setSaveError,
  })

  // [BIZ] รายการนอกแคตตาล็อก (catalogCode ว่าง) — docs/07-quotation-adhoc-line.md
  const addAdHocMutation = useMutation({
    mutationFn: (payload: UpsertLine) => addQuotationLine(id, payload),
    onSuccess: (quotation) => {
      applyServerQuotation(quotation)
      toast.success('เพิ่มรายการนอกแคตตาล็อกแล้ว')
    },
    onError: setSaveError,
  })

  const deleteMutation = useMutation({
    mutationFn: (lineId: string) => deleteQuotationLine(id, lineId),
    onSuccess: (quotation) => {
      setLineToDelete(null)
      applyServerQuotation(quotation)
      toast.success('ลบรายการแล้ว')
    },
    onError: setSaveError,
  })

  const sendMutation = useMutation({
    mutationFn: async () => {
      const quotation = await sendQuotation(id)
      // [BIZ] ส่งใบเสนอราคาแล้วต้องขยับ job ไป "รออนุมัติ" ด้วย — ไม่งั้น job ค้างข้างหลังตลอด
      // แม้ลูกค้าจะเห็นใบเสนอราคาแล้ว (ตั้งใจไม่ปล่อยให้ error หลุดออกไปทำให้ทั้ง mutation ดูเหมือนล้มเหลว
      // เพราะการส่งใบเสนอราคาสำเร็จแล้วจริง — แค่ job status อาจไม่ขยับตาม)
      //
      // เดิมยิงตรงไป waitapprove ทีเดียว ซึ่งล้มเงียบเมื่อจ๊อบยังอยู่ waitinspect (ไม่มีเส้นทางนั้น)
      // แล้วจ๊อบค้างถาวรเพราะลูกค้าเซ็นจากมือถือได้แต่แอปดันจ๊อบต่อไม่ได้ — ต้องไต่ทีละขั้นแทน
      let jobTransitionError: unknown = null
      try {
        const job = await getJob(quotation.jobId)
        await advanceJobToWaitApprove(quotation.jobId, job.status)
      } catch (error) {
        jobTransitionError = error
      }
      return { quotation, jobTransitionError }
    },
    onSuccess: ({ quotation, jobTransitionError }) => {
      applyServerQuotation(quotation)
      void queryClient.invalidateQueries({ queryKey: ['job-detail', quotation.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', quotation.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
      toast.success('ส่งใบเสนอราคาให้ลูกค้าแล้ว')
      if (jobTransitionError) {
        toast.error(
          isApiError(jobTransitionError)
            ? `ส่งใบเสนอราคาสำเร็จ แต่เปลี่ยนสถานะจ๊อบเป็น "รออนุมัติ" ไม่สำเร็จ: ${jobTransitionError.messageTh}`
            : 'ส่งใบเสนอราคาสำเร็จ แต่เปลี่ยนสถานะจ๊อบเป็น "รออนุมัติ" ไม่สำเร็จ',
        )
      }
      onOpenDocument(quotation.id)
    },
  })

  let title = 'ใบเสนอราคา'
  let description: string | undefined
  let body: ReactNode = null

  if (!quotationId) {
    body = null
  } else if (quotationQuery.isPending) {
    body = (
      <StateBlock
        variant="loading"
        title="กำลังเปิดใบเสนอราคา"
        reason="ระบบกำลังโหลดรายการ ราคา และผลตรวจสอบล่าสุด"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void quotationQuery.refetch()}
      >
        <SkeletonRows count={4} />
      </StateBlock>
    )
  } else if (quotationQuery.isError) {
    const error = quotationQuery.error
    const notFound = isApiError(error) && error.status === 404
    const forbidden = isForbiddenError(error)
    body = (
      <StateBlock
        variant={forbidden ? 'forbidden' : notFound ? 'empty' : 'error'}
        title={
          forbidden
            ? 'ไม่มีสิทธิ์เปิดใบเสนอราคานี้'
            : notFound
              ? 'ไม่พบใบเสนอราคา'
              : 'เปิดใบเสนอราคาไม่สำเร็จ'
        }
        reason={isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(error) ? error.traceId : undefined}
        actionLabel={notFound ? 'ปิดหน้าต่าง' : 'ลองใหม่'}
        onAction={() => (notFound ? onClose() : void quotationQuery.refetch())}
      />
    )
  } else if (!quotationQuery.data) {
    body = (
      <StateBlock
        variant="empty"
        title="ใบเสนอราคาไม่มีข้อมูล"
        reason="บริการตอบกลับสำเร็จแต่ไม่มีข้อมูลใบเสนอราคาที่เปิดอยู่"
        traceId="ไม่พบ traceId จากข้อมูลว่าง"
        actionLabel="ปิดหน้าต่าง"
        onAction={onClose}
      />
    )
  } else {
    const quotation = quotationQuery.data
    title = quotation.code
    description = `งาน ${quotation.jobNo} · ${quotation.vehicle.registration} ${quotation.vehicle.model || 'ไม่ระบุรุ่น'} · ${quotation.customer.name}`

    const lockedByOther = Boolean(quotation.lock) && quotation.lock!.userId !== activeSession?.user.userId
    const statusReadOnly = quotation.status !== 'draft'
    const readOnly = statusReadOnly || lockedByOther
    const validation = validationQuery.data
    const invalidReasons = validation?.errors.map(getIssueMessage) ?? []
    const sendDisabled =
      readOnly ||
      validationQuery.isPending ||
      validationQuery.isError ||
      !validation?.isValid ||
      pendingSaves > 0 ||
      sendMutation.isPending

    let disabledReason = ''
    if (lockedByOther) disabledReason = `${quotation.lock?.userName} กำลังแก้ไขอยู่`
    else if (statusReadOnly) disabledReason = `ใบเสนอราคาสถานะ “${quotation.statusLabelTh}” แก้ไขหรือส่งซ้ำไม่ได้`
    else if (pendingSaves > 0) disabledReason = 'กำลังบันทึกการแก้ไขล่าสุด'
    else if (validationQuery.isPending) disabledReason = 'กำลังตรวจสอบความพร้อมของใบเสนอราคา'
    else if (validationQuery.isError) disabledReason = 'ยังตรวจสอบความพร้อมไม่สำเร็จ กรุณาลองตรวจสอบใหม่'
    else if (!validation?.isValid) disabledReason = invalidReasons.join(' · ') || 'ใบเสนอราคายังไม่พร้อมส่ง'
    else if (sendMutation.isPending) disabledReason = 'กำลังส่งใบเสนอราคาให้ลูกค้า กรุณารอสักครู่'

    body = (
      <div className="editor-page editor-page--modal">
        <header className="editor-header">
          <div>
            <div className="editor-title-row">
              <h2>{quotation.code}</h2>
              <span className="version-chip">v{quotation.version}</span>
              <StatusChip status={quotation.status} label={quotation.statusLabelTh} />
            </div>
          </div>
          <div className="editor-header__actions">
            <span className={`save-indicator ${saveError ? 'save-indicator--error' : ''}`}>
              {saveError
                ? <CircleAlert aria-hidden="true" />
                : pendingSaves
                  ? <LoaderCircle className="spin" aria-hidden="true" />
                  : <CheckCircle2 aria-hidden="true" />}
              {saveError ? 'บันทึกไม่สำเร็จ' : pendingSaves ? 'กำลังบันทึก…' : 'บันทึกแล้ว'}
            </span>
            <Button variant="outline" onClick={() => onOpenDocument(quotation.id)}>
              <FileText aria-hidden="true" /> ดูเอกสาร
            </Button>
          </div>
        </header>

        {lockedByOther ? (
          <Alert className="readonly-banner readonly-banner--lock" role="status">
            <LockKeyhole className="readonly-banner__icon" aria-hidden="true" />
            <div>
              <AlertTitle>{quotation.lock?.userName} กำลังแก้ไขอยู่</AlertTitle>
              <AlertDescription>เปิดดูข้อมูลได้ แต่ระบบปิดการแก้ไขเพื่อป้องกันข้อมูลทับกัน</AlertDescription>
            </div>
          </Alert>
        ) : null}

        {statusReadOnly ? (
          <Alert className="readonly-banner" role="status">
            <Info className="readonly-banner__icon" aria-hidden="true" />
            <div>
              <AlertTitle>ใบเสนอราคานี้เป็นแบบอ่านอย่างเดียว</AlertTitle>
              <AlertDescription>สถานะปัจจุบันคือ {quotation.statusLabelTh} หากต้องเปลี่ยนรายการให้ออกฉบับแก้ไข</AlertDescription>
            </div>
            <Button variant="outline" onClick={() => setRevisionOpen(true)}>
              ออกฉบับแก้ไข
            </Button>
          </Alert>
        ) : null}

        {saveError ? (
          <Alert variant="destructive" className="save-error">
            <CircleAlert aria-hidden="true" />
            <div>
              <AlertTitle>{isApiError(saveError) ? saveError.messageTh : 'บันทึกรายการไม่สำเร็จ'}</AlertTitle>
              <AlertDescription className="trace-id">
                รหัสติดตาม (traceId): {isApiError(saveError) ? saveError.traceId : 'ไม่พบรหัสติดตาม'}
              </AlertDescription>
            </div>
            <Button variant="link" onClick={() => void quotationQuery.refetch()}>
              โหลดข้อมูลล่าสุด
            </Button>
          </Alert>
        ) : null}

        {techniciansQuery.isError ? (
          <Alert variant="destructive" className="save-error">
            <CircleAlert aria-hidden="true" />
            <div>
              <AlertTitle>
                {isApiError(techniciansQuery.error)
                  ? techniciansQuery.error.messageTh
                  : 'โหลดรายชื่อช่างไม่สำเร็จ'}
              </AlertTitle>
              <AlertDescription className="trace-id">
                รหัสติดตาม (traceId):{' '}
                {isApiError(techniciansQuery.error) ? techniciansQuery.error.traceId : 'ไม่พบรหัสติดตาม'}
              </AlertDescription>
            </div>
            <Button variant="link" onClick={() => void techniciansQuery.refetch()}>
              ลองใหม่
            </Button>
          </Alert>
        ) : null}

        <div className="editor-grid">
          <CatalogPanel
            readOnly={readOnly}
            adding={addMutation.isPending}
            onAdd={(item, source) => addMutation.mutate({ item, source })}
            onAddAdHoc={(payload) => addAdHocMutation.mutate(payload)}
            addingAdHoc={addAdHocMutation.isPending}
            quotationId={id}
            onApplied={applyServerQuotation}
          />
          <LineEditor
            lines={quotation.lines}
            technicians={techniciansQuery.data ?? []}
            readOnly={readOnly}
            canSeeCost={Boolean(activeSession?.user.canSeeCost)}
            deletingLineId={deleteMutation.isPending ? lineToDelete?.id ?? null : null}
            onPatch={patchLine}
            onRequestDelete={setLineToDelete}
          />
          <Card className="summary-panel">
            <div className="summary-panel__sticky">
              <div className="panel-heading">
                <div>
                  <span className="panel-heading__step">03</span>
                  <h3>สรุปยอด</h3>
                </div>
                <span className="panel-heading__hint">บาท</span>
              </div>
              <MoneySummary totals={quotation.totals} showCost={Boolean(activeSession?.user.canSeeCost)} />
              <div className="quotation-meta">
                <span>สร้างโดย</span>
                <strong>{quotation.createdByUserName}</strong>
                <span>ใช้ได้ถึง</span>
                <strong>{formatDate(quotation.validUntil)}</strong>
              </div>
              <WarningPanel
                validation={validation}
                pending={validationQuery.isPending || validationQuery.isFetching}
                error={validationQuery.error}
                onRetry={() => void validationQuery.refetch()}
              />
              <div className="send-area">
                <Tooltip content={sendDisabled ? disabledReason : 'ส่งใบเสนอราคาให้ลูกค้าตรวจสอบและอนุมัติ'}>
                  <span className="send-button-wrap">
                    <Button
                      className="button--wide"
                      disabled={sendDisabled}
                      aria-describedby={sendDisabled ? 'send-disabled-reason' : undefined}
                      onClick={() => sendMutation.mutate()}
                    >
                      {sendMutation.isPending
                        ? <LoaderCircle className="spin" aria-hidden="true" />
                        : <Send aria-hidden="true" />}
                      {sendMutation.isPending ? 'กำลังส่ง' : 'ส่งให้ลูกค้าอนุมัติ'}
                    </Button>
                  </span>
                </Tooltip>
                {sendDisabled && disabledReason ? (
                  <p className="disabled-reason" id="send-disabled-reason">
                    <CircleAlert aria-hidden="true" /> {disabledReason}
                  </p>
                ) : null}
                {sendMutation.isError ? (
                  <Alert variant="destructive" className="form-error">
                    <CircleAlert aria-hidden="true" />
                    <div>
                      <AlertTitle>
                        {isApiError(sendMutation.error)
                          ? sendMutation.error.messageTh
                          : 'ส่งใบเสนอราคาไม่สำเร็จ'}
                      </AlertTitle>
                      <AlertDescription className="trace-id">
                        รหัสติดตาม (traceId):{' '}
                        {isApiError(sendMutation.error) ? sendMutation.error.traceId : 'ไม่พบรหัสติดตาม'}
                      </AlertDescription>
                    </div>
                  </Alert>
                ) : null}
              </div>
            </div>
          </Card>
        </div>

        <ConfirmModal
          open={lineToDelete !== null}
          title="ลบรายการออกจากใบเสนอราคา"
          description={lineToDelete ? `รายการ “${lineToDelete.name}” จะถูกนำออกจากใบเสนอราคานี้` : undefined}
          onClose={() => {
            if (!deleteMutation.isPending) setLineToDelete(null)
          }}
          size="small"
          footer={
            <>
              <Button variant="ghost" onClick={() => setLineToDelete(null)}>
                ยกเลิก
              </Button>
              <Button
                variant="destructive"
                disabled={deleteMutation.isPending}
                onClick={() => {
                  if (lineToDelete) deleteMutation.mutate(lineToDelete.id)
                }}
              >
                {deleteMutation.isPending ? <LoaderCircle className="spin" aria-hidden="true" /> : <Trash2 aria-hidden="true" />}
                {deleteMutation.isPending ? 'กำลังลบ' : 'ลบรายการ'}
              </Button>
            </>
          }
        >
          <p className="modal-confirm-copy">ยอดรวมและผลตรวจสอบจะคำนวณใหม่หลังลบรายการ</p>
        </ConfirmModal>

        <RevisionModal
          open={revisionOpen}
          quotationId={quotation.id}
          jobId={quotation.jobId}
          onClose={() => setRevisionOpen(false)}
          onRevised={onRevised}
        />
      </div>
    )
  }

  return (
    <ConfirmModal open={quotationId !== null} title={title} description={description} onClose={onClose} size="xlarge">
      {body}
    </ConfirmModal>
  )
}

const revisionSchema = z.object({
  revisionReason: z.string().trim().min(5, 'กรุณาระบุเหตุผลอย่างน้อย 5 ตัวอักษร'),
})

type RevisionForm = z.infer<typeof revisionSchema>

function RevisionModal({
  open,
  quotationId,
  jobId,
  onClose,
  onRevised,
}: {
  open: boolean
  quotationId: string
  jobId: string
  onClose: () => void
  onRevised: (quotationId: string) => void
}) {
  const queryClient = useQueryClient()
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<RevisionForm>({ resolver: zodResolver(revisionSchema) })

  const mutation = useMutation({
    mutationFn: (values: RevisionForm) => reviseQuotation(quotationId, values.revisionReason),
    onSuccess: (quotation) => {
      reset()
      onClose()
      toast.success('สร้างใบเสนอราคาฉบับแก้ไขแล้ว')
      // [BUG แก้แล้ว] เดิมไม่ invalidate 'job-quotations' — รายการใบเสนอราคาใน JobCardModal ค้างสถานะเก่า
      // (เช่นใบก่อนหน้ายังโชว์ว่ายังไม่ superseded) จนกว่าจะปิด editor ทั้งหมด ทำให้ปุ่ม "สร้างใบเสนอราคา" ดูเหมือนถูก
      // disable ผิดพลาดค้างอยู่ทั้งที่ backend อนุญาตแล้ว (ล่าสุดเป็น superseded/rejected จริง)
      void queryClient.invalidateQueries({ queryKey: ['job-quotations', jobId] })
      void queryClient.invalidateQueries({ queryKey: ['job-detail', jobId] })
      onRevised(quotation.id)
    },
  })

  const close = () => {
    if (mutation.isPending) return
    mutation.reset()
    reset()
    onClose()
  }

  return (
    <ConfirmModal
      open={open}
      title="ออกใบเสนอราคาฉบับแก้ไข"
      description="ระบบจะสร้างเวอร์ชันใหม่และทำให้การอนุมัติในเวอร์ชันก่อนหน้าเป็นโมฆะ"
      onClose={close}
      footer={
        <>
          <Button variant="ghost" onClick={close}>
            ยกเลิก
          </Button>
          <Button
            type="submit"
            form="revision-form"
            disabled={mutation.isPending}
          >
            {mutation.isPending ? 'กำลังสร้าง…' : 'สร้างฉบับแก้ไข'}
          </Button>
        </>
      }
    >
      <form id="revision-form" onSubmit={handleSubmit((values) => mutation.mutate(values))}>
        <Label className="field">
          <span>เหตุผลที่แก้ไข</span>
          <Textarea
            rows={4}
            placeholder="เช่น ลูกค้าขอเปลี่ยนยี่ห้ออะไหล่และปรับจำนวน"
            {...register('revisionReason')}
          />
          {errors.revisionReason ? (
            <small className="field-error">{errors.revisionReason.message}</small>
          ) : null}
        </Label>
        {mutation.isError ? (
          <Alert variant="destructive" className="form-error">
            <CircleAlert aria-hidden="true" />
            <div>
              <AlertTitle>
                {isApiError(mutation.error) ? mutation.error.messageTh : 'สร้างฉบับแก้ไขไม่สำเร็จ'}
              </AlertTitle>
              <AlertDescription className="trace-id">
                รหัสติดตาม (traceId): {isApiError(mutation.error) ? mutation.error.traceId : 'ไม่พบรหัสติดตาม'}
              </AlertDescription>
            </div>
          </Alert>
        ) : null}
      </form>
    </ConfirmModal>
  )
}
