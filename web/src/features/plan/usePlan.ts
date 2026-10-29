import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'
import {
  getSelfServeStatus,
  isExtensionAlreadyRequested,
  isSelfServeNotApplicable,
  requestExtension,
  type SelfServeStatus,
} from '../../api/selfserve'
import type { PlanInfo, WorkspaceInfo } from '../../api/types'
import { FEATURES, isFeatureLocked, type FeatureKey } from '../../lib/plan'
import { useSession } from '../../lib/session'
import { isExtensionRecentlyRequested, isSelfServePlan, lockedActionReason } from './trialUx'

export const ME_QUERY_KEY = ['me'] as const
export const SELFSERVE_STATUS_QUERY_KEY = ['selfserve-status'] as const
/** ธงในแคชว่าเพิ่งส่งคำขอต่ออายุ (สำเร็จหรือ 409) — ใช้ร่วมกันระหว่างแถบ/หน้าต่าง/หน้าฟีเจอร์ที่ล็อก */
const EXTENSION_SENT_QUERY_KEY = ['selfserve-extension-sent'] as const

/** แพ็กเกจจากเซสชันที่เก็บไว้ (อ่านอย่างเดียว ไม่ยิง API) — undefined = ลูกค้าเดิม */
export function useSessionPlan(): { plan: PlanInfo | undefined; workspace: WorkspaceInfo | undefined } {
  const { session } = useSession()
  return { plan: session?.user.plan, workspace: session?.user.workspace }
}

/**
 * เรียกครั้งเดียวใน AppShell: อ่าน plan/workspace สดจาก /auth/me (เปลี่ยนได้ระหว่างใช้งาน เช่น ทีมขายต่ออายุ/หมดอายุ)
 * แล้วเขียนกลับเข้าเซสชัน + ตั้ง `body[data-plan]` ให้ CSS ลายน้ำเอกสารพิมพ์ — ลูกค้าเดิมไม่มี plan จึงไม่มีอะไรเปลี่ยน
 */
export function usePlan() {
  const { session } = useSession()
  const plan = session?.user.plan
  const workspace = session?.user.workspace
  const kind = plan?.kind ?? ''
  useEffect(() => {
    if (typeof document === 'undefined') return
    if (kind) document.body.dataset.plan = kind
    else delete document.body.dataset.plan
    return () => {
      delete document.body.dataset.plan
    }
  }, [kind])

  return { plan, workspace }
}

/** ปุ่มที่ล็อกตามแพ็กเกจ (เช่น ส่งออก) — `reason` แสดงเป็นข้อความจริงข้างปุ่ม */
export function useFeatureLock(key: FeatureKey): { locked: boolean; reason: string } {
  const { plan } = useSessionPlan()
  const locked = isFeatureLocked(plan, key)
  return { locked, reason: lockedActionReason(FEATURES[key].label) }
}

/** GET /selfserve/status — ยิงเฉพาะอู่ที่สมัครเอง · 404 (ไม่ใช่ self-serve) ไม่ลองซ้ำ */
export function useSelfServeStatus(enabled = true) {
  const { plan } = useSessionPlan()
  return useQuery<SelfServeStatus>({
    queryKey: SELFSERVE_STATUS_QUERY_KEY,
    queryFn: getSelfServeStatus,
    enabled: enabled && isSelfServePlan(plan),
    staleTime: 60_000,
    retry: (failureCount, error) => !isSelfServeNotApplicable(error) && failureCount < 1,
  })
}

/**
 * คำขอต่ออายุ/สนใจฟีเจอร์ (POST /selfserve/extension-request) — ส่งได้วันละครั้ง
 * `alreadyRequested` = ส่งแล้วใน 24 ชม. (จาก status) หรือเพิ่งส่งสำเร็จ/ได้ 409 ในแท็บนี้
 */
export function useExtensionRequest() {
  const queryClient = useQueryClient()
  const status = useSelfServeStatus()
  const sent = useQuery({
    queryKey: EXTENSION_SENT_QUERY_KEY,
    queryFn: () => false,
    enabled: false,
    staleTime: Infinity,
  })
  const markSent = () => {
    queryClient.setQueryData(EXTENSION_SENT_QUERY_KEY, true)
    void queryClient.invalidateQueries({ queryKey: SELFSERVE_STATUS_QUERY_KEY })
  }
  const mutation = useMutation({
    mutationFn: requestExtension,
    onSuccess: markSent,
    onError: (error) => {
      if (isExtensionAlreadyRequested(error)) markSent()
    },
  })
  const alreadyRequested = sent.data === true || isExtensionRecentlyRequested(status.data?.extensionRequestedAt)
  return { mutation, alreadyRequested }
}
