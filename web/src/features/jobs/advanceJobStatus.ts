import { transitionJob } from '../../api/jobs'
import type { JobStatusToken } from '../../api/types'

/**
 * [ASSUME] ข้ามขั้นตรวจเช็คจากเว็บ — ตรวจเช็ค 31 รายการของช่างบนมือถือยังไม่มี
 * ใช้ checklist สภาพรถ 20 รายการที่ส่งแล้วแทน · guard `InspectionComplete` เป็น manual override
 * จึงต้องส่ง reason เสมอ
 */
export const INSPECTION_BYPASS_REASON =
  'ยืนยันแทนขั้นตรวจสอบจากเว็บ — ใช้ checklist สภาพรถ 20 รายการที่ส่งแล้วแทนตรวจเช็ค 31 รายการของช่างบนมือถือ (อยู่ระหว่างพัฒนา)'

/** สถานะที่ยังไม่ถึง "รออนุมัติ" — ต้องไต่ขึ้นไปทีละขั้นเพราะ state machine ไม่มีทางลัด */
const BEFORE_WAIT_APPROVE: JobStatusToken[] = ['waitinspect', 'waitquote']

/**
 * ดันจ๊อบขึ้นไปถึง "รออนุมัติ" ผ่านทุกขั้นที่ยังขาด
 *
 * [BIZ] มีไว้เพราะสถานะ "ใบเสนอราคา" กับสถานะ "จ๊อบ" เดินคนละจังหวะกันได้ง่ายมาก:
 * การส่งใบเสนอราคาเปลี่ยนสถานะใบให้เป็น `sent` เสมอ แต่การขยับจ๊อบเป็นคนละคำสั่งที่ล้มได้เงียบๆ
 * ถ้าจ๊อบยังอยู่ `waitinspect` (ไม่มีเส้นทาง `waitinspect → waitapprove`)
 *
 * ผลของการล้มเงียบคือ **จ๊อบค้างถาวร**: ลูกค้าเซ็นอนุมัติจากมือถือได้ (เพราะคิวอนุมัติดูสถานะ "ใบ"
 * ไม่ใช่สถานะ "จ๊อบ") แต่แอปดันจ๊อบต่อไม่ได้เพราะ `waitquote → approved` ไม่มีเส้นทาง
 * — เจอจริงกับจ๊อบ JB2609180227003 (2026-09-21) ซึ่งต้องมาแก้ด้วยการยิง API ตรงๆ
 */
export async function advanceJobToWaitApprove(
  jobId: string,
  currentStatus: JobStatusToken,
): Promise<void> {
  if (!BEFORE_WAIT_APPROVE.includes(currentStatus)) return

  if (currentStatus === 'waitinspect') {
    await transitionJob(jobId, { toStatus: 'waitquote', reason: INSPECTION_BYPASS_REASON })
  }
  await transitionJob(jobId, { toStatus: 'waitapprove' })
}
