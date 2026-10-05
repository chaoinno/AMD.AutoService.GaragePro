import { useQuery } from '@tanstack/react-query'
import { Ban, Package, PiggyBank, ReceiptText, ShoppingCart, Tag, TrendingUp, Wallet } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router'
import { getRetailSalesReport, type RetailSalesReport } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { Money } from '../../components/Money'
import { ReportBar } from '../../components/ReportBar'
import { StatTile } from '../../components/StatTile'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { formatDateTime, formatMoney, formatNumber } from '../../lib/format'
import { QueryState } from '../master-data/MasterDataCommon'
import { paymentMethodLabel } from '../sales/saleFormat'
import { RetailDailyChart } from './RetailDailyChart'
import './reports.css'

/// วันที่ตามเครื่องผู้ใช้ (ไทย) — ห้ามใช้ toISOString().slice(0,10) เพราะช่วง 00:00–07:00 จะได้วันของ UTC (เมื่อวาน)
const localIso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

type Preset = 'today' | '7d' | 'month' | 'lastMonth'
function presetRange(preset: Preset): [string, string] {
  const now = new Date()
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())
  if (preset === 'today') return [localIso(today), localIso(today)]
  if (preset === '7d') return [localIso(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 6)), localIso(today)]
  if (preset === 'lastMonth') return [localIso(new Date(today.getFullYear(), today.getMonth() - 1, 1)), localIso(new Date(today.getFullYear(), today.getMonth(), 0))]
  return [localIso(new Date(today.getFullYear(), today.getMonth(), 1)), localIso(today)]
}
const PRESETS: { value: Preset; label: string }[] = [
  { value: 'today', label: 'วันนี้' },
  { value: '7d', label: '7 วัน' },
  { value: 'month', label: 'เดือนนี้' },
  { value: 'lastMonth', label: 'เดือนก่อน' },
]

function CostCell({ value }: { value: number | null | undefined }) {
  return value == null ? <span className="report-cost-hidden">ไม่เปิดเผยต้นทุน</span> : <Money value={value} />
}

export function RetailSalesReportPage() {
  const [[fromDate, toDate], setRange] = useState<[string, string]>(() => presetRange('month'))
  const query = useQuery({
    queryKey: ['reports', 'retail-sales', fromDate, toDate],
    queryFn: () => getRetailSalesReport(fromDate, toDate),
    enabled: Boolean(fromDate && toDate),
  })
  const activePreset = PRESETS.find((p) => { const [f, t] = presetRange(p.value); return f === fromDate && t === toDate })?.value

  return (
    <AppShell title="รายงานขายหน้าร้าน">
      <section className="report-page-heading">
        <div>
          <h2><ShoppingCart aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />รายงานขายหน้าร้าน (POS)</h2>
          <p>นับเฉพาะบิลที่ชำระเงินแล้ว ตามวันที่ชำระ · บิลที่ยกเลิกภายหลังไม่รวมในยอด (แสดงแยกด้านล่าง) · ต้นทุน/กำไรเห็นได้เฉพาะผู้จัดการ</p>
        </div>
        <div className="report-date-filter">
          <div className="report-presets" role="group" aria-label="ช่วงเวลาสำเร็จรูป">
            {PRESETS.map((p) => (
              <button key={p.value} type="button" className={activePreset === p.value ? 'is-active' : undefined} aria-pressed={activePreset === p.value} onClick={() => setRange(presetRange(p.value))}>
                {p.label}
              </button>
            ))}
          </div>
          <label>จากวันที่<Input type="date" value={fromDate} max={toDate} onChange={(e) => setRange([e.target.value, toDate])} /></label>
          <label>ถึงวันที่<Input type="date" value={toDate} min={fromDate} onChange={(e) => setRange([fromDate, e.target.value])} /></label>
        </div>
      </section>

      <QueryState query={query} loadingTitle="กำลังโหลดรายงานขายหน้าร้าน" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ไม่พบข้อมูลในช่วงนี้" onRetry={() => void query.refetch()}>
        {query.data ? <ReportBody report={query.data} /> : null}
      </QueryState>
    </AppShell>
  )
}

