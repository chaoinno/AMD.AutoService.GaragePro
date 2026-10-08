import type { ServiceDueItem } from '../../api/reports'
import { formatDateOnly, formatDateTime } from '../../lib/format'

/// รายชื่อโทรตามรถใกล้ครบรอบบริการ — ไม่มีตัวเงิน ใช้รูปแบบหัวตารางเดียวกับ inventoryExport.ts
export async function exportServiceDue(items: ServiceDueItem[], fromDate: string, toDate: string) {
  if (!items.length) throw new Error('ไม่มีรายการสำหรับส่งออก')
  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'ServicePro'
  const sheet = workbook.addWorksheet('รถใกล้ครบรอบบริการ')
  const columns: [string, number][] = [
    ['ทะเบียน', 16], ['รุ่น', 24], ['ลูกค้า', 26], ['เบอร์โทร', 16], ['เข้าครั้งล่าสุด (จ๊อบ)', 22],
    ['ส่งมอบเมื่อ', 22], ['ไมล์ตอนส่งมอบ (กม.)', 18], ['นัดที่ไมล์ (กม.)', 18], ['วันนัด', 16], ['เหลือ (วัน)', 12],
  ]
  sheet.columns = columns.map(([header, width]) => ({ header, width }))
  for (const x of items) {
    sheet.addRow([
      x.vehicleRegistration, x.vehicleModel ?? '', x.customerName, x.customerPhone ?? '', x.lastJobNo,
      formatDateTime(x.handedOverAt), x.mileageAtHandover ?? null, x.nextServiceMileage ?? null,
      formatDateOnly(x.nextServiceDueOn), x.daysUntilDue,
    ])
  }
  sheet.getColumn(7).numFmt = '#,##0'
  sheet.getColumn(8).numFmt = '#,##0'
  sheet.views = [{ state: 'frozen', ySplit: 1 }]
  sheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: sheet.rowCount, column: sheet.columnCount } }
  sheet.getRow(1).height = 30
  sheet.getRow(1).eachCell(cell => {
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF10243F' } }
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
  })
  sheet.eachRow(row => { row.alignment = { vertical: 'middle', wrapText: true } })

  const buffer = await workbook.xlsx.writeBuffer()
  const url = URL.createObjectURL(new Blob([new Uint8Array(buffer)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `service-due-${fromDate}-to-${toDate}.xlsx`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
