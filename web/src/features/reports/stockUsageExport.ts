import type { StockUsageReport } from '../../api/reports'
import { formatDateTime } from '../../lib/format'

const SOURCE: Record<string, string> = { job: 'เบิกเข้างาน', manual: 'เบิกหน้าสต็อก', pos: 'ขายหน้าร้าน (POS)' }
const KIND: Record<string, string> = { 'job-issue': 'เบิกเข้างาน', 'manual-issue': 'เบิกหน้าสต็อก', 'job-return': 'คืนจากงาน', 'pos-sale': 'ขาย POS', 'pos-void': 'ยกเลิกบิลขาย' }

/// ส่งออกตามมุมมองที่เปิดอยู่ — ต้นทุนใส่เฉพาะเมื่อ server ส่งมา (role ที่ไม่เห็นต้นทุนไม่มีคอลัมน์นี้) · รูปแบบหัวตารางเดียวกับ serviceDueExport.ts
export async function exportStockUsage(report: StockUsageReport) {
  const showCost = report.netCost != null
  const header: [string, number][] = []
  const rows: unknown[][] = []
  let name = 'การเบิกอะไหล่'
  if (report.documents) {
    name = 'รายใบเบิก'
    header.push(['วันที่', 22], ['เลขที่เอกสาร', 22], ['แหล่งที่มา', 18], ['เลขจ๊อบ', 18], ['ทะเบียน', 16], ['ผู้เบิก', 22], ['ผู้จ่าย', 22], ['รายการ', 10], ['เบิก', 10], ['คืน', 10], ['สุทธิ', 10], ['ใบคืน', 24])
    if (showCost) header.push(['ต้นทุนสุทธิ', 16])
    for (const d of report.documents) rows.push([formatDateTime(d.occurredAt), d.documentNumber, SOURCE[d.source], d.jobNo ?? '', d.vehicleRegistration ?? '', d.requesterName ?? '', d.performedByName, d.lineCount, d.issuedQuantity, d.returnedQuantity, d.netQuantity, d.returns.map(r => r.documentNumber).join(', '), ...(showCost ? [d.netCost ?? 0] : [])])
  } else if (report.items) {
    name = 'สรุปตามสินค้า'
    header.push(['รหัส', 16], ['สินค้า', 30], ['หน่วย', 10], ['เบิกเข้างาน', 12], ['เบิกหน้าสต็อก', 14], ['ขาย POS', 10], ['คืน', 10], ['ยกเลิกบิล', 12], ['สุทธิ', 10])
    if (showCost) header.push(['ต้นทุนสุทธิ', 16])
    header.push(['วันที่ (รายละเอียด)', 22], ['รายการ', 16], ['เลขเอกสาร', 22], ['เลขจ๊อบ / ใบเสร็จ', 22], ['ทะเบียน', 16], ['จำนวน', 10])
    for (const i of report.items) {
      const head = [i.code, i.name, i.unit, i.jobQuantity, i.manualQuantity, i.posQuantity, i.returnedQuantity, i.posVoidQuantity, i.netQuantity, ...(showCost ? [i.netCost ?? 0] : [])]
      if (!i.details.length) rows.push(head)
      for (const d of i.details) rows.push([...head, formatDateTime(d.occurredAt), KIND[d.kind], d.documentNumber, d.jobNo ?? d.receiptNo ?? '', d.vehicleRegistration ?? '', d.quantity])
    }
  } else if (report.requesters) {
    name = 'สรุปตามช่างผู้เบิก'
    header.push(['ช่างผู้เบิก', 26], ['จำนวนใบเบิก', 14], ['จำนวนงาน', 12], ['เบิก (ชิ้น)', 12], ['คืน (ชิ้น)', 12], ['สุทธิ (ชิ้น)', 12])
    if (showCost) header.push(['ต้นทุนสุทธิ', 16])
    for (const r of report.requesters) rows.push([r.requesterName, r.documentCount, r.jobCount, r.issuedQuantity, r.returnedQuantity, r.netQuantity, ...(showCost ? [r.netCost ?? 0] : [])])
  }
  if (!rows.length) throw new Error('ไม่มีรายการสำหรับส่งออก')

  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'ServicePro'
  const sheet = workbook.addWorksheet(name)
  sheet.columns = header.map(([h, width]) => ({ header: h, width }))
  rows.forEach(row => sheet.addRow(row))
  sheet.views = [{ state: 'frozen', ySplit: 1 }]
  sheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: sheet.rowCount, column: sheet.columnCount } }
  sheet.getRow(1).height = 30
  sheet.getRow(1).eachCell(cell => {
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF10243F' } }
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
  })
  sheet.eachRow(row => { row.alignment = { vertical: 'middle', wrapText: true } })
  header.forEach(([h], index) => { if (h.startsWith('ต้นทุน')) sheet.getColumn(index + 1).numFmt = '#,##0.00' })

  const buffer = await workbook.xlsx.writeBuffer()
  const url = URL.createObjectURL(new Blob([new Uint8Array(buffer)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `stock-usage-${report.view}-${report.fromDate}-to-${report.toDate}.xlsx`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
