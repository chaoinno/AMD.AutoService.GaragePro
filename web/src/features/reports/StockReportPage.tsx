import { useQuery } from '@tanstack/react-query'
import { AlertTriangle, Boxes, Warehouse as WarehouseIcon } from 'lucide-react'
import { getStockReport } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { Money } from '../../components/Money'
import { ReportBar } from '../../components/ReportBar'
import { StatTile } from '../../components/StatTile'
import { Card } from '../../components/ui/card'
import { ManagementTable } from '../../components/ManagementTable'
import { QueryState } from '../master-data/MasterDataCommon'
import { agingBucketColors } from './reportColors'
import './reports.css'

export function StockReportPage() {
  const query = useQuery({ queryKey: ['reports', 'stock'], queryFn: getStockReport })

  return (
    <AppShell title="สต็อกสินค้า">
      <section className="report-page-heading">
        <div>
          <h2><Boxes aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />มูลค่าและอายุสต็อกคงเหลือ</h2>
          <p>คำนวณจากต้นทุน FIFO จริงของแต่ละล็อต (ไม่ใช่ราคาทุนในแคตตาล็อก)</p>
        </div>
      </section>

      <QueryState query={query} loadingTitle="กำลังโหลดรายงาน" emptyTitle="ยังไม่มีสต็อก" emptyReason="ยังไม่มีการรับสินค้าเข้าคลังของสาขานี้" onRetry={() => void query.refetch()}>
        {query.data ? (
          <>
            <div className="stat-tile-grid">
              <StatTile icon={WarehouseIcon} label="มูลค่าสต็อกรวม" value={<Money value={query.data.totalValuation} />} hint="ผลรวมต้นทุน FIFO ของทุกล็อตที่เหลือ" />
              <StatTile icon={AlertTriangle} label="มูลค่าสินค้าเสียหาย" value={<Money value={query.data.damagedValuation} />} tone={query.data.damagedValuation > 0 ? 'warning' : 'default'} />
            </div>

            <h3 className="report-section-title">อายุสต็อกคงเหลือ (ตามมูลค่า)</h3>
            <Card className="report-bar-group">
              {(() => {
                const max = Math.max(1, ...query.data.agingBuckets.map((b) => b.value))
                return query.data.agingBuckets.map((b, i) => (
                  <ReportBar key={b.bucketLabelTh} label={b.bucketLabelTh} valueLabel={new Intl.NumberFormat('th-TH', { style: 'currency', currency: 'THB', minimumFractionDigits: 0 }).format(b.value)} ratio={b.value / max} color={agingBucketColors[i] ?? '#94a3b8'} />
                ))
              })()}
            </Card>

            <h3 className="report-section-title">ล็อตที่ค้างสต็อกนานที่สุด</h3>
            {query.data.oldestLots.length === 0 ? (
              <p className="purchase-empty">ไม่มีล็อตคงเหลือ</p>
            ) : (
              <Card className="management-table-card">
                <ManagementTable data={query.data.oldestLots} sortScope="page" columns={[
                  { id: 'code', header: 'รหัส / ชื่อสินค้า', value: (l) => l.catalogName, render: (l) => <><strong>{l.catalogCode}</strong><small className="purchase-sub">{l.catalogName}</small></> },
                  { id: 'warehouse', header: 'คลัง', value: (l) => l.warehouseName, render: (l) => l.warehouseName },
                  { id: 'qty', header: 'คงเหลือ', value: (l) => l.remainingQuantity, render: (l) => l.remainingQuantity },
                  { id: 'cost', header: 'ต้นทุน/หน่วย', value: (l) => l.unitCost, render: (l) => <Money value={l.unitCost} /> },
                  { id: 'age', header: 'ค้างมาแล้ว', value: (l) => l.ageDays, render: (l) => <span className="report-num">{l.ageDays} วัน</span> },
                ]} />
              </Card>
            )}
          </>
        ) : null}
      </QueryState>
    </AppShell>
  )
}
