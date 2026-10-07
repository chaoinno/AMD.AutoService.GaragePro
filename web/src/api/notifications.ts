import { apiRequest } from './client'
import type { AppNotification, NotificationCount, NotificationPage } from './types'

export type NotificationCursor = { beforeAt: string; beforeId: string }

export function getNotifications(unreadOnly: boolean, cursor?: NotificationCursor, take = 30) {
  const params = new URLSearchParams({ take: String(take) })
  if (unreadOnly) params.set('unreadOnly', 'true')
  if (cursor) {
    params.set('beforeAt', cursor.beforeAt)
    params.set('beforeId', cursor.beforeId)
  }
  return apiRequest<NotificationPage>(`/api/v1/notifications?${params}`)
}

export function getUnreadNotificationCount() {
  return apiRequest<NotificationCount>('/api/v1/notifications/unread-count')
}

export function markNotificationRead(id: string) {
  return apiRequest<NotificationCount>(`/api/v1/notifications/${id}/read`, { method: 'POST' })
}

export function markNotificationUnread(id: string) {
  return apiRequest<NotificationCount>(`/api/v1/notifications/${id}/read`, { method: 'DELETE' })
}

export function markAllNotificationsRead() {
  return apiRequest<NotificationCount>('/api/v1/notifications/read-all', { method: 'POST' })
}

/// ปลายทางเมื่อคลิกการ์ด — ทุกชนิดต้องได้ที่หมาย (ห้ามมีการ์ดที่คลิกแล้วไม่ไปไหน)
export function notificationHref(n: AppNotification): string {
  if (n.entityType === 'PR' || n.entityType === 'PO') {
    const kind = n.entityType.toLowerCase()
    return n.entityId ? `/purchasing/${kind}?doc=${n.entityId}` : `/purchasing/${kind}`
  }
  if (n.jobId) {
    const params = new URLSearchParams({ job: n.jobId })
    if (n.linkHint === 'chat') params.set('chat', '1')
    if (n.linkHint === 'quote') params.set('stage', 'quote')
    return `/jobs?${params}`
  }
  return '/jobs'
}
