import { API_BASE_URL, apiDownload, apiRequest } from './client'
import type {
  CustomerDetail,
  CustomerInput,
  CustomerSummary,
  LookupItem,
  PagedResult,
  VehicleDetail,
  VehicleInput,
  VehicleReferenceData,
  VehicleSummary,
} from './types'

export type CustomerFilters = {
  keyword?: string
  brandId?: number
  modelId?: number
  provinceId?: number
  amphureId?: number
  districtId?: number
  includeDeleted?: boolean
  page?: number
  pageSize?: number
  sortBy?: string
}

export type VehicleFilters = {
  keyword?: string
  searchFields?: string[]
  brandId?: number
  modelId?: number
  nicknameId?: number
  carTypeId?: number
  yearId?: number
  insuranceId?: number
  includeDeleted?: boolean
  page?: number
  pageSize?: number
  sortBy?: string
}

function queryString(values: Record<string, unknown>) {
  const params = new URLSearchParams()
  Object.entries(values).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '' || value === false) return
    if (Array.isArray(value)) value.forEach((item) => params.append(key, String(item)))
    else params.set(key, String(value))
  })
  const query = params.toString()
  return query ? `?${query}` : ''
}

export const getCustomers = (filters: CustomerFilters) =>
  apiRequest<PagedResult<CustomerSummary>>(`/api/v1/customers${queryString(filters)}`)
export const getCustomer = (id: number) => apiRequest<CustomerDetail>(`/api/v1/customers/${id}`)
export const createCustomer = (input: CustomerInput) => apiRequest<CustomerDetail>('/api/v1/customers', { method: 'POST', body: JSON.stringify(input) })
export const updateCustomer = (id: number, input: CustomerInput) => apiRequest<CustomerDetail>(`/api/v1/customers/${id}`, { method: 'PUT', body: JSON.stringify(input) })
export const deleteCustomer = (id: number) => apiRequest<boolean>(`/api/v1/customers/${id}`, { method: 'DELETE' })
export const exportCustomers = (filters: CustomerFilters) => apiDownload(`/api/v1/customers/export${queryString(filters)}`)

export const getVehicles = (filters: VehicleFilters) =>
  apiRequest<PagedResult<VehicleSummary>>(`/api/v1/vehicles${queryString(filters)}`)
export const getVehicle = (id: number) => apiRequest<VehicleDetail>(`/api/v1/vehicles/${id}`)
export const deleteVehicle = (id: number) => apiRequest<boolean>(`/api/v1/vehicles/${id}`, { method: 'DELETE' })
export const exportVehicles = (filters: VehicleFilters) => apiDownload(`/api/v1/vehicles/export${queryString(filters)}`)

function vehicleForm(input: VehicleInput, image?: File | null) {
  const form = new FormData()
  Object.entries(input).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') form.set(key, String(value))
  })
  if (image) form.set('image', image)
  return form
}
export const createVehicle = (input: VehicleInput, image?: File | null) =>
  apiRequest<VehicleDetail>('/api/v1/vehicles', { method: 'POST', body: vehicleForm(input, image) })
export const updateVehicle = (id: number, input: VehicleInput, image?: File | null) =>
  apiRequest<VehicleDetail>(`/api/v1/vehicles/${id}`, { method: 'PUT', body: vehicleForm(input, image) })

/// เปลี่ยนเฉพาะรูปรถ ไม่แตะข้อมูลอื่น — ใช้จากหน้าที่ไม่มีฟอร์มรถเต็มให้กรอกซ้ำ (เปิดจ๊อบ/การ์ดจ๊อบ)
export const updateVehicleImage = (id: number, image: File) => {
  const form = new FormData()
  form.set('image', image)
  return apiRequest<VehicleDetail>(`/api/v1/vehicles/${id}/image`, { method: 'POST', body: form })
}

export const getProvinces = () => apiRequest<LookupItem[]>('/api/v1/locations/provinces')
export const getAmphures = (provinceId: number) => apiRequest<LookupItem[]>(`/api/v1/locations/amphures?provinceId=${provinceId}`)
export const getDistricts = (amphureId: number) => apiRequest<LookupItem[]>(`/api/v1/locations/districts?amphureId=${amphureId}`)
export const getZipCode = (districtId: number) => apiRequest<{ zipCode: string | null }>(`/api/v1/locations/zipcode?districtId=${districtId}`)
export const getVehicleReferenceData = () => apiRequest<VehicleReferenceData>('/api/v1/cars/reference-data')
export const getModels = (brandId: number) => apiRequest<LookupItem[]>(`/api/v1/cars/models?brandId=${brandId}`)
export const getNicknames = (modelId: number) => apiRequest<LookupItem[]>(`/api/v1/cars/nicknames?modelId=${modelId}`)

export function triggerDownload(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

export function absoluteApiUrl(path: string | null) {
  return path ? `${API_BASE_URL}${path}` : null
}
