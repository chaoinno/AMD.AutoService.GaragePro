import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ChevronDown, ChevronRight, Download, PackageMinus, Undo2 } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { toast } from 'sonner'
import { getStockUsageReport, type StockUsageDetail, type StockUsageItem, type StockUsageReport, type StockUsageSource, type StockUsageView } from '../../api/reports'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { StatTile } from '../../components/StatTile'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { formatDateOnly, formatDateTime, formatNumber } from '../../lib/format'
import { JobCardModal } from '../jobs/JobCardModal'
import { QueryState } from '../master-data/MasterDataCommon'
import { PlanLockNote } from '../plan/PlanLockNote'
import { useFeatureLock } from '../plan/usePlan'
import { StockReturnDocumentModal } from '../purchasing/StockReturnDocumentModal'
import { StockWithdrawalDocumentModal } from '../purchasing/StockWithdrawalDocumentModal'
import { money } from '../purchasing/PurchasingPage'
import { exportStockUsage } from './stockUsageExport'
import './reports.css'

const localIso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
const monthStart = () => { const n = new Date(); return localIso(new Date(n.getFullYear(), n.getMonth(), 1)) }

const VIEWS: { value: StockUsageView; label: string }[] = [
  { value: 'documents', label: 'รายใบเบิก' },
  { value: 'items', label: 'สรุปตามสินค้า' },
  { value: 'requesters', label: 'สรุปตามช่างผู้เบิก' },
]
export const SOURCE_LABEL: Record<string, string> = { job: 'เบิกเข้างาน', manual: 'เบิกหน้าสต็อก', pos: 'ขายหน้าร้าน (POS)' }
const DETAIL_LABEL: Record<StockUsageDetail['kind'], string> = {
  'job-issue': 'เบิกเข้างาน', 'manual-issue': 'เบิกหน้าสต็อก', 'job-return': 'คืนจากงาน', 'pos-sale': 'ขาย POS', 'pos-void': 'ยกเลิกบิลขาย',
}

