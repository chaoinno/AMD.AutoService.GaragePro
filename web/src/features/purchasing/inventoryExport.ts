import type { StockItem } from '../../api/purchasing'

export async function createInventoryWorkbook(items: StockItem[], canSeeCost: boolean) {
  const { default: ExcelJS } = await import('exceljs')
  const workbook = new ExcelJS.Workbook()
  workbook.creator = 'GaragePro'
  const sheet = workbook.addWorksheet('สต็อก FIFO')
  const columns: [string, number][] = [['รหัสสินค้า', 22], ['สินค้า', 40], ['หน่วย', 14], ['การใช้ FIFO', 24], ['คงเหลือ', 16], ['จอง', 16], ['พร้อมใช้', 16], ['รอรับ', 16], ['ชำรุด', 16]]
  if (canSeeCost) columns.push(['มูลค่า FIFO (บาท)', 24])
  sheet.columns = columns.map(([header, width]) => ({ header, width }))
  for (const item of items) {
    const values: (string | number | null)[] = [item.code, item.name, item.unit, item.stockManaged ? 'ใช้ FIFO แล้ว' : 'รอตั้งยอดยกมา', item.onHand, item.reserved, item.available, item.onOrder, item.damaged]
    if (canSeeCost) values.push(item.value)
    sheet.addRow(values)
  }
  for (let i = 5; i <= sheet.columnCount; i++) sheet.getColumn(i).numFmt = '#,##0.00'
  sheet.views = [{ state: 'frozen', ySplit: 1 }]
  sheet.autoFilter = { from: { row: 1, column: 1 }, to: { row: sheet.rowCount, column: sheet.columnCount } }
  sheet.getRow(1).height = 30
  sheet.getRow(1).eachCell(cell => {
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF10243F' } }
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
  })
  sheet.eachRow(row => { row.alignment = { vertical: 'middle', wrapText: true } })
  return workbook
}

export async function exportInventory(items: StockItem[], canSeeCost: boolean) {
  if (!items.length) throw new Error('ไม่มีรายการสำหรับส่งออก')
  const workbook = await createInventoryWorkbook(items, canSeeCost)
  const buffer = await workbook.xlsx.writeBuffer()
  const url = URL.createObjectURL(new Blob([new Uint8Array(buffer)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  const link = document.createElement('a')
  link.href = url
  link.download = `FIFO-${new Date().toISOString().slice(0, 10)}.xlsx`
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
