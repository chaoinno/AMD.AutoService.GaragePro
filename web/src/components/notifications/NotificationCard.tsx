import {
  AtSign,
  BadgeCheck,
  Bell,
  CircleCheck,
  CircleX,
  Hourglass,
  type LucideIcon,
  Mail,
  MailOpen,
  TriangleAlert,
  Undo2,
} from 'lucide-react'
import type { AppNotification } from '../../api/types'
import { formatDateTime, formatRelativeTime } from '../../lib/format'

type Tone = 'info' | 'success' | 'warning' | 'danger' | 'neutral'

/// [UI] ทุกชนิดสื่อด้วย ไอคอน + สี + ข้อความ — ห้ามใช้สีอย่างเดียว
const KINDS: Record<string, { icon: LucideIcon; tone: Tone; label: string }> = {
  'chat.mention': { icon: AtSign, tone: 'info', label: 'ถูกกล่าวถึงในแชท' },
  'quotation.approved': { icon: CircleCheck, tone: 'success', label: 'ลูกค้าอนุมัติ' },
  'quotation.partial': { icon: TriangleAlert, tone: 'warning', label: 'อนุมัติบางรายการ' },
  'quotation.all_rejected': { icon: CircleX, tone: 'danger', label: 'ลูกค้าไม่อนุมัติ' },
  'purchase.pending': { icon: Hourglass, tone: 'warning', label: 'รออนุมัติ' },
  'purchase.approved': { icon: BadgeCheck, tone: 'success', label: 'อนุมัติแล้ว' },
  'purchase.returned': { icon: Undo2, tone: 'danger', label: 'ถูกตีกลับ' },
}

const FALLBACK = { icon: Bell, tone: 'neutral' as Tone, label: 'แจ้งเตือน' }

export function NotificationCard({
  item,
  onOpen,
  onToggleRead,
}: {
  item: AppNotification
  onOpen: () => void
  onToggleRead: (read: boolean) => void
}) {
  const kind = KINDS[item.kind] ?? FALLBACK
  const Icon = kind.icon
  const resolved = Boolean(item.resolvedAt)
  const read = Boolean(item.readAt)
  // เรื่องที่มีคนจัดการแล้วไม่ใช่ "ใหม่" อีก แม้ผู้ใช้ยังไม่ได้เปิด (ตรงกับที่ server ไม่นับเป็นยังไม่อ่าน)
  const isNew = !read && !resolved
  const state = isNew ? 'unread' : 'read'

  return (
    <li className={`notification-card notification-card--${state}`} data-tone={kind.tone}>
      <button type="button" className="notification-card__main" onClick={onOpen}>
        <span className="notification-card__dot" aria-hidden="true" />
        <span className="notification-card__icon" aria-hidden="true">
          <Icon />
        </span>
        <span className="notification-card__content">
          <span className="notification-card__meta">
            <span className="notification-card__kind">{kind.label}</span>
            {isNew && <span className="notification-card__new">ใหม่</span>}
            {resolved && (
              <span className="notification-card__resolved">
                <CircleCheck aria-hidden="true" />
                ดำเนินการแล้ว{item.resolvedByName ? `โดย ${item.resolvedByName}` : ''}
              </span>
            )}
          </span>
          <span className="notification-card__title">{item.titleTh}</span>
          {item.bodyTh && <span className="notification-card__body">{item.bodyTh}</span>}
          <span className="notification-card__time">
            <time dateTime={item.createdAt} title={formatDateTime(item.createdAt)}>
              {formatRelativeTime(item.createdAt)}
            </time>
            {' · '}
            {item.actorName}
            {read && item.readAt && (
              <span className="notification-card__read-at"> · อ่านแล้ว {formatRelativeTime(item.readAt)}</span>
            )}
          </span>
        </span>
      </button>
      <button
        type="button"
        className="notification-card__toggle"
        onClick={() => onToggleRead(!read)}
        aria-label={read ? `ทำเครื่องหมายว่ายังไม่อ่าน: ${item.titleTh}` : `ทำเครื่องหมายว่าอ่านแล้ว: ${item.titleTh}`}
        title={read ? 'ทำเครื่องหมายว่ายังไม่อ่าน' : 'ทำเครื่องหมายว่าอ่านแล้ว'}
      >
        {read ? <Mail aria-hidden="true" /> : <MailOpen aria-hidden="true" />}
      </button>
    </li>
  )
}
