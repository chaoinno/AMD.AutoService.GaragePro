import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlarmClockOff, Timer, TimerOff, Wrench } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import {
  getWorkIntervals,
  updateWorkInterval,
  voidWorkInterval,
  type WorkIntervalRow,
} from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { StatTile } from '../../components/StatTile'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { ConfirmModal } from '../../components/ConfirmModal'
import {
  formatDateTime,
  formatWorkDuration,
  isoToLocalInput,
  localInputToIso,
  nowLocalInputValue,
} from '../../lib/format'
import { Field, QueryState } from '../master-data/MasterDataCommon'
import './reports.css'

const isoDate = (d: Date) => d.toISOString().slice(0, 10)
const defaultFrom = isoDate(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000))
const defaultTo = isoDate(new Date())

/** [ASSUME] docs/09 §12 — ต้องตรงกับ WorkTimeOptions.EditWindowDays ฝั่ง backend */
const EDIT_WINDOW_DAYS = 7

/**
 * เวลาทำงานของช่าง — **ไม่ใช่รายงานประเมินประสิทธิภาพ** (ยังไม่ได้ทำ)
 *
 * หน้านี้มีไว้ให้หัวหน้าช่าง/ผู้จัดการเห็นและแก้ข้อมูลดิบระหว่างช่วงเก็บข้อมูล — ถ้าไม่มี คาบที่ช่าง
 * ลืมกดหยุดจะสะสมโดยไม่มีใครแก้ได้ แล้วพอถึงรอบทำรายงานจริงอาจพบว่าข้อมูลทั้งเดือนใช้ไม่ได้
 */
