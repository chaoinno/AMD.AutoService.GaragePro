import { apiRequest } from './client'

export type HandoverItem = { id: string; itemCode: string; name: string; isReturned: boolean; note: string | null; updatedAt: string | null; updatedByUserName: string | null }
export type Handover = {
  id: string
  jobId: string
  isLocked: boolean
  signatureImagePath: string | null
  submittedAt: string | null
  submittedByUserName: string | null
  /** [BIZ] ต้องออกใบเสร็จก่อนจึงจะยืนยันส่งมอบได้ (HANDOVER_RECEIPT_REQUIRED) */
  receiptIssued: boolean
  receiptDocumentNo: string | null
  items: HandoverItem[]
  /** เลขไมล์ขณะรับรถของจ๊อบ — ไว้เทียบกับไมล์ส่งมอบ (ไม่มีใน JSON ถ้ายังไม่บันทึก) */
  mileageAtIntake?: number | null
  mileageAtHandover?: number | null
  /** นัดเข้ารับบริการครั้งถัดไปได้หลายรายการ (น้ำมันเครื่อง/กรอง/เบรก…) — ต้องมีอย่างน้อย 1 รายการก่อนเซ็น */
  serviceItems?: HandoverServiceItem[]
  /** ฟิลด์เดี่ยวเดิม = รายการที่ครบเร็วที่สุด (ไว้ให้แอปรุ่นเก่า) — เว็บอ่านจาก serviceItems แทน */
  nextServiceMileage?: number | null
  nextServiceMonths?: number | null
  nextServiceDueOn?: string | null
}

export type HandoverServiceItem = {
  id: string
  templateId?: string | null
  name: string
  nextServiceMileage: number
  nextServiceMonths: number
  /** yyyy-MM-dd ตามปฏิทินไทย — ก่อนเซ็นเป็นพรีวิวจากวันที่บันทึก ตอนเซ็นระบบคำนวณใหม่จากวันส่งมอบจริง */
  nextServiceDueOn: string
}

export type HandoverServiceItemInput = {
  templateId?: string | null
  name: string
  nextServiceMileage: number
  nextServiceMonths: number
}

export type HandoverServiceInfoInput = {
  mileageAtHandover: number
  items: HandoverServiceItemInput[]
}

export const getHandover = (jobId: string) => apiRequest<Handover>(`/api/v1/jobs/${jobId}/handover`)

export const saveHandoverItem = (jobId: string, itemId: string, input: { isReturned: boolean; note: string | null }) =>
  apiRequest<HandoverItem>(`/api/v1/jobs/${jobId}/handover/items/${itemId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })

/** เลขไมล์ตอนส่งมอบ + นัดครั้งถัดไป — ต้องบันทึกก่อนเซ็น (HANDOVER_SERVICE_INFO_REQUIRED) */
export const saveHandoverServiceInfo = (jobId: string, input: HandoverServiceInfoInput) =>
  apiRequest<Handover>(`/api/v1/jobs/${jobId}/handover/service-info`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })

export const submitHandover = (jobId: string, signatureAttachmentPath: string) =>
  apiRequest<Handover>(`/api/v1/jobs/${jobId}/handover/submit`, {
    method: 'PUT',
    body: JSON.stringify({ signatureAttachmentPath }),
  })
