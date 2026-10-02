import { useQuery } from '@tanstack/react-query'
import { getJobIntakeChecklist } from '../../api/intake'
import { getJob } from '../../api/jobs'
import type { JobStatusToken, Quotation } from '../../api/types'
import { useSession } from '../../lib/session'

export const BEFORE_APPROVED: JobStatusToken[] = ['waitinspect', 'waitquote', 'waitapprove']
const TERMINAL: JobStatusToken[] = ['completed', 'cancelled']

/**
 * ตัดสินว่าใบเสนอราคานี้ให้พนักงาน "ดำเนินการแทนลูกค้า" (CustomerApprovalModal) ได้หรือไม่
 * ใช้ query key เดียวกับ JobCardModal (`job-detail`/`job-intake-checklist`) จึงอ่านจาก cache ที่การ์ดจ๊อบโหลดไว้แล้ว
 *
 * - `visible` — ใบส่งแล้วรอลูกค้า หรือใบเซ็นแล้วแต่จ๊อบยังค้างก่อน "อนุมัติแล้ว"
 *   (ลูกค้าเซ็นจากมือถือแล้วแต่จ๊อบขยับตามไม่ได้ — เจอจริง 2026-09-21 ถ้าไม่รวม ทางกู้จ๊อบจะหายไปพร้อมกัน)
 * - `blockedReason` — [UI] เหตุผลที่กดไม่ได้ (ต้องแสดงเสมอ ห้าม disable เฉยๆ)
 */
export function useActForCustomer(quotation: Quotation | undefined) {
  const { session } = useSession()
  const jobId = quotation?.jobId ?? null

  const jobQuery = useQuery({
    queryKey: ['job-detail', jobId],
    queryFn: () => getJob(jobId!),
    enabled: jobId !== null,
  })
  const job = jobQuery.data ?? null

  const checklistQuery = useQuery({
    queryKey: ['job-intake-checklist', jobId],
    queryFn: () => getJobIntakeChecklist(jobId!),
    enabled: job?.status === 'waitinspect',
  })

  // [BIZ] จ๊อบมีใบเสนอราคาได้หลายใบ — ใบที่ส่งแล้วยังไม่เซ็น (sent) ให้ลูกค้าอนุมัติได้ทุกขั้นจนกว่าจ๊อบจะปิด
  // (เช่น ใบที่สองระหว่างซ่อม) · ใบที่เซ็นแล้ว (approved/partial) โชว์เฉพาะตอนจ๊อบค้างก่อน "อนุมัติแล้ว"
  const visible =
    Boolean(job && quotation) &&
    ((quotation!.status === 'sent' && !TERMINAL.includes(job!.status)) ||
      (BEFORE_APPROVED.includes(job!.status) && ['partial', 'approved'].includes(quotation!.status)))

  // FrontDesk/Office/Manager ตรงกับ role ของ JobStateMachine.WaitApprove→Approved
  // ไต่จาก waitinspect/waitquote ไปรออนุมัติ เปิดเฉพาะ Office/Manager
  const role = session?.user.role.toLowerCase() ?? ''
  const needsAdvance = job?.status === 'waitinspect' || job?.status === 'waitquote'
  const checklistDone = checklistQuery.data?.isLocked ?? false

  const blockedReason = !['frontdesk', 'office', 'manager'].includes(role)
    ? 'สำหรับพนักงานหน้าร้าน ธุรการ หรือผู้จัดการเท่านั้น'
    : needsAdvance && !['office', 'manager'].includes(role)
      ? 'จ๊อบยังไม่ถึงขั้น "รออนุมัติ" — ต้องให้ผู้จัดการหรือธุรการดำเนินการ'
      : job?.status === 'waitinspect' && !checklistDone
        ? checklistQuery.isPending
          ? 'กำลังตรวจสอบ checklist สภาพรถขณะรับ'
          : 'ยังไม่ได้ส่ง checklist สภาพรถขณะรับ — ส่งให้ครบก่อนจึงข้ามขั้นตรวจสอบได้'
        : null

  return { job, visible, blockedReason }
}
