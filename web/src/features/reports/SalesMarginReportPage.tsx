import { useQuery } from '@tanstack/react-query'
import { PiggyBank, TrendingUp, Wallet } from 'lucide-react'
import { useState } from 'react'
import { getSalesMarginReport } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { Money } from '../../components/Money'
import { StatTile } from '../../components/StatTile'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { ManagementTable } from '../../components/ManagementTable'
import { QueryState } from '../master-data/MasterDataCommon'
import { lineTypeColors } from './reportColors'
import './reports.css'

const isoDate = (d: Date) => d.toISOString().slice(0, 10)
const startOfMonth = () => { const d = new Date(); return isoDate(new Date(d.getFullYear(), d.getMonth(), 1)) }
const defaultTo = isoDate(new Date())
const lineTypeLabel: Record<string, string> = { Part: 'อะไหล่', Labor: 'ค่าแรง' }

function CostCell({ value }: { value: number | null }) {
  return value === null ? <span className="report-cost-hidden">ไม่เปิดเผยต้นทุน</span> : <Money value={value} />
}

export function SalesMarginReportPage() {
  const [fromDate, setFromDate] = useState(startOfMonth())
  const [toDate, setToDate] = useState(defaultTo)
  const query = useQuery({
    queryKey: ['reports', 'sales-margin', fromDate, toDate],
    queryFn: () => getSalesMarginReport(fromDate, `${toDate}T23:59:59`),
  })

  return (
    <AppShell title="ยอดขาย-ต้นทุน-กำไร">
      <section className="report-page-heading">
        <div>
          <h2><TrendingUp aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />ยอดขาย-ต้นทุน-กำไร</h2>
          <p>รวมเฉพาะบรรทัดที่ลูกค้าอนุมัติแล้ว — ต้นทุน/กำไรเห็นได้เฉพาะผู้จัดการ</p>
        </div>
        <div className="report-date-filter">
          <label>จากวันที่<Input type="date" value={fromDate} onChange={(e) => setFromDate(e.target.value)} /></label>
          <label>ถึงวันที่<Input type="date" value={toDate} onChange={(e) => setToDate(e.target.value)} /></label>
        </div>
      </section>

      <QueryState query={query} loadingTitle="กำลังโหลดรายงาน" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ยังไม่มีใบเสนอราคาที่สร้างในช่วงนี้" onRetry={() => void query.refetch()}>
        {query.data ? (
          <>
            <div className="stat-tile-grid">
              <StatTile icon={Wallet} label="ยอดขาย (สุทธิ)" value={<Money value={query.data.netAmount} />} hint={`จาก ${query.data.quotationCount} ใบเสนอราคา`} />
              <StatTile icon={PiggyBank} label="ต้นทุน" value={<CostCell value={query.data.costAmount} />} />
              <StatTile icon={TrendingUp} label="กำไรขั้นต้น" value={<CostCell value={query.data.marginAmount} />} hint={query.data.marginPercent != null ? `คิดเป็น ${query.data.marginPercent}%` : undefined} />
            </div>

            <h3 className="report-section-title">แยกตามประเภทรายการ</h3>
            <Card className="management-table-card">
              <ManagementTable data={query.data.byType} sortScope="page" columns={[
                { id: 'type', header: 'ประเภท', value: (t) => lineTypeLabel[t.type] ?? t.type, render: (t) => <span style={{ color: lineTypeColors[t.type] ?? undefined, fontWeight: 600 }}>{lineTypeLabel[t.type] ?? t.type}</span> },
                { id: 'net', header: 'ยอดขาย', value: (t) => t.netAmount, render: (t) => <Money value={t.netAmount} /> },
                { id: 'cost', header: 'ต้นทุน', value: (t) => t.costAmount ?? 0, render: (t) => <CostCell value={t.costAmount} /> },
                { id: 'margin', header: 'กำไร', value: (t) => t.marginAmount ?? 0, render: (t) => <CostCell value={t.marginAmount} /> },
              ]} />
            </Card>

            <h3 className="report-section-title">ยอดขายค่าแรงต่อช่าง</h3>
            {query.data.byTechnician.length === 0 ? (
              <p className="purchase-empty">ยังไม่มีบรรทัดค่าแรงที่อนุมัติในช่วงนี้</p>
            ) : (
              <Card className="management-table-card">
                <ManagementTable data={query.data.byTechnician} sortScope="page" columns={[
                  { id: 'tech', header: 'ช่าง', value: (t) => t.technicianName, render: (t) => <strong>{t.technicianName}</strong> },
                  { id: 'count', header: 'จำนวนรายการ', value: (t) => t.lineCount, render: (t) => t.lineCount },
                  { id: 'net', header: 'ยอดค่าแรง', value: (t) => t.netAmount, render: (t) => <Money value={t.netAmount} /> },
                ]} />
              </Card>
            )}
          </>
        ) : null}
      </QueryState>
    </AppShell>
  )
}