function ReportBody({ report }: { report: RetailSalesReport }) {
  const methodMax = Math.max(1, ...report.byPaymentMethod.map((m) => m.amount))
  const paidTotal = report.byPaymentMethod.reduce((sum, m) => sum + m.amount, 0)

  return (
    <>
      <div className="stat-tile-grid">
        <StatTile icon={Wallet} label="ยอดขายรวม (รวม VAT)" value={<Money value={report.totalAmount} />} hint={`ก่อน VAT ${formatMoney(report.netAmount)} · VAT ${formatMoney(report.vatAmount)}`} />
        <StatTile icon={ReceiptText} label="จำนวนบิล" value={formatNumber(report.billCount)} hint={`เฉลี่ย ${formatMoney(report.averageBillAmount)} บาท/บิล · ${formatNumber(report.itemQuantity)} ชิ้น`} />
        <StatTile icon={Tag} label="ส่วนลด + โปรโมชัน" value={<Money value={report.discountAmount} />} hint="รวมรายบรรทัดและท้ายบิล" />
        <StatTile icon={TrendingUp} label="กำไรขั้นต้น (ก่อน VAT)" value={<CostCell value={report.marginAmount} />} hint={report.marginPercent != null ? `${report.marginPercent}% · ต้นทุน FIFO ${formatMoney(report.costAmount ?? 0)}` : 'เฉพาะผู้จัดการ'} />
        <StatTile icon={Ban} label="บิลที่ยกเลิก" value={formatNumber(report.voidedCount)} tone={report.voidedCount > 0 ? 'warning' : 'default'} hint={report.voidedCount > 0 ? `มูลค่า ${formatMoney(report.voidedAmount)} บาท (ไม่รวมในยอด)` : 'ไม่มีบิลยกเลิก'} />
      </div>

      <h3 className="report-section-title">ยอดขายรายวัน</h3>
      <Card className="report-chart-card">
        <RetailDailyChart days={report.daily} />
      </Card>

      <div className="report-two-col">
        <section>
          <h3 className="report-section-title">ช่องทางชำระเงิน</h3>
          <Card className="report-bar-group">
            {report.byPaymentMethod.length === 0 ? <p className="purchase-empty">ยังไม่มีการรับเงินในช่วงนี้</p> : report.byPaymentMethod.map((m) => (
              <ReportBar
                key={m.method}
                label={`${paymentMethodLabel(m.method)} · ${paidTotal > 0 ? Math.round((m.amount / paidTotal) * 100) : 0}%`}
                valueLabel={formatMoney(m.amount)}
                ratio={m.amount / methodMax}
                color="var(--blue-600)"
              />
            ))}
          </Card>
        </section>
        <section>
          <h3 className="report-section-title">ยอดขายตามผู้รับเงิน</h3>
          <Card className="report-bar-group">
            {report.bySeller.length === 0 ? <p className="purchase-empty">ยังไม่มีบิลในช่วงนี้</p> : (() => {
              const max = Math.max(1, ...report.bySeller.map((x) => x.totalAmount))
              return report.bySeller.map((x) => (
                <ReportBar key={x.sellerName} label={`${x.sellerName} · ${x.billCount} บิล`} valueLabel={formatMoney(x.totalAmount)} ratio={x.totalAmount / max} color="var(--blue-600)" />
              ))
            })()}
          </Card>
        </section>
      </div>

      <h3 className="report-section-title"><Package aria-hidden="true" style={{ width: 16 }} /> สินค้าขายดี (20 อันดับ)</h3>
      {report.topProducts.length === 0 ? <p className="purchase-empty">ยังไม่มีสินค้าที่ขายได้ในช่วงนี้</p> : (
        <Card className="management-table-card">
          <p className="section-help report-table-note">ยอดตามบรรทัด = หลังส่วนลด/โปรรายบรรทัด ก่อนส่วนลดท้ายบิลและ VAT</p>
          <ManagementTable data={report.topProducts} sortScope="loaded" columns={[
            { id: 'code', header: 'รหัส', size: 130, value: (p) => p.code, render: (p) => <span className="report-num">{p.code}</span> },
            { id: 'name', header: 'สินค้า', size: 260, value: (p) => p.name, render: (p) => <strong>{p.name}</strong> },
            { id: 'qty', header: 'จำนวน', size: 110, value: (p) => p.quantity, render: (p) => <span className="report-num">{formatNumber(p.quantity)} {p.unit}</span> },
            { id: 'bills', header: 'บิล', size: 80, value: (p) => p.billCount, render: (p) => <span className="report-num">{p.billCount}</span> },
            { id: 'net', header: 'ยอดตามบรรทัด', value: (p) => p.netAmount, render: (p) => <Money value={p.netAmount} /> },
            { id: 'cost', header: 'ต้นทุน', value: (p) => p.costAmount ?? 0, render: (p) => <CostCell value={p.costAmount} /> },
            { id: 'margin', header: 'กำไร', value: (p) => p.marginAmount ?? 0, render: (p) => <CostCell value={p.marginAmount} /> },
          ]} />
        </Card>
      )}

      <h3 className="report-section-title"><Tag aria-hidden="true" style={{ width: 16 }} /> โปรโมชันที่ถูกใช้</h3>
      {report.promotions.length === 0 ? <p className="purchase-empty">ไม่มีการใช้โปรโมชันในช่วงนี้</p> : (
        <Card className="management-table-card">
          <ManagementTable data={report.promotions} sortScope="loaded" columns={[
            { id: 'name', header: 'โปรโมชัน', value: (p) => p.name, render: (p) => <strong>{p.name}</strong> },
            { id: 'scope', header: 'ใช้กับ', size: 120, value: (p) => p.scope, render: (p) => (p.scope === 'bill' ? 'ท้ายบิล' : 'รายบรรทัด') },
            { id: 'uses', header: 'ครั้งที่ใช้', size: 110, value: (p) => p.useCount, render: (p) => <span className="report-num">{p.useCount}</span> },
            { id: 'amount', header: 'ส่วนลดที่ให้', value: (p) => p.discountAmount, render: (p) => <Money value={p.discountAmount} /> },
          ]} />
        </Card>
      )}

      <h3 className="report-section-title"><Ban aria-hidden="true" style={{ width: 16 }} /> บิลที่ยกเลิก</h3>
      {report.voidedSales.length === 0 ? <p className="purchase-empty">ไม่มีบิลที่ถูกยกเลิกในช่วงนี้</p> : (
        <Card className="management-table-card">
          <ManagementTable data={report.voidedSales} sortScope="loaded" columns={[
            { id: 'receipt', header: 'เลขที่', value: (v) => v.receiptNo ?? '', render: (v) => <Link className="report-link" to={`/sales/${v.saleId}`}>{v.receiptNo ?? 'บิลขาย'}</Link> },
            { id: 'paid', header: 'ชำระเมื่อ', value: (v) => (v.completedAt ? new Date(v.completedAt).getTime() : 0), render: (v) => formatDateTime(v.completedAt) },
            { id: 'voided', header: 'ยกเลิกเมื่อ', value: (v) => (v.voidedAt ? new Date(v.voidedAt).getTime() : 0), render: (v) => <>{formatDateTime(v.voidedAt)}<small className="purchase-sub">{v.voidedByName ?? ''}</small></> },
            { id: 'reason', header: 'เหตุผล', size: 240, value: (v) => v.voidReason ?? '', render: (v) => v.voidReason || '—' },
            { id: 'amount', header: 'ยอดบิล', value: (v) => v.totalAmount, render: (v) => <Money value={v.totalAmount} /> },
          ]} />
        </Card>
      )}

      <p className="section-help">
        <PiggyBank aria-hidden="true" style={{ width: 14, verticalAlign: -2 }} /> ต้นทุนคิดจากล็อต FIFO ที่ถูกตัดจริงตอนชำระเงิน · คืนเงินลูกค้าของบิลที่ยกเลิกทำนอกระบบ
      </p>
    </>
  )
}
