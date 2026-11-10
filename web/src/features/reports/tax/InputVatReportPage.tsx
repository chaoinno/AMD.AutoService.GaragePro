import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, Download, FilePlus2, FileText, PackageCheck, ReceiptText } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { formatTaxMonth, getInputVatReport, type InputVatReport, type PurchaseTaxInvoice } from '../../../api/taxReports'
import { AppShell } from '../../../components/AppShell'
import { ManagementTable } from '../../../components/ManagementTable'
import { StatTile } from '../../../components/StatTile'
import { Button } from '../../../components/ui/button'
import { Card } from '../../../components/ui/card'
import { formatDateOnly, formatDateTime, formatMoney, formatNumber } from '../../../lib/format'
import { QueryState } from '../../master-data/MasterDataCommon'
import { PlanLockNote } from '../../plan/PlanLockNote'
import { useFeatureLock } from '../../plan/usePlan'
import '../reports.css'
import { PurchaseTaxInvoiceModal, VoidPurchaseTaxInvoiceModal, type PurchaseInvoiceTarget } from './PurchaseTaxInvoiceModal'
import { partyBranch, partyTaxId, TaxMonthInput, TaxReportHeader, useTaxMonth } from './TaxReportCommon'
import { exportInputVat } from './taxExport'

/// รายงานภาษีซื้อ + บันทึกใบกำกับภาษีซื้อ (docs/20 §4) — ตามเดือนที่ใช้สิทธิ์ · ผู้จัดการ/ธุรการ · ไม่แบ่งหน้า
export function InputVatReportPage() {
  const [year, month, setMonth] = useTaxMonth()
  const query = useQuery({
    queryKey: ['reports', 'input-vat', year, month],
    queryFn: () => getInputVatReport(year, month),
  })

  return (
    <AppShell title="ภาษีซื้อ">
      <section className="report-page-heading">
        <div>
          <h2><ReceiptText aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />ภาษีซื้อ</h2>
          <p>บันทึกใบกำกับภาษีที่ได้รับจากผู้ขาย และรายงานตามเดือนที่ใช้สิทธิ์ · ตรวจกับฝ่ายบัญชีก่อนยื่นแบบ ภ.พ.30</p>
        </div>
        <TaxMonthInput label="เดือนที่ใช้สิทธิ์" year={year} month={month} onChange={setMonth} />
      </section>
      <QueryState query={query} loadingTitle="กำลังโหลดรายงานภาษีซื้อ" emptyTitle="ยังไม่มีข้อมูล" emptyReason="ไม่พบข้อมูลในเดือนนี้" onRetry={() => void query.refetch()}>
        {query.data ? <ReportBody report={query.data} /> : null}
      </QueryState>
    </AppShell>
  )
}