export function WorkIntervalsPage() {
  const [fromDate, setFromDate] = useState(defaultFrom)
  const [toDate, setToDate] = useState(defaultTo)
  const [onlyNeedsReview, setOnlyNeedsReview] = useState(false)
  const [editing, setEditing] = useState<WorkIntervalRow | null>(null)
  const [voiding, setVoiding] = useState<WorkIntervalRow | null>(null)

  const queryClient = useQueryClient()
  const queryKey = ['reports', 'work-intervals', fromDate, toDate, onlyNeedsReview]
  const query = useQuery({
    queryKey,
    queryFn: () => getWorkIntervals(fromDate, `${toDate}T23:59:59`, onlyNeedsReview),
  })

  const rows = query.data ?? []
  const needsReview = rows.filter((r) => r.isAutoCapped || r.durationSeconds == null).length
  const trackedSeconds = rows
    .filter((r) => r.kind === 'work' && !r.isVoided && !r.isAutoCapped)
    .reduce((sum, r) => sum + (r.durationSeconds ?? 0), 0)

  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['reports', 'work-intervals'] })

  const columns = [
    {
      id: 'technician',
      header: 'ช่าง',
      value: (r: WorkIntervalRow) => r.technicianName,
      render: (r: WorkIntervalRow) => (
        <>
          <strong>{r.technicianName}</strong>
          <br />
          <small className="purchase-sub">
            {r.vehicleRegistration} · {r.jobNo}
          </small>
        </>
      ),
    },
    {
      id: 'startedAt',
      header: 'เริ่ม',
      value: (r: WorkIntervalRow) => new Date(r.startedAt).getTime(),
      render: (r: WorkIntervalRow) => formatDateTime(r.startedAt),
    },
    {
      id: 'duration',
      header: 'ระยะเวลา',
      // sort ด้วยตัวเลขดิบ ไม่ใช่ข้อความที่ format แล้ว (กฎตาราง CLAUDE.md)
      value: (r: WorkIntervalRow) => r.durationSeconds ?? -1,
      render: (r: WorkIntervalRow) => (
        <span className="report-num">{formatWorkDuration(r.durationSeconds)}</span>
      ),
    },
    {
      id: 'kind',
      header: 'ชนิด',
      value: (r: WorkIntervalRow) => r.kind,
      render: (r: WorkIntervalRow) => (
        <>
          {r.kind === 'pause' ? 'พักงาน' : 'ทำงาน'}
          {r.isRework ? <> · <span title="กลับมาแก้งานหลังเข้า QC">แก้งาน</span></> : null}
        </>
      ),
    },
    {
      id: 'quality',
      header: 'คุณภาพข้อมูล',
      value: (r: WorkIntervalRow) => (r.isVoided ? 3 : r.isAutoCapped ? 2 : r.durationSeconds == null ? 1 : 0),
      render: (r: WorkIntervalRow) => {
        if (r.isVoided) return <span className="report-lowsample">ยกเลิกแล้ว — {r.voidReason}</span>
        if (r.isAutoCapped)
          return <span className="report-lowsample">ระบบตัดให้เพราะลืมกดหยุด — ไม่เข้าการคำนวณ</span>
        if (r.durationSeconds == null) return <span className="report-lowsample">ยังจับเวลาอยู่</span>
        if (r.editedAt) return <span className="report-lowsample">แก้เวลาแล้ว — {r.editReason}</span>
        return 'ปกติ'
      },
    },
    {
      id: 'actions',
      header: '',
      render: (r: WorkIntervalRow) =>
        r.isVoided ? null : (
          <div className="report-row-actions">
            <Button variant="ghost" onClick={() => setEditing(r)}>
              แก้เวลา
            </Button>
            <Button variant="ghost" onClick={() => setVoiding(r)}>
              ยกเลิกคาบ
            </Button>
          </div>
        ),
    },
  ]

  return (
    <AppShell title="เวลาทำงานของช่าง">
      <section className="report-page-heading">
        <div>
          <h2>
            <Wrench aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />
            เวลาทำงานของช่าง
          </h2>
          <p>
            ข้อมูลดิบจากการจับเวลาบนมือถือ — ยังไม่ใช่รายงานประเมินประสิทธิภาพ
            หน้านี้มีไว้ตรวจและแก้คาบที่ผิดก่อนจะเอาข้อมูลไปใช้จริง
          </p>
        </div>
        <div className="report-date-filter">
          <label>
            จากวันที่
            <Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} />
          </label>
          <label>
            ถึงวันที่
            <Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} />
          </label>
          <label className="report-filter-check">
            <input
              type="checkbox"
              checked={onlyNeedsReview}
              onChange={(e) => setOnlyNeedsReview(e.target.checked)}
            />
            เฉพาะที่ต้องตรวจ
          </label>
        </div>
      </section>

      <QueryState
        query={query}
        loadingTitle="กำลังโหลดเวลาทำงาน"
        emptyTitle="ยังไม่มีข้อมูลเวลาทำงาน"
        emptyReason="ยังไม่มีช่างคนไหนกดจับเวลาในช่วงที่เลือก"
        onRetry={() => void query.refetch()}
      >
        {query.data ? (
          <>
            <div className="stat-tile-grid">
              <StatTile icon={Timer} label="จำนวนคาบ" value={rows.length} hint="ในช่วงที่เลือก" />
              <StatTile
                icon={Wrench}
                label="ชั่วโมงลงมือรวม"
                value={formatWorkDuration(trackedSeconds)}
                hint="ชั่วโมง-คน ไม่ใช่เวลาที่รถอยู่ในอู่"
              />
              <StatTile
                icon={AlarmClockOff}
                label="คาบที่ต้องตรวจ"
                value={needsReview}
                hint="ลืมกดหยุดหรือยังเปิดค้าง"
                tone={needsReview > 0 ? 'warning' : 'default'}
              />
            </div>

            {rows.length === 0 ? (
              <p className="purchase-empty">ไม่มีคาบที่ตรงกับเงื่อนไขนี้</p>
            ) : (
              <Card className="management-table-card">
                <ManagementTable data={rows} columns={columns} sortScope="loaded" />
              </Card>
            )}
          </>
        ) : null}
      </QueryState>

      {editing ? (
        <EditIntervalModal interval={editing} onClose={() => setEditing(null)} onSaved={refresh} />
      ) : null}
      {voiding ? (
        <VoidIntervalModal interval={voiding} onClose={() => setVoiding(null)} onSaved={refresh} />
      ) : null}
    </AppShell>
  )
}

