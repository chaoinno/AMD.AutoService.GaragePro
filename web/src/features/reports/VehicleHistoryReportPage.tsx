import { useQuery } from '@tanstack/react-query'
import { CalendarClock, CarFront, Gauge, History, ReceiptText, Search } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import {
  getVehicleHistory,
  searchVehicleHistory,
  type VehicleHistory,
  type VehicleHistoryVisit,
} from '../../api/reports'
import type { JobStatusToken } from '../../api/types'
import { AppShell } from '../../components/AppShell'
import { JobStatusChip } from '../../components/JobStatusChip'
import { ManagementTable } from '../../components/ManagementTable'
import { Money } from '../../components/Money'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { formatDateTime, formatKm, formatNumber } from '../../lib/format'
import { QueryState } from '../master-data/MasterDataCommon'
import { nextServiceText } from '../jobs/HandoverServiceInfoSection'
import './reports.css'

const MIN_TERM = 3
const normalize = (value: string) => value.replace(/[\s-]/g, '')

/// [BIZ] เพิ่ม 2026-10-08 — ค้นประวัติรถจากทะเบียน/เบอร์โทร เปิดทุกบทบาท (หน้าร้าน/ช่างใช้ตอนลูกค้ามาที่เคาน์เตอร์)
/// ช่าง/หัวหน้าช่างไม่เห็นตัวเงิน (server ตัดยอดเงินออกแล้ว) · คำค้น/รถที่เลือกอยู่ใน URL ให้กดย้อนกลับ/ส่งลิงก์ต่อได้
export function VehicleHistoryReportPage() {
  const [params, setParams] = useSearchParams()
  const q = params.get('q') ?? ''
  const vehicleId = Number(params.get('vehicle')) || null
  const [draft, setDraft] = useState(q)
  const termTooShort = normalize(draft).length < MIN_TERM

  const searchQuery = useQuery({
    queryKey: ['reports', 'vehicle-history', 'search', q],
    queryFn: () => searchVehicleHistory(q),
    enabled: normalize(q).length >= MIN_TERM,
  })

  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (termTooShort) return
    setParams({ q: draft.trim() })
  }

  return (
    <AppShell title="ประวัติรถ">
      <section className="report-page-heading">
        <div>
          <h2><History aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />ประวัติรถ</h2>
          <p>ค้นจากทะเบียนรถหรือเบอร์โทรลูกค้า · แสดงเฉพาะงานที่เปิดในระบบนี้ของสาขาปัจจุบัน (ตั้งแต่เริ่มใช้ระบบ) ไม่รวมประวัติจากระบบเดิม</p>
        </div>
        <form className="vehicle-history-search" onSubmit={submit} role="search">
          <Input
            aria-label="ทะเบียนรถหรือเบอร์โทร"
            placeholder="ทะเบียนรถ หรือ เบอร์โทร"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
          />
          <Button type="submit" disabled={termTooShort} title={termTooShort ? `พิมพ์อย่างน้อย ${MIN_TERM} ตัวอักษร` : undefined}>
            <Search aria-hidden="true" /> ค้นหา
          </Button>
        </form>
      </section>

      {vehicleId ? (
        <VehicleHistoryPanel vehicleId={vehicleId} onBack={q ? () => setParams({ q }) : undefined} />
      ) : !q ? (
        <p className="purchase-empty">พิมพ์ทะเบียนรถหรือเบอร์โทรอย่างน้อย {MIN_TERM} ตัวอักษร แล้วกดค้นหา (ไม่ต้องพิมพ์ขีด/ช่องว่าง)</p>
      ) : (
        <QueryState
          query={searchQuery.data ? { ...searchQuery, data: searchQuery.data.items } : searchQuery}
          loadingTitle="กำลังค้นหารถ"
          emptyTitle="ไม่พบรถที่ตรงกับคำค้น"
          emptyReason={`ไม่มีงานในสาขานี้ที่ทะเบียนหรือเบอร์โทรมี "${q}" — ลองพิมพ์เฉพาะตัวเลขของทะเบียน หรือเบอร์โทร 4 ตัวท้าย`}
          onRetry={() => void searchQuery.refetch()}
        >
          {searchQuery.data ? (
            <Card className="management-table-card">
              {searchQuery.data.truncated ? (
                <p className="section-help report-table-note">ผลลัพธ์มากเกินไป แสดงเฉพาะรถที่เข้าล่าสุด — พิมพ์ให้เจาะจงขึ้นเพื่อดูให้ครบ</p>
              ) : null}
              <ManagementTable data={searchQuery.data.items} sortScope="loaded" columns={[
                { id: 'plate', header: 'ทะเบียน', size: 140, value: (v) => v.vehicleRegistration, render: (v) => <strong>{v.vehicleRegistration}</strong> },
                { id: 'model', header: 'รุ่น', size: 180, value: (v) => v.vehicleModel ?? '', render: (v) => v.vehicleModel || '—' },
                { id: 'customer', header: 'ลูกค้า', size: 200, value: (v) => v.customerName, render: (v) => <>{v.customerName}<small className="purchase-sub">{v.customerPhone ?? ''}</small></> },
                { id: 'visits', header: 'จำนวนครั้ง', size: 110, value: (v) => v.visitCount, render: (v) => <span className="report-num">{formatNumber(v.visitCount)}</span> },
                { id: 'last', header: 'เข้าล่าสุด', size: 190, value: (v) => new Date(v.lastVisitAt).getTime(), render: (v) => <>{formatDateTime(v.lastVisitAt)}<small className="purchase-sub">{v.lastJobNo}</small></> },
                { id: 'action', header: '', size: 120, render: (v) => (
                  <Button variant="outline" size="sm" onClick={() => setParams({ q, vehicle: String(v.vehicleId) })}>ดูประวัติ</Button>
                ) },
              ]} />
            </Card>
          ) : null}
        </QueryState>
      )}
    </AppShell>
  )
}

