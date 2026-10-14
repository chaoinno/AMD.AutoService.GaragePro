import { CheckCircle2, CircleAlert, ExternalLink, Lock, MessageCircle, Send } from 'lucide-react'
import { createContext, type ReactNode, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { isApiError, PLAN_ERROR_EVENT } from '../../api/client'
import { isExtensionAlreadyRequested } from '../../api/selfserve'
import type { ApiError } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Label } from '../../components/ui/label'
import { Textarea } from '../../components/ui/textarea'
import { useExtensionRequest, useSessionPlan } from './usePlan'
import { EXTENSION_REQUESTED_REASON, isSelfServePlan, LINE_OA_ID, LINE_OA_URL } from './trialUx'
import './plan.css'

export type TrialDialogRequest = {
  /** ข้อความตั้งต้นในช่องหมายเหตุ เช่น "สนใจฟีเจอร์: จัดซื้อ" */
  note?: string
  /** ชื่อฟีเจอร์ที่ผู้ใช้สนใจ — เปลี่ยนหัวข้อหน้าต่างให้ตรงกับที่กดมา */
  featureLabel?: string
  /** error เรื่องแพ็กเกจจาก API (TRIAL_EXPIRED / PLAN_LIMIT_REACHED) — แสดง messageTh + traceId ของ server */
  error?: ApiError
}

type OpenTrialDialog = (request?: TrialDialogRequest) => void

const TrialDialogContext = createContext<OpenTrialDialog | null>(null)

/** เปิดหน้าต่างขอต่ออายุ/สนใจแพ็กเกจ — นอก provider (เช่น ลูกค้าเดิม) เป็น no-op */
export function useTrialDialog(): OpenTrialDialog {
  return useContext(TrialDialogContext) ?? noop
}

function noop() {}

// เปิดหน้าต่างเองเฉพาะ error ที่ "ทำต่อไม่ได้จนกว่าจะติดต่อทีมงาน" — ฟีเจอร์ล็อกมีหน้า LockedFeaturePage อธิบายอยู่แล้ว
const DIALOG_ERROR_CODES = new Set(['TRIAL_EXPIRED', 'PLAN_LIMIT_REACHED'])

/**
 * ครอบเนื้อหาของ AppShell: ถือสถานะหน้าต่างทดลองใช้ + ฟัง `garagepro-plan-error` จาก api/client
 * ลูกค้าเดิม (ไม่มี plan หรือ legacy) ไม่ฟัง event และไม่ render หน้าต่างเลย
 */
export function TrialDialogProvider({ children }: { children: ReactNode }) {
  const { plan } = useSessionPlan()
  const active = isSelfServePlan(plan)
  const [request, setRequest] = useState<TrialDialogRequest | null>(null)

  const open = useCallback<OpenTrialDialog>((next) => {
    setRequest(next ?? {})
  }, [])

  useEffect(() => {
    if (!active || typeof window === 'undefined') return
    const onPlanError = (event: Event) => {
      const error = (event as CustomEvent<unknown>).detail
      if (!isApiError(error) || !DIALOG_ERROR_CODES.has(error.code)) return
      setRequest((current) => ({ ...(current ?? {}), error }))
    }
    window.addEventListener(PLAN_ERROR_EVENT, onPlanError)
    return () => window.removeEventListener(PLAN_ERROR_EVENT, onPlanError)
  }, [active])

  const value = useMemo(() => (active ? open : noop), [active, open])

  return (
    <TrialDialogContext.Provider value={value}>
      {children}
      {active && request ? <TrialDialog request={request} onClose={() => setRequest(null)} /> : null}
    </TrialDialogContext.Provider>
  )
}

