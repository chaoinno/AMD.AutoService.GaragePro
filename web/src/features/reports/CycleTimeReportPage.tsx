import { useQuery } from '@tanstack/react-query'
import { Clock3, Hourglass, TimerReset } from 'lucide-react'
import { useState } from 'react'
import { getCycleTimeReport } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { ReportBar } from '../../components/ReportBar'
import { StatTile } from '../../components/StatTile'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { ManagementTable } from '../../components/ManagementTable'
import { QueryState } from '../master-data/MasterDataCommon'
import { jobStatusColors } from './reportColors'
import './reports.css'

const isoDate = (d: Date) => d.toISOString().slice(0, 10)
const defaultFrom = isoDate(new Date(Date.now() - 30 * 24 * 60 * 60 * 1000))
const defaultTo = isoDate(new Date())

export function CycleTimeReportPage() {
  const [fromDate, setFromDate] = useState(defaultFrom)
  const [toDate, setToDate] = useState(defaultTo)
  const query = useQuery({
    queryKey: ['reports', 'cycle-time', fromDate, toDate],
    queryFn: () => getCycleTimeReport(fromDate, `${toDate}T23:59:59`),
  })

  return (
    <AppShell title="รอบเวลาทำงาน (SLA)">
      <section className="report-page-heading">
        <div>
          <h2><Clock3 aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />รอบเวลาต่อขั้นตอนงาน</h2>
          <p>เวลาเฉลี่ยที่จ๊อบค้างในแต่ละสถานะ คำนวณจากประวัติเปลี่ยนสถานะจริง — เฉพาะจ๊อบที่เปิดในช่วงที่เลือก</p>
        </div>
        <div className="report-date-filter">
          <label>จากวันที่<Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} /></label>
          <label>ถึงวันที่<Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} /></label>
        </div>
      </section>

      <QueryState query={query} loadingTitle="กำลังโหลดรายงาน" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ยังไม่มีจ๊อบที่เปิดในช่วงนี้" onRetry={() => void query.refetch()}>
        {query.data ? (
          <>
            <div className="stat-tile-grid">
              <StatTile icon={Hourglass} label="จ๊อบที่ปิดในช่วงนี้" value={query.data.completedJobCount} hint="เสร็จสมบูรณ์หรือยกเลิก" />
              <StatTile icon={TimerReset} label="รอบเวลาเฉลี่ย" value={query.data.averageTotalHours != null ? `${query.data.averageTotalHours} ชม.` : '—'} hint="ตั้งแต่เปิดจ๊อบถึงปิดงาน" />
              <StatTile icon={TimerReset} label="รอบเวลา P90" value={query.data.p90TotalHours != null ? `${query.data.p90TotalHours} ชม.` : '—'} hint="90% ของจ๊อบที่ปิดใช้เวลาไม่เกินนี้ (ยังไม่มี SLA เป้าหมายยืนยัน)" />
            </div>

            <h3 className="report-section-title">เวลาเฉลี่ยต่อสถานะ (ชั่วโมง)</h3>
            {query.data.averageDurationByStatus.length === 0 ? (
              <p className="purchase-empty">ยังไม่มีข้อมูลรอบเวลาในช่วงนี้ (ต้องมีจ๊อบที่เปลี่ยนสถานะแล้ว)</p>
            ) : (
              <Card className="report-bar-group">
                {(() => {
                  const max = Math.max(1, ...query.data.averageDurationByStatus.map((s) => s.averageHours))
                  return query.data.averageDurationByStatus.map((s) => (
                    <ReportBar key={s.status} label={s.statusLabelTh} valueLabel={`${s.averageHours} ชม. (${s.segmentCount} ครั้ง)`} ratio={s.averageHours / max} color={jobStatusColors[s.status] ?? '#64748b'} />
                  ))
                })()}
              </Card>
            )}

            <h3 className="report-section-title">จ๊อบที่ค้างในสถานะปัจจุบันนานที่สุด</h3>
            {query.data.topStuckJobs.length === 0 ? (
              <p className="purchase-empty">ไม่มีจ๊อบค้างอยู่ตอนนี้</p>
            ) : (
              <Card className="management-table-card">
                <ManagementTable data={query.data.topStuckJobs} sortScope="page" columns={[
                  { id: 'jobNo', header: 'เลขจ๊อบ', value: (j) => j.jobNo, render: (j) => <strong>{j.jobNo}</strong> },
                  { id: 'customer', header: 'ลูกค้า', value: (j) => j.customerName, render: (j) => j.customerName },
                  { id: 'status', header: 'สถานะปัจจุบัน', value: (j) => j.statusLabelTh, render: (j) => <span className={`job-status-chip job-status-${j.status}`}>{j.statusLabelTh}</span> },
                  { id: 'hours', header: 'ค้างมาแล้ว', value: (j) => j.hoursInStatus, render: (j) => <span className="report-num">{j.hoursInStatus} ชม.</span> },
                  { id: 'overdue', header: 'เกินนัดหรือไม่', value: (j) => (j.isOverdue ? 1 : 0), render: (j) => (j.isOverdue ? <span className="job-status-chip job-status-waitparts">เกินนัด</span> : '—') },
                ]} />
              </Card>
            )}
          </>
        ) : null}
      </QueryState>
    </AppShell>
  )
}
