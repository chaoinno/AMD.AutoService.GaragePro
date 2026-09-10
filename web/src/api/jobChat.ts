import { apiRequest } from './client'
import type { JobChatMessage, JobChatPage, SendJobChatMessageInput } from './types'

export type JobChatPageCursor = {
  beforeAt?: string
  beforeId?: string
  afterAt?: string
  afterId?: string
  take?: number
}

export function getJobChatMessages(jobId: string, cursor: JobChatPageCursor = {}) {
  const params = new URLSearchParams()
  if (cursor.beforeAt) params.set('beforeAt', cursor.beforeAt)
  if (cursor.beforeId) params.set('beforeId', cursor.beforeId)
  if (cursor.afterAt) params.set('afterAt', cursor.afterAt)
  if (cursor.afterId) params.set('afterId', cursor.afterId)
  if (cursor.take) params.set('take', String(cursor.take))
  const qs = params.size ? `?${params}` : ''
  return apiRequest<JobChatPage>(`/api/v1/jobs/${jobId}/chat/messages${qs}`)
}

export function sendJobChatMessage(jobId: string, input: SendJobChatMessageInput) {
  return apiRequest<JobChatMessage>(`/api/v1/jobs/${jobId}/chat/messages`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function deleteJobChatMessage(jobId: string, messageId: string) {
  return apiRequest<JobChatMessage>(`/api/v1/jobs/${jobId}/chat/messages/${messageId}`, {
    method: 'DELETE',
  })
}
