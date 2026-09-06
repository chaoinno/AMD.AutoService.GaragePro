import { useQuery } from '@tanstack/react-query'
import { CircleAlert, LoaderCircle, Package, Plus, Search, TriangleAlert, UserRound, Wrench } from 'lucide-react'
import { useState } from 'react'
import { isApiError } from '../../api/client'
import { searchCatalog } from '../../api/catalog'
import type { CatalogItem, UpsertLineSource } from '../../api/types'
import { Money } from '../../components/Money'
import { formatNumber } from '../../lib/format'
import { useSession } from '../../lib/session'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { ScrollArea } from '../../components/ui/scroll-area'
import { CatalogFormModal } from '../catalog/CatalogPage'

type CatalogPanelProps = {
  readOnly: boolean
  adding: boolean
  onAdd: (item: CatalogItem, source: UpsertLineSource) => void
}

function getCatalogCode(item: CatalogItem) {
  return item.catalogCode || item.code || ''
}

function getPrice(item: CatalogItem) {
  return item.unitPrice ?? item.price ?? 0
}

export function CatalogPanel({ readOnly, adding, onAdd }: CatalogPanelProps) {
  const { session } = useSession()
  const canManageCatalog = Boolean(session?.user.canSeeCost)
  const [queryText, setQueryText] = useState('')
  const [showCreateForm, setShowCreateForm] = useState(false)
  const normalizedQuery = queryText.trim()
  const query = useQuery({
    queryKey: ['catalog', normalizedQuery],
    queryFn: () => searchCatalog(normalizedQuery),
    enabled: normalizedQuery.length >= 2,
  })

  return (
    <Card className="catalog-panel">
      <div className="panel-heading">
        <div>
          <span className="panel-heading__step">01</span>
          <h3>ค้นหาแคตตาล็อก</h3>
        </div>
        <span className="panel-heading__hint">อะไหล่ · ค่าแรง</span>
      </div>
      <label className="field field--search catalog-search">
        <span className="sr-only">ค้นหาแคตตาล็อก</span>
        <Search className="field__search-icon" aria-hidden="true" />
        <Input
          type="search"
          placeholder="รหัสหรือชื่อรายการ"
          value={queryText}
          onChange={(event) => setQueryText(event.target.value)}
          autoComplete="off"
        />
      </label>

      <ScrollArea className="catalog-results" aria-live="polite">
        {normalizedQuery.length < 2 ? (
          <div className="catalog-prompt">
            <span className="catalog-prompt__icon" aria-hidden="true"><Search /></span>
            <strong>ค้นหารายการเพื่อเพิ่ม</strong>
            <p>พิมพ์อย่างน้อย 2 ตัวอักษร ระบบจะแสดงราคาและสต็อกที่ใช้ได้</p>
          </div>
        ) : query.isPending ? (
          <div className="catalog-loading">
            <LoaderCircle className="spin" aria-hidden="true" />
            <strong>กำลังค้นหาแคตตาล็อก</strong>
            <small>ระบบกำลังค้นหาราคาและสต็อก · ยังไม่มี traceId ระหว่างรอการตอบกลับ</small>
            <Button variant="link" onClick={() => setQueryText('')}>ล้างคำค้น</Button>
          </div>
        ) : query.isError ? (
          <div className="inline-error">
            <CircleAlert aria-hidden="true" />
            <strong>{isApiError(query.error) ? query.error.messageTh : 'ค้นหาแคตตาล็อกไม่สำเร็จ'}</strong>
            <small>
              รหัสติดตาม (traceId): {isApiError(query.error) ? query.error.traceId : 'ไม่พบรหัสติดตาม'}
            </small>
            <Button variant="link" onClick={() => void query.refetch()}>
              ลองใหม่
            </Button>
          </div>
        ) : query.data?.length ? (
          query.data.map((item) => {
            const unavailable = item.available <= 0
            return (
              <Card className="catalog-card" key={getCatalogCode(item)}>
                <header>
                  <span className="catalog-card__code">{getCatalogCode(item)}</span>
                  <Badge variant="outline" className={`type-chip type-chip--${item.type}`}>
                    {item.type === 'part' ? <Package aria-hidden="true" /> : <Wrench aria-hidden="true" />}
                    {item.type === 'part' ? 'อะไหล่' : 'ค่าแรง'}
                  </Badge>
                </header>
                <h4>{item.name}</h4>
                <p className="catalog-card__compatibility">
                  {item.compatibility || 'ใช้ได้กับรถในงานนี้'}
                </p>
                <div className="catalog-card__meta">
                  <span>
                    <small>ราคา</small>
                    <strong>
                      <Money value={getPrice(item)} /> บาท
                    </strong>
                  </span>
                  <span>
                    <small>คงเหลือใช้ได้</small>
                    <strong className={unavailable ? 'stock-low' : 'stock-ok'}>
                      {formatNumber(item.available)} {item.unit}
                    </strong>
                  </span>
                </div>
                {unavailable ? (
                  <div className="stock-warning">
                    <Badge variant="warning" className="stock-warning__badge">
                      <TriangleAlert aria-hidden="true" /> ของไม่พอ
                    </Badge>
                    <span>{item.etaNote || 'ยังไม่มีกำหนดของเข้า'}</span>
                  </div>
                ) : null}
                <div className="catalog-card__actions">
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={readOnly || adding}
                    onClick={() => onAdd(item, 'Customer')}
                  >
                    <UserRound aria-hidden="true" /> ลูกค้าขอ
                  </Button>
                  <Button
                    variant="soft"
                    size="sm"
                    disabled={readOnly || adding}
                    onClick={() => onAdd(item, 'Technician')}
                  >
                    <Wrench aria-hidden="true" /> ช่างแนะนำ
                  </Button>
                </div>
              </Card>
            )
          })
        ) : (
          <div className="inline-empty">
            <strong>ไม่พบรายการในแคตตาล็อก</strong>
            <span>ลองค้นด้วยรหัสหรือคำที่สั้นลง</span>
            <small>รหัสติดตาม (traceId): คำขอนี้สำเร็จและไม่พบรายการ</small>
            <Button variant="link" onClick={() => setQueryText('')}>ล้างคำค้น</Button>
            {canManageCatalog ? (
              <Button
                variant="outline"
                size="sm"
                disabled={readOnly}
                onClick={() => setShowCreateForm(true)}
              >
                <Plus aria-hidden="true" /> เพิ่มรายการใหม่
              </Button>
            ) : null}
          </div>
        )}
      </ScrollArea>

      <CatalogFormModal
        open={showCreateForm}
        itemId={null}
        initialName={normalizedQuery}
        onClose={() => setShowCreateForm(false)}
      />
    </Card>
  )
}