function TrialDialog({ request, onClose }: { request: TrialDialogRequest; onClose: () => void }) {
  const { mutation, alreadyRequested } = useExtensionRequest()
  const [note, setNote] = useState(request.note ?? '')
  // requestId ใหม่ทุกครั้งที่แก้หมายเหตุ — กด "ลองใหม่" หลังเน็ตหลุดโดยไม่แก้อะไรใช้ค่าเดิม ทีมขายไม่ได้ข้อความซ้ำ
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())
  const { reset } = mutation
  useEffect(() => {
    setNote(request.note ?? '')
    setRequestId(crypto.randomUUID())
    reset()
  }, [request.note, reset])

  const sentNow = mutation.isSuccess || (mutation.isError && isExtensionAlreadyRequested(mutation.error))
  const failed = mutation.isError && !isExtensionAlreadyRequested(mutation.error) ? mutation.error : null
  const disabledReason = alreadyRequested || sentNow
    ? EXTENSION_REQUESTED_REASON
    : mutation.isPending ? 'กำลังส่งคำขอ…' : null

  const title = request.featureLabel
    ? `สนใจ${request.featureLabel} — ติดต่อทีมงาน`
    : request.error?.code === 'PLAN_LIMIT_REACHED'
      ? 'ครบจำนวนที่แพ็กเกจทดลองใช้รองรับ'
      : request.error?.code === 'TRIAL_EXPIRED'
        ? 'ช่วงทดลองใช้สิ้นสุดแล้ว'
        : 'ขอต่ออายุ / สมัครแพ็กเกจ'

  return (
    <ConfirmModal
      open
      title={title}
      description="ส่งคำขอถึงทีมขาย ServicePro พร้อมชื่ออู่และเบอร์โทรที่ใช้สมัคร — ทีมงานจะติดต่อกลับ"
      onClose={onClose}
      size="medium"
      footer={
        <div className="trial-dialog__footer">
          {disabledReason ? (
            <span className="trial-dialog__reason" role="status">
              {sentNow || alreadyRequested ? <CheckCircle2 aria-hidden="true" /> : null}
              {disabledReason}
            </span>
          ) : null}
          <Button variant="ghost" onClick={onClose}>ปิด</Button>
          <Button
            disabled={Boolean(disabledReason)}
            title={disabledReason ?? undefined}
            onClick={() => mutation.mutate({ requestId, note })}
          >
            <Send aria-hidden="true" /> {mutation.isPending ? 'กำลังส่ง…' : 'ส่งคำขอถึงทีมงาน'}
          </Button>
        </div>
      }
    >
      <div className="trial-dialog">
        {request.error ? (
          <Alert variant={request.error.code === 'TRIAL_EXPIRED' ? 'destructive' : 'warning'}>
            {request.error.code === 'TRIAL_EXPIRED' ? <Lock aria-hidden="true" /> : <CircleAlert aria-hidden="true" />}
            <div>
              <AlertTitle>{request.error.messageTh}</AlertTitle>
              <AlertDescription>รหัสติดตาม (traceId): {request.error.traceId || 'ไม่พบรหัสติดตาม'}</AlertDescription>
            </div>
          </Alert>
        ) : null}

        {sentNow ? (
          <Alert variant="success">
            <CheckCircle2 aria-hidden="true" />
            <div>
              <AlertTitle>{EXTENSION_REQUESTED_REASON}</AlertTitle>
              <AlertDescription>ระหว่างนี้ยังดูและพิมพ์ข้อมูลเดิมได้ตามปกติ หรือทักทีมงานทาง LINE {LINE_OA_ID} ได้ทันที</AlertDescription>
            </div>
          </Alert>
        ) : null}

        {failed ? (
          <Alert variant="destructive">
            <CircleAlert aria-hidden="true" />
            <div>
              <AlertTitle>{isApiError(failed) ? failed.messageTh : 'ส่งคำขอไม่สำเร็จ'}</AlertTitle>
              <AlertDescription>
                รหัสติดตาม (traceId): {isApiError(failed) ? failed.traceId : 'ไม่พบรหัสติดตาม'} — กด "ส่งคำขอถึงทีมงาน" อีกครั้ง หรือทัก LINE {LINE_OA_ID}
              </AlertDescription>
            </div>
          </Alert>
        ) : null}

        <Label className="field">
          <span>ข้อความถึงทีมงาน (ไม่บังคับ)</span>
          <Textarea
            rows={3}
            maxLength={500}
            value={note}
            disabled={Boolean(disabledReason) && !mutation.isPending}
            placeholder="เช่น สะดวกให้โทรกลับช่วงบ่าย · จำนวนช่างในอู่ · ฟีเจอร์ที่อยากใช้"
            onChange={(event) => {
              setNote(event.target.value)
              setRequestId(crypto.randomUUID())
            }}
          />
        </Label>

        <div className="trial-dialog__contact">
          <MessageCircle aria-hidden="true" />
          <span>ต้องการคุยทันที? ทักทีมงานทาง LINE {LINE_OA_ID}</span>
          <a className="ui-button ui-button--outline ui-button--size-sm" href={LINE_OA_URL} target="_blank" rel="noopener noreferrer">
            เปิด LINE <ExternalLink aria-hidden="true" />
          </a>
        </div>
      </div>
    </ConfirmModal>
  )
}
