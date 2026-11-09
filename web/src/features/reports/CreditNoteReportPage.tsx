import { useQuery } from '@tanstack/react-query'
import { CheckCircle2, Clock3, Download, FileMinus, HandCoins, ReceiptText, Wallet } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { getCreditNoteReport, REFUND_STATUS_LABEL, type CreditNote, type CreditNoteReport } from '../../api/creditNotes'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { StatTile } from '../../components/StatTile'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { formatDateOnly, formatDateTime, formatMoney, formatNumber } from '../../lib/format'
import { CreditNoteDocumentModal } from '../jobs/billing/CreditNoteDocument'
import { JobCardModal } from '../jobs/JobCardModal'
import { QueryState } from '../master-data/MasterDataCommon'
import { PlanLockNote } from '../plan/PlanLockNote'
import { useFeatureLock } from '../plan/usePlan'
import { exportCreditNotes } from './creditNoteExport'
import './reports.css'

const localIso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/// รายงานใบลดหนี้ (docs/20 §2.5) — ตามวันที่ออก (เวลาไทย) · ผู้จัดการ/ธุรการ · ไม่แบ่งหน้า ส่งออกได้ครบทุกแถว
export function CreditNoteReportPage() {
  const [[from, to], setRange] = useState<[string, string]>(() => {
    const now = new Date()
    return [localIso(new Date(now.getFullYear(), now.getMonth(), 1)), localIso(now)]
  })
  const query = useQuery({
    queryKey: ['reports', 'credit-notes', from, to],
    queryFn: () => getCreditNoteReport(from, to),
    enabled: Boolean(from && to),
  })

  return (
    <AppShell title="ใบลดหนี้">
      <section className="report-page-heading">
        <div>
          <h2><FileMinus aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />รายงานใบลดหนี้</h2>
          <p>ใบลดหนี้ที่ออกในช่วงวันที่ · อ้างใบกำกับภาษี (เอกสารทางภาษี) หรือใบเสร็จ · ยอดขายในรายงานอื่นยังไม่หักใบลดหนี้</p>
        </div>
        <div className="report-date-filter">
          <label>ออกตั้งแต่<Input type="date" value={from} max={to} onChange={(e) => setRange([e.target.value, to])} /></label>
          <label>ถึง<Input type="date" value={to} min={from} onChange={(e) => setRange([from, e.target.value])} /></label>
        </div>
      </section>
      <QueryState query={query} loadingTitle="กำลังโหลดรายงานใบลดหนี้" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ไม่พบข้อมูลในช่วงนี้" onRetry={() => void query.refetch()}>
        {query.data ? <ReportBody report={query.data} /> : null}
      </QueryState>
    </AppShell>
  )
}

