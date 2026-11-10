import { useQuery } from '@tanstack/react-query'
import { Download, FileMinus, FileText, Info, ReceiptText, Store } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { formatTaxMonth, getOutputVatReport, type OutputVatReport, type OutputVatRow } from '../../../api/taxReports'
import { AppShell } from '../../../components/AppShell'
import { ManagementTable } from '../../../components/ManagementTable'
import { StatTile } from '../../../components/StatTile'
import { Button } from '../../../components/ui/button'
import { Card } from '../../../components/ui/card'
import { formatDateTime, formatMoney, formatNumber } from '../../../lib/format'
import { JobCardModal } from '../../jobs/JobCardModal'
import { QueryState } from '../../master-data/MasterDataCommon'
import { PlanLockNote } from '../../plan/PlanLockNote'
import { useFeatureLock } from '../../plan/usePlan'
import '../reports.css'
import { partyBranch, partyTaxId, TaxMonthInput, TaxReportHeader, useTaxMonth } from './TaxReportCommon'
import { exportOutputVat } from './taxExport'

const KIND: Record<OutputVatRow['kind'], { label: string; icon: typeof FileText }> = {
  taxInvoice: { label: 'ใบกำกับภาษี', icon: FileText },
  creditNote: { label: 'ใบลดหนี้', icon: FileMinus },
  receipt: { label: 'ใบเสร็จงานซ่อม', icon: ReceiptText },
  sale: { label: 'บิลขายหน้าร้าน', icon: Store },
}

/// รายงานภาษีขาย (docs/20 §3) — เดือนภาษีตามวันที่เอกสาร (เวลาไทย) · ผู้จัดการ/ธุรการ · ไม่แบ่งหน้า ส่งออกครบทุกแถว
export function OutputVatReportPage() {
  const [year, month, setMonth] = useTaxMonth()
  const query = useQuery({
    queryKey: ['reports', 'output-vat', year, month],
    queryFn: () => getOutputVatReport(year, month),
  })

  return (
    <AppShell title="รายงานภาษีขาย">
      <section className="report-page-heading">
        <div>
          <h2><FileText aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />รายงานภาษีขาย</h2>
          <p>ใบกำกับภาษีและใบลดหนี้ที่ออกในเดือน · ตรวจกับฝ่ายบัญชีก่อนยื่นแบบ ภ.พ.30</p>
        </div>
        <TaxMonthInput label="เดือนภาษี" year={year} month={month} onChange={setMonth} />
      </section>
      <QueryState query={query} loadingTitle="กำลังโหลดรายงานภาษีขาย" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ไม่พบข้อมูลในเดือนนี้" onRetry={() => void query.refetch()}>
        {query.data ? <ReportBody report={query.data} /> : null}
      </QueryState>
    </AppShell>
  )
}