/// [BIZ] เพิ่ม 2026-10-25 — การเบิกอะไหล่ 3 มุมมอง (ธุรการ/ผู้จัดการ) · จำนวนสุทธิหลังคืน · ต้นทุนมาจากล็อตจริงและเป็น null ที่ server เมื่อ role ไม่เห็นต้นทุน
export function StockUsageReportPage() {
  const [params, setParams] = useSearchParams()
  const view = (VIEWS.find(v => v.value === params.get('view'))?.value ?? 'documents') as StockUsageView
  const source = (['job', 'manual', 'pos'].includes(params.get('source') ?? '') ? params.get('source') : '') as StockUsageSource | ''
  const fromDate = params.get('from') || monthStart()
  const toDate = params.get('to') || localIso(new Date())
  const set = (patch: Record<string, string>) => setParams(old => {
    const next = new URLSearchParams(old)
    for (const [k, v] of Object.entries(patch)) { if (v) next.set(k, v); else next.delete(k) }
    return next
  }, { replace: true })

  const query = useQuery({
    queryKey: ['reports', 'stock-usage', view, source, fromDate, toDate],
    queryFn: () => getStockUsageReport({ view, source: source || undefined, fromDate, toDate }),
    enabled: Boolean(fromDate && toDate),
  })
  const [withdrawalDoc, setWithdrawalDoc] = useState<string | null>(null)
  const [returnDoc, setReturnDoc] = useState<string | null>(null)
  // เปิดการ์ดจ๊อบบนหน้ารายงานเลย ไม่พาไป /jobs — ปิดแล้วต้องกลับมาที่รายงานเดิมพร้อมตัวกรองเดิม
  const [jobId, setJobId] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const closeJob = () => {
    setJobId(null)
    // ในการ์ดจ๊อบเบิก/คืนของได้ ยอดในรายงานอาจเปลี่ยน
    void queryClient.invalidateQueries({ queryKey: ['reports', 'stock-usage'] })
  }

  return (
    <AppShell title="การเบิกอะไหล่">
      <section className="report-page-heading">
        <div>
          <h2><PackageMinus aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />การเบิกอะไหล่</h2>
          <p>ใบเบิกงาน · เบิกหน้าสต็อก · ขายหน้าร้าน พร้อมจำนวนสุทธิหลังคืน · วันที่ตามปฏิทินไทย ช่วงยาวสุด 366 วัน</p>
        </div>
        <div className="report-date-filter">
          <label>ตั้งแต่<Input type="date" value={fromDate} max={toDate} onChange={e => set({ from: e.target.value })} /></label>
          <label>ถึง<Input type="date" value={toDate} min={fromDate} onChange={e => set({ to: e.target.value })} /></label>
          <label>แหล่งที่มา
            <Select value={source} onChange={e => set({ source: e.target.value })}>
              <option value="">ทุกแหล่ง</option>
              {Object.entries(SOURCE_LABEL).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </Select>
          </label>
        </div>
      </section>

      <div className="report-presets" role="tablist" aria-label="มุมมองรายงาน">
        {VIEWS.map(v => (
          <button key={v.value} type="button" role="tab" aria-selected={view === v.value} className={view === v.value ? 'is-active' : undefined} onClick={() => set({ view: v.value === 'documents' ? '' : v.value })}>{v.label}</button>
        ))}
      </div>

      <QueryState query={query} loadingTitle="กำลังโหลดรายงานการเบิกอะไหล่" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ไม่พบข้อมูลในช่วงนี้" onRetry={() => void query.refetch()}>
        {query.data ? <Body report={query.data} onWithdrawal={setWithdrawalDoc} onReturn={setReturnDoc} onJob={setJobId} /> : null}
      </QueryState>
      <StockWithdrawalDocumentModal operationId={withdrawalDoc} onClose={() => setWithdrawalDoc(null)} />
      <StockReturnDocumentModal operationId={returnDoc} onClose={() => setReturnDoc(null)} />
      <JobCardModal jobId={jobId} onClose={closeJob} />
    </AppShell>
  )
}

function Body({ report, onWithdrawal, onReturn, onJob }: { report: StockUsageReport; onWithdrawal: (id: string) => void; onReturn: (id: string) => void; onJob: (id: string) => void }) {
  const [exporting, setExporting] = useState(false)
  const exportLock = useFeatureLock('export')
  const showCost = report.netCost != null
  const rows = report.documents?.length ?? report.items?.length ?? report.requesters?.length ?? 0
  const exportExcel = async () => {
    setExporting(true)
    try { await exportStockUsage(report) }
    catch (error) { toast.error(error instanceof Error ? error.message : 'ส่งออก Excel ไม่สำเร็จ') }
    finally { setExporting(false) }
  }
  return (
    <>
      <div className="stat-tile-grid">
        <StatTile icon={PackageMinus} label="จำนวนสุทธิหลังคืน" value={formatNumber(report.netQuantity)} hint={`${formatDateOnly(report.fromDate)} – ${formatDateOnly(report.toDate)}`} />
        {showCost ? <StatTile icon={PackageMinus} label="ต้นทุนสุทธิ" value={money(report.netCost)} hint="บาท · จากล็อตจริง" /> : null}
        <StatTile icon={Undo2} label="รายการในมุมมองนี้" value={formatNumber(rows)} />
      </div>
      {report.truncated ? <p role="status" className="purchase-empty">ข้อมูลเกินเพดานที่แสดงได้ ตัดรายการท้ายช่วงออก — ลดช่วงวันที่หรือเลือกแหล่งที่มาเพื่อดูครบ</p> : null}
      <div className="report-section-title service-due-toolbar">
        <span>{VIEWS.find(v => v.value === report.view)?.label}</span>
        <span className="plan-lock-group">
          <PlanLockNote feature="export" />
          <Button variant="outline" size="sm" disabled={!rows || exporting || exportLock.locked} title={exportLock.locked ? exportLock.reason : !rows ? 'ไม่มีรายการสำหรับส่งออก' : undefined} onClick={() => void exportExcel()}>
            <Download aria-hidden="true" /> {exporting ? 'กำลังส่งออก…' : 'ส่งออก Excel'}
          </Button>
        </span>
      </div>
      {!rows ? <p className="purchase-empty">ไม่มีรายการในช่วงนี้ ลองขยายช่วงวันที่หรือเปลี่ยนแหล่งที่มา</p>
        : report.documents ? <DocumentsTable report={report} showCost={showCost} onWithdrawal={onWithdrawal} onReturn={onReturn} onJob={onJob} />
        : report.items ? <ItemsTable items={report.items} showCost={showCost} onWithdrawal={onWithdrawal} onReturn={onReturn} onJob={onJob} />
        : <RequestersTable report={report} showCost={showCost} />}
    </>
  )
}

function DocumentsTable({ report, showCost, onWithdrawal, onReturn, onJob }: { report: StockUsageReport; showCost: boolean; onWithdrawal: (id: string) => void; onReturn: (id: string) => void; onJob: (id: string) => void }) {
  return (
    <Card className="management-table-card">
      <ManagementTable data={report.documents ?? []} sortScope="loaded" columns={[
        { id: 'date', header: 'วันที่', size: 170, value: x => new Date(x.occurredAt).getTime(), render: x => <>{formatDateTime(x.occurredAt)}</> },
        { id: 'doc', header: 'เลขที่เอกสาร', size: 190, value: x => x.documentNumber, render: x => x.source === 'pos'
          ? (x.saleId ? <Link className="report-link" to={`/sales/${x.saleId}`}>{x.documentNumber}</Link> : <>{x.documentNumber}</>)
          : <button type="button" className="link-button" onClick={() => onWithdrawal(x.operationId)}>{x.documentNumber}</button> },
        { id: 'source', header: 'แหล่งที่มา', size: 150, value: x => SOURCE_LABEL[x.source], render: x => <>{SOURCE_LABEL[x.source]}</> },
        { id: 'job', header: 'งาน / ทะเบียน', size: 170, value: x => x.jobNo, render: x => x.jobId ? <><button type="button" className="link-button report-link" onClick={() => onJob(x.jobId!)}>{x.jobNo}</button><small className="purchase-sub">{x.vehicleRegistration ?? ''}</small></> : <>-</> },
        { id: 'requester', header: 'ผู้เบิก / ผู้จ่าย', size: 190, value: x => x.requesterName, render: x => <>{x.requesterName || '-'}<small className="purchase-sub">{x.performedByName}</small></> },
        { id: 'lines', header: 'รายการ', size: 90, value: x => x.lineCount, render: x => <span className="report-num">{x.lineCount}</span> },
        { id: 'issued', header: 'เบิก', size: 90, value: x => x.issuedQuantity, render: x => <span className="report-num">{x.issuedQuantity}</span> },
        { id: 'returned', header: 'คืน', size: 110, value: x => x.returnedQuantity, render: x => <>
          <span className="report-num">{x.returnedQuantity}</span>
          {x.returns.map(r => x.source === 'pos' ? null : <button key={r.operationId} type="button" className="link-button" onClick={() => onReturn(r.operationId)}>{r.documentNumber}</button>)}</> },
        { id: 'net', header: 'สุทธิ', size: 90, value: x => x.netQuantity, render: x => <strong className="report-num">{x.netQuantity}</strong> },
        { id: 'cost', header: 'ต้นทุนสุทธิ', size: 130, hidden: !showCost, value: x => x.netCost, render: x => <span className="money">{money(x.netCost)}</span> },
      ]} />
    </Card>
  )
}

function ItemsTable({ items, showCost, onWithdrawal, onReturn, onJob }: { items: StockUsageItem[]; showCost: boolean; onWithdrawal: (id: string) => void; onReturn: (id: string) => void; onJob: (id: string) => void }) {
  const [open, setOpen] = useState<Set<string>>(new Set())
  const toggle = (id: string) => setOpen(old => { const next = new Set(old); if (next.has(id)) next.delete(id); else next.add(id); return next })
  return (
    <Card className="management-table-card">
      <p className="section-help report-table-note">กดแถวสินค้าเพื่อดูว่าเอาไปใช้กับงานไหน หรือขายออกเมื่อไหร่ · ขาย POS ที่ยกเลิกแล้วแสดงคู่กับรายการยกเลิก</p>
      <div className="purchase-table-scroll">
        <table className="master-table purchase-lines">
          <thead><tr><th /><th>สินค้า</th><th>เบิกเข้างาน</th><th>เบิกหน้าสต็อก</th><th>ขาย POS</th><th>คืน</th><th>ยกเลิกบิล</th><th>สุทธิ</th>{showCost ? <th>ต้นทุนสุทธิ</th> : null}</tr></thead>
          <tbody>
            {items.map(item => {
              const expanded = open.has(item.catalogItemId)
              return (
                <ItemRows key={item.catalogItemId} item={item} expanded={expanded} showCost={showCost} onToggle={() => toggle(item.catalogItemId)} onWithdrawal={onWithdrawal} onReturn={onReturn} onJob={onJob} />
              )
            })}
          </tbody>
        </table>
      </div>
    </Card>
  )
}

function ItemRows({ item, expanded, showCost, onToggle, onWithdrawal, onReturn, onJob }: { item: StockUsageItem; expanded: boolean; showCost: boolean; onToggle: () => void; onWithdrawal: (id: string) => void; onReturn: (id: string) => void; onJob: (id: string) => void }) {
  const Chevron = expanded ? ChevronDown : ChevronRight
  return (
    <>
      <tr>
        <td><button type="button" className="link-button" aria-expanded={expanded} aria-label={`${expanded ? 'ซ่อน' : 'ดู'}รายละเอียด ${item.name}`} onClick={onToggle}><Chevron size={16} aria-hidden="true" /></button></td>
        <td><strong>{item.name}</strong><small className="purchase-sub">{item.code} · {item.unit}</small></td>
        <td className="report-num">{item.jobQuantity}</td><td className="report-num">{item.manualQuantity}</td><td className="report-num">{item.posQuantity}</td>
        <td className="report-num">{item.returnedQuantity}</td><td className="report-num">{item.posVoidQuantity}</td>
        <td><strong className="report-num">{item.netQuantity}</strong></td>
        {showCost ? <td><span className="money">{money(item.netCost)}</span></td> : null}
      </tr>
      {expanded ? (
        <tr>
          <td />
          <td colSpan={showCost ? 8 : 7}>
            <table className="master-table purchase-lines">
              <thead><tr><th>วันที่</th><th>รายการ</th><th>เลขเอกสาร</th><th>งาน / ทะเบียน / ใบเสร็จ</th><th>จำนวน</th>{showCost ? <th>ต้นทุน</th> : null}</tr></thead>
              <tbody>
                {item.details.map((d, i) => (
                  <tr key={`${d.operationId}-${i}`}>
                    <td>{formatDateTime(d.occurredAt)}</td>
                    <td>{DETAIL_LABEL[d.kind]}</td>
                    <td>{d.kind === 'job-issue' || d.kind === 'manual-issue'
                      ? <button type="button" className="link-button" onClick={() => onWithdrawal(d.operationId)}>{d.documentNumber}</button>
                      : d.kind === 'job-return' ? <button type="button" className="link-button" onClick={() => onReturn(d.operationId)}>{d.documentNumber}</button>
                      : <>{d.documentNumber}</>}</td>
                    <td>{d.jobId ? <><button type="button" className="link-button report-link" onClick={() => onJob(d.jobId!)}>{d.jobNo}</button> · {d.vehicleRegistration ?? '-'}</>
                      : d.saleId ? <Link className="report-link" to={`/sales/${d.saleId}`}>{d.receiptNo ?? d.documentNumber}</Link> : '-'}</td>
                    <td className="report-num">{d.quantity > 0 ? d.quantity : `${d.quantity} (คืน)`}</td>
                    {showCost ? <td><span className="money">{money(d.cost)}</span></td> : null}
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

function RequestersTable({ report, showCost }: { report: StockUsageReport; showCost: boolean }) {
  return (
    <Card className="management-table-card">
      <p className="section-help report-table-note">ขายหน้าร้าน (POS) และเบิกที่ไม่ระบุผู้เบิกไม่อยู่ในมุมมองนี้</p>
      <ManagementTable data={report.requesters ?? []} sortScope="loaded" columns={[
        { id: 'name', header: 'ช่างผู้เบิก', size: 240, value: x => x.requesterName, render: x => <strong>{x.requesterName}</strong> },
        { id: 'docs', header: 'จำนวนใบเบิก', size: 130, value: x => x.documentCount, render: x => <span className="report-num">{x.documentCount}</span> },
        { id: 'jobs', header: 'จำนวนงาน', size: 120, value: x => x.jobCount, render: x => <span className="report-num">{x.jobCount}</span> },
        { id: 'issued', header: 'เบิก (ชิ้น)', size: 120, value: x => x.issuedQuantity, render: x => <span className="report-num">{x.issuedQuantity}</span> },
        { id: 'returned', header: 'คืน (ชิ้น)', size: 120, value: x => x.returnedQuantity, render: x => <span className="report-num">{x.returnedQuantity}</span> },
        { id: 'net', header: 'สุทธิ (ชิ้น)', size: 120, value: x => x.netQuantity, render: x => <strong className="report-num">{x.netQuantity}</strong> },
        { id: 'cost', header: 'ต้นทุนสุทธิ', size: 140, hidden: !showCost, value: x => x.netCost, render: x => <span className="money">{money(x.netCost)}</span> },
      ]} />
    </Card>
  )
}
