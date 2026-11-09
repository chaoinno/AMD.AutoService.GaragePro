import { REFUND_STATUS_LABEL, type CreditNoteReport } from '../../api/creditNotes'
import { formatDateTime } from '../../lib/format'

/// ส่งออกรายงานใบลดหนี้ — ทุกแถวของช่วงวันที่ (รายงานไม่แบ่งหน้า) · แผ่นที่ 2 = รายการที่ลดรายบรรทัด
export async function exportCreditNotes(report: CreditNoteReport) {
  if (!report.items.length) throw new Error('ไม่มีรายการสำหรับส่งออก')
  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'ServicePro'

  const notes = workbook.addWorksheet('ใบลดหนี้')
  notes.columns = ([
    ['เลขที่', 16], ['วันที่', 20], ['อ้างอิงบิล', 16], ['ประเภทเอกสาร', 22], ['เลขที่งาน', 20], ['ทะเบียน', 14],
    ['ลูกค้า/ผู้ซื้อ', 28], ['เลขผู้เสียภาษีผู้ซื้อ', 18], ['เหตุผล', 40], ['มูลค่าที่ลด (บาท)', 18], ['VAT (บาท)', 14],
    ['รวม (บาท)', 16], ['คืนเงินแล้ว (บาท)', 18], ['สถานะคืนเงิน', 16], ['ผู้ออก', 22],
  ] as [string, number][]).map(([header, width]) => ({ header, width }))
  for (const n of report.items) {
    notes.addRow([
      n.documentNo, formatDateTime(n.issuedAt), n.sourceDocumentNo,
      n.isTaxDocument ? 'ใบลดหนี้/ใบกำกับภาษี' : 'ใบลดหนี้ (อ้างใบเสร็จ)', n.jobNo, n.vehicleRegistration,
      n.buyer.name, n.buyer.taxId ?? '', n.reasonText ? `${n.reasonLabelTh} — ${n.reasonText}` : n.reasonLabelTh,
      n.diffNetAmount, n.vatAmount, n.totalAmount, n.refundedAmount, REFUND_STATUS_LABEL[n.refundStatus], n.issuedByName,
    ])
  }
  for (const c of [10, 11, 12, 13]) notes.getColumn(c).numFmt = '#,##0.00'

  const lines = workbook.addWorksheet('รายการที่ลด')
  lines.columns = ([
    ['เลขที่ใบลดหนี้', 16], ['อ้างอิงบิล', 16], ['ใบเสนอราคา', 22], ['รายการ', 40], ['จำนวนเดิม', 12], ['มูลค่าเดิม (บาท)', 16],
    ['จำนวนที่ลด', 12], ['มูลค่าที่ลด (บาท)', 16],
  ] as [string, number][]).map(([header, width]) => ({ header, width }))
  for (const n of report.items) {
    for (const l of n.lines) {
      lines.addRow([n.documentNo, n.sourceDocumentNo, l.quotationCode, l.description, l.originalQuantity, l.originalNetAmount,
        l.quantity > 0 ? l.quantity : null, l.amount])
    }
  }
  for (const c of [6, 8]) lines.getColumn(c).numFmt = '#,##0.00'

  for (const sheet of [notes, lines]) {
    sheet.views = [{ state: 'frozen', ySplit: 1 }]
    sheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: sheet.rowCount, column: sheet.columnCount } }
    sheet.getRow(1).height = 30
    sheet.getRow(1).eachCell(cell => {
      cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF10243F' } }
      cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    })
    sheet.eachRow(row => { row.alignment = { vertical: 'middle', wrapText: true } })
  }

  const buffer = await workbook.xlsx.writeBuffer()
  const url = URL.createObjectURL(new Blob([new Uint8Array(buffer)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `credit-notes-${report.from}-${report.to}.xlsx`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
