import { apiRequest } from './client'
import type {
  IntakeChecklist,
  IntakeChecklistItem,
  IntakeChecklistTemplateItem,
  SaveIntakeChecklistItemInput,
  SubmitIntakeChecklistResult,
} from './types'

export function getIntakeChecklistTemplate() {
  return apiRequest<IntakeChecklistTemplateItem[]>('/api/v1/intake-checklist/template')
}

export function getJobIntakeChecklist(jobId: string) {
  return apiRequest<IntakeChecklist>(`/api/v1/jobs/${jobId}/intake-checklist`)
}

export function saveIntakeChecklistItem(
  jobId: string,
  itemCode: string,
  input: SaveIntakeChecklistItemInput,
) {
  return apiRequest<IntakeChecklistItem>(
    `/api/v1/jobs/${jobId}/intake-checklist/items/${itemCode}`,
    { method: 'PUT', body: JSON.stringify(input) },
  )
}

export function submitIntakeChecklist(jobId: string) {
  return apiRequest<SubmitIntakeChecklistResult>(
    `/api/v1/jobs/${jobId}/intake-checklist/submit`,
    { method: 'POST' },
  )
}
