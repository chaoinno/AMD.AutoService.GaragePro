import { useQuery } from '@tanstack/react-query'
import { AlarmClock, CalendarClock, CalendarDays, Download, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { getServiceDueReport, type ServiceDueItem, type ServiceDueReport } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { StatTile } from '../../components/StatTile'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { formatDate, formatDateOnly, formatKm, formatNumber } from '../../lib/format'
import { QueryState } from '../master-data/MasterDataCommon'
import { exportServiceDue } from './serviceDueExport'
import './reports.css'

const localIso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

type Preset = 'overdue-30' | 'next-7' | 'next-30' | 'next-90'
function presetRange(preset: Preset): [string, string] {
  const now = new Date()
  const day = (offset: number) => localIso(new Date(now.getFullYear(), now.getMonth(), now.getDate() + offset))
  if (preset === 'next-7') return [day(0), day(7)]
  if (preset === 'next-90') return [day(0), day(90)]
  if (preset === 'next-30') return [day(0), day(30)]
  return [day(-30), day(30)]
}
const PRESETS: { value: Preset; label: string }[] = [
  { value: 'overdue-30', label: 'เลยกำหนด + 30 วัน' },
  { value: 'next-7', label: '7 วันข้างหน้า' },
  { value: 'next-30', label: '30 วันข้างหน้า' },
  { value: 'next-90', label: '90 วันข้างหน้า' },
]

/// [BIZ] เพิ่ม 2026-10-08 — รายชื่อรถที่ถึง/ใกล้ถึงวันนัดเข้ารับบริการครั้งถัดไป (จากใบส่งมอบ) ให้ผู้จัดการ/ธุรการโทรตาม
/// ใช้การส่งมอบล่าสุดของรถแต่ละคัน และตัดคันที่กลับมาเปิดจ๊อบใหม่แล้วหรือยังมีงานเปิดค้างอยู่ (server ทำให้)
export function ServiceDueReportPage() {
  const [[fromDate, toDate], setRange] = useState<[string, string]>(() => presetRange('overdue-30'))
  const query = useQuery({
    queryKey: ['reports', 'service-due', fromDate, toDate],
    queryFn: () => getServiceDueReport(fromDate, toDate),
    enabled: Boolean(fromDate && toDate),
  })
  const activePreset = PRESETS.find((p) => { const [f, t] = presetRange(p.value); return f === fromDate && t === toDate })?.value

  return (
    <AppShell title="รถใกล้ครบรอบบริการ">
      <section className="report-page-heading">
        <div>
          <h2><AlarmClock aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />รถใกล้ครบรอบบริการ</h2>
          <p>จากวันนัดครั้งถัดไปที่บันทึกตอนส่งมอบรถ · ใช้การส่งมอบล่าสุดของรถแต่ละคัน · ไม่รวมคันที่กลับมาเปิดจ๊อบใหม่แล้วหรือยังมีงานเปิดค้างอยู่</p>
        </div>
        <div className="report-date-filter">
          <div className="report-presets" role="group" aria-label="ช่วงวันนัดสำเร็จรูป">
            {PRESETS.map((p) => (
              <button key={p.value} type="button" className={activePreset === p.value ? 'is-active' : undefined} aria-pressed={activePreset === p.value} onClick={() => setRange(presetRange(p.value))}>
                {p.label}
              </button>
            ))}
          </div>
          <label>วันนัดตั้งแต่<Input type="date" value={fromDate} max={toDate} onChange={(e) => setRange([e.target.value, toDate])} /></label>
          <label>ถึง<Input type="date" value={toDate} min={fromDate} onChange={(e) => setRange([fromDate, e.target.value])} /></label>
        </div>
      </section>

      <QueryState query={query} loadingTitle="กำลังโหลดรายชื่อรถใกล้ครบรอบบริการ" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ไม่พบข้อมูลในช่วงนี้" onRetry={() => void query.refetch()}>
        {query.data ? <ReportBody report={query.data} /> : null}
      </QueryState>
    </AppShell>
  )
}

function DueStatus({ item }: { item: ServiceDueItem }) {
  // [UI] สี + ไอคอน + ข้อความเสมอ
  if (item.daysUntilDue < 0) {
    return <span className="service-due-status service-due-status--overdue"><TriangleAlert aria-hidden="true" /> เลยกำหนด {formatNumber(-item.daysUntilDue)} วัน</span>
  }
  if (item.daysUntilDue === 0) {
    return <span className="service-due-status service-due-status--soon"><AlarmClock aria-hidden="true" /> ครบกำหนดวันนี้</span>
  }
  return (
    <span className={`service-due-status ${item.daysUntilDue <= 7 ? 'service-due-status--soon' : 'service-due-status--later'}`}>
      <CalendarClock aria-hidden="true" /> อีก {formatNumber(item.daysUntilDue)} วัน
    </span>
  )
}

function ReportBody({ report }: { report: ServiceDueReport }) {
  const [exporting, setExporting] = useState(false)
  const exportExcel = async () => {
    setExporting(true)
    try {
      await exportServiceDue(report.items, report.fromDate, report.toDate)
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'ส่งออก Excel ไม่สำเร็จ')
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <div className="stat-tile-grid">
        <StatTile icon={TriangleAlert} label="เลยกำหนดแล้ว" value={formatNumber(report.overdueCount)} tone={report.overdueCount > 0 ? 'danger' : 'default'} hint="ยังไม่กลับมาเข้ารับบริการ" />
        <StatTile icon={AlarmClock} label="ครบกำหนดภายใน 7 วัน" value={formatNumber(report.dueWithin7DaysCount)} tone={report.dueWithin7DaysCount > 0 ? 'warning' : 'default'} />
        <StatTile icon={CalendarDays} label="ครบกำหนดภายใน 30 วัน" value={formatNumber(report.dueWithin30DaysCount)} />
        <StatTile icon={CalendarClock} label="รถในช่วงที่เลือก" value={formatNumber(report.items.length)} hint={`วันนัด ${formatDateOnly(report.fromDate)} – ${formatDateOnly(report.toDate)}`} />
      </div>

      <div className="report-section-title service-due-toolbar">
        <span>รายชื่อรถที่ต้องติดตาม</span>
        <Button
          variant="outline"
          size="sm"
          disabled={!report.items.length || exporting}
          title={!report.items.length ? 'ไม่มีรายการสำหรับส่งออก' : undefined}
          onClick={() => void exportExcel()}
        >
          <Download aria-hidden="true" /> {exporting ? 'กำลังส่งออก…' : 'ส่งออก Excel'}
        </Button>
      </div>
      {report.items.length === 0 ? (
        <p className="purchase-empty">ไม่มีรถที่ครบกำหนดในช่วงนี้ — ลองขยายช่วงวันนัด (นัดมาจากใบส่งมอบที่บันทึกตั้งแต่ 2026-10-08 เท่านั้น)</p>
      ) : (
        <Card className="management-table-card">
          <p className="section-help report-table-note">ไมล์นัดแสดงประกอบเท่านั้น — ระบบไม่รู้เลขไมล์ปัจจุบันของรถ จึงจัดลำดับตามวันนัด</p>
          <ManagementTable data={report.items} sortScope="loaded" columns={[
            { id: 'due', header: 'วันนัด', size: 140, value: (x) => x.nextServiceDueOn, render: (x) => <strong>{formatDateOnly(x.nextServiceDueOn)}</strong> },
            { id: 'status', header: 'สถานะ', size: 170, value: (x) => x.daysUntilDue, render: (x) => <DueStatus item={x} /> },
            { id: 'plate', header: 'ทะเบียน', size: 140, value: (x) => x.vehicleRegistration, render: (x) => <>{x.vehicleRegistration}<small className="purchase-sub">{x.vehicleModel ?? ''}</small></> },
            { id: 'customer', header: 'ลูกค้า', size: 200, value: (x) => x.customerName, render: (x) => <>{x.customerName}<small className="purchase-sub">{x.customerPhone ?? 'ไม่มีเบอร์โทร'}</small></> },
            { id: 'nextKm', header: 'นัดที่ไมล์', size: 150, value: (x) => x.nextServiceMileage ?? 0, render: (x) => <span className="report-num">{formatKm(x.nextServiceMileage)}</span> },
            { id: 'handoverKm', header: 'ไมล์ส่งมอบ', size: 150, value: (x) => x.mileageAtHandover ?? 0, render: (x) => <span className="report-num">{formatKm(x.mileageAtHandover)}</span> },
            { id: 'last', header: 'เข้าครั้งล่าสุด', size: 190, value: (x) => new Date(x.handedOverAt).getTime(), render: (x) => (
              <>{formatDate(x.handedOverAt)}<small className="purchase-sub"><Link className="report-link" to={`/jobs?job=${x.lastJobId}`}>{x.lastJobNo}</Link></small></>
            ) },
            { id: 'history', header: '', size: 140, render: (x) => (
              <Link className="report-link" to={`/reports/vehicle-history?vehicle=${x.vehicleId}`}>ดูประวัติรถ</Link>
            ) },
          ]} />
        </Card>
      )}
    </>
  )
}
