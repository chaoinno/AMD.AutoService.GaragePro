import { apiRequest } from './client'
import type {
  ConvertToInShopInput,
  CreateJobInput,
  CreatedJob,
  Job,
  JobCalendarResult,
  JobStatusOption,
  JobStatusToken,
  JobTransitionResult,
  TransitionJobInput,
  UpdateJobAppointmentInput,
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

/// แปลงงานนัดหมายเป็นรถในอู่พร้อมบันทึกวันเวลาที่รถเข้าอู่จริง — ไม่ผูกกับวันนัดหมายที่ตั้งไว้
export function convertJobToInShop(jobId: string, input: ConvertToInShopInput) {
  return apiRequest<Job>(`/api/v1/jobs/${jobId}/convert-to-in-shop`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

/// มุมมองปฏิทินนัดหมาย — คนละ contract กับ searchJobs (ไม่ใช่ keyset cursor, คืนทุกแถวในช่วง [from, to))
export function getJobCalendar(args: { from: string; to: string; query?: string; status?: JobStatusToken }) {
  const params = new URLSearchParams({ from: args.from, to: args.to })
  if (args.query?.trim()) params.set('q', args.query.trim())
  if (args.status) params.set('status', args.status)
  return apiRequest<JobCalendarResult>(`/api/v1/jobs/calendar?${params}`)
}