function ReportBody({ report }: { report: InputVatReport }) {
  const queryClient = useQueryClient()
  const [exporting, setExporting] = useState(false)
  const [recording, setRecording] = useState<PurchaseInvoiceTarget | null>(null)
  const [voiding, setVoiding] = useState<PurchaseTaxInvoice | null>(null)
  const exportLock = useFeatureLock('export')
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['reports', 'input-vat'] })
  const record = (goodsReceiptId: string | null) =>
    setRecording({ goodsReceiptId, claimYear: report.year, claimMonth: report.month })

  const exportExcel = async () => {
    setExporting(true)
    try {
      await exportInputVat(report)
      toast.success(`ส่งออกรายงานภาษีซื้อ ${report.rows.length} รายการแล้ว`)
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'ส่งออก Excel ไม่สำเร็จ')
    } finally {
      setExporting(false)
    }
  }

  return (
    <>
      <TaxReportHeader party={report.buyer} periodLabel={formatTaxMonth(report.year, report.month)} />

      <div className="stat-tile-grid">
        <StatTile icon={FileText} label="ใบกำกับที่ใช้สิทธิ์" value={formatNumber(report.rows.length)} hint={formatTaxMonth(report.year, report.month)} />
        <StatTile icon={ReceiptText} label="มูลค่า (ก่อน VAT)" value={formatMoney(report.netAmount)} />
        <StatTile icon={ReceiptText} label="ภาษีซื้อ" value={formatMoney(report.vatAmount)} hint={`รวมทั้งสิ้น ${formatMoney(report.totalAmount)} บาท`} />
        <StatTile
          icon={PackageCheck}
          label="ใบรับสินค้ายังไม่บันทึกใบกำกับ"
          value={formatNumber(report.pendingGoodsReceipts.length)}
          hint="PO ที่มี VAT ย้อนหลัง 6 เดือน"
          tone={report.pendingGoodsReceipts.length > 0 ? 'warning' : 'default'}
        />
      </div>

      <div className="report-section-title service-due-toolbar">
        <span>ใบกำกับภาษีซื้อที่ใช้สิทธิ์เดือนนี้</span>
        <span className="plan-lock-group">
          <PlanLockNote feature="export" />
          <Button
            variant="outline"
            size="sm"
            disabled={!report.rows.length || exporting || exportLock.locked}
            title={exportLock.locked ? exportLock.reason : !report.rows.length ? 'ไม่มีรายการสำหรับส่งออก' : undefined}
            onClick={() => void exportExcel()}
          >
            <Download aria-hidden="true" /> {exporting ? 'กำลังส่งออก…' : 'ส่งออก Excel'}
          </Button>
          <Button size="sm" onClick={() => record(null)}>
            <FilePlus2 aria-hidden="true" /> บันทึกใบกำกับภาษีซื้อ
          </Button>
        </span>
      </div>
      {report.rows.length === 0 ? (
        <p className="purchase-empty">ยังไม่มีใบกำกับภาษีซื้อที่ใช้สิทธิ์เดือนนี้</p>
      ) : (
        <Card className="management-table-card">
          <ManagementTable data={report.rows} sortScope="loaded" columns={[
            { id: 'date', header: 'วันที่ใบกำกับ', size: 130, value: (x) => x.invoiceDate, render: (x) => formatDateOnly(x.invoiceDate) },
            { id: 'no', header: 'เลขที่ใบกำกับ', size: 160, value: (x) => x.invoiceNo, render: (x) => <span className="credit-note-docno">{x.invoiceNo}</span> },
            { id: 'supplier', header: 'ผู้ขาย', size: 260, value: (x) => x.supplierName, render: (x) => (
              <>{x.supplierName}<small className="purchase-sub">{partyTaxId(x.supplierTaxId)} · {partyBranch(x.supplierBranchNo)}</small></>
            ) },
            { id: 'ref', header: 'อ้างอิง', size: 170, value: (x) => x.goodsReceiptNumber ?? '', render: (x) => (
              x.goodsReceiptNumber
                ? <><span className="credit-note-docno">{x.goodsReceiptNumber}</span><small className="purchase-sub">{x.purchaseOrderNumber}</small></>
                : <span className="purchase-sub">บันทึกเอง</span>
            ) },
            { id: 'net', header: 'มูลค่า', size: 130, value: (x) => x.netAmount, render: (x) => <span className="report-num">{formatMoney(x.netAmount)}</span> },
            { id: 'vat', header: 'ภาษี', size: 120, value: (x) => x.vatAmount, render: (x) => <strong className="report-num">{formatMoney(x.vatAmount)}</strong> },
            { id: 'by', header: 'บันทึกโดย', size: 170, value: (x) => new Date(x.recordedAt).getTime(), render: (x) => (
              <>{x.recordedByName}<small className="purchase-sub">{formatDateTime(x.recordedAt)}</small>{x.note ? <small className="purchase-sub">{x.note}</small> : null}</>
            ) },
            { id: 'actions', header: '', size: 110, render: (x) => (
              <Button variant="destructive-outline" size="sm" onClick={() => setVoiding(x)}>
                <Ban aria-hidden="true" /> ยกเลิก
              </Button>
            ) },
          ]} />
          <div className="tax-report-totals">
            <span>รวม</span>
            <span>มูลค่า <strong className="report-num">{formatMoney(report.netAmount)}</strong></span>
            <span>ภาษี <strong className="report-num">{formatMoney(report.vatAmount)}</strong></span>
            <span>รวมทั้งสิ้น <strong className="report-num">{formatMoney(report.totalAmount)}</strong></span>
          </div>
        </Card>
      )}

      <div className="report-section-title"><span>ใบรับสินค้าของ PO ที่มี VAT ที่ยังไม่บันทึกใบกำกับ</span></div>
      <p className="section-help">
        รับตั้งแต่ 6 เดือนก่อนถึงสิ้นเดือนที่เลือก (ยังใช้สิทธิ์ได้) — ตัวช่วยเตือน ไม่ใช่ส่วนของรายงาน · ยอดเป็นค่าประมาณจากใบรับสินค้า
      </p>
      {report.pendingGoodsReceipts.length === 0 ? (
        <p className="purchase-empty">ไม่มีใบรับสินค้าที่ค้างบันทึกใบกำกับ</p>
      ) : (
        <Card className="management-table-card">
          <ManagementTable data={report.pendingGoodsReceipts} sortScope="loaded" columns={[
            { id: 'date', header: 'วันที่รับ', size: 150, value: (x) => new Date(x.receivedAt).getTime(), render: (x) => formatDateTime(x.receivedAt) },
            { id: 'no', header: 'ใบรับสินค้า', size: 170, value: (x) => x.number, render: (x) => (
              <><span className="credit-note-docno">{x.number}</span><small className="purchase-sub">{x.purchaseOrderNumber}</small></>
            ) },
            { id: 'supplier', header: 'ผู้ขาย', size: 240, value: (x) => x.supplierName ?? '', render: (x) => (
              <>{x.supplierName ?? '-'}{x.deliveryNumber ? <small className="purchase-sub">ใบส่งของ {x.deliveryNumber}</small> : null}</>
            ) },
            { id: 'net', header: 'มูลค่า (ประมาณ)', size: 140, value: (x) => x.netAmount, render: (x) => <span className="report-num">{formatMoney(x.netAmount)}</span> },
            { id: 'vat', header: 'ภาษี (ประมาณ)', size: 130, value: (x) => x.vatAmount, render: (x) => <span className="report-num">{formatMoney(x.vatAmount)}</span> },
            { id: 'actions', header: '', size: 160, render: (x) => (
              <Button variant="outline" size="sm" onClick={() => record(x.goodsReceiptId)}>
                <FilePlus2 aria-hidden="true" /> บันทึกใบกำกับ
              </Button>
            ) },
          ]} />
        </Card>
      )}

      {report.voided.length > 0 ? (
        <>
          <div className="report-section-title"><span>ยกเลิกแล้ว (ไม่นับในรายงาน)</span></div>
          <Card className="management-table-card">
            <ManagementTable data={report.voided} sortScope="loaded" columns={[
              { id: 'no', header: 'เลขที่ใบกำกับ', size: 160, value: (x) => x.invoiceNo, render: (x) => (
                <span className="tax-voided"><Ban size={13} aria-hidden="true" /> {x.invoiceNo}</span>
              ) },
              { id: 'supplier', header: 'ผู้ขาย', size: 240, value: (x) => x.supplierName, render: (x) => x.supplierName },
              { id: 'vat', header: 'ภาษี', size: 120, value: (x) => x.vatAmount, render: (x) => <span className="report-num">{formatMoney(x.vatAmount)}</span> },
              { id: 'reason', header: 'เหตุผล', size: 300, value: (x) => x.voidReason ?? '', render: (x) => (
                <>{x.voidReason}<small className="purchase-sub">{x.voidedByName} · {x.voidedAt ? formatDateTime(x.voidedAt) : ''}</small></>
              ) },
            ]} />
          </Card>
        </>
      ) : null}

      <PurchaseTaxInvoiceModal
        target={recording}
        onClose={() => setRecording(null)}
        onSaved={() => { setRecording(null); refresh() }}
      />
      <VoidPurchaseTaxInvoiceModal
        invoice={voiding}
        onClose={() => setVoiding(null)}
        onDone={() => { setVoiding(null); refresh() }}
      />
    </>
  )
}
