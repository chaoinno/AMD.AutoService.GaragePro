import { useInfiniteQuery } from '@tanstack/react-query'
import { Loader2, X } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { isApiError } from '../../../api/client'
import { getJobChatMessages } from '../../../api/jobChat'
import type { JobChatMessage } from '../../../api/types'
import { AttachmentImage } from '../../../components/AttachmentImage'
import { formatDateTime } from '../../../lib/format'
import { useSession } from '../../../lib/session'
import { JobChatComposer } from './JobChatComposer'
import { parseChatBody } from './mentionToken'

const PAGE_SIZE = 50

type BeforeCursor = { beforeAt: string; beforeId: string }

/// poll ต่อโดยดึงหน้าล่าสุดซ้ำทุก 5 วิ (refetchInterval ของ useInfiniteQuery รีเฟรชทุกหน้าที่โหลดไว้แล้ว)
/// แล้ว dedupe ด้วย Map ตอน flatten — เรียบง่ายกว่าการทำ cursor ฝั่ง "ใหม่กว่า" แยกอีกชุดหนึ่ง เหมาะกับสเกล
/// ข้อความต่อ job ของระบบนี้ (ไม่ใช่แชทสาธารณะปริมาณสูง)
export function JobChatPanel({
  jobId,
  onClose,
  onLatestMessageId,
}: {
  jobId: string
  onClose: () => void
  onLatestMessageId: (messageId: string) => void
}) {
  const { session } = useSession()
  const [replyTo, setReplyTo] = useState<JobChatMessage | null>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const previousCount = useRef(0)

  const query = useInfiniteQuery({
    queryKey: ['job-chat-messages', jobId],
    queryFn: ({ pageParam }: { pageParam: BeforeCursor | null }) =>
      getJobChatMessages(jobId, pageParam
        ? { beforeAt: pageParam.beforeAt, beforeId: pageParam.beforeId, take: PAGE_SIZE }
        : { take: PAGE_SIZE }),
    initialPageParam: null as BeforeCursor | null,
    getNextPageParam: (lastPage) => {
      const oldest = lastPage.messages[lastPage.messages.length - 1]
      if (!lastPage.hasMore || !oldest) return undefined
      return { beforeAt: oldest.createdAt, beforeId: oldest.id }
    },
    refetchInterval: 5_000,
    refetchIntervalInBackground: false,
  })

  const messages = useMemo(() => {
    const map = new Map<string, JobChatMessage>()
    for (const page of query.data?.pages ?? []) {
      for (const message of page.messages) map.set(message.id, message)
    }
    return Array.from(map.values()).sort((a, b) =>
      a.createdAt === b.createdAt ? a.id.localeCompare(b.id) : a.createdAt.localeCompare(b.createdAt))
  }, [query.data])

  useEffect(() => {
    const latest = messages[messages.length - 1]
    if (latest) onLatestMessageId(latest.id)
    // onLatestMessageId มาจาก parent ทุก render — ใส่ใน deps จะ loop ไม่จบ ใช้แค่ messages พอ
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [messages])

  useEffect(() => {
    const el = listRef.current
    if (!el) return
    const nearBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 150
    if (previousCount.current === 0 || (messages.length > previousCount.current && nearBottom)) {
      el.scrollTop = el.scrollHeight
    }
    previousCount.current = messages.length
  }, [messages])

  return (
    <div className="job-chat-panel" role="dialog" aria-label="แชทของงานนี้">
      <header className="job-chat-panel__header">
        <span>แชทงานนี้</span>
        <button type="button" className="job-chat-panel__close" onClick={onClose} aria-label="ปิดแชท">
          <X aria-hidden="true" />
        </button>
      </header>

      <div className="job-chat-panel__messages" ref={listRef}>
        {query.hasNextPage ? (
          <button
            type="button"
            className="job-chat-panel__load-more"
            onClick={() => void query.fetchNextPage()}
            disabled={query.isFetchingNextPage}
          >
            {query.isFetchingNextPage ? 'กำลังโหลด…' : 'โหลดข้อความเก่ากว่า'}
          </button>
        ) : null}

        {query.isPending ? (
          <div className="job-chat-panel__status">
            <Loader2 className="intake-item__spin" aria-hidden="true" /> กำลังโหลดข้อความ
          </div>
        ) : query.isError ? (
          <div className="job-chat-panel__status job-chat-panel__status--error">
            {isApiError(query.error) ? query.error.messageTh : 'โหลดข้อความไม่สำเร็จ'}
          </div>
        ) : messages.length === 0 ? (
          <div className="job-chat-panel__status">ยังไม่มีข้อความในงานนี้ — เริ่มพิมพ์ได้เลย</div>
        ) : (
          messages.map((message) => (
            <JobChatBubble
              key={message.id}
              message={message}
              isOwn={message.createdByUserId === session?.user.userId}
              onReply={() => setReplyTo(message)}
            />
          ))
        )}
      </div>

      <JobChatComposer
        jobId={jobId}
        replyTo={replyTo}
        onClearReply={() => setReplyTo(null)}
        onSent={() => void query.refetch()}
      />
    </div>
  )
}

function JobChatBubble({
  message, isOwn, onReply,
}: { message: JobChatMessage; isOwn: boolean; onReply: () => void }) {
  const segments = message.body ? parseChatBody(message.body) : []

  return (
    <div className={`job-chat-bubble ${isOwn ? 'job-chat-bubble--own' : ''}`}>
      <div className="job-chat-bubble__meta">
        <span className="job-chat-bubble__author">{message.createdByUserName}</span>
        <span className="job-chat-bubble__time">{formatDateTime(message.createdAt)}</span>
      </div>

      {message.replyTo ? (
        <div className="job-chat-bubble__reply">
          <strong>{message.replyTo.createdByUserName}</strong>
          <span>{message.replyTo.isDeleted ? 'ข้อความนี้ถูกลบ' : (message.replyTo.body || 'รูปภาพ')}</span>
        </div>
      ) : null}

      {message.isDeleted ? (
        <p className="job-chat-bubble__deleted">ข้อความนี้ถูกลบ</p>
      ) : (
        <>
          {message.body ? (
            <p className="job-chat-bubble__body">
              {segments.map((segment, index) =>
                segment.type === 'mention' ? (
                  <span key={index} className="job-chat-mention-chip">@{segment.name}</span>
                ) : (
                  <span key={index}>{segment.value}</span>
                ))}
            </p>
          ) : null}

          {message.attachments.length > 0 ? (
            <div className="job-chat-bubble__attachments">
              {message.attachments.map((attachment) => (
                <AttachmentImage
                  key={attachment.id}
                  relativePath={attachment.relativePath}
                  alt="รูปแนบในแชท"
                  className="job-chat-bubble__attachment-image"
                />
              ))}
            </div>
          ) : null}

          <button type="button" className="job-chat-bubble__reply-action" onClick={onReply}>
            ตอบกลับ
          </button>
        </>
      )}
    </div>
  )
}
