import { useQuery } from '@tanstack/react-query'
import {
  CalendarClock,
  CirclePause,
  Clock,
  MessageCircle,
  RefreshCw,
  Send,
  Timer,
  TriangleAlert,
} from 'lucide-react'
import { getJobBoard } from '../../api/jobs'
import { isApiError } from '../../api/client'
import type { JobBoardCard, JobBoardColumn } from '../../api/types'
import { JobStatusChip } from '../../components/JobStatusChip'
import { StateBlock } from '../../components/StateBlock'
import { VehicleImage } from '../../components/VehicleImage'
import { Button } from '../../components/ui/button'
import { formatDateTime, formatStatusAge } from '../../lib/format'
import { getLastSeenChatId } from './chat/chatSeen'

export const JOB_BOARD_QUERY_KEY = 'job-board'

/// [UI] เปิดค้างไว้บนจอมอนิเตอร์ได้ — รีเฟรชเองทุกนาทีเฉพาะตอนแท็บนี้อยู่หน้าจอ (แบบกระดิ่งแจ้งเตือน)
const REFRESH_MS = 60_000

const updatedAtFormatter = new Intl.DateTimeFormat('th-TH', { hour: '2-digit', minute: '2-digit' })

/// บอร์ดสถานะรถในอู่ — **ดูอย่างเดียว** คลิกการ์ดเพื่อเปิดการ์ดจ๊อบ ไม่มีการลากเปลี่ยนสถานะ
/// [BIZ] transition เกือบทุกเส้นมี guard ที่คำนวณจากข้อมูลจริงหรือจำกัด role/source ลากแล้วจะเด้งกลับหรือกลายเป็นช่องข้ามขั้นตอน
export function JobsBoard({ query, onSelectJob }: { query: string; onSelectJob: (jobId: string) => void }) {
  const boardQuery = useQuery({
    queryKey: [JOB_BOARD_QUERY_KEY, query],
    queryFn: () => getJobBoard(query),
    refetchInterval: REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  })

  const board = boardQuery.data
  // รีเฟรชเบื้องหลังล้มขณะมีข้อมูลเดิมอยู่แล้ว ยังแสดงบอร์ดเดิม + แถบเตือนข้อมูลเก่า ไม่ล้างจอจอมอนิเตอร์ทิ้ง
  if (!board && !boardQuery.isError) {
    return (
      <StateBlock
        variant="loading"
        title="กำลังโหลดบอร์ดรถในอู่"
        reason="กำลังดึงจ๊อบรถในอู่ที่ยังไม่ปิดงานของสาขานี้"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void boardQuery.refetch()}
      />
    )
  }

  if (!board) {
    return (
      <StateBlock
        variant="error"
        title="โหลดบอร์ดรถในอู่ไม่สำเร็จ"
        reason={isApiError(boardQuery.error) ? boardQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(boardQuery.error) ? boardQuery.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void boardQuery.refetch()}
      />
    )
  }

  if (board.total === 0) {
    return (
      <StateBlock
        variant="empty"
        title={query ? 'ไม่พบรถในอู่ที่ตรงกับคำค้น' : 'ไม่มีรถในอู่ที่ยังไม่ปิดงาน'}
        reason={query
          ? `ไม่มีจ๊อบรถในอู่ที่ยังเปิดอยู่ตรงกับ "${query}" — ลองล้างคำค้นหรือดูมุมมองรายการ`
          : 'จ๊อบที่ปิดงานแล้วหรือเป็นรถนัดหมายจะไม่แสดงบนบอร์ดนี้'}
        actionLabel="โหลดใหม่"
        onAction={() => void boardQuery.refetch()}
      />
    )
  }

  // [UI] "รออะไหล่" แสดงเฉพาะตอนมีงาน — แอปช่างตัดปุ่มแจ้งรออะไหล่ออกแล้ว คอลัมน์นี้จึงว่างเกือบตลอด
  const columns = board.columns.filter((column) => column.status !== 'waitparts' || column.cards.length > 0)
  const now = new Date()

  return (
    <section className="jobs-board" aria-label="บอร์ดสถานะรถในอู่">
      <div className="jobs-board__toolbar">
        <p>
          รถในอู่ที่ยังไม่ปิดงาน <strong>{board.total.toLocaleString('th-TH')}</strong> คัน
          <span className="jobs-board__updated">
            · อัปเดตล่าสุด {updatedAtFormatter.format(new Date(boardQuery.dataUpdatedAt))} น. (รีเฟรชเองทุก 1 นาที)
          </span>
        </p>
        <Button
          variant="outline"
          size="sm"
          onClick={() => void boardQuery.refetch()}
          disabled={boardQuery.isFetching}
        >
          <RefreshCw aria-hidden="true" className={boardQuery.isFetching ? 'spin' : undefined} />
          {boardQuery.isFetching ? 'กำลังรีเฟรช' : 'รีเฟรช'}
        </Button>
      </div>

      {board.truncated ? (
        <div className="jobs-calendar__notice jobs-calendar__notice--warning" role="status">
          <TriangleAlert aria-hidden="true" />
          <div>
            <strong>แสดงได้สูงสุด {board.limit.toLocaleString('th-TH')} คัน</strong>
            <p>มีรถในอู่มากกว่านี้ — พิมพ์ค้นหาทะเบียนหรือเลขจ๊อบเพื่อหาคันที่ไม่อยู่บนบอร์ด</p>
          </div>
        </div>
      ) : null}

      {boardQuery.isError ? (
        <p className="jobs-board__stale" role="alert">
          <TriangleAlert aria-hidden="true" />
          รีเฟรชล่าสุดไม่สำเร็จ ข้อมูลบนบอร์ดอาจไม่เป็นปัจจุบัน —{' '}
          {isApiError(boardQuery.error) ? `${boardQuery.error.messageTh} (traceId: ${boardQuery.error.traceId ?? '-'})` : 'ลองกดรีเฟรช'}
        </p>
      ) : null}

      <div className="jobs-board__columns">
        {columns.map((column) => (
          <BoardColumn key={column.status} column={column} now={now} onSelectJob={onSelectJob} />
        ))}
      </div>
    </section>
  )
}

