import { apiRequest } from './client'

export type QcItemResult = 'pending' | 'pass'

export type QcChecklistItem = {
  id: string
  quotationLineId: string
  catalogCode: string
  name: string
  type: 'part' | 'labor'
  result: QcItemResult
  note: string | null
  updatedAt: string | null
  updatedByUserName: string | null
}

export type QcChecklist = {
  id: string
  jobId: string
  isLocked: boolean
  testDriveKm: number | null
  testDriveNote: string | null
  testDriveRecordedAt: string | null
  testDriveRecordedByUserName: string | null
  submittedAt: string | null
  submittedByUserName: string | null
  items: QcChecklistItem[]
}

export const getQcChecklist = (jobId: string) => apiRequest<QcChecklist>(`/api/v1/jobs/${jobId}/qc-checklist`)

export const saveQcChecklistItem = (jobId: string, itemId: string, input: { result: QcItemResult; note: string | null }) =>
  apiRequest<QcChecklistItem>(`/api/v1/jobs/${jobId}/qc-checklist/items/${itemId}`, { method: 'PUT', body: JSON.stringify(input) })

export const saveQcTestDrive = (jobId: string, input: { km: number; note: string }) =>
  apiRequest<QcChecklist>(`/api/v1/jobs/${jobId}/qc-checklist/test-drive`, { method: 'PUT', body: JSON.stringify(input) })
