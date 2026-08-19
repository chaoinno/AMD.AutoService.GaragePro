import { apiRequest } from './client'
import type { CatalogItem, Technician } from './types'

export function searchCatalog(query: string) {
  return apiRequest<CatalogItem[]>(`/api/v1/catalog?q=${encodeURIComponent(query)}`)
}

export function getTechnicians() {
  return apiRequest<Technician[]>('/api/v1/technicians')
}
