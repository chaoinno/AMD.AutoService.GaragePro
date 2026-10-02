import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronLeft, ChevronRight, ClipboardX, GripVertical, Lock, MoveHorizontal } from 'lucide-react'
import { useMemo, useState, type DragEvent } from 'react'
import { toast } from 'sonner'
import { getJobCalendar, updateJobAppointment, updateJobPromise } from '../../api/jobs'
import type { Job, JobCalendarDateField, JobCalendarResult, JobStatusToken } from '../../api/types'
import { isApiError } from '../../api/client'
import { ConfirmModal } from '../../components/ConfirmModal'
import { JobStatusChip } from '../../components/JobStatusChip'
import { StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { formatDateTime } from '../../lib/format'
import { buildMonthGrid, dayKey, isSameDay, moveToDay, WEEKDAY_LABELS_TH } from './calendarMonth'
import { invalidateJobSchedule } from './scheduleQueries'

const monthYearFormatter = new Intl.DateTimeFormat('th-TH', { month: 'long', year: 'numeric' })
const chipTimeFormatter = new Intl.DateTimeFormat('th-TH', { hour: '2-digit', minute: '2-digit' })

// ตรงกับ tolerance ที่ API ใช้ (JobService.AppointmentPastToleranceMinutes) — กันลากไปวันที่ server จะปฏิเสธแน่ๆ
const PAST_TOLERANCE_MS = 5 * 60 * 1000
const DRAG_MIME = 'application/x-garagepro-job'

const FIELD_COPY: Record<JobCalendarDateField, { noun: string; loading: string; empty: string; emptyReason: string }> = {
  appointment: {
    noun: 'วันที่นัดเข้า',
    loading: 'ระบบกำลังอ่านข้อมูลวันนัดเข้าของเดือนนี้',
    empty: 'ไม่มีงานที่นัดเข้าในเดือนนี้',
    emptyReason: 'ไม่มีจ๊อบที่มีวันเวลานัดเข้าในช่วงนี้ — ลองเปลี่ยนเดือนหรือเปิดจ๊อบประเภท "รถนัดหมาย"',
  },
  promise: {
    noun: 'วันที่นัดส่งมอบ',
    loading: 'ระบบกำลังอ่านข้อมูลวันนัดส่งมอบของเดือนนี้',
    empty: 'ไม่มีงานที่นัดส่งมอบในเดือนนี้',
    emptyReason: 'ไม่มีจ๊อบที่ตั้งวันนัดส่งมอบไว้ในช่วงนี้ — ตั้งวันนัดส่งมอบได้จากการ์ดจ๊อบหรือตอนเปิดจ๊อบ',
  },
}

type JobsCalendarProps = {
  query: string
  status?: JobStatusToken
  dateField: JobCalendarDateField
  onSelectJob: (jobId: string) => void
}

/// field ติดไปกับ variables (ไม่อ่านจาก props ตอนยิง) เพราะปุ่ม "เลิกทำ" ใน toast อาจถูกกดหลังผู้ใช้สลับตัวกรองแล้ว
type RescheduleVars = { job: Job; field: JobCalendarDateField; from: string; to: Date; isUndo?: boolean }

function dateOf(job: Job, field: JobCalendarDateField): string | null {
  return field === 'promise' ? job.promiseAt : job.appointmentAt
}

/// เหตุผลที่ลากจ๊อบนี้ไม่ได้ (null = ลากได้) — ต้องตรงกับที่ API บังคับ (JobService.UpdateAppointmentAsync/
/// UpdatePromiseAsync) ไม่งั้นผู้ใช้จะลากได้แล้วโดนปฏิเสธทีหลัง
function rescheduleBlockedReason(job: Job, field: JobCalendarDateField): string | null {
  if (job.status === 'completed' || job.status === 'cancelled')
    return 'จ๊อบนี้ปิดแล้ว — เปลี่ยนวันไม่ได้'
  if (field === 'appointment' && job.jobTypeId !== 10)
    return 'รถเข้าอู่แล้ว (เปลี่ยนเป็นรถในอู่) — เปลี่ยนวันนัดเข้าไม่ได้'
  return null
}

/// [BIZ] วันส่งมอบต้องไม่ก่อนวันที่รถเข้า — โหมดส่งมอบ: ห้ามวางก่อนวันเข้า (วันเข้าจริงถ้าแปลงเป็นรถในอู่แล้ว)
/// · โหมดนัดเข้า: ห้ามวางเลยวันส่งมอบ · null = ไม่มีวันให้เทียบ
function scheduleBound(job: Job, field: JobCalendarDateField): { date: Date; label: string } | null {
  if (field === 'promise') {
    const arrival = job.actualArrivalAt ?? job.appointmentAt
    return arrival ? { date: new Date(arrival), label: `ก่อนวันที่รถเข้า (${formatDateTime(arrival)})` } : null
  }
  return job.promiseAt ? { date: new Date(job.promiseAt), label: `เลยวันนัดส่งมอบ (${formatDateTime(job.promiseAt)})` } : null
}

/// มุมมองปฏิทิน — วางจ๊อบตามวันที่นัดเข้า (AppointmentAt) หรือวันที่นัดส่งมอบ (PromiseAt) ตามตัวกรอง
/// ลากจ๊อบไปวางบนวันอื่นเพื่อเปลี่ยนวันของฟิลด์ที่เลือกอยู่ (คงเวลาเดิม) · server บันทึกประวัติทุกครั้ง
/// (ไม่กรองด้วยประเภทงาน เพราะจ๊อบที่ปิดแล้วถูกเปลี่ยนประเภทเป็น "ปิดจ๊อบ" โดยระบบเอง
/// ถ้ากรองด้วยประเภทจะทำให้นัดเก่าที่จบงานแล้วหายไปจากเดือนย้อนหลัง)
export function JobsCalendar({ query, status, dateField, onSelectJob }: JobsCalendarProps) {
  const queryClient = useQueryClient()
  const [cursor, setCursor] = useState(() => {
    const now = new Date()
    return { year: now.getFullYear(), month: now.getMonth() }
  })
  const [dayDetail, setDayDetail] = useState<Date | null>(null)
  const [dragging, setDragging] = useState<Job | null>(null)
  const [dragOverKey, setDragOverKey] = useState<string | null>(null)
  const copy = FIELD_COPY[dateField]

  const grid = useMemo(() => buildMonthGrid(cursor.year, cursor.month), [cursor.year, cursor.month])
  const calendarKey = ['jobs-calendar', cursor.year, cursor.month, query, status, dateField] as const

  const calendarQuery = useQuery({
    queryKey: calendarKey,
    queryFn: () => getJobCalendar({
      from: grid.rangeStart.toISOString(),
      to: grid.rangeEnd.toISOString(),
      query,
      status,
      dateField,
    }),
  })

  const rescheduleMutation = useMutation({
    mutationFn: ({ job, field, to }: RescheduleVars) => field === 'promise'
      ? updateJobPromise(job.jobId, { promiseAt: to.toISOString() })
      : updateJobAppointment(job.jobId, { appointmentAt: to.toISOString() }),
    // ย้ายชิปทันทีที่วาง (ไม่ต้องรอ round-trip) แล้วคืนค่าเดิมถ้า server ปฏิเสธ
    onMutate: async ({ job, field, to }) => {
      const key = calendarKey
      await queryClient.cancelQueries({ queryKey: key })
      const previous = queryClient.getQueryData<JobCalendarResult>(key)
      const prop = field === 'promise' ? 'promiseAt' : 'appointmentAt'
      queryClient.setQueryData<JobCalendarResult>(key, (old) => old && {
        ...old,
        items: old.items.map((j) => (j.jobId === job.jobId ? { ...j, [prop]: to.toISOString() } : j)),
      })
      return { previous, key }
    },
    onError: (error, { field }, context) => {
      if (context?.previous) queryClient.setQueryData(context.key, context.previous)
      toast.error(isApiError(error) ? error.messageTh : `เปลี่ยน${FIELD_COPY[field].noun}ไม่สำเร็จ`, {
        description: isApiError(error) ? `traceId: ${error.traceId}` : undefined,
      })
    },
    onSuccess: (_data, { job, field, from, to, isUndo }) => {
      const fromDate = new Date(from)
      const noun = FIELD_COPY[field].noun
      const message = isUndo
        ? `คืน${noun}ของ ${job.vehicleRegistration || job.jobNo} เป็น ${formatDateTime(to.toISOString())} แล้ว`
        : `เลื่อน${noun}ของ ${job.vehicleRegistration || job.jobNo} เป็น ${formatDateTime(to.toISOString())} แล้ว`
      // เลิกทำได้เฉพาะเมื่อค่าเดิมยังไม่เป็นอดีต — ไม่งั้น server ปฏิเสธอยู่ดี (วันนัดห้ามน้อยกว่าวันเวลาปัจจุบัน)
      const canUndo = !isUndo && fromDate.getTime() >= Date.now() - PAST_TOLERANCE_MS
      toast.success(message, canUndo ? {
        action: {
          label: 'เลิกทำ',
          onClick: () => rescheduleMutation.mutate({ job, field, from: to.toISOString(), to: fromDate, isUndo: true }),
        },
      } : undefined)
    },
    onSettled: (_data, _error, { job }) => invalidateJobSchedule(queryClient, job.jobId),
  })

  const jobsByDay = useMemo(() => {
    const map = new Map<string, Job[]>()
    for (const job of calendarQuery.data?.items ?? []) {
      const value = dateOf(job, dateField)
      if (!value) continue
      const key = dayKey(new Date(value))
      const bucket = map.get(key)
      if (bucket) bucket.push(job)
      else map.set(key, [job])
    }
    for (const bucket of map.values()) {
      bucket.sort((a, b) => new Date(dateOf(a, dateField)!).getTime() - new Date(dateOf(b, dateField)!).getTime())
    }
    return map
  }, [calendarQuery.data, dateField])

  const today = new Date()
  const monthTitle = monthYearFormatter.format(new Date(cursor.year, cursor.month, 1))

  const goToMonth = (delta: number) => {
    setCursor((prev) => {
      const next = new Date(prev.year, prev.month + delta, 1)
      return { year: next.getFullYear(), month: next.getMonth() }
    })
  }

  /// ปลายทางของการวาง: same = วันเดิม (ไม่ทำอะไร) · past = วันเวลาเป็นอดีต · conflict = ผิดลำดับนัดเข้า/ส่งมอบ
  /// · ok = วางได้ — past/conflict ต้องตรงกับที่ API ปฏิเสธ (JobService.ValidatePromise/ValidateScheduleOrder)
  const dropTargetFor = (job: Job, day: Date): { state: 'same' | 'past' | 'conflict' | 'ok'; target: Date } | null => {
    const value = dateOf(job, dateField)
    if (!value) return null
    const original = new Date(value)
    const target = moveToDay(original, day)
    if (isSameDay(original, day)) return { state: 'same', target }
    if (target.getTime() < Date.now() - PAST_TOLERANCE_MS) return { state: 'past', target }
    const bound = scheduleBound(job, dateField)
    if (bound && (dateField === 'promise' ? target < bound.date : target > bound.date)) return { state: 'conflict', target }
    return { state: 'ok', target }
  }

  const endDrag = () => {
    setDragging(null)
    setDragOverKey(null)
  }

  const handleDragStart = (event: DragEvent<HTMLButtonElement>, job: Job) => {
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData(DRAG_MIME, job.jobId)
    event.dataTransfer.setData('text/plain', job.jobNo)
    setDragging(job)
  }

  const handleDragOver = (event: DragEvent<HTMLDivElement>, day: Date) => {
    if (!dragging) return
    const drop = dropTargetFor(dragging, day)
    if (drop?.state === 'ok') {
      // preventDefault = อนุญาตให้วาง · ไม่เรียก = เบราว์เซอร์แสดงเคอร์เซอร์ห้ามวางเอง
      event.preventDefault()
      event.dataTransfer.dropEffect = 'move'
    }
    const key = dayKey(day)
    if (dragOverKey !== key) setDragOverKey(key)
  }

  const handleDrop = (event: DragEvent<HTMLDivElement>, day: Date) => {
    event.preventDefault()
    const job = dragging
    endDrag()
    if (!job || event.dataTransfer.getData(DRAG_MIME) !== job.jobId) return
    const drop = dropTargetFor(job, day)
    if (drop?.state !== 'ok') return
    rescheduleMutation.mutate({ job, field: dateField, from: dateOf(job, dateField)!, to: drop.target })
  }

  if (calendarQuery.isPending) {
    return (
      <StateBlock
        variant="loading"
        title="กำลังโหลดปฏิทิน"
        reason={copy.loading}
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
        title="โหลดปฏิทินไม่สำเร็จ"
        reason={isApiError(calendarQuery.error) ? calendarQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(calendarQuery.error) ? calendarQuery.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void calendarQuery.refetch()}
      />
    )
  }

  const isEmpty = (calendarQuery.data?.items.length ?? 0) === 0
  const draggingTime = dragging && dateOf(dragging, dateField)
    ? chipTimeFormatter.format(new Date(dateOf(dragging, dateField)!))
    : ''
  const draggingBound = dragging ? scheduleBound(dragging, dateField) : null

  return (
    <div className="jobs-calendar">
      <div className="jobs-calendar__header">
        <Button variant="outline" size="sm" onClick={() => goToMonth(-1)}>
          <ChevronLeft aria-hidden="true" /> เดือนก่อนหน้า
        </Button>
        <h3>{monthTitle} · ตาม{copy.noun}</h3>
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

      <div
        className={`jobs-calendar__drag-hint${dragging ? ' jobs-calendar__drag-hint--active' : ''}`}
        role="status"
        aria-live="polite"
      >
        <MoveHorizontal aria-hidden="true" />
        {dragging ? (
          <span>
            กำลังย้าย <strong>{dragging.vehicleRegistration || dragging.jobNo}</strong> — วางบนวันที่ต้องการ
            (คงเวลาเดิม {draggingTime} น.) · ช่องลายขีดวางไม่ได้: วันที่ผ่านมาแล้ว
            {draggingBound ? <> หรือ{draggingBound.label} — วันส่งมอบต้องไม่ก่อนวันนัดเข้า</> : null}
          </span>
        ) : (
          <span>
            ลากจ๊อบไปวางบนวันอื่นเพื่อเปลี่ยน{copy.noun} (คงเวลาเดิม) · ระบบบันทึกประวัติการเปลี่ยนทุกครั้ง ·
            ต้องการเปลี่ยนเวลาด้วย ให้คลิกจ๊อบแล้วแก้ในการ์ดจ๊อบ
          </span>
        )}
      </div>

      {isEmpty ? (
        <div className="jobs-calendar__notice" role="status">
          <ClipboardX aria-hidden="true" />
          <div>
            <strong>{copy.empty}</strong>
            <p>สาเหตุ: {copy.emptyReason}</p>
          </div>
        </div>
      ) : null}

      {calendarQuery.data?.truncated ? (
        <div className="jobs-calendar__notice jobs-calendar__notice--warning" role="alert">
          แสดง {calendarQuery.data.limit} รายการแรกของช่วงนี้ — โปรดใช้ตัวกรองคำค้นหา/สถานะเพื่อจำกัดผลลัพธ์
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
          const drop = dragging ? dropTargetFor(dragging, day) : null
          const classes = [
            'jobs-calendar__day',
            outside ? 'jobs-calendar__day--outside' : '',
            isToday ? 'jobs-calendar__day--today' : '',
            drop?.state === 'past' || drop?.state === 'conflict' ? 'jobs-calendar__day--drop-disabled' : '',
            drop?.state === 'ok' && dragOverKey === key ? 'jobs-calendar__day--drop-target' : '',
          ].filter(Boolean).join(' ')

          return (
            <div
              key={key}
              className={classes}
              onDragOver={(event) => handleDragOver(event, day)}
              onDragLeave={() => { if (dragOverKey === key) setDragOverKey(null) }}
              onDrop={(event) => handleDrop(event, day)}
            >
              <div className="jobs-calendar__day-head">
                <span className="jobs-calendar__day-number">{day.getDate()}</span>
                {isToday ? <span className="jobs-calendar__today-badge">วันนี้</span> : null}
              </div>
              <div className="jobs-calendar__chips">
                {visible.map((job) => {
                  const value = dateOf(job, dateField)
                  const time = value ? chipTimeFormatter.format(new Date(value)) : '—'
                  const blockedReason = rescheduleBlockedReason(job, dateField)
                  const saving = rescheduleMutation.isPending && rescheduleMutation.variables?.job.jobId === job.jobId
                  return (
                    <button
                      key={job.jobId}
                      type="button"
                      draggable={!blockedReason && !saving}
                      onDragStart={(event) => handleDragStart(event, job)}
                      onDragEnd={endDrag}
                      className={[
                        'jobs-calendar__chip',
                        `job-status-${job.status}`,
                        blockedReason ? 'jobs-calendar__chip--locked' : 'jobs-calendar__chip--draggable',
                        dragging?.jobId === job.jobId ? 'jobs-calendar__chip--dragging' : '',
                        saving ? 'jobs-calendar__chip--saving' : '',
                      ].filter(Boolean).join(' ')}
                      onClick={() => onSelectJob(job.jobId)}
                      title={blockedReason ?? `ลากเพื่อเปลี่ยน${copy.noun} · คลิกเพื่อเปิดการ์ดจ๊อบ`}
                      aria-label={`${copy.noun} ${time} น. ทะเบียน ${job.vehicleRegistration} สถานะ ${job.statusLabel}${blockedReason ? ` — ${blockedReason}` : ''}`}
                    >
                      <span className="jobs-calendar__chip-time">
                        {blockedReason
                          ? <Lock aria-hidden="true" className="jobs-calendar__chip-icon" />
                          : <GripVertical aria-hidden="true" className="jobs-calendar__chip-icon" />}
                        {time} · {job.vehicleRegistration || 'ไม่ระบุทะเบียน'}
                        {saving ? ' · กำลังบันทึก…' : ''}
                      </span>
                      <JobStatusChip status={job.status} label={job.statusLabel} />
                    </button>
                  )
                })}
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
        <span className="jobs-calendar__legend-lock"><Lock aria-hidden="true" /> เปลี่ยนวันไม่ได้ (ชี้ที่จ๊อบเพื่อดูเหตุผล)</span>
      </div>

      <ConfirmModal
        open={dayDetail !== null}
        title={dayDetail ? `${copy.noun} ${dayDetail.getDate()}/${dayDetail.getMonth() + 1}/${dayDetail.getFullYear() + 543}` : ''}
        description="ลากวางได้เฉพาะจ๊อบที่แสดงในช่องปฏิทิน — รายการในหน้าต่างนี้ให้คลิกเปิดการ์ดจ๊อบแล้วแก้วันเวลาจากในการ์ด"
        onClose={() => setDayDetail(null)}
        size="medium"
      >
        <ul className="jobs-calendar__day-detail-list">
          {(dayDetail ? jobsByDay.get(dayKey(dayDetail)) ?? [] : []).map((job) => {
            const value = dateOf(job, dateField)
            return (
              <li key={job.jobId}>
                <button
                  type="button"
                  onClick={() => { onSelectJob(job.jobId); setDayDetail(null) }}
                >
                  <span>{value ? chipTimeFormatter.format(new Date(value)) : '—'} น. · {job.vehicleRegistration} · {job.customerName}</span>
                  <JobStatusChip status={job.status} label={job.statusLabel} />
                </button>
              </li>
            )
          })}
        </ul>
      </ConfirmModal>
    </div>
  )
}
