import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, CheckCircle2, Circle, ImagePlus, Loader2, MinusCircle } from 'lucide-react'
import { useRef, useState } from 'react'
import { toast } from 'sonner'
import { attachmentFileUrl, getJobAttachments, uploadAttachment } from '../../api/attachments'
import { isApiError } from '../../api/client'
import { getJobIntakeChecklist, saveIntakeChecklistItem, submitIntakeChecklist } from '../../api/intake'
import type { Attachment, IntakeCheckResult, IntakeChecklistItem, Job } from '../../api/types'
import { StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card'
import { formatDateTime } from '../../lib/format'

const RESULT_META: Record<Exclude<IntakeCheckResult, 'pending'>, { label: string; icon: typeof CheckCircle2; className: string }> = {
  ok: { label: 'ปกติ', icon: CheckCircle2, className: 'intake-item__result-btn--ok' },
  issue: { label: 'พบปัญหา', icon: AlertTriangle, className: 'intake-item__result-btn--issue' },
  na: { label: 'ไม่เกี่ยวข้อง', icon: MinusCircle, className: 'intake-item__result-btn--na' },
}

export function IntakeChecklistPanel({ job }: { job: Job }) {
  const queryClient = useQueryClient()

  const checklistQuery = useQuery({
    queryKey: ['job-intake-checklist', job.jobId],
    queryFn: () => getJobIntakeChecklist(job.jobId),
  })

  const attachmentsQuery = useQuery({
    queryKey: ['job-attachments', job.jobId, 'intake'],
    queryFn: () => getJobAttachments(job.jobId, 'intake'),
  })

  const submitMutation = useMutation({
    mutationFn: () => submitIntakeChecklist(job.jobId),
    onSuccess: () => {
      toast.success('ส่ง checklist สภาพรถขณะรับแล้ว')
      void queryClient.invalidateQueries({ queryKey: ['job-intake-checklist', job.jobId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'ส่ง checklist ไม่สำเร็จ')
    },
  })

  if (checklistQuery.isPending) {
    return (
      <Card>
        <CardContent className="job-detail-empty">
          <p>กำลังโหลด checklist สภาพรถ…</p>
        </CardContent>
      </Card>
    )
  }

  if (checklistQuery.isError || !checklistQuery.data) {
    return (
      <StateBlock
        variant="error"
        title="โหลด checklist สภาพรถไม่สำเร็จ"
        reason={isApiError(checklistQuery.error) ? checklistQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(checklistQuery.error) ? checklistQuery.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void checklistQuery.refetch()}
      />
    )
  }

  const checklist = checklistQuery.data
  const attachmentsByItemId = new Map<string, Attachment[]>()
  for (const a of attachmentsQuery.data ?? []) {
    if (!a.entityId) continue
    const list = attachmentsByItemId.get(a.entityId) ?? []
    list.push(a)
    attachmentsByItemId.set(a.entityId, list)
  }

  const categories = groupByCategory(checklist.items)
  const pendingCount = checklist.items.filter((i) => i.result === 'pending').length
  const issueCount = checklist.items.filter((i) => i.result === 'issue').length

  return (
    <Card>
      <CardHeader className="intake-checklist__header">
        <CardTitle>ตรวจสภาพรถขณะรับ (ขั้นที่ 3 ของรับรถ 6 ขั้น)</CardTitle>
        {checklist.isLocked ? (
          <span className="intake-checklist__locked-badge">
            <CheckCircle2 className="status-chip__icon" aria-hidden="true" />
            ส่งแล้วเมื่อ {formatDateTime(checklist.submittedAt!)} โดย {checklist.submittedByUserName}
          </span>
        ) : null}
      </CardHeader>
      <CardContent>
        <div className="intake-checklist__categories">
          {categories.map((category) => (
            <section key={category.key} className="intake-category">
              <h4 className="intake-category__title">{category.labelTh}</h4>
              <ul className="intake-category__list">
                {category.items.map((item) => (
                  <IntakeChecklistRow
                    key={item.itemCode}
                    job={job}
                    item={item}
                    locked={checklist.isLocked}
                    photos={attachmentsByItemId.get(item.id) ?? []}
                  />
                ))}
              </ul>
            </section>
          ))}
        </div>

        {!checklist.isLocked ? (
          <div className="job-card-panel-actions intake-checklist__submit-row">
            {pendingCount > 0 ? (
              <span className="intake-checklist__submit-hint">ยังตรวจไม่ครบ — เหลืออีก {pendingCount} รายการ</span>
            ) : issueCount > 0 ? (
              <span className="intake-checklist__submit-hint">พบสภาพที่ต้องบันทึกไว้ {issueCount} รายการ — ตรวจทานก่อนส่ง</span>
            ) : null}
            <Button
              onClick={() => submitMutation.mutate()}
              disabled={pendingCount > 0 || submitMutation.isPending}
              title={pendingCount > 0 ? `ยังตรวจไม่ครบ — เหลืออีก ${pendingCount} รายการ` : undefined}
            >
              {submitMutation.isPending ? 'กำลังส่ง…' : 'ส่ง checklist และล็อกรายการ'}
            </Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}

// หมวดมาจาก template ฝั่ง backend อยู่แล้ว แต่ label ไม่ได้ติดมากับ IntakeChecklistItem —
// ใช้ตารางคงที่นี้แทนการยิง /intake-checklist/template เพิ่มอีกครั้งสำหรับแค่ label
const CATEGORY_LABEL_TH: Record<string, string> = {
  exterior: 'ตรวจสอบภายนอกรอบคัน',
  wheels: 'ล้อและยาง',
  interior: 'ภายในห้องโดยสาร',
  underhood: 'ห้องเครื่องยนต์เบื้องต้น',
}

function groupByCategory(items: IntakeChecklistItem[]) {
  const order: string[] = []
  const byKey = new Map<string, { key: string; labelTh: string; items: IntakeChecklistItem[] }>()
  for (const item of items) {
    if (!byKey.has(item.categoryKey)) {
      byKey.set(item.categoryKey, { key: item.categoryKey, labelTh: CATEGORY_LABEL_TH[item.categoryKey] ?? item.categoryKey, items: [] })
      order.push(item.categoryKey)
    }
    byKey.get(item.categoryKey)!.items.push(item)
  }
  return order.map((key) => byKey.get(key)!)
}

function IntakeChecklistRow({
  job, item, locked, photos,
}: {
  job: Job
  item: IntakeChecklistItem
  locked: boolean
  photos: Attachment[]
}) {
  const queryClient = useQueryClient()
  const [draftResult, setDraftResult] = useState<Exclude<IntakeCheckResult, 'pending'> | null>(null)
  const [note, setNote] = useState(item.note ?? '')
  const fileInputRef = useRef<HTMLInputElement>(null)

  const saveMutation = useMutation({
    mutationFn: (input: { result: IntakeCheckResult; note?: string }) =>
      saveIntakeChecklistItem(job.jobId, item.itemCode, input),
    onSuccess: () => {
      setDraftResult(null)
      void queryClient.invalidateQueries({ queryKey: ['job-intake-checklist', job.jobId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'บันทึกรายการไม่สำเร็จ')
    },
  })

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadAttachment({ jobId: job.jobId, kind: 'intake', entityId: item.id, file }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['job-attachments', job.jobId, 'intake'] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'แนบรูปไม่สำเร็จ')
    },
  })

  const currentResult = item.result === 'pending' ? draftResult : item.result
  const needsNote = currentResult === 'issue' || currentResult === 'na'
  const canPhoto = item.result === 'issue'

  const selectResult = (result: Exclude<IntakeCheckResult, 'pending'>) => {
    if (result === 'ok') {
      saveMutation.mutate({ result: 'ok' })
      return
    }
    // issue/na ต้องมีหมายเหตุก่อนบันทึกจริง — เลือกไว้ก่อนแล้วค่อยกดบันทึก
    setDraftResult(result)
  }

  return (
    <li className="intake-item">
      <div className="intake-item__main">
        <div className="intake-item__text">
          <span className="intake-item__label">{item.labelTh}</span>
          {item.hintTh ? <span className="intake-item__hint">{item.hintTh}</span> : null}
        </div>
        <div className="intake-item__results" role="group" aria-label={`ผลตรวจ: ${item.labelTh}`}>
          {(['ok', 'issue', 'na'] as const).map((r) => {
            const meta = RESULT_META[r]
            const Icon = meta.icon
            const selected = currentResult === r
            return (
              <button
                key={r}
                type="button"
                className={[
                  'intake-item__result-btn',
                  meta.className,
                  selected ? 'intake-item__result-btn--selected' : '',
                ].filter(Boolean).join(' ')}
                disabled={locked || saveMutation.isPending}
                onClick={() => selectResult(r)}
              >
                <Icon className="status-chip__icon" aria-hidden="true" />
                {meta.label}
              </button>
            )
          })}
          {item.result === 'pending' && draftResult === null ? (
            <span className="intake-item__pending">
              <Circle className="status-chip__icon" aria-hidden="true" /> ยังไม่ได้ตรวจ
            </span>
          ) : null}
        </div>
      </div>

      {needsNote && !locked ? (
        <div className="intake-item__note-row">
          <textarea
            className="intake-item__note-input"
            placeholder={currentResult === 'na' ? 'เหตุผลที่ไม่เกี่ยวข้อง เช่น รถรุ่นนี้ไม่มีอุปกรณ์นี้' : 'อธิบายสภาพที่พบ เช่น รอยขีดข่วนที่ประตูซ้าย ยาว 5 ซม.'}
            value={note}
            onChange={(e) => setNote(e.target.value)}
          />
          <Button
            size="sm"
            disabled={!note.trim() || saveMutation.isPending}
            title={!note.trim() ? 'ต้องระบุหมายเหตุก่อนบันทึก' : undefined}
            onClick={() => saveMutation.mutate({ result: currentResult!, note: note.trim() })}
          >
            {saveMutation.isPending ? 'กำลังบันทึก…' : 'บันทึกรายการนี้'}
          </Button>
        </div>
      ) : needsNote && locked && item.note ? (
        <p className="intake-item__note-locked">{item.note}</p>
      ) : null}

      {canPhoto ? (
        <div className="intake-item__photos">
          {photos.map((p) => (
            <a key={p.id} className="intake-item__photo" href={attachmentFileUrl(p.relativePath)} target="_blank" rel="noreferrer">
              <img src={attachmentFileUrl(p.relativePath)} alt={p.fileName} loading="lazy" />
            </a>
          ))}
          {!locked ? (
            <>
              <input
                ref={fileInputRef}
                type="file"
                accept="image/*"
                className="intake-item__file-input"
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  if (file) uploadMutation.mutate(file)
                  e.target.value = ''
                }}
              />
              <Button
                variant="outline"
                size="sm"
                disabled={uploadMutation.isPending}
                onClick={() => fileInputRef.current?.click()}
              >
                {uploadMutation.isPending ? <Loader2 className="status-chip__icon intake-item__spin" aria-hidden="true" /> : <ImagePlus className="status-chip__icon" aria-hidden="true" />}
                แนบรูป
              </Button>
            </>
          ) : null}
        </div>
      ) : null}
    </li>
  )
}