function BoardColumn({
  column,
  now,
  onSelectJob,
}: {
  column: JobBoardColumn
  now: Date
  onSelectJob: (jobId: string) => void
}) {
  const headingId = `jobs-board-column-${column.status}`
  return (
    <section className="jobs-board__column" aria-labelledby={headingId}>
      <header className="jobs-board__column-head">
        <h3 id={headingId}>
          <JobStatusChip status={column.status} label={column.statusLabel} />
        </h3>
        <span className="jobs-board__count" aria-label={`${column.cards.length} คัน`}>{column.cards.length}</span>
      </header>
      {column.cards.length === 0 ? (
        <p className="jobs-board__column-empty">ไม่มีรถในสถานะนี้</p>
      ) : (
        <ol className="jobs-board__cards">
          {column.cards.map((card) => (
            <li key={card.job.jobId}>
              <BoardCard card={card} now={now} onSelectJob={onSelectJob} />
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}

function BoardCard({ card, now, onSelectJob }: { card: JobBoardCard; now: Date; onSelectJob: (jobId: string) => void }) {
  const { job } = card
  const hasNewChat = Boolean(card.latestChatMessageId) && card.latestChatMessageId !== getLastSeenChatId(job.jobId)

  return (
    <button
      type="button"
      className={`jobs-board-card${job.isOverdue ? ' jobs-board-card--overdue' : ''}`}
      onClick={() => onSelectJob(job.jobId)}
      aria-label={`เปิดการ์ดจ๊อบ ${job.jobNo} ทะเบียน ${job.vehicleRegistration}`}
    >
      <div className="jobs-board-card__head">
        <VehicleImage vehicleId={job.vehicleId} className="jobs-board-card__car" />
        <div className="jobs-board-card__vehicle">
          <strong>{job.vehicleRegistration}</strong>
          <span>{job.vehicleModel || 'ไม่ระบุรุ่น'}</span>
        </div>
      </div>
      <p className="jobs-board-card__job-no">{job.jobNo}</p>
      <p className="jobs-board-card__customer" title={job.customerName}>{job.customerName}</p>

      <ul className="jobs-board-card__facts">
        <li>
          <Clock aria-hidden="true" />
          อยู่สถานะนี้ {formatStatusAge(card.statusSince, now)}
        </li>
        {job.promiseAt ? (
          <li className={job.isOverdue ? 'jobs-board-card__fact--danger' : undefined}>
            {job.isOverdue ? <TriangleAlert aria-hidden="true" /> : <CalendarClock aria-hidden="true" />}
            {job.isOverdue ? 'เกินกำหนดส่งมอบ' : 'นัดส่ง'} {formatDateTime(job.promiseAt)}
          </li>
        ) : null}
        {card.activeWorkers.map((worker) => (
          <li
            key={`${worker.technicianName}-${worker.startedAt}`}
            className={worker.kind === 'pause' ? 'jobs-board-card__fact--paused' : 'jobs-board-card__fact--working'}
          >
            {worker.kind === 'pause' ? <CirclePause aria-hidden="true" /> : <Timer aria-hidden="true" />}
            {worker.technicianName} {worker.kind === 'pause' ? 'พักอยู่' : 'กำลังทำ'}
          </li>
        ))}
        {card.awaitingCustomerQuotationCodes.length > 0 ? (
          <li className="jobs-board-card__fact--waiting">
            <Send aria-hidden="true" />
            รอลูกค้าเซ็น {card.awaitingCustomerQuotationCodes.join(', ')}
          </li>
        ) : null}
        {hasNewChat ? (
          <li className="jobs-board-card__fact--chat">
            <MessageCircle aria-hidden="true" />
            มีข้อความใหม่ในแชท
          </li>
        ) : null}
      </ul>
    </button>
  )
}
