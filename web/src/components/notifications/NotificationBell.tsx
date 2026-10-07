import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Bell } from 'lucide-react'
import { toast } from 'sonner'
import { getUnreadNotificationCount } from '../../api/notifications'
import type { NotificationCount } from '../../api/types'
import { NotificationDrawer } from './NotificationDrawer'
import './notifications.css'

export const UNREAD_COUNT_KEY = ['notifications', 'unread-count'] as const

/// [UI] ยังไม่มี realtime (Open Question #2) — poll ทุก 30 วิ เฉพาะตอนแท็บนี้ถูกมองอยู่ และดึงทันทีเมื่อกลับมาที่แท็บ
const POLL_MS = 30_000

/// กระดิ่งบน topbar — AppShell ถูก mount ใหม่ทุกหน้า แต่ตัวเลขอยู่ใน query cache จึงไม่กระพริบเป็น 0 ตอนเปลี่ยนหน้า
export function NotificationBell() {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const count = useQuery({
    queryKey: UNREAD_COUNT_KEY,
    queryFn: getUnreadNotificationCount,
    refetchInterval: POLL_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
    staleTime: 10_000,
  })
  const unread = count.data?.unread ?? 0

  // จำนวนล่าสุดที่ผู้ใช้ "รู้แล้ว" — ถ้าค่าจาก poll มากกว่านี้แปลว่ามีเรื่องใหม่จริง
  // ค่าที่เปลี่ยนเพราะผู้ใช้กดเอง (อ่าน/ยังไม่อ่าน) อัปเดตผ่าน applyCount จึงไม่ทำให้ toast เด้งผิดๆ
  const known = useRef<number | null>(null)
  useEffect(() => {
    if (!count.data) return
    const next = count.data.unread
    if (known.current !== null && next > known.current && !open) {
      const added = next - known.current
      toast.info(`มีการแจ้งเตือนใหม่ ${added} รายการ`, {
        action: { label: 'ดู', onClick: () => setOpen(true) },
      })
    }
    known.current = next
  }, [count.data, open])

  const applyCount = (value: NotificationCount) => {
    known.current = value.unread
    queryClient.setQueryData(UNREAD_COUNT_KEY, value)
  }

  const label = unread > 0 ? `การแจ้งเตือน ยังไม่อ่าน ${unread} รายการ` : 'การแจ้งเตือน ไม่มีรายการที่ยังไม่อ่าน'

  return (
    <>
      <button
        type="button"
        className="notification-bell"
        aria-label={label}
        title={count.isError ? 'โหลดจำนวนแจ้งเตือนไม่สำเร็จ — เปิดเพื่อดูรายละเอียด' : label}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={() => setOpen(true)}
      >
        <Bell aria-hidden="true" />
        {unread > 0 && (
          <span className="notification-bell__badge" aria-hidden="true">
            {unread > 99 ? '99+' : unread}
          </span>
        )}
      </button>
      <NotificationDrawer open={open} onOpenChange={setOpen} unread={unread} onCount={applyCount} />
    </>
  )
}
