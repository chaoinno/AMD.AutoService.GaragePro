import { apiDownload, apiRequest } from './client'
import type { LookupItem, PagedResult, StaffCodePreview, StaffDetail, StaffInput, StaffReferenceData, StaffSummary } from './types'

export type StaffFilters = {
  keyword?: string
  departmentId?: number
  sectorId?: number
  positionId?: number
  provinceId?: number
  amphureId?: number
  districtId?: number
  includeInactive?: boolean
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

function staffForm(input: StaffInput, image?: File | null) {
  const form = new FormData()
  Object.entries(input).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') return
    if (Array.isArray(value)) value.forEach((item) => form.append(key, String(item)))
    else form.set(key, String(value))
  })
  if (image) form.set('image', image)
  return form
}

export const getStaffs = (filters: StaffFilters) => apiRequest<PagedResult<StaffSummary>>(`/api/v1/staffs${queryString(filters)}`)
export const getStaff = (id: number) => apiRequest<StaffDetail>(`/api/v1/staffs/${id}`)
export const createStaff = (input: StaffInput, image?: File | null) => apiRequest<StaffDetail>('/api/v1/staffs', { method: 'POST', body: staffForm(input, image) })
export const updateStaff = (id: number, input: StaffInput, image?: File | null) => apiRequest<StaffDetail>(`/api/v1/staffs/${id}`, { method: 'PUT', body: staffForm(input, image) })
export const setStaffStatus = (id: number, isActive: boolean, endJobDate?: string) => apiRequest<boolean>(`/api/v1/staffs/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive, endJobDate: endJobDate || null }) })
export const getStaffReferenceData = () => apiRequest<StaffReferenceData>('/api/v1/staffs/reference-data')
export const getStaffCodePreview = (branchId?: number) => apiRequest<StaffCodePreview>(`/api/v1/staffs/code-preview${queryString({ branchId })}`)
export const getStaffSectors = (departmentId?: number) => apiRequest<LookupItem[]>(`/api/v1/sectors${queryString({ departmentId })}`)
export const getStaffImage = (id: number) => apiDownload(`/api/v1/staffs/${id}/image`)
