import { apiRequest } from './client'
import type { CatalogItem, CatalogItemInput, CatalogManagementItem, PagedResult, Technician } from './types'

export function searchCatalog(query: string) {
  return apiRequest<CatalogItem[]>(`/api/v1/catalog?q=${encodeURIComponent(query)}`)
}

export function getTechnicians() {
  return apiRequest<Technician[]>('/api/v1/technicians')
}

export type CatalogFilters = {
  keyword?: string
  type?: 'part' | 'labor'
  includeInactive?: boolean
  lowStockOnly?: boolean
  page?: number
  pageSize?: number
}

function queryString(values: Record<string, unknown>) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '' || value === false) return
    params.set(key, String(value))
  })
  return params.size ? `?${params}` : ''
}

export const getCatalogItems = (filters: CatalogFilters) =>
  apiRequest<PagedResult<CatalogManagementItem>>(`/api/v1/catalog/manage${queryString(filters)}`)

export const getCatalogItem = (id: string) =>
  apiRequest<CatalogManagementItem>(`/api/v1/catalog/manage/${id}`)

export const createCatalogItem = (input: CatalogItemInput) =>
  apiRequest<CatalogManagementItem>('/api/v1/catalog/manage', { method: 'POST', body: JSON.stringify(input) })

export const updateCatalogItem = (id: string, input: CatalogItemInput) =>
  apiRequest<CatalogManagementItem>(`/api/v1/catalog/manage/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const setCatalogItemStatus = (id: string, isActive: boolean) =>
  apiRequest<boolean>(`/api/v1/catalog/manage/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) })
