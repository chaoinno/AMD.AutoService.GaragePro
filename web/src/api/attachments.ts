import { apiDownload, apiRequest } from './client'
import type { Attachment, AttachmentKind } from './types'

export function getJobAttachments(jobId: string, kind?: AttachmentKind) {
  const params = new URLSearchParams({ jobId })
  if (kind) params.set('kind', kind)
  return apiRequest<Attachment[]>(`/api/v1/attachments?${params}`)
}

/// endpoint นี้ต้อง Bearer token เสมอ — ใช้ผ่าน AttachmentImage/downloadAttachment + URL.createObjectURL เท่านั้น
/// ห้ามใช้ path ตรงๆ ใน <img src> (401 เงียบๆ ทำให้รูปพัง — ดู web/src/components/AttachmentImage.tsx)
export function downloadAttachment(relativePath: string) {
  return apiDownload(`/api/v1/attachments/file?path=${encodeURIComponent(relativePath)}`)
}

export function uploadAttachment(input: {
  jobId: string
  kind: AttachmentKind
  entityId?: string
  file: File
}) {
  const form = new FormData()
  form.set('JobId', input.jobId)
  form.set('Kind', input.kind)
  if (input.entityId) form.set('EntityId', input.entityId)
  form.set('File', input.file)
  return apiRequest<Attachment>('/api/v1/attachments', { method: 'POST', body: form })
}
