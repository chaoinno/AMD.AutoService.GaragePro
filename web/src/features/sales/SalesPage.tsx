import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { FileEdit, LoaderCircle, Plus, RefreshCw, Search, ShoppingCart } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { isApiError, isForbiddenError } from '../../api/client'
import { getWarehouses } from '../../api/masterData'
import { countDraftSales, createSale, sales, type Sale, type SaleStatus } from '../../api/sales'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { Money } from '../../components/Money'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { formatDateTime } from '../../lib/format'
import { useSession } from '../../lib/session'
import { SaleDetail } from './SaleDetail'
import { SaleEditor } from './SaleEditor'
import { canUseRetailSale, ErrorAlert, SALE_STATUS_OPTIONS, SaleStatusChip } from './saleFormat'
import { useSaleQuery } from './useSale'
import './sales.css'

export function SalesPage() {
  const { id } = useParams()
  return id ? <SaleRoute id={id} /> : <SaleList />
}

/// บิลร่างเปิด editor · บิลที่ปิดแล้ว/ยกเลิกแล้วเปิดหน้าดูใบเสร็จ — แยกกันเพราะงานบนหน้าจอคนละแบบ
function SaleRoute({ id }: { id: string }) {
  const navigate = useNavigate()
  const query = useSaleQuery(id)

  if (query.isPending) {
    return (
      <AppShell title="ขายสินค้า (POS)">
        <StateBlock variant="loading" title="กำลังเปิดบิลขาย" reason="กำลังโหลดรายการและยอดล่าสุดจากระบบ" actionLabel="กลับรายการบิล" onAction={() => navigate('/sales')}>
          <SkeletonRows count={4} />
        </StateBlock>
      </AppShell>
    )
  }
  if (query.isError) {
    const forbidden = isForbiddenError(query.error)
    return (
      <AppShell title="ขายสินค้า (POS)">
        <StateBlock
          variant={forbidden ? 'forbidden' : 'error'}
          title={forbidden ? 'ไม่มีสิทธิ์เปิดบิลขาย' : 'เปิดบิลขายไม่สำเร็จ'}
          reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
          traceId={isApiError(query.error) ? query.error.traceId : undefined}
          actionLabel={forbidden ? 'กลับรายการบิล' : 'ลองใหม่'}
          onAction={() => (forbidden ? navigate('/sales') : void query.refetch())}
        />
      </AppShell>
    )
  }
  return query.data.status === 'draft' ? <SaleEditor sale={query.data} /> : <SaleDetail sale={query.data} />
}

type DateRange = 'today' | '7d' | '30d' | 'all'
const DATE_RANGE_OPTIONS: { value: DateRange; label: string }[] = [
  { value: 'today', label: 'วันนี้' },
  { value: '7d', label: '7 วันล่าสุด' },
  { value: '30d', label: '30 วันล่าสุด' },
  { value: 'all', label: 'ทุกช่วงเวลา' },
]

function rangeStart(range: DateRange): string | undefined {
  if (range === 'all') return undefined
  const start = new Date()
  start.setHours(0, 0, 0, 0)
  if (range === '7d') start.setDate(start.getDate() - 6)
  if (range === '30d') start.setDate(start.getDate() - 29)
  return start.toISOString()
}