function VehicleHistoryPanel({ vehicleId, onBack }: { vehicleId: number; onBack?: () => void }) {
  const query = useQuery({
    queryKey: ['reports', 'vehicle-history', vehicleId],
    queryFn: () => getVehicleHistory(vehicleId),
  })

  return (
    <>
      {onBack ? <Button variant="ghost" size="sm" onClick={onBack}>← กลับไปผลการค้นหา</Button> : null}
      <QueryState
        query={query}
        loadingTitle="กำลังโหลดประวัติรถ"
        emptyTitle="ไม่พบประวัติ"
        emptyReason="ไม่มีงานของรถคันนี้ในสาขานี้"
        onRetry={() => void query.refetch()}
      >
        {query.data ? <HistoryBody history={query.data} /> : null}
      </QueryState>
    </>
  )
}

function HistoryBody({ history }: { history: VehicleHistory }) {
  return (
    <>
      <Card className="vehicle-history-head">
        <div>
          <h3><CarFront aria-hidden="true" /> {history.vehicleRegistration}</h3>
          <p>{history.vehicleModel || 'ไม่ระบุรุ่น'}{history.vehicleVin ? ` · เลขตัวถัง ${history.vehicleVin}` : ''}</p>
          <p>{history.customerName}{history.customerPhone ? ` · ${history.customerPhone}` : ''}</p>
          <p className="section-help">เข้ารับบริการ {formatNumber(history.visits.length)} ครั้ง (ข้อมูลลูกค้าจากงานล่าสุด)</p>
        </div>
        <div className="vehicle-history-next">
          <span><CalendarClock aria-hidden="true" /> นัดเข้ารับบริการครั้งถัดไป</span>
          {history.nextService ? (
            <>
              <strong>{nextServiceText(history.nextService.mileage, history.nextService.dueOn)}</strong>
              <small>จากการส่งมอบงาน {history.nextService.fromJobNo}</small>
            </>
          ) : (
            <strong>ยังไม่มีนัด (ไม่มีการส่งมอบที่บันทึกนัดไว้)</strong>
          )}
        </div>
      </Card>

      {!history.showAmounts ? (
        <p className="section-help">บทบาทของคุณไม่เห็นยอดเงิน — แสดงเฉพาะรายการงานที่ทำ</p>
      ) : null}

      <ol className="vehicle-history-timeline">
        {history.visits.map((visit) => (
          <li key={visit.jobId}><VisitCard visit={visit} showAmounts={history.showAmounts} /></li>
        ))}
      </ol>
    </>
  )
}

