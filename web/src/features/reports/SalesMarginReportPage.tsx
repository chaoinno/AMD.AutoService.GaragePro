import { useQuery } from '@tanstack/react-query'
import { ChevronDown, ChevronRight, PiggyBank, TrendingUp, Wallet } from 'lucide-react'
import { Fragment, useState } from 'react'
import { getSalesMarginReport, type LineTypeTotals, type SalesMarginItem } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { Money } from '../../components/Money'
import { StatTile } from '../../components/StatTile'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { ManagementTable } from '../../components/ManagementTable'
import { formatDateTime, formatNumber } from '../../lib/format'
import { JobCardModal } from '../jobs/JobCardModal'
import { QueryState } from '../master-data/MasterDataCommon'
import { lineTypeColors } from './reportColors'
import './reports.css'

// วันที่ตามเครื่องผู้ใช้ (เวลาไทย) — toISOString เป็น UTC ทำให้ช่วง 00:00–07:00 ได้ "วันนี้" เป็นเมื่อวาน
const isoDate = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const startOfMonth = () => { const d = new Date(); return isoDate(new Date(d.getFullYear(), d.getMonth(), 1)) }
const defaultTo = isoDate(new Date())
const lineTypeLabel: Record<string, string> = { Part: 'อะไหล่', Labor: 'ค่าแรง' }

function CostCell({ value }: { value: number | null }) {
  return value === null ? <span className="report-cost-hidden">ไม่เปิดเผยต้นทุน</span> : <Money value={value} />
}

const percentOf = (part: number, whole: number) => (whole === 0 ? '0.0%' : `${(part / whole * 100).toFixed(1)}%`)
const marginPercent = (net: number, margin: number | null) => (margin == null || net === 0 ? '-' : `${(margin / net * 100).toFixed(1)}%`)
/** รายการต่อประเภทที่แสดงก่อนกด "แสดงทั้งหมด" — ประเภทหนึ่งอาจมีหลายร้อยรายการในช่วงยาว */
const ITEM_PREVIEW = 15

export function SalesMarginReportPage() {
  const [fromDate, setFromDate] = useState(startOfMonth())
  const [toDate, setToDate] = useState(defaultTo)
  // เปิดการ์ดจ๊อบบนหน้ารายงานเลย ปิดแล้วอยู่ที่รายงานพร้อมแถวที่กางไว้เดิม
  const [jobId, setJobId] = useState<string | null>(null)
  const query = useQuery({
    queryKey: ['reports', 'sales-margin', fromDate, toDate],
    queryFn: () => getSalesMarginReport(fromDate, `${toDate}T23:59:59`),
  })

  return (
    <AppShell title="ยอดขาย-ต้นทุน-กำไร">
      <section className="report-page-heading">
        <div>
          <h2><TrendingUp aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />ยอดขาย-ต้นทุน-กำไร</h2>
          <p>รวมเฉพาะบรรทัดที่ลูกค้าอนุมัติแล้ว (ไม่นับใบที่ถูกแทนที่ด้วยฉบับแก้ไข) ตามวันที่สร้างใบเสนอราคา · ต้นทุน/กำไรเห็นได้เฉพาะผู้จัดการ</p>
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
              <StatTile icon={Wallet} label="ยอดขาย (สุทธิ)" value={<Money value={query.data.netAmount} />} hint={`จาก ${query.data.quotationCount} ใบเสนอราคาที่อนุมัติ`} />
              <StatTile icon={PiggyBank} label="ต้นทุน" value={<CostCell value={query.data.costAmount} />} />
              <StatTile icon={TrendingUp} label="กำไรขั้นต้น" value={<CostCell value={query.data.marginAmount} />} hint={query.data.marginPercent != null ? `คิดเป็น ${query.data.marginPercent}%` : undefined} />
            </div>

            <h3 className="report-section-title">แยกตามประเภทรายการ</h3>
            {query.data.byType.length === 0 ? (
              <p className="purchase-empty">ยังไม่มีบรรทัดที่ลูกค้าอนุมัติในช่วงนี้</p>
            ) : (
              <TypeBreakdown types={query.data.byType} total={query.data.netAmount} showCost={query.data.costAmount != null} onJob={setJobId} />
            )}

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
      <JobCardModal jobId={jobId} onClose={() => setJobId(null)} />
    </AppShell>
  )
}

