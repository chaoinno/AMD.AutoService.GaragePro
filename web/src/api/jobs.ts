import { apiRequest } from './client'
import type {
  CreateJobInput,
  CreatedJob,
  Job,
  JobStatusOption,
  JobStatusToken,
  JobTransitionResult,
  TransitionJobInput,
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
