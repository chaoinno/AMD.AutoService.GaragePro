import { useQuery } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, ClipboardX } from 'lucide-react'
import { useMemo, useState } from 'react'
import { getJobCalendar } from '../../api/jobs'
import type { Job, JobStatusToken } from '../../api/types'
import { isApiError } from '../../api/client'
import { ConfirmModal } from '../../components/ConfirmModal'
import { JobStatusChip } from '../../components/JobStatusChip'
import { StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { buildMonthGrid, dayKey, isSameDay, WEEKDAY_LABELS_TH } from './calendarMonth'

const monthYearFormatter = new Intl.DateTimeFormat('th-TH', { month: 'long', year: 'numeric' })
const chipTimeFormatter = new Intl.DateTimeFormat('th-TH', { hour: '2-digit', minute: '2-digit' })

type JobsCalendarProps = {
  query: string
  status?: JobStatusToken
  onSelectJob: (jobId: string) => void
}

/// มุมมองปฏิทินนัดหมาย — แสดงเฉพาะงานที่มี AppointmentAt (ไม่ใช่กรองด้วยประเภทงาน เพราะจ๊อบที่ปิดแล้วถูกเปลี่ยน
/// ประเภทเป็น "ปิดจ๊อบ" โดยระบบเอง ถ้ากรองด้วยประเภทจะทำให้นัดหมายเก่าที่จบงานแล้วหายไปจากเดือนย้อนหลัง)
export function JobsCalendar({ query, status, onSelectJob }: JobsCalendarProps) {
  const [cursor, setCursor] = useState(() => {
    const now = new Date()
    return { year: now.getFullYear(), month: now.getMonth() }
  })
  const [dayDetail, setDayDetail] = useState<Date | null>(null)

  const grid = useMemo(() => buildMonthGrid(cursor.year, cursor.month), [cursor.year, cursor.month])

  const calendarQuery = useQuery({
    queryKey: ['jobs-calendar', cursor.year, cursor.month, query, status],
    queryFn: () => getJobCalendar({
      from: grid.rangeStart.toISOString(),
      to: grid.rangeEnd.toISOString(),
      query,
      status,
    }),
  })

  const jobsByDay = useMemo(() => {
    const map = new Map<string, Job[]>()
    for (const job of calendarQuery.data?.items ?? []) {
      if (!job.appointmentAt) continue
      const key = dayKey(new Date(job.appointmentAt))
      const bucket = map.get(key)
      if (bucket) bucket.push(job)
      else map.set(key, [job])
    }
    return map
  }, [calendarQuery.data])

  const today = new Date()
  const monthTitle = monthYearFormatter.format(new Date(cursor.year, cursor.month, 1))

  const goToMonth = (delta: number) => {
    setCursor((prev) => {
      const next = new Date(prev.year, prev.month + delta, 1)
      return { year: next.getFullYear(), month: next.getMonth() }
    })
  }

  if (calendarQuery.isPending) {
    return (
      <StateBlock
        variant="loading"
        title="กำลังโหลดปฏิทินนัดหมาย"
        reason="ระบบกำลังอ่านข้อมูลนัดหมายของเดือนนี้"
        traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
        actionLabel="โหลดใหม่"
        onAction={() => void calendarQuery.refetch()}
      />
    )
  }

  if (calendarQuery.isError) {
    return (
      <StateBlock
        variant="error"
        title="โหลดปฏิทินนัดหมายไม่สำเร็จ"
        reason={isApiError(calendarQuery.error) ? calendarQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(calendarQuery.error) ? calendarQuery.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void calendarQuery.refetch()}
      />
    )
  }

  const isEmpty = (calendarQuery.data?.items.length ?? 0) === 0

  return (
    <div className="jobs-calendar">
      <div className="jobs-calendar__header">
        <Button variant="outline" size="sm" onClick={() => goToMonth(-1)}>
          <ChevronLeft aria-hidden="true" /> เดือนก่อนหน้า
        </Button>
        <h3>{monthTitle}</h3>
        <Button variant="outline" size="sm" onClick={() => goToMonth(1)}>
          เดือนถัดไป <ChevronRight aria-hidden="true" />
        </Button>
        <Button
          variant="ghost"
          size="sm"
          onClick={() => setCursor({ year: today.getFullYear(), month: today.getMonth() })}
        >
          วันนี้
        </Button>
      </div>

      {isEmpty ? (
        <div className="jobs-calendar__notice" role="status">
          <ClipboardX aria-hidden="true" />
          <div>
            <strong>ไม่มีงานนัดหมายในเดือนนี้</strong>
            <p>สาเหตุ: ไม่มีจ๊อบประเภท "รถนัดหมาย" ที่มีวันเวลานัดในช่วงนี้ — ลองเปลี่ยนเดือนหรือเปิดจ๊อบนัดหมายใหม่</p>
          </div>
        </div>
      ) : null}

      {calendarQuery.data?.truncated ? (
        <div className="jobs-calendar__notice jobs-calendar__notice--warning" role="alert">
          แสดง {calendarQuery.data.limit} นัดหมายแรกของช่วงนี้ — โปรดใช้ตัวกรองคำค้นหา/สถานะเพื่อจำกัดผลลัพธ์
        </div>
      ) : null}

      <div className="jobs-calendar__weekdays" aria-hidden="true">
        {WEEKDAY_LABELS_TH.map((label) => <span key={label}>{label}</span>)}
      </div>

      <div className="jobs-calendar__grid">
        {grid.days.map((day) => {
          const key = dayKey(day)
          const jobsToday = jobsByDay.get(key) ?? []
          const outside = day.getMonth() !== cursor.month
          const isToday = isSameDay(day, today)
          const visible = jobsToday.slice(0, 3)
          const overflow = jobsToday.length - visible.length

          return (
            <div
              key={key}
              className={`jobs-calendar__day${outside ? ' jobs-calendar__day--outside' : ''}${isToday ? ' jobs-calendar__day--today' : ''}`}
            >
              <div className="jobs-calendar__day-head">
                <span className="jobs-calendar__day-number">{day.getDate()}</span>
                {isToday ? <span className="jobs-calendar__today-badge">วันนี้</span> : null}
              </div>
              <div className="jobs-calendar__chips">
                {visible.map((job) => (
                  <button
                    key={job.jobId}
                    type="button"
                    className={`jobs-calendar__chip job-status-${job.status}`}
                    onClick={() => onSelectJob(job.jobId)}
                    aria-label={`นัดหมาย ${job.appointmentAt ? chipTimeFormatter.format(new Date(job.appointmentAt)) : ''} น. ทะเบียน ${job.vehicleRegistration} สถานะ ${job.statusLabel}`}
                  >
                    <span className="jobs-calendar__chip-time">
                      {job.appointmentAt ? chipTimeFormatter.format(new Date(job.appointmentAt)) : '—'} · {job.vehicleRegistration || 'ไม่ระบุทะเบียน'}
                    </span>
                    <JobStatusChip status={job.status} label={job.statusLabel} />
                  </button>
                ))}
                {overflow > 0 ? (
                  <button type="button" className="jobs-calendar__more" onClick={() => setDayDetail(day)}>
                    + อีก {overflow} รายการ
                  </button>
                ) : null}
              </div>
            </div>
          )
        })}
      </div>

      <div className="jobs-calendar__legend">
        <span className="job-status-waitinspect">รอตรวจเช็ค</span>
        <span className="job-status-waitquote">รอเสนอราคา</span>
        <span className="job-status-waitapprove">รออนุมัติ</span>
        <span className="job-status-approved">อนุมัติแล้ว</span>
        <span className="job-status-inprogress">กำลังซ่อม</span>
        <span className="job-status-waitparts">รออะไหล่</span>
        <span className="job-status-qc">QC ตรวจสอบ</span>
        <span className="job-status-ready">พร้อมส่งมอบ</span>
        <span className="job-status-completed">เสร็จสมบูรณ์</span>
        <span className="job-status-cancelled">ยกเลิก</span>
      </div>

      <ConfirmModal
        open={dayDetail !== null}
        title={dayDetail ? `นัดหมายวันที่ ${dayDetail.getDate()}/${dayDetail.getMonth() + 1}/${dayDetail.getFullYear() + 543}` : ''}
        onClose={() => setDayDetail(null)}
        size="medium"
      >
        <ul className="jobs-calendar__day-detail-list">
          {(dayDetail ? jobsByDay.get(dayKey(dayDetail)) ?? [] : []).map((job) => (
            <li key={job.jobId}>
              <button
                type="button"
                onClick={() => { onSelectJob(job.jobId); setDayDetail(null) }}
              >
                <span>{job.appointmentAt ? chipTimeFormatter.format(new Date(job.appointmentAt)) : '—'} น. · {job.vehicleRegistration} · {job.customerName}</span>
                <JobStatusChip status={job.status} label={job.statusLabel} />
              </button>
            </li>
          ))}
        </ul>
      </ConfirmModal>
    </div>
  )
}