function ReportBody({ report }: { report: CreditNoteReport }) {
  const [exporting, setExporting] = useState(false)
  const [jobId, setJobId] = useState<string | null>(null)
  const [printing, setPrinting] = useState<CreditNote | null>(null)
  const exportLock = useFeatureLock('export')
  const exportExcel = async () => {
    setExporting(true)
    try { await exportCreditNotes(report) }
    catch (error) { toast.error(error instanceof Error ? error.message : 'ส่งออก Excel ไม่สำเร็จ') }
    finally { setExporting(false) }
  }

  return (
    <>
      <div className="stat-tile-grid">
        <StatTile icon={ReceiptText} label="จำนวนใบลดหนี้" value={formatNumber(report.count)} hint={`${formatDateOnly(report.from)} – ${formatDateOnly(report.to)}`} />
        <StatTile icon={FileMinus} label="มูลค่าที่ลด (ก่อน VAT)" value={formatMoney(report.diffNetAmount)} hint={`VAT ${formatMoney(report.vatAmount)} บาท`} />
        <StatTile icon={HandCoins} label="รวมลดหนี้ทั้งสิ้น" value={formatMoney(report.totalAmount)} hint={`คืนเงินแล้ว ${formatMoney(report.refundedAmount)} บาท`} />
        <StatTile icon={Wallet} label="ยังค้างคืนเงิน" value={formatMoney(report.outstandingRefund)} tone={report.outstandingRefund > 0 ? 'warning' : 'default'} />
      </div>

      <div className="report-section-title service-due-toolbar">
        <span>ใบลดหนี้ในช่วงที่เลือก</span>
        <span className="plan-lock-group">
          <PlanLockNote feature="export" />
          <Button
            variant="outline"
            size="sm"
            disabled={!report.items.length || exporting || exportLock.locked}
            title={exportLock.locked ? exportLock.reason : !report.items.length ? 'ไม่มีรายการสำหรับส่งออก' : undefined}
            onClick={() => void exportExcel()}
          >
            <Download aria-hidden="true" /> {exporting ? 'กำลังส่งออก…' : 'ส่งออก Excel'}
          </Button>
        </span>
      </div>
      {report.items.length === 0 ? (
        <p className="purchase-empty">ไม่มีใบลดหนี้ในช่วงนี้</p>
      ) : (
        <Card className="management-table-card">
          <ManagementTable data={report.items} sortScope="loaded" columns={[
            { id: 'doc', header: 'เลขที่', size: 150, value: (x) => x.documentNo, render: (x) => (
              <>
                <button type="button" className="link-button report-link credit-note-docno" onClick={() => setPrinting(x)}>{x.documentNo}</button>
                <small className="purchase-sub">{x.isTaxDocument ? 'ใบลดหนี้/ใบกำกับภาษี' : 'ไม่ใช่เอกสารภาษี'}</small>
              </>
            ) },
            { id: 'date', header: 'วันที่', size: 170, value: (x) => new Date(x.issuedAt).getTime(), render: (x) => (
              <>{formatDateTime(x.issuedAt)}<small className="purchase-sub">โดย {x.issuedByName}</small></>
            ) },
            { id: 'source', header: 'อ้างอิงบิล', size: 140, value: (x) => x.sourceDocumentNo, render: (x) => <span className="credit-note-docno">{x.sourceDocumentNo}</span> },
            { id: 'job', header: 'งาน / ลูกค้า', size: 220, value: (x) => x.jobNo, render: (x) => (
              <>
                <button type="button" className="link-button report-link credit-note-docno" onClick={() => setJobId(x.jobId)}>{x.jobNo}</button>
                <small className="purchase-sub">{x.vehicleRegistration} · {x.buyer.name}</small>
              </>
            ) },
            { id: 'reason', header: 'เหตุผล', size: 260, value: (x) => x.reasonLabelTh, render: (x) => <>{x.reasonLabelTh}{x.reasonText ? <small className="purchase-sub">{x.reasonText}</small> : null}</> },
            { id: 'diff', header: 'มูลค่าที่ลด', size: 140, value: (x) => x.diffNetAmount, render: (x) => (
              <><span className="report-num">{formatMoney(x.diffNetAmount)}</span><small className="purchase-sub">VAT {formatMoney(x.vatAmount)}</small></>
            ) },
            { id: 'total', header: 'รวม', size: 120, value: (x) => x.totalAmount, render: (x) => <strong className="report-num">{formatMoney(x.totalAmount)}</strong> },
            { id: 'refund', header: 'คืนเงิน', size: 170, value: (x) => x.refundedAmount, render: (x) => (
              <>
                <span className={`purchase-status purchase-status--${x.refundStatus === 'full' ? 'approved' : 'pending'}`}>
                  {x.refundStatus === 'full' ? <CheckCircle2 size={14} aria-hidden="true" /> : <Clock3 size={14} aria-hidden="true" />}
                  {REFUND_STATUS_LABEL[x.refundStatus]}
                </span>
                <small className="purchase-sub">{formatMoney(x.refundedAmount)} บาท</small>
              </>
            ) },
          ]} />
        </Card>
      )}
      <JobCardModal jobId={jobId} onClose={() => setJobId(null)} />
      <CreditNoteDocumentModal note={printing} onClose={() => setPrinting(null)} />
    </>
  )
}