function ReportBody({ report }: { report: OutputVatReport }) {
  const [exporting, setExporting] = useState(false)
  const [jobId, setJobId] = useState<string | null>(null)
  const exportLock = useFeatureLock('export')
  const empty = report.rows.length === 0 && report.uninvoiced.length === 0
  const exportExcel = async () => {
    setExporting(true)
    try {
      await exportOutputVat(report)
      toast.success(`ส่งออกรายงานภาษีขาย ${report.rows.length + report.uninvoiced.length} รายการแล้ว`)
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'ส่งออก Excel ไม่สำเร็จ')
    } finally {
      setExporting(false)
    }
  }

  const columns = [
    { id: 'seq', header: 'ลำดับ', size: 70, value: (x: OutputVatRow) => x.sequence, render: (x: OutputVatRow) => <span className="report-num">{x.sequence}</span> },
    { id: 'date', header: 'วันที่', size: 150, value: (x: OutputVatRow) => new Date(x.issuedAt).getTime(), render: (x: OutputVatRow) => formatDateTime(x.issuedAt) },
    { id: 'doc', header: 'เลขที่เอกสาร', size: 170, value: (x: OutputVatRow) => x.documentNo, render: (x: OutputVatRow) => {
      const kind = KIND[x.kind]
      return (
        <>
          <span className="credit-note-docno">{x.documentNo}</span>
          <small className="purchase-sub tax-kind"><kind.icon size={13} aria-hidden="true" /> {kind.label}</small>
        </>
      )
    } },
    { id: 'buyer', header: 'ผู้ซื้อ/ผู้รับบริการ', size: 260, value: (x: OutputVatRow) => x.buyerName, render: (x: OutputVatRow) => (
      <>
        {x.jobId ? <button type="button" className="link-button report-link" onClick={() => setJobId(x.jobId!)}>{x.buyerName}</button> : x.buyerName}
        {x.buyerTaxId ? <small className="purchase-sub">{partyTaxId(x.buyerTaxId)} · {partyBranch(x.buyerBranchNo)}</small> : null}
      </>
    ) },
    { id: 'ref', header: 'อ้างอิง', size: 150, value: (x: OutputVatRow) => x.referenceDocumentNo ?? '', render: (x: OutputVatRow) => (
      x.referenceDocumentNo ? <span className="credit-note-docno">{x.referenceDocumentNo}</span> : '-'
    ) },
    { id: 'net', header: 'มูลค่า', size: 130, value: (x: OutputVatRow) => x.netAmount, render: (x: OutputVatRow) => <span className={`report-num ${x.netAmount < 0 ? 'tax-negative' : ''}`}>{formatMoney(x.netAmount)}</span> },
    { id: 'vat', header: 'ภาษี', size: 120, value: (x: OutputVatRow) => x.vatAmount, render: (x: OutputVatRow) => <span className={`report-num ${x.vatAmount < 0 ? 'tax-negative' : ''}`}>{formatMoney(x.vatAmount)}</span> },
    { id: 'total', header: 'รวม', size: 130, value: (x: OutputVatRow) => x.totalAmount, render: (x: OutputVatRow) => <strong className={`report-num ${x.totalAmount < 0 ? 'tax-negative' : ''}`}>{formatMoney(x.totalAmount)}</strong> },
  ]

  return (
    <>
      <TaxReportHeader party={report.seller} periodLabel={formatTaxMonth(report.year, report.month)} />

      <div className="stat-tile-grid">
        <StatTile icon={FileText} label="จำนวนรายการ" value={formatNumber(report.rows.length)} hint="ใบกำกับภาษี + ใบลดหนี้" />
        <StatTile icon={ReceiptText} label="มูลค่า (ก่อน VAT)" value={formatMoney(report.netAmount)} hint="หักใบลดหนี้แล้ว" />
        <StatTile icon={FileText} label="ภาษีขาย" value={formatMoney(report.vatAmount)} hint={`รวมทั้งสิ้น ${formatMoney(report.totalAmount)} บาท`} />
        <StatTile
          icon={Info}
          label="ยังไม่ออกใบกำกับ (VAT)"
          value={formatMoney(report.uninvoicedVatAmount)}
          hint={`${formatNumber(report.uninvoiced.length)} รายการ · แยกไว้ด้านล่าง`}
          tone={report.uninvoiced.length > 0 ? 'warning' : 'default'}
        />
      </div>

      <div className="report-section-title service-due-toolbar">
        <span>ส่วนที่ 1 · รายการตามรายงานภาษีขาย</span>
        <span className="plan-lock-group">
          <PlanLockNote feature="export" />
          <Button
            variant="outline"
            size="sm"
            disabled={empty || exporting || exportLock.locked}
            title={exportLock.locked ? exportLock.reason : empty ? 'ไม่มีรายการสำหรับส่งออก' : undefined}
            onClick={() => void exportExcel()}
          >
            <Download aria-hidden="true" /> {exporting ? 'กำลังส่งออก…' : 'ส่งออก Excel'}
          </Button>
        </span>
      </div>
      {report.rows.length === 0 ? (
        <p className="purchase-empty">ไม่มีใบกำกับภาษีหรือใบลดหนี้ในเดือนนี้</p>
      ) : (
        <Card className="management-table-card">
          <ManagementTable data={report.rows} sortScope="loaded" columns={columns} />
          <TotalsRow net={report.netAmount} vat={report.vatAmount} total={report.totalAmount} />
        </Card>
      )}

      <div className="report-section-title">
        <span>ส่วนที่ 2 · รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับภาษี</span>
      </div>
      <p className="section-help">
        ใบเสร็จงานซ่อมของจ๊อบที่ยังไม่มีใบกำกับ · บิลขายหน้าร้าน · ใบลดหนี้ที่อ้างใบเสร็จ — ไม่ใช่รายการในรายงานภาษีขาย
        แต่ภาษีของงานบริการเกิดเมื่อรับเงินแล้ว ให้ฝ่ายบัญชีพิจารณายอดนี้ประกอบ
      </p>
      {report.uninvoiced.length === 0 ? (
        <p className="purchase-empty">ไม่มีรายการในเดือนนี้</p>
      ) : (
        <Card className="management-table-card">
          <ManagementTable data={report.uninvoiced} sortScope="loaded" columns={columns} />
          <TotalsRow net={report.uninvoicedNetAmount} vat={report.uninvoicedVatAmount} total={report.uninvoicedTotalAmount} />
        </Card>
      )}
      <JobCardModal jobId={jobId} onClose={() => setJobId(null)} />
    </>
  )
}

function TotalsRow({ net, vat, total }: { net: number; vat: number; total: number }) {
  return (
    <div className="tax-report-totals">
      <span>รวม</span>
      <span>มูลค่า <strong className="report-num">{formatMoney(net)}</strong></span>
      <span>ภาษี <strong className="report-num">{formatMoney(vat)}</strong></span>
      <span>รวมทั้งสิ้น <strong className="report-num">{formatMoney(total)}</strong></span>
    </div>
  )
}
