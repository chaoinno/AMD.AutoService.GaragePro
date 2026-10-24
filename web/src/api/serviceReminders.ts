import { apiRequest } from './client'

/// รายการนัดเข้ารับบริการตั้งต้นของสาขา (น้ำมันเครื่อง/กรอง/เบรก…) — ทุกบทบาทอ่านได้ ผู้จัดการจัดการ
export type ServiceReminderTemplate = {
  id: string
  name: string
  /** ไมล์ที่นัดห่างจากไมล์ตอนส่งมอบ (กม.) — ค่าตั้งต้น แก้ต่อคันได้ */
  defaultMileageInterval: number
  defaultMonths: number
  sortOrder: number
  isActive: boolean
}

export type ServiceReminderTemplateInput = {
  name: string
  defaultMileageInterval: number
  defaultMonths: number
  sortOrder: number
}

/// สาขาที่ยังไม่เคยมีรายการจะได้ชุดตั้งต้นให้เองตอนเรียกครั้งแรก
export const getServiceReminderTemplates = (includeInactive = false) =>
  apiRequest<ServiceReminderTemplate[]>(`/api/v1/service-reminder-templates${includeInactive ? '?includeInactive=true' : ''}`)

export const createServiceReminderTemplate = (input: ServiceReminderTemplateInput) =>
  apiRequest<ServiceReminderTemplate>('/api/v1/service-reminder-templates', { method: 'POST', body: JSON.stringify(input) })

export const updateServiceReminderTemplate = (id: string, input: ServiceReminderTemplateInput) =>
  apiRequest<ServiceReminderTemplate>(`/api/v1/service-reminder-templates/${id}`, { method: 'PUT', body: JSON.stringify(input) })

export const setServiceReminderTemplateStatus = (id: string, isActive: boolean) =>
  apiRequest<boolean>(`/api/v1/service-reminder-templates/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) })
