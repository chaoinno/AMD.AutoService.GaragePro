import { AlertTriangle } from 'lucide-react'
import { useState } from 'react'
import { useSearchParams } from 'react-router'
import type { TaxReportParty } from '../../../api/taxReports'
import { Input } from '../../../components/ui/input'
import { branchLabel, formatTaxId } from '../../jobs/billing/BillingDocument'

/** เดือนภาษีเก็บใน URL (?month=2026-11) — ไม่ระบุ = เดือนปัจจุบันตามเครื่อง */
export function useTaxMonth(): [number, number, (year: number, month: number) => void] {
  const [params, setParams] = useSearchParams()
  const [fallback] = useState(() => {
    const now = new Date()
    return [now.getFullYear(), now.getMonth() + 1] as const
  })
  const match = /^(\d{4})-(\d{2})$/.exec(params.get('month') ?? '')
  const year = match ? Number(match[1]) : fallback[0]
  const month = match && Number(match[2]) >= 1 && Number(match[2]) <= 12 ? Number(match[2]) : fallback[1]
  const set = (y: number, m: number) => {
    const next = new URLSearchParams(params)
    next.set('month', `${y}-${String(m).padStart(2, '0')}`)
    setParams(next, { replace: true })
  }
  return [year, month, set]
}

export function TaxMonthInput({ label, year, month, onChange }: {
  label: string
  year: number
  month: number
  onChange: (year: number, month: number) => void
}) {
  return (
    <div className="report-date-filter">
      <label>
        {label}
        <Input
          type="month"
          value={`${year}-${String(month).padStart(2, '0')}`}
          onChange={(e) => {
            const m = /^(\d{4})-(\d{2})$/.exec(e.target.value)
            if (m) onChange(Number(m[1]), Number(m[2]))
          }}
        />
      </label>
    </div>
  )
}

/** หัวรายงานตามแบบรายงานภาษี: ชื่อผู้ประกอบการ · เลขผู้เสียภาษี · สถานประกอบการ — ข้อมูลไม่ครบบอกให้แก้ก่อนยื่น */
export function TaxReportHeader({ party, periodLabel }: { party?: TaxReportParty; periodLabel: string }) {
  if (!party) {
    return (
      <p className="tax-report-warning">
        <AlertTriangle aria-hidden="true" /> ไม่พบข้อมูลสาขาสำหรับหัวรายงาน — ติดต่อผู้ดูแลระบบ
      </p>
    )
  }
  return (
    <dl className="tax-report-header">
      <div><dt>ชื่อผู้ประกอบการ</dt><dd>{party.name}</dd></div>
      <div>
        <dt>เลขประจำตัวผู้เสียภาษี</dt>
        <dd>
          {party.taxId ? <span className="report-num">{formatTaxId(party.taxId)}</span> : (
            <span className="tax-report-warning"><AlertTriangle aria-hidden="true" /> ยังไม่มีเลข 13 หลักในข้อมูลสาขา</span>
          )}
        </dd>
      </div>
      <div><dt>สถานประกอบการ</dt><dd>{branchLabel(party.branchNo)}</dd></div>
      <div><dt>เดือนภาษี</dt><dd>{periodLabel}</dd></div>
      {party.address ? <div className="tax-report-header__wide"><dt>ที่อยู่</dt><dd>{party.address}</dd></div> : null}
    </dl>
  )
}

export function partyBranch(branchNo?: string | null) {
  return branchLabel(branchNo ?? null) ?? '-'
}

export function partyTaxId(taxId?: string | null) {
  return taxId ? formatTaxId(taxId) : '-'
}

/** ส่วนท้ายของการส่งออก Excel — หัวตารางสีกรมท่า · ตรึงแถวแรก · ตัวกรอง (แบบเดียวกับรายงานอื่น) */
export function styleSheet(sheet: import('exceljs').Worksheet, headerRow = 1) {
  sheet.views = [{ state: 'frozen', ySplit: headerRow }]
  sheet.getRow(headerRow).height = 30
  sheet.getRow(headerRow).eachCell((cell) => {
    cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF10243F' } }
    cell.font = { bold: true, color: { argb: 'FFFFFFFF' } }
  })
  sheet.eachRow((row) => { row.alignment = { vertical: 'middle', wrapText: true } })
}

export async function downloadWorkbook(workbook: import('exceljs').Workbook, fileName: string) {
  const buffer = await workbook.xlsx.writeBuffer()
  const url = URL.createObjectURL(new Blob([new Uint8Array(buffer)], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }))
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
