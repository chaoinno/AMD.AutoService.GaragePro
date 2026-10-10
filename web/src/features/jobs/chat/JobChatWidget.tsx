import { useQuery } from '@tanstack/react-query'
import { MessageCircle } from 'lucide-react'
import { useState } from 'react'
import { getJobChatMessages } from '../../../api/jobChat'
import { JobChatPanel } from './JobChatPanel'
import { getLastSeenChatId, setLastSeenChatId } from './chatSeen'

/// widget แชทลอยมุมล่างขวาของ Job Card — mount ทันทีที่มี jobId ไม่ต้องรอ job โหลดเสร็จ
/// เก็บ "เห็นข้อความล่าสุดถึงไหนแล้ว" ไว้ที่ localStorage ต่อเครื่อง (ไม่ sync ข้าม device — ดูแผนที่ตกลงไว้)
export function JobChatWidget({ jobId, defaultOpen = false }: { jobId: string; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)

  // แค่ peek ข้อความล่าสุด (take=1) เพื่อโชว์จุดแดง "มีข้อความใหม่" ตอน panel ปิดอยู่ — ไม่โหลดประวัติเต็ม
  // หยุด poll ทันทีที่เปิด panel เพราะ JobChatPanel เองมี refetchInterval ที่ถี่กว่าอยู่แล้ว
  const peekQuery = useQuery({
    queryKey: ['job-chat-peek', jobId],
    queryFn: () => getJobChatMessages(jobId, { take: 1 }),
    refetchInterval: open ? false : 20_000,
    staleTime: 10_000,
  })

  const latestId = peekQuery.data?.messages[0]?.id ?? null
  const hasUnseen = latestId !== null && latestId !== getLastSeenChatId(jobId)

  const openPanel = () => {
    setOpen(true)
    if (latestId) setLastSeenChatId(jobId, latestId)
  }

  return (
    <div className="job-chat-widget">
      {open ? (
        <JobChatPanel
          jobId={jobId}
          onClose={() => setOpen(false)}
          onLatestMessageId={(id) => setLastSeenChatId(jobId, id)}
        />
      ) : (
        <button
          type="button"
          className="job-chat-widget__launcher"
          onClick={openPanel}
          aria-label="เปิดแชทของงานนี้"
        >
          <MessageCircle aria-hidden="true" />
          {hasUnseen ? <span className="job-chat-widget__dot" aria-hidden="true" /> : null}
        </button>
      )}
    </div>
  )
}
