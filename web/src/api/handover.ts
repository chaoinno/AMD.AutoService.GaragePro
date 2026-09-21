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
}

export const getHandover = (jobId: string) => apiRequest<Handover>(`/api/v1/jobs/${jobId}/handover`)

export const saveHandoverItem = (jobId: string, itemId: string, input: { isReturned: boolean; note: string | null }) =>
  apiRequest<HandoverItem>(`/api/v1/jobs/${jobId}/handover/items/${itemId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })

export const submitHandover = (jobId: string, signatureAttachmentPath: string) =>
  apiRequest<Handover>(`/api/v1/jobs/${jobId}/handover/submit`, {
    method: 'PUT',
    body: JSON.stringify({ signatureAttachmentPath }),
  })