/// [BIZ] drill-down ประเภท → รายการ → จ๊อบ (คำขอผู้ใช้ 2026-10-29: "อยากรู้ว่าค่าแรง/อะไหล่มาจากอะไรบ้าง")
function TypeBreakdown({ types, total, showCost, onJob }: { types: LineTypeTotals[]; total: number; showCost: boolean; onJob: (id: string) => void }) {
  const [openTypes, setOpenTypes] = useState<Set<string>>(new Set())
  const [openItems, setOpenItems] = useState<Set<string>>(new Set())
  const [showAll, setShowAll] = useState<Set<string>>(new Set())
  const toggle = (setter: typeof setOpenTypes, key: string) => setter(old => {
    const next = new Set(old)
    if (next.has(key)) next.delete(key); else next.add(key)
    return next
  })
  const columns = showCost ? 8 : 5
  return (
    <Card className="management-table-card">
      <p className="section-help report-table-note">กดแถวประเภทเพื่อดูว่ามาจากรายการไหน แล้วกดรายการเพื่อดูว่ามาจากจ๊อบไหน · สัดส่วนคิดจากยอดขายรวมของช่วงนี้</p>
      <div className="purchase-table-scroll">
        <table className="master-table sales-drill">
          <thead>
            <tr>
              <th>ประเภท / รายการ</th>
              <th className="report-num">บรรทัด</th>
              <th className="report-num">จำนวน</th>
              <th className="report-num">ยอดขาย</th>
              <th className="report-num">สัดส่วน</th>
              {showCost ? <><th className="report-num">ต้นทุน</th><th className="report-num">กำไร</th><th className="report-num">กำไร %</th></> : null}
            </tr>
          </thead>
          <tbody>
            {types.map(type => {
              const typeOpen = openTypes.has(type.type)
              const label = lineTypeLabel[type.type] ?? type.type
              const Chevron = typeOpen ? ChevronDown : ChevronRight
              const visible = showAll.has(type.type) ? type.items : type.items.slice(0, ITEM_PREVIEW)
              return (
                <Fragment key={type.type}>
                  <tr className="sales-drill__type">
                    <td>
                      <button type="button" className="sales-drill__toggle" aria-expanded={typeOpen} onClick={() => toggle(setOpenTypes, type.type)}>
                        <Chevron size={16} aria-hidden="true" />
                        <span className="sales-drill__swatch" style={{ background: lineTypeColors[type.type] }} aria-hidden="true" />
                        <strong>{label}</strong>
                        <small>{formatNumber(type.items.length)} รายการ</small>
                      </button>
                    </td>
                    <td className="report-num">{formatNumber(type.lineCount)}</td>
                    <td className="report-num">-</td>
                    <td className="report-num"><Money value={type.netAmount} /></td>
                    <td className="report-num">{percentOf(type.netAmount, total)}</td>
                    {showCost ? <><td className="report-num"><CostCell value={type.costAmount} /></td><td className="report-num"><CostCell value={type.marginAmount} /></td><td className="report-num">{marginPercent(type.netAmount, type.marginAmount)}</td></> : null}
                  </tr>
                  {typeOpen ? visible.map(item => (
                    <ItemRows key={`${type.type}|${item.catalogCode}|${item.name}`} item={item} itemKey={`${type.type}|${item.catalogCode}|${item.name}`}
                      isLabor={type.type === 'Labor'} total={total} showCost={showCost} columns={columns}
                      open={openItems.has(`${type.type}|${item.catalogCode}|${item.name}`)} onToggle={key => toggle(setOpenItems, key)} onJob={onJob} />
                  )) : null}
                  {typeOpen && type.items.length > ITEM_PREVIEW ? (
                    <tr className="sales-drill__more">
                      <td colSpan={columns}>
                        <button type="button" className="link-button" onClick={() => toggle(setShowAll, type.type)}>
                          {showAll.has(type.type) ? `แสดงเฉพาะ ${ITEM_PREVIEW} อันดับแรก` : `แสดงทั้งหมด ${formatNumber(type.items.length)} รายการ`}
                        </button>
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              )
            })}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

function ItemRows({ item, itemKey, isLabor, total, showCost, columns, open, onToggle, onJob }: {
  item: SalesMarginItem; itemKey: string; isLabor: boolean; total: number; showCost: boolean; columns: number
  open: boolean; onToggle: (key: string) => void; onJob: (id: string) => void
}) {
  const Chevron = open ? ChevronDown : ChevronRight
  return (
    <>
      <tr className="sales-drill__item">
        <td>
          <button type="button" className="sales-drill__toggle" aria-expanded={open} aria-label={`${open ? 'ซ่อน' : 'ดู'}จ๊อบของ ${item.name}`} onClick={() => onToggle(itemKey)}>
            <Chevron size={16} aria-hidden="true" />
            <span>
              {item.name}
              <small className="purchase-sub">{item.isAdHoc ? 'รายการนอกแคตตาล็อก' : item.catalogCode}{item.unit ? ` · ${item.unit}` : ''}</small>
            </span>
          </button>
        </td>
        <td className="report-num">{formatNumber(item.lineCount)}</td>
        <td className="report-num">{formatNumber(item.quantity)}</td>
        <td className="report-num"><Money value={item.netAmount} /></td>
        <td className="report-num">{percentOf(item.netAmount, total)}</td>
        {showCost ? <><td className="report-num"><CostCell value={item.costAmount} /></td><td className="report-num"><CostCell value={item.marginAmount} /></td><td className="report-num">{marginPercent(item.netAmount, item.marginAmount)}</td></> : null}
      </tr>
      {open ? (
        <tr className="sales-drill__lines">
          <td colSpan={columns}>
            <table className="master-table purchase-lines">
              <thead>
                <tr>
                  <th>จ๊อบ / ทะเบียน</th><th>ใบเสนอราคา</th><th>ลูกค้า</th>{isLabor ? <th>ช่าง</th> : null}
                  <th className="report-num">จำนวน</th><th className="report-num">ราคา/หน่วย</th><th className="report-num">ยอดขาย</th>
                  {showCost ? <><th className="report-num">ต้นทุน</th><th className="report-num">กำไร</th></> : null}
                </tr>
              </thead>
              <tbody>
                {item.lines.map((line, i) => (
                  <tr key={`${line.quotationId}-${i}`}>
                    <td><button type="button" className="link-button report-link" onClick={() => onJob(line.jobId)}>{line.jobNo}</button><small className="purchase-sub">{line.vehicleRegistration || '-'}</small></td>
                    <td>{line.quotationCode}<small className="purchase-sub">{formatDateTime(line.quotationCreatedAt)}</small></td>
                    <td>{line.customerName || '-'}</td>
                    {isLabor ? <td>{line.technicianName || <span className="report-cost-hidden">ไม่ระบุช่าง</span>}</td> : null}
                    <td className="report-num">{formatNumber(line.quantity)}</td>
                    <td className="report-num"><Money value={line.unitPrice} /></td>
                    <td className="report-num"><Money value={line.netAmount} /></td>
                    {showCost ? <><td className="report-num"><CostCell value={line.costAmount} /></td><td className="report-num"><CostCell value={line.marginAmount} /></td></> : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </td>
        </tr>
      ) : null}
    </>
  )
}
