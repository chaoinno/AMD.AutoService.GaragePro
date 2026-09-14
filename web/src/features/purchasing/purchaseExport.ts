import { purchases, type Purchase, type PurchaseKind } from '../../api/purchasing'

export const purchaseStatusLabels: Record<string, string> = { draft: 'ฉบับร่าง', pending: 'รออนุมัติ', approved: 'อนุมัติแล้ว', converted: 'แปลงเป็น PO แล้ว', sent: 'ส่งสั่งซื้อแล้ว', partial: 'รับบางส่วน', complete: 'รับครบแล้ว', cancelled: 'ยกเลิก' }

export async function createPurchaseWorkbook(documents: Purchase[], branch: string) {
  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'GaragePro'
  const summary = workbook.addWorksheet('รายการเอกสาร')
  summary.columns = [
    ['เลขเอกสาร', 26], ['ประเภท', 12], ['สถานะ', 22], ['สาขา', 28], ['วันที่สร้าง', 23],
    ['ผู้สร้าง', 26], ['ผู้อนุมัติ', 26], ['วันที่อนุมัติ', 23], ['ซัพพลายเออร์', 32], ['คลัง', 24],
    ['วันที่ต้องการ', 20], ['มูลค่าก่อนภาษี (บาท)', 25], ['เงื่อนไขชำระเงิน', 32], ['หมายเหตุ', 40],
  ].map(([header, width]) => ({ header: String(header), width: Number(width) }))
  const lines = workbook.addWorksheet('รายละเอียดสินค้า')
  lines.columns = [['เลขเอกสาร', 26], ['รหัสสินค้า', 22], ['สินค้า', 40], ['หน่วย', 14], ['จำนวน', 16], ['ราคา/หน่วย (บาท)', 24], ['มูลค่า (บาท)', 24], ['รับดี', 16], ['ชำรุด', 16], ['ค้างรับ', 16]].map(([header, width]) => ({ header: String(header), width: Number(width) }))
  for (const doc of documents) {
    summary.addRow([doc.number, doc.kind, purchaseStatusLabels[doc.status] || doc.status, branch, new Date(doc.createdAt), doc.createdByName, doc.approvedByName, doc.approvedAt ? new Date(doc.approvedAt) : null, doc.supplierName, doc.warehouseName, doc.requiredDate ? new Date(doc.requiredDate) : null, doc.total, doc.paymentTerms, doc.note])
    for (const line of doc.lines) lines.addRow([doc.number, line.code, line.name, line.unit, line.quantity, line.unitCost, Math.round(line.quantity * line.unitCost * 100) / 100, doc.kind === 'PO' ? line.receivedGood : null, doc.kind === 'PO' ? line.receivedDamaged : null, doc.kind === 'PO' ? line.outstanding : null])
  }
  for (const column of [5, 8]) summary.getColumn(column).numFmt = 'dd/mm/yyyy hh:mm'
  summary.getColumn(11).numFmt = 'dd/mm/yyyy'
  summary.getColumn(12).numFmt = '#,##0.00'
  for (const column of [5, 6, 7, 8, 9, 10]) lines.getColumn(column).numFmt = '#,##0.00'
  for (const sheet of [summary, lines]) {
    sheet.views = [{ state: 'frozen', ySplit: 1 }]
    sheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: sheet.rowCount, column: sheet.columnCount } }
    sheet.getRow(1).height = 30
    sheet.getRow(1).eachCell(cell => {
      cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF10243F' } }
      cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
    })
    sheet.eachRow(row => { row.alignment = { vertical: 'middle', wrapText: true } })
  }
  return workbook
}

export async function exportPurchases(kind: PurchaseKind, q: string, status: string, branch: string) {
  const documents = new Map<string, Purchase>()
  let page = 1
  let totalPages = 1
  do {
    const result = await purchases(kind, q, status, page, 100)
    result.items.forEach(doc => documents.set(doc.id, doc))
    totalPages = result.totalPages
    page++
  } while (page <= totalPages)
  if (!documents.size) throw new Error('ไม่มีรายการสำหรับส่งออกตามตัวกรองนี้')
  const workbook = await createPurchaseWorkbook([...documents.values()], branch)
  const buffer = await workbook.xlsx.writeBuffer()
  const url = URL.createObjectURL(new Blob([new Uint8Array(buffer)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `${kind}-${new Date().toISOString().slice(0, 10)}.xlsx`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
