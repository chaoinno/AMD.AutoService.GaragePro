import { apiRequest } from './client'
import type { LegacyJob } from './types'

export function searchJobs(query: string) {
  const params = new URLSearchParams({ q: query, take: '25' })
  return apiRequest<LegacyJob[]>(`/api/v1/jobs/search?${params}`)
}