function VisitCard({ visit, showAmounts }: { visit: VehicleHistoryVisit; showAmounts: boolean }) {
  return (
    <Card className="vehicle-history-visit">
      <header>
        <div>
          <strong>{formatDateTime(visit.openedAt)}</strong>
          <Link className="report-link" to={`/jobs?job=${visit.jobId}`}>{visit.jobNo}</Link>
          {visit.jobTypeName ? <span className="job-type-chip">{visit.jobTypeName}</span> : null}
        </div>
        <JobStatusChip status={visit.status as JobStatusToken} label={visit.statusLabelTh} />
      </header>

      {visit.detail ? <p className="vehicle-history-visit__detail">อาการที่แจ้ง: {visit.detail}</p> : null}

      <dl className="vehicle-history-visit__facts">
        <div><dt><Gauge aria-hidden="true" /> ไมล์รับรถ</dt><dd className="report-num">{formatKm(visit.mileageAtIntake)}</dd></div>
        <div><dt><Gauge aria-hidden="true" /> ไมล์ส่งมอบ</dt><dd className="report-num">{visit.mileageAtHandover != null ? formatKm(visit.mileageAtHandover) : 'ยังไม่ส่งมอบ'}</dd></div>
        {visit.handedOverAt ? <div><dt>ส่งมอบเมื่อ</dt><dd>{formatDateTime(visit.handedOverAt)}</dd></div> : null}
        {visit.nextServiceDueOn ? (
          <div><dt><CalendarClock aria-hidden="true" /> นัดครั้งถัดไป</dt><dd>{nextServiceText(visit.nextServiceMileage, visit.nextServiceDueOn)}</dd></div>
        ) : null}
        {visit.receiptDocumentNo ? (
          <div>
            <dt><ReceiptText aria-hidden="true" /> ใบเสร็จ</dt>
            <dd>{visit.receiptDocumentNo}{showAmounts && visit.receiptTotal != null ? <> · <Money value={visit.receiptTotal} /> บาท</> : null}</dd>
          </div>
        ) : null}
      </dl>

      {visit.lines.length === 0 ? (
        <p className="purchase-empty">ยังไม่มีรายการซ่อมที่ลูกค้าอนุมัติ</p>
      ) : (
        <table className="vehicle-history-lines">
          <thead>
            <tr>
              <th>รายการที่ทำ</th>
              <th>ประเภท</th>
              <th>จำนวน</th>
              {showAmounts ? <th>ยอด (ก่อน VAT)</th> : null}
              <th>ใบเสนอราคา</th>
            </tr>
          </thead>
          <tbody>
            {visit.lines.map((line, i) => (
              <tr key={`${line.quotationCode}-${i}`}>
                <td>{line.name}{line.technicianName ? <small className="purchase-sub">ช่าง: {line.technicianName}</small> : null}</td>
                <td>{line.type === 'part' ? 'อะไหล่' : 'ค่าแรง'}</td>
                <td className="report-num">{formatNumber(line.quantity)} {line.unit}</td>
                {showAmounts ? <td><Money value={line.amount ?? 0} /></td> : null}
                <td className="report-num">{line.quotationCode}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Card>
  )
}
