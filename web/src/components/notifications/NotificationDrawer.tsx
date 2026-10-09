import { useEffect, useMemo, useState } from 'react'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { CheckCheck } from 'lucide-react'
import {
  getNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  markNotificationUnread,
  notificationHref,
} from '../../api/notifications'
import { isApiError } from '../../api/client'
import type { AppNotification, NotificationCount } from '../../api/types'
import { StateBlock } from '../StateBlock'
import { Button } from '../ui/button'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '../ui/sheet'
import { Tabs, TabsList, TabsTrigger } from '../ui/tabs'
import { NotificationCard } from './NotificationCard'

type Tab = 'unread' | 'all'

const LIST_KEY = ['notifications', 'list'] as const

export function NotificationDrawer({
  open,
  onOpenChange,
  unread,
  onCount,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  unread: number
  onCount: (value: NotificationCount) => void
}) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [tab, setTab] = useState<Tab>('unread')

  // [UI] สถานะอ่านที่เพิ่งเปลี่ยนระหว่างเปิด drawer — การ์ดเปลี่ยนหน้าตาทันทีแต่ยังอยู่ที่เดิม
  // (ไม่ refetch รายการ) เพื่อไม่ให้รายการกระโดดใต้เมาส์ แล้วค่อยหายไปตอนเปิดครั้งถัดไป
  // ค่า null = ผู้ใช้เพิ่งกด "ยังไม่อ่าน"
  const [readOverrides, setReadOverrides] = useState<Record<string, string | null>>({})

  const list = useInfiniteQuery({
    queryKey: [...LIST_KEY, tab],
    queryFn: ({ pageParam }) => getNotifications(tab === 'unread', pageParam),
    initialPageParam: undefined as { beforeAt: string; beforeId: string } | undefined,
    getNextPageParam: (last) => {
      const tail = last.items.at(-1)
      return last.hasMore && tail ? { beforeAt: tail.createdAt, beforeId: tail.id } : undefined
    },
    enabled: open,
    staleTime: Infinity,
  })

  // ปิดแล้วทิ้งรายการเก่า — เปิดครั้งถัดไปได้ของสดเสมอ (รายการไม่ poll เอง มีแค่ตัวเลขที่ poll)
  useEffect(() => {
    if (open) return
    setReadOverrides({})
    void queryClient.removeQueries({ queryKey: LIST_KEY })
  }, [open, queryClient])

  const items = useMemo(() => {
    const seen = new Set<string>()
    return (list.data?.pages ?? []).flatMap((page) => page.items).filter((item) => {
      if (seen.has(item.id)) return false
      seen.add(item.id)
      return true
    })
  }, [list.data])

  const withOverride = (item: AppNotification): AppNotification =>
    item.id in readOverrides ? { ...item, readAt: readOverrides[item.id] ?? undefined } : item

  const failed = (fallback: string) => (error: Error) =>
    toast.error(isApiError(error) ? error.messageTh : fallback)

  const readMutation = useMutation({
    mutationFn: markNotificationRead,
    onSuccess: onCount,
    onError: failed('ทำเครื่องหมายว่าอ่านแล้วไม่สำเร็จ'),
  })
  const unreadMutation = useMutation({
    mutationFn: markNotificationUnread,
    onSuccess: onCount,
    onError: failed('ทำเครื่องหมายว่ายังไม่อ่านไม่สำเร็จ'),
  })
  const readAllMutation = useMutation({
    mutationFn: markAllNotificationsRead,
    onSuccess: (value) => {
      const now = new Date().toISOString()
      setReadOverrides((current) => ({
        ...current,
        ...Object.fromEntries(items.filter((item) => !withOverride(item).readAt).map((item) => [item.id, now])),
      }))
      onCount(value)
    },
    onError: failed('ทำเครื่องหมายว่าอ่านทั้งหมดไม่สำเร็จ'),
  })

  const setRead = (item: AppNotification, read: boolean) => {
    setReadOverrides((current) => ({ ...current, [item.id]: read ? new Date().toISOString() : null }))
    if (read) readMutation.mutate(item.id)
    else unreadMutation.mutate(item.id)
  }

  const openItem = (item: AppNotification) => {
    // การเปิดเรื่องนั้นคือการอ่าน — ส่งคำขอแล้วไปต่อเลย ไม่รอ (mutation ทำงานต่อได้แม้หน้านี้ถูกถอดออก)
    if (!withOverride(item).readAt) readMutation.mutate(item.id)
    onOpenChange(false)
    navigate(notificationHref(item))
  }

  const firstError = list.error

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent className="notification-sheet">
        <SheetHeader className="notification-sheet__header">
          <SheetTitle>การแจ้งเตือน</SheetTitle>
          <SheetDescription>
            ย้อนหลัง 30 วัน · ตัวเลขบนกระดิ่งอัปเดตทุก 30 วินาที · คลิกรายการเพื่อเปิดงานนั้น
          </SheetDescription>
          <div className="notification-sheet__toolbar">
            <Tabs value={tab} onValueChange={(value) => setTab(value as Tab)}>
              <TabsList aria-label="กรองการแจ้งเตือน">
                <TabsTrigger value="unread">ยังไม่อ่าน{unread > 0 ? ` (${unread > 99 ? '99+' : unread})` : ''}</TabsTrigger>
                <TabsTrigger value="all">ทั้งหมด</TabsTrigger>
              </TabsList>
            </Tabs>
            {unread > 0 && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => readAllMutation.mutate()}
                disabled={readAllMutation.isPending}
              >
                <CheckCheck aria-hidden="true" />
                {readAllMutation.isPending ? 'กำลังบันทึก…' : 'อ่านทั้งหมด'}
              </Button>
            )}
          </div>
        </SheetHeader>

        <div className="notification-sheet__body">
          {list.isPending ? (
            <StateBlock
              variant="loading"
              title="กำลังโหลดการแจ้งเตือน"
              reason="กำลังดึงรายการล่าสุดจากระบบ"
              traceId="ยังไม่มีรหัสติดตามระหว่างโหลด"
              actionLabel="ปิด"
              onAction={() => onOpenChange(false)}
            />
          ) : firstError && items.length === 0 ? (
            <StateBlock
              variant="error"
              title="โหลดการแจ้งเตือนไม่สำเร็จ"
              reason={isApiError(firstError) ? firstError.messageTh : 'ไม่สามารถเชื่อมต่อระบบบริการได้'}
              traceId={isApiError(firstError) ? firstError.traceId : undefined}
              actionLabel="ลองใหม่"
              onAction={() => void list.refetch()}
            />
          ) : items.length === 0 ? (
            <StateBlock
              variant="empty"
              title={tab === 'unread' ? 'ไม่มีการแจ้งเตือนที่ยังไม่อ่าน' : 'ยังไม่มีการแจ้งเตือนใน 30 วันที่ผ่านมา'}
              reason={
                tab === 'unread'
                  ? 'อ่านครบทุกเรื่องแล้ว หรือเรื่องที่รออยู่มีคนดำเนินการไปแล้ว'
                  : 'จะมีแจ้งเตือนเมื่อมีคน @ ถึงคุณ ลูกค้าตัดสินใจใบเสนอราคาที่คุณสร้าง หรือมีเอกสารจัดซื้อรอคุณอนุมัติ'
              }
              traceId="คำขอสำเร็จและไม่พบรายการ"
              actionLabel={tab === 'unread' ? 'ดูทั้งหมด' : 'ปิด'}
              onAction={() => (tab === 'unread' ? setTab('all') : onOpenChange(false))}
            />
          ) : (
            <>
              <ul className="notification-list" aria-label="รายการแจ้งเตือน">
                {items.map((item) => (
                  <NotificationCard
                    key={item.id}
                    item={withOverride(item)}
                    onOpen={() => openItem(item)}
                    onToggleRead={(read) => setRead(item, read)}
                  />
                ))}
              </ul>
              {list.hasNextPage && (
                <Button
                  variant="outline"
                  className="notification-sheet__more"
                  onClick={() => void list.fetchNextPage()}
                  disabled={list.isFetchingNextPage}
                >
                  {list.isFetchingNextPage ? 'กำลังโหลด…' : 'โหลดเพิ่ม'}
                </Button>
              )}
              {list.isError && (
                <p className="notification-sheet__error" role="alert">
                  โหลดหน้าถัดไปไม่สำเร็จ — {isApiError(list.error) ? `${list.error.messageTh} (traceId: ${list.error.traceId})` : 'ลองใหม่อีกครั้ง'}
                </p>
              )}
            </>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}
