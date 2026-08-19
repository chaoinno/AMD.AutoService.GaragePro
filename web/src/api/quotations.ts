import { apiRequest } from './client'
import type {
  CreateQuotationInput,
  Quotation,
  QuotationSummary,
  QuotationValidation,
  UpsertLine,
} from './types'

export type QuotationFilter = '' | 'todo' | 'wait' | 'rev' | 'done'

export function getQuotations(filter: QuotationFilter) {
  const query = filter ? `?filter=${encodeURIComponent(filter)}` : ''
  return apiRequest<QuotationSummary[]>(`/api/v1/quotations${query}`)
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
