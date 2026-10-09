import { apiRequest } from './client'
import type {
  ConvertToInShopInput,
  CreateJobInput,
  CreatedJob,
  Job,
  JobCalendarDateField,
  JobCalendarResult,
  JobScheduleChange,
  JobStatusOption,
  JobStatusToken,
  JobTransitionResult,
  TransitionJobInput,
  UpdateJobAppointmentInput,
  UpdateJobPromiseInput,
  UpdateJobMileageInput,
} from './types'

export type JobsCursor = { beforeCreatedAt: string; beforeJobId: string }

export function searchJobs(
  query: string,
  take = 50,
  cursor?: JobsCursor,
  jobTypeId?: number,
  status?: JobStatusToken,
) {
  const params = new URLSearchParams({ take: String(take) })
  if (query.trim()) params.set('q', query.trim())
  if (cursor) {
    params.set('beforeCreatedAt', cursor.beforeCreatedAt)
    params.set('beforeJobId', cursor.beforeJobId)
  }
  if (jobTypeId) params.set('jobTypeId', String(jobTypeId))
  if (status) params.set('status', status)
  return apiRequest<Job[]>(`/api/v1/jobs/search?${params}`)
}

export function countOpenJobs(jobTypeId?: number) {
  const params = new URLSearchParams()
  if (jobTypeId) params.set('jobTypeId', String(jobTypeId))
  return apiRequest<number>(`/api/v1/jobs/count-open?${params}`)
}

export function getJob(jobId: string) {
  return apiRequest<Job>(`/api/v1/jobs/${jobId}`)
}

export function getJobStatusOptions() {
  return apiRequest<JobStatusOption[]>('/api/v1/jobs/status-options')
}

export function createJob(input: CreateJobInput) {
  return apiRequest<CreatedJob>('/api/v1/jobs', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function transitionJob(jobId: string, input: TransitionJobInput) {
  return apiRequest<JobTransitionResult>(`/api/v1/jobs/${jobId}/transitions`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function updateJobAppointment(jobId: string, input: UpdateJobAppointmentInput) {
  return apiRequest<Job>(`/api/v1/jobs/${jobId}/appointment`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/// ตั้ง/เลื่อนวันเวลานัดส่งมอบรถ — server บันทึกประวัติ (ActivityEvent job.promise.changed) ทุกครั้งที่ค่าเปลี่ยน
export function updateJobPromise(jobId: string, input: UpdateJobPromiseInput) {
  return apiRequest<Job>(`/api/v1/jobs/${jobId}/promise`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/// บันทึก/แก้เลขไมล์ขณะรับรถ — ล็อกเมื่อส่งมอบรถแล้วหรือจ๊อบปิดแล้ว (JOB_MILEAGE_LOCKED)
export function updateJobMileage(jobId: string, input: UpdateJobMileageInput) {
  return apiRequest<Job>(`/api/v1/jobs/${jobId}/mileage`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/// ประวัติการเปลี่ยนวันนัดเข้า/วันนัดส่งมอบ (ใหม่สุดก่อน)
export function getJobScheduleHistory(jobId: string) {
  return apiRequest<JobScheduleChange[]>(`/api/v1/jobs/${jobId}/schedule-history`)
}

/// แปลงงานนัดหมายเป็นรถในอู่พร้อมบันทึกวันเวลาที่รถเข้าอู่จริง — ไม่ผูกกับวันนัดหมายที่ตั้งไว้
export function convertJobToInShop(jobId: string, input: ConvertToInShopInput) {
  return apiRequest<Job>(`/api/v1/jobs/${jobId}/convert-to-in-shop`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/// มุมมองปฏิทินนัดหมาย — คนละ contract กับ searchJobs (ไม่ใช่ keyset cursor, คืนทุกแถวในช่วง [from, to))
export function getJobCalendar(args: {
  from: string
  to: string
  query?: string
  status?: JobStatusToken
  dateField?: JobCalendarDateField
}) {
  const params = new URLSearchParams({ from: args.from, to: args.to })
  if (args.dateField) params.set('dateField', args.dateField)
  if (args.query?.trim()) params.set('q', args.query.trim())
  if (args.status) params.set('status', args.status)
  return apiRequest<JobCalendarResult>(`/api/v1/jobs/calendar?${params}`)
}
