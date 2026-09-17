import { apiRequest } from './client'
import type {
  ApplyTemplateInput,
  CreateQuotationInput,
  Quotation,
  QuotationSummary,
  QuotationValidation,
  UpsertLine,
} from './types'

export type LineDecisionInput = { decision: 'Approved' | 'Rejected'; rejectReason: string | null }

export type SignQuotationInput = {
  signatureImagePath: string
  consentText: string
  deviceInfo: string | null
  witnessEmployeeId: number
  witnessEmployeeName: string
}

export type QuotationFilter = '' | 'todo' | 'wait' | 'rev' | 'done'

export function getQuotations(filter: QuotationFilter, jobId?: string) {
  const params = new URLSearchParams()
  if (filter) params.set('filter', filter)
  if (jobId) params.set('jobId', jobId)
  const query = params.toString()
  return apiRequest<QuotationSummary[]>(`/api/v1/quotations${query ? `?${query}` : ''}`)
}

export function getQuotation(id: string | number) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}`)
}

export function createQuotation(input: CreateQuotationInput) {
  return apiRequest<Quotation>('/api/v1/quotations', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function addQuotationLine(id: string | number, input: UpsertLine) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/lines`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function updateQuotationLine(
  id: string | number,
  lineId: string | number,
  input: UpsertLine,
) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/lines/${lineId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function deleteQuotationLine(id: string | number, lineId: string | number) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/lines/${lineId}`, {
    method: 'DELETE',
  })
}

/// เพิ่มหลายบรรทัดจากเทมเพลตในครั้งเดียว — docs/08-quotation-template.md
export function applyQuotationTemplate(id: string | number, input: ApplyTemplateInput) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/lines/from-template`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function validateQuotation(id: string | number) {
  return apiRequest<QuotationValidation>(`/api/v1/quotations/${id}/validate`)
}

export function sendQuotation(id: string | number) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/send`, { method: 'POST' })
}

export function reviseQuotation(id: string | number, revisionReason: string) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/revise`, {
    method: 'POST',
    body: JSON.stringify({ revisionReason }),
  })
}

export function decideQuotationLine(
  id: string | number,
  lineId: string | number,
  input: LineDecisionInput,
) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/lines/${lineId}/decision`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function signQuotation(id: string | number, input: SignQuotationInput) {
  return apiRequest<Quotation>(`/api/v1/quotations/${id}/sign`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}