function SaleList() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { session } = useSession()
  const canSell = canUseRetailSale(session?.user.role)
  const [q, setQ] = useState('')
  const [status, setStatus] = useState<SaleStatus | ''>('')
  const [range, setRange] = useState<DateRange>('today')
  const from = useMemo(() => rangeStart(range), [range])

  const query = useQuery({
    queryKey: ['sales', q.trim(), status, from ?? 'all'],
    queryFn: () => sales({ q: q.trim() || undefined, status: status || undefined, from, page: 1, pageSize: 100 }),
    enabled: canSell,
  })
  const draftCount = useQuery({ queryKey: ['sale-draft-count'], queryFn: countDraftSales, enabled: canSell })

  const create = useMutation({
    mutationFn: async () => {
      const warehouses = await getWarehouses()
      const warehouse = warehouses.find((w) => w.isActive) ?? warehouses[0]
      if (!warehouse) throw new Error('ยังไม่มีคลังสินค้าที่เปิดใช้งานในสาขานี้ — เพิ่มคลังที่เมนู "คลัง" ก่อน')
      return createSale({ warehouseId: warehouse.id, legacyCustomerId: null, customerName: null, customerPhone: null, vatIncluded: true })
    },
    onSuccess: (sale) => {
      queryClient.setQueryData(['sale', sale.id], sale)
      void queryClient.invalidateQueries({ queryKey: ['sale-draft-count'] })
      navigate(`/sales/${sale.id}`)
    },
  })

  const items = query.data?.items ?? []
  const shownDrafts = items.filter((sale) => sale.status === 'draft').length
  const hiddenDrafts = Math.max(0, (draftCount.data ?? 0) - shownDrafts)

  return (
    <AppShell title="ขายสินค้า (POS)">
      <section className="sale-page-heading">
        <span className="sale-page-heading__icon" aria-hidden="true"><ShoppingCart /></span>
        <div>
          <h2>ขายสินค้าหน้าร้าน</h2>
          <p>ขายอะไหล่/สินค้าให้ลูกค้าที่ไม่มีรถเข้าซ่อม · ตัดสต็อก FIFO และออกใบเสร็จ SL- ตอนชำระเงินเท่านั้น</p>
        </div>
        <div className="sale-page-heading__actions">
          <Button variant="outline" onClick={() => void query.refetch()} disabled={!canSell || query.isFetching}>
            <RefreshCw aria-hidden="true" className={query.isFetching ? 'spin' : undefined} /> โหลดใหม่
          </Button>
          <Button onClick={() => create.mutate()} disabled={!canSell || create.isPending} title={!canSell ? 'เฉพาะแคชเชียร์ ธุรการ หรือผู้จัดการ' : undefined}>
            {create.isPending ? <LoaderCircle className="spin" aria-hidden="true" /> : <Plus aria-hidden="true" />}
            {create.isPending ? 'กำลังเปิดบิล…' : 'ขายใหม่'}
          </Button>
        </div>
      </section>

      {!canSell ? (
        <StateBlock
          variant="forbidden"
          title="บทบาทนี้ใช้การขายหน้าร้านไม่ได้"
          reason="การขายหน้าร้านเปิดให้แคชเชียร์ ธุรการ และผู้จัดการเท่านั้น"
          actionLabel="ไปหน้าจ๊อบ"
          onAction={() => navigate('/jobs')}
        />
      ) : (
        <>
          {create.isError ? <ErrorAlert title="เปิดบิลใหม่ไม่สำเร็จ" error={create.error} /> : null}

          <Card className="sale-filters">
            <div className="input-with-icon">
              <Search aria-hidden="true" />
              <Input aria-label="ค้นหาบิลขาย" placeholder="ค้นหาเลขที่ใบเสร็จ SL- / ชื่อลูกค้า" value={q} onChange={(e) => setQ(e.target.value)} />
            </div>
            <Select aria-label="สถานะบิล" value={status} onChange={(e) => setStatus(e.target.value as SaleStatus | '')}>
              <option value="">ทุกสถานะ</option>
              {SALE_STATUS_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
            </Select>
            <Select aria-label="ช่วงวันที่" value={range} onChange={(e) => setRange(e.target.value as DateRange)}>
              {DATE_RANGE_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
            </Select>
          </Card>

          {hiddenDrafts > 0 ? (
            <div className="sale-draft-banner" role="status">
              <FileEdit aria-hidden="true" />
              <span>มีบิลร่างค้างอยู่อีก <strong>{hiddenDrafts}</strong> บิลนอกช่วงวันที่/ตัวกรองนี้</span>
              <Button size="sm" variant="outline" onClick={() => { setStatus('draft'); setRange('all'); setQ('') }}>ดูบิลร่างทั้งหมด</Button>
            </div>
          ) : null}

          {query.isError ? (
            <StateBlock
              variant={isForbiddenError(query.error) ? 'forbidden' : 'error'}
              title="โหลดรายการบิลไม่สำเร็จ"
              reason={isApiError(query.error) ? query.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
              traceId={isApiError(query.error) ? query.error.traceId : undefined}
              actionLabel="ลองใหม่"
              onAction={() => void query.refetch()}
            />
          ) : query.isPending ? (
            <Card className="management-table-card"><SkeletonRows count={6} /></Card>
          ) : items.length === 0 ? (
            <StateBlock
              variant="empty"
              title="ยังไม่มีบิลในช่วงนี้"
              reason={q.trim() || status ? 'ไม่พบบิลที่ตรงกับคำค้นหา/ตัวกรอง ลองขยายช่วงวันที่หรือล้างตัวกรอง' : 'กด "ขายใหม่" เพื่อเปิดบิลแรกของวัน'}
              actionLabel={q.trim() || status || range !== 'all' ? 'ดูทุกช่วงเวลา' : 'ขายใหม่'}
              onAction={() => (q.trim() || status || range !== 'all' ? (setQ(''), setStatus(''), setRange('all')) : create.mutate())}
            />
          ) : (
            <Card className="management-table-card">
              <ManagementTable<Sale>
                data={items}
                sortScope="loaded"
                columns={[
                  { id: 'receipt', header: 'เลขที่', value: (x) => x.receiptNo || '', render: (x) => (
                    <button type="button" className="sale-link" onClick={() => navigate(`/sales/${x.id}`)}>
                      {x.receiptNo || 'ฉบับร่าง'}
                    </button>
                  ) },
                  { id: 'date', header: 'วันเวลา', value: (x) => new Date(x.completedAt || x.createdAt).getTime(), render: (x) => formatDateTime(x.completedAt || x.createdAt) },
                  { id: 'customer', header: 'ลูกค้า', value: (x) => x.customerName || '', render: (x) => x.customerName || <span className="sale-muted">ลูกค้าทั่วไป</span> },
                  { id: 'lines', header: 'รายการ', size: 100, value: (x) => x.lines.length, render: (x) => `${x.lines.length} รายการ` },
                  { id: 'total', header: 'ยอดรวม', size: 140, value: (x) => x.totalAmount, render: (x) => <Money value={x.totalAmount} /> },
                  { id: 'status', header: 'สถานะ', size: 150, value: (x) => x.status, render: (x) => <SaleStatusChip status={x.status} /> },
                ]}
              />
              {(query.data?.total ?? 0) > items.length ? (
                <p className="section-help sale-table-note">แสดง {items.length} จาก {query.data?.total} บิลล่าสุด — ใช้ตัวกรองเพื่อหาบิลที่เก่ากว่า · การเรียงคอลัมน์เรียงเฉพาะรายการที่แสดงอยู่</p>
              ) : null}
            </Card>
          )}
        </>
      )}
    </AppShell>
  )
}
