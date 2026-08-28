import { API_BASE_URL, apiRequest } from './client'
import type { Attachment, AttachmentKind } from './types'

export function getJobAttachments(jobId: number, kind?: AttachmentKind) {
  const params = new URLSearchParams({ jobId: String(jobId) })
  if (kind) params.set('kind', kind)
  return apiRequest<Attachment[]>(`/api/v1/attachments?${params}`)
}

export function attachmentFileUrl(relativePath: string) {
  return `${API_BASE_URL}/api/v1/attachments/file?path=${encodeURIComponent(relativePath)}`
}
