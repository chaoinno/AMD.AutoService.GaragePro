import { formatTaxMonth, type InputVatReport, type OutputVatReport, type OutputVatRow, type TaxReportParty } from '../../../api/taxReports'
import { formatDateOnly, formatDateTime } from '../../../lib/format'
import { downloadWorkbook, partyBranch, partyTaxId, styleSheet } from './TaxReportCommon'

const KIND_LABEL: Record<OutputVatRow['kind'], string> = {
  taxInvoice: 'ใบกำกับภาษี',
  creditNote: 'ใบลดหนี้',
  receipt: 'ใบเสร็จงานซ่อม',
  sale: 'บิลขายหน้าร้าน',
}

type Sheet = import('exceljs').Worksheet

/** หัวรายงาน 5 แถว แล้วเว้น 1 แถว — คืนเลขแถวของหัวตาราง */
function writeHeader(sheet: Sheet, title: string, party: TaxReportParty | undefined, period: string): number {
  sheet.addRow([title]).font = { bold: true, size: 14 }
  sheet.addRow(['เดือนภาษี', period])
  sheet.addRow(['ชื่อผู้ประกอบการ', party?.name ?? ''])
  sheet.addRow(['เลขประจำตัวผู้เสียภาษี', party?.taxId ? partyTaxId(party.taxId) : 'ไม่มีข้อมูล'])
  sheet.addRow(['สถานประกอบการ', partyBranch(party?.branchNo)])
  sheet.addRow([])
  return sheet.rowCount + 1
}

function moneyColumns(sheet: Sheet, columns: number[]) {
  for (const c of columns) sheet.getColumn(c).numFmt = '#,##0.00;-#,##0.00'
}

function outputRows(sheet: Sheet, rows: OutputVatRow[], net: number, vat: number, total: number) {
  for (const r of rows) {
    sheet.addRow([
      r.sequence, formatDateTime(r.issuedAt), r.documentNo, KIND_LABEL[r.kind], r.buyerName,
      r.buyerTaxId ? partyTaxId(r.buyerTaxId) : '', r.buyerBranchNo ? partyBranch(r.buyerBranchNo) : '',
      r.netAmount, r.vatAmount, r.totalAmount, r.kind === 'creditNote' && r.referenceDocumentNo ? `อ้างอิง ${r.referenceDocumentNo}` : (r.referenceDocumentNo ?? ''),
    ])
  }
  const sum = sheet.addRow(['', '', '', '', 'รวม', '', '', net, vat, total, ''])
  sum.font = { bold: true }
}

const OUTPUT_COLUMNS: [string, number][] = [
  ['ลำดับ', 8], ['วันที่', 20], ['เลขที่เอกสาร', 18], ['ประเภท', 18], ['ชื่อผู้ซื้อ/ผู้รับบริการ', 32],
  ['เลขผู้เสียภาษีผู้ซื้อ', 20], ['สนญ./สาขา', 16], ['มูลค่าสินค้า/บริการ', 18], ['ภาษีมูลค่าเพิ่ม', 16], ['รวม', 16], ['หมายเหตุ', 24],
]

function addTable(sheet: Sheet, columns: [string, number][], headerRow: number) {
  columns.forEach(([, width], i) => { sheet.getColumn(i + 1).width = width })
  sheet.getRow(headerRow).values = columns.map(([h]) => h)
}

/// รายงานภาษีขาย — แผ่นที่ 1 รายการตามกฎหมาย · แผ่นที่ 2 รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับ (Q1)
export async function exportOutputVat(report: OutputVatReport) {
  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'ServicePro'
  const period = formatTaxMonth(report.year, report.month)

  const legal = workbook.addWorksheet('รายงานภาษีขาย')
  const h1 = writeHeader(legal, 'รายงานภาษีขาย', report.seller, period)
  addTable(legal, OUTPUT_COLUMNS, h1)
  outputRows(legal, report.rows, report.netAmount, report.vatAmount, report.totalAmount)
  moneyColumns(legal, [8, 9, 10])
  styleSheet(legal, h1)

  const other = workbook.addWorksheet('ยังไม่ออกใบกำกับ')
  const h2 = writeHeader(other, 'รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับภาษี (ไม่ใช่ส่วนของรายงานภาษีขาย)', report.seller, period)
  addTable(other, OUTPUT_COLUMNS, h2)
  outputRows(other, report.uninvoiced, report.uninvoicedNetAmount, report.uninvoicedVatAmount, report.uninvoicedTotalAmount)
  moneyColumns(other, [8, 9, 10])
  styleSheet(other, h2)

  await downloadWorkbook(workbook, `output-vat-${report.year}-${String(report.month).padStart(2, '0')}.xlsx`)
}

/// รายงานภาษีซื้อ — แผ่นที่ 1 ใบที่ใช้สิทธิ์เดือนนี้ · แผ่นที่ 2 ใบที่ยกเลิก (ไว้ตรวจย้อนหลัง)
export async function exportInputVat(report: InputVatReport) {
  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'ServicePro'
  const period = formatTaxMonth(report.year, report.month)
  const columns: [string, number][] = [
    ['ลำดับ', 8], ['วันที่ใบกำกับ', 16], ['เลขที่ใบกำกับ', 20], ['ชื่อผู้ขาย', 32], ['เลขผู้เสียภาษีผู้ขาย', 20],
    ['สนญ./สาขา', 16], ['มูลค่าสินค้า/บริการ', 18], ['ภาษีมูลค่าเพิ่ม', 16], ['รวม', 16], ['อ้างอิง PO/ใบรับสินค้า', 26], ['หมายเหตุ', 28],
  ]

  const sheet = workbook.addWorksheet('รายงานภาษีซื้อ')
  const h = writeHeader(sheet, 'รายงานภาษีซื้อ', report.buyer, period)
  addTable(sheet, columns, h)
  report.rows.forEach((r, i) => sheet.addRow([
    i + 1, formatDateOnly(r.invoiceDate), r.invoiceNo, r.supplierName, partyTaxId(r.supplierTaxId), partyBranch(r.supplierBranchNo),
    r.netAmount, r.vatAmount, r.totalAmount, [r.purchaseOrderNumber, r.goodsReceiptNumber].filter(Boolean).join(' · '), r.note ?? '',
  ]))
  sheet.addRow(['', '', '', 'รวม', '', '', report.netAmount, report.vatAmount, report.totalAmount, '', '']).font = { bold: true }
  moneyColumns(sheet, [7, 8, 9])
  styleSheet(sheet, h)

  if (report.voided.length) {
    const voided = workbook.addWorksheet('ยกเลิกแล้ว')
    voided.columns = ([
      ['เลขที่ใบกำกับ', 20], ['ชื่อผู้ขาย', 32], ['เลขผู้เสียภาษีผู้ขาย', 20], ['มูลค่า', 16], ['ภาษี', 14],
      ['ยกเลิกเมื่อ', 20], ['โดย', 22], ['เหตุผล', 40],
    ] as [string, number][]).map(([header, width]) => ({ header, width }))
    for (const r of report.voided) {
      voided.addRow([r.invoiceNo, r.supplierName, partyTaxId(r.supplierTaxId), r.netAmount, r.vatAmount,
        r.voidedAt ? formatDateTime(r.voidedAt) : '', r.voidedByName ?? '', r.voidReason ?? ''])
    }
    moneyColumns(voided, [4, 5])
    styleSheet(voided)
  }

  await downloadWorkbook(workbook, `input-vat-${report.year}-${String(report.month).padStart(2, '0')}.xlsx`)
}
