import { apiRequest } from './client'
import type { CreateJobInput, CreatedJob, JobFormOptions, JobStatusOption, LegacyJob } from './types'

export type JobsCursor = { beforeCreatedDate: string; beforeJobId: number }

export function searchJobs(
  query: string,
  take = 50,
  cursor?: JobsCursor,
  pjTypeId?: number,
  pjStatusId?: number,
) {
  const params = new URLSearchParams({ take: String(take) })
  if (query.trim()) params.set('q', query.trim())
  if (cursor) {
    params.set('beforeCreatedDate', cursor.beforeCreatedDate)
    params.set('beforeJobId', String(cursor.beforeJobId))
  }
  if (pjTypeId) params.set('pjTypeId', String(pjTypeId))
  if (pjStatusId) params.set('pjStatusId', String(pjStatusId))
  return apiRequest<LegacyJob[]>(`/api/v1/jobs/search?${params}`)
}

export function getJob(jobId: number) {
  return apiRequest<LegacyJob>(`/api/v1/jobs/${jobId}`)
}

export function getJobFormOptions() {
  return apiRequest<JobFormOptions>('/api/v1/jobs/form-options')
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
