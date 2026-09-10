import { useQuery } from '@tanstack/react-query'
import { AlarmClockOff, BadgeCheck, ClipboardCheck, LayoutDashboard, ReceiptText, Wallet } from 'lucide-react'
import { getDashboardReport } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { Money } from '../../components/Money'
import { ReportBar } from '../../components/ReportBar'
import { StatTile } from '../../components/StatTile'
import { Card } from '../../components/ui/card'
import { ManagementTable } from '../../components/ManagementTable'
import { QueryState } from '../master-data/MasterDataCommon'
import { jobStatusColors } from './reportColors'
import './reports.css'

export function DashboardReportPage() {
  const query = useQuery({ queryKey: ['reports', 'dashboard'], queryFn: getDashboardReport, refetchInterval: 60_000 })

  return (
    <AppShell title="แดชบอร์ดภาพรวมวันนี้">
      <section className="report-page-heading">
        <div>
          <h2><LayoutDashboard aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />ภาพรวมวันนี้</h2>
          <p>สถานะจ๊อบทั้งหมด งานที่เกินนัด และยอดรับชำระวันนี้ — รีเฟรชอัตโนมัติทุก 1 นาที</p>
        </div>
      </section>

      <QueryState query={query} loadingTitle="กำลังโหลดแดชบอร์ด" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ยังไม่มีจ๊อบในสาขานี้" onRetry={() => void query.refetch()}>
        {query.data ? (
          <>
            <div className="stat-tile-grid">
              <StatTile icon={AlarmClockOff} label="จ๊อบเกินนัด" value={query.data.overdueCount} tone={query.data.overdueCount > 0 ? 'danger' : 'default'} hint="เลยเวลานัดรับรถแล้วยังไม่เสร็จ" />
              <StatTile icon={ClipboardCheck} label="รอ QC" value={query.data.waitingQcCount} tone="warning" hint="งานซ่อมเสร็จ รอตรวจสอบคุณภาพ" />
              <StatTile icon={BadgeCheck} label="รอชำระเงิน/ส่งมอบ" value={query.data.waitingPaymentCount} tone="warning" hint="ผ่าน QC แล้ว รอปิดงาน" />
              <StatTile icon={Wallet} label="ยอดรับชำระวันนี้" value={<Money value={query.data.collectedToday} />} hint="รวมทุกช่องทางชำระเงิน" />
              <StatTile icon={ReceiptText} label="ใบเสร็จออกวันนี้" value={query.data.receiptsIssuedToday} />
            </div>

            <h3 className="report-section-title">จ๊อบแยกตามสถานะ</h3>
            <Card className="report-bar-group">
              {(() => {
                const max = Math.max(1, ...query.data.jobsByStatus.map((s) => s.count))
                return query.data.jobsByStatus.map((s) => (
                  <ReportBar key={s.status} label={s.statusLabelTh} valueLabel={`${s.count} จ๊อบ`} ratio={s.count / max} color={jobStatusColors[s.status] ?? '#64748b'} />
                ))
              })()}
            </Card>

            <h3 className="report-section-title">จ๊อบเกินนัด</h3>
            {query.data.overdueJobs.length === 0 ? (
              <p className="purchase-empty">ไม่มีจ๊อบที่เกินนัดตอนนี้</p>
            ) : (
              <Card className="management-table-card">
                <ManagementTable data={query.data.overdueJobs} sortScope="page" columns={[
                  { id: 'jobNo', header: 'เลขจ๊อบ', value: (j) => j.jobNo, render: (j) => <strong>{j.jobNo}</strong> },
                  { id: 'customer', header: 'ลูกค้า / ทะเบียน', value: (j) => j.customerName, render: (j) => <>{j.customerName}<small className="purchase-sub">{j.vehicleRegistration}</small></> },
                  { id: 'status', header: 'สถานะ', value: (j) => j.statusLabelTh, render: (j) => <span className={`job-status-chip job-status-${j.status}`}>{j.statusLabelTh}</span> },
                  { id: 'promiseAt', header: 'นัดรับรถ', value: (j) => new Date(j.promiseAt).getTime(), render: (j) => new Date(j.promiseAt).toLocaleString('th-TH', { dateStyle: 'short', timeStyle: 'short' }) },
                ]} />
              </Card>
            )}
          </>
        ) : null}
      </QueryState>
    </AppShell>
  )
}