function EditIntervalModal({
  interval,
  onClose,
  onSaved,
}: {
  interval: WorkIntervalRow
  onClose: () => void
  onSaved: () => void
}) {
  const [startedAt, setStartedAt] = useState(isoToLocalInput(interval.startedAt))
  const [endedAt, setEndedAt] = useState(
    interval.endedAt ? isoToLocalInput(interval.endedAt) : nowLocalInputValue(),
  )
  const [reason, setReason] = useState('')

  const expired =
    Date.now() - new Date(interval.startedAt).getTime() > EDIT_WINDOW_DAYS * 24 * 60 * 60 * 1000

  // [UI] ปุ่มที่ปิดใช้งานต้องบอกเหตุผลบนหน้า ไม่ใช่ tooltip ที่ต้องเอาเมาส์ไปชี้
  const blockedReason = expired
    ? `คาบนี้เริ่มมานานเกิน ${EDIT_WINDOW_DAYS} วันแล้ว แก้ย้อนหลังไม่ได้`
    : !reason.trim()
      ? 'ต้องระบุเหตุผลที่แก้เวลา'
      : !startedAt || !endedAt
        ? 'กรอกเวลาเริ่มและเวลาสิ้นสุดให้ครบ'
        : null

  const mutation = useMutation({
    mutationFn: () =>
      updateWorkInterval(interval.id, {
        startedAt: localInputToIso(startedAt),
        endedAt: localInputToIso(endedAt),
        reason: reason.trim(),
      }),
    onSuccess: () => {
      toast.success('แก้เวลาเรียบร้อย')
      onSaved()
      onClose()
    },
  })

  return (
    <ConfirmModal
      open
      title={`แก้เวลาของ ${interval.technicianName}`}
      description="เวลาที่แก้จะเข้าการคำนวณแทนค่าที่ระบบเดาให้ และบันทึกไว้ว่าใครแก้เพราะอะไร"
      onClose={() => { if (!mutation.isPending) onClose() }}
      footer={
        <>
          <Button variant="ghost" disabled={mutation.isPending} onClick={onClose}>
            กลับ
          </Button>
          <Button disabled={!!blockedReason || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึกเวลาใหม่'}
          </Button>
        </>
      }
    >
      <p className="section-help">
        {interval.vehicleRegistration} · {interval.jobNo}
        {interval.isAutoCapped
          ? ' — ระบบตัดเวลานี้ให้เองเพราะลืมกดหยุด แก้แล้วจะกลับเข้าการคำนวณ'
          : ''}
      </p>
      {blockedReason ? <p className="section-help">{blockedReason}</p> : null}
      <Field label="เวลาเริ่ม">
        <Input
          type="datetime-local"
          value={startedAt}
          max={nowLocalInputValue()}
          onChange={(e) => setStartedAt(e.target.value)}
        />
      </Field>
      <Field label="เวลาสิ้นสุด">
        <Input
          type="datetime-local"
          value={endedAt}
          max={nowLocalInputValue()}
          onChange={(e) => setEndedAt(e.target.value)}
        />
      </Field>
      <Field label="เหตุผล">
        <Input
          value={reason}
          placeholder="เช่น ช่างลืมกดหยุด ยืนยันเวลาจริงกับหัวหน้าแล้ว"
          onChange={(e) => setReason(e.target.value)}
        />
      </Field>
    </ConfirmModal>
  )
}

function VoidIntervalModal({
  interval,
  onClose,
  onSaved,
}: {
  interval: WorkIntervalRow
  onClose: () => void
  onSaved: () => void
}) {
  const [reason, setReason] = useState('')

  const mutation = useMutation({
    mutationFn: () => voidWorkInterval(interval.id, reason.trim()),
    onSuccess: () => {
      toast.success('ยกเลิกคาบเรียบร้อย')
      onSaved()
      onClose()
    },
  })

  return (
    <ConfirmModal
      open
      title={`ยกเลิกคาบของ ${interval.technicianName}`}
      description="คาบที่ยกเลิกจะไม่เข้าการคำนวณ แต่ยังอยู่ในระบบพร้อมเหตุผล"
      onClose={() => { if (!mutation.isPending) onClose() }}
      footer={
        <>
          <Button variant="ghost" disabled={mutation.isPending} onClick={onClose}>
            กลับ
          </Button>
          <Button disabled={!reason.trim() || mutation.isPending} onClick={() => mutation.mutate()}>
            {mutation.isPending ? 'กำลังยกเลิก…' : 'ยกเลิกคาบนี้'}
          </Button>
        </>
      }
    >
      <p className="section-help">
        <TimerOff aria-hidden="true" style={{ width: 15, verticalAlign: -2, marginRight: 4 }} />
        คาบจะไม่เข้าการคำนวณอีกต่อไป แต่แถวยังอยู่ในระบบพร้อมเหตุผล — ลบทิ้งจริงไม่ได้
        เพราะจะแยกไม่ออกว่าเวลาที่หายไปคือ “ไม่เคยมี” หรือ “ถูกลบ”
      </p>
      {!reason.trim() ? <p className="section-help">ต้องระบุเหตุผลที่ยกเลิก</p> : null}
      <Field label="เหตุผล">
        <Input
          value={reason}
          placeholder="เช่น กดผิดคัน"
          onChange={(e) => setReason(e.target.value)}
        />
      </Field>
    </ConfirmModal>
  )
}
