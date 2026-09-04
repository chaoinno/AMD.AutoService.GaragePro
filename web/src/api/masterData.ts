import { apiRequest } from './client'
import type {
  CatalogCategory,
  CatalogCategoryInput,
  CatalogItemSupplier,
  CatalogItemSupplierInput,
  PagedResult,
  Supplier,
  SupplierInput,
  Warehouse,
  WarehouseInput,
} from './types'

function queryString(values: Record<string, unknown>) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '' || value === false) return
    params.set(key, String(value))
  })
  return params.size ? `?${params}` : ''
}

export const getSuppliers = (filters: { keyword?: string; includeInactive?: boolean; page?: number; pageSize?: number } = {}) =>
  apiRequest<Supplier[] | PagedResult<Supplier>>(`/api/v1/suppliers${queryString({ q: filters.keyword, includeInactive: filters.includeInactive, page: filters.page, pageSize: filters.pageSize })}`)
export const getSupplier = (id: string) => apiRequest<Supplier>(`/api/v1/suppliers/${id}`)
export const createSupplier = (input: SupplierInput) => apiRequest<Supplier>('/api/v1/suppliers', { method: 'POST', body: JSON.stringify(input) })
export const updateSupplier = (id: string, input: SupplierInput) => apiRequest<Supplier>(`/api/v1/suppliers/${id}`, { method: 'PUT', body: JSON.stringify(input) })
export const setSupplierStatus = (id: string, isActive: boolean) => apiRequest<boolean>(`/api/v1/suppliers/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) })

export const getWarehouses = (filters: { keyword?: string; includeInactive?: boolean } = {}) =>
  apiRequest<Warehouse[]>(`/api/v1/warehouses${queryString({ q: filters.keyword, includeInactive: filters.includeInactive })}`)
export const getWarehouse = (id: string) => apiRequest<Warehouse>(`/api/v1/warehouses/${id}`)
export const createWarehouse = (input: WarehouseInput) => apiRequest<Warehouse>('/api/v1/warehouses', { method: 'POST', body: JSON.stringify(input) })
export const updateWarehouse = (id: string, input: WarehouseInput) => apiRequest<Warehouse>(`/api/v1/warehouses/${id}`, { method: 'PUT', body: JSON.stringify(input) })
export const setWarehouseStatus = (id: string, isActive: boolean) => apiRequest<boolean>(`/api/v1/warehouses/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) })

export const getCatalogCategories = (filters: { keyword?: string; includeInactive?: boolean } = {}) =>
  apiRequest<CatalogCategory[]>(`/api/v1/catalog-categories${queryString({ q: filters.keyword, includeInactive: filters.includeInactive })}`)
export const getCatalogCategory = (id: string) => apiRequest<CatalogCategory>(`/api/v1/catalog-categories/${id}`)
export const createCatalogCategory = (input: CatalogCategoryInput) => apiRequest<CatalogCategory>('/api/v1/catalog-categories', { method: 'POST', body: JSON.stringify(input) })
export const updateCatalogCategory = (id: string, input: CatalogCategoryInput) => apiRequest<CatalogCategory>(`/api/v1/catalog-categories/${id}`, { method: 'PUT', body: JSON.stringify(input) })
export const setCatalogCategoryStatus = (id: string, isActive: boolean) => apiRequest<boolean>(`/api/v1/catalog-categories/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) })

export const getCatalogItemSuppliers = (catalogItemId: string) =>
  apiRequest<CatalogItemSupplier[]>(`/api/v1/catalog/manage/${catalogItemId}/suppliers`)
export const upsertCatalogItemSupplier = (catalogItemId: string, supplierId: string, input: CatalogItemSupplierInput) =>
  apiRequest<CatalogItemSupplier>(`/api/v1/catalog/manage/${catalogItemId}/suppliers/${supplierId}`, { method: 'PUT', body: JSON.stringify(input) })
export const removeCatalogItemSupplier = (catalogItemId: string, supplierId: string) =>
  apiRequest<boolean>(`/api/v1/catalog/manage/${catalogItemId}/suppliers/${supplierId}`, { method: 'DELETE' })
