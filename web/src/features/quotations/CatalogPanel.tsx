import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  CircleAlert,
  FileStack,
  LoaderCircle,
  Package,
  Plus,
  Search,
  TriangleAlert,
  UserRound,
  WandSparkles,
  Wrench,
  X,
} from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { createCatalogItem, searchCatalog } from '../../api/catalog'
import { applyQuotationTemplate } from '../../api/quotations'
import type { CatalogItem, CatalogItemInput, Quotation, UpsertLine, UpsertLineSource } from '../../api/types'
import { Money } from '../../components/Money'
import { MoneyInput } from '../../components/MoneyInput'
import { formatNumber } from '../../lib/format'
import { useSession } from '../../lib/session'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { ScrollArea } from '../../components/ui/scroll-area'
import { QuotationTemplatePickerModal } from './QuotationTemplatePickerModal'

type CatalogPanelProps = {
  readOnly: boolean
  adding: boolean
  onAdd: (item: CatalogItem, source: UpsertLineSource) => void
  onAddAdHoc: (payload: UpsertLine) => void
  addingAdHoc: boolean
  quotationId: string
  onApplied: (quotation: Quotation) => void
}

function getCatalogCode(item: CatalogItem) {
  return item.catalogCode || item.code || ''
}

function getPrice(item: CatalogItem) {
  return item.unitPrice ?? item.price ?? 0
}

/// AD-{yyMMdd}-{HHmmss} — เดา/ชนกันยากพอที่จะไม่ต้องตรวจซ้ำกับรหัสที่มีอยู่ก่อนส่ง (backend ยังตรวจซ้ำให้อยู่ดี)
function generateAdHocCode() {
  const now = new Date()
  const pad = (value: number) => String(value).padStart(2, '0')
  const datePart = `${String(now.getFullYear()).slice(-2)}${pad(now.getMonth() + 1)}${pad(now.getDate())}`
  const timePart = `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`
  return `AD-${datePart}-${timePart}`
}

export function CatalogPanel({
  readOnly, adding, onAdd, onAddAdHoc, addingAdHoc, quotationId, onApplied,
}: CatalogPanelProps) {
  const { session } = useSession()
  const canManageCatalog = Boolean(session?.user.canSeeCost)
  const queryClient = useQueryClient()
  const [showTemplatePicker, setShowTemplatePicker] = useState(false)
  const applyTemplateMutation = useMutation({
    mutationFn: (input: { templateId: string; source: UpsertLineSource | null }) =>
      applyQuotationTemplate(quotationId, { templateId: input.templateId, source: input.source ?? undefined }),
    onSuccess: (quotation) => {
      toast.success('เพิ่มรายการจากเทมเพลตแล้ว')
      onApplied(quotation)
      setShowTemplatePicker(false)
    },
  })
  const [queryText, setQueryText] = useState('')
  const normalizedQuery = queryText.trim()
  const query = useQuery({
    queryKey: ['catalog', normalizedQuery],
    queryFn: () => searchCatalog(normalizedQuery),
    enabled: normalizedQuery.length >= 2,
  })

  // ---- ฟอร์มเพิ่มรายการนอกแคตตาล็อก (ad-hoc) — docs/07-quotation-adhoc-line.md ----
  const [showAdHocForm, setShowAdHocForm] = useState(false)
  const [adHocType, setAdHocType] = useState<'part' | 'labor'>('part')
  const [adHocName, setAdHocName] = useState('')
  const [adHocPrice, setAdHocPrice] = useState(0)
  const [adHocQuantity, setAdHocQuantity] = useState('1')
  const [adHocUnit, setAdHocUnit] = useState('')
  const [adHocCost, setAdHocCost] = useState(0)
  const [adHocStandardHours, setAdHocStandardHours] = useState('')
  const [saveToCatalog, setSaveToCatalog] = useState(false)
  const [adHocCode, setAdHocCode] = useState('')

  const openAdHocForm = () => {
    if (!adHocName.trim() && normalizedQuery) setAdHocName(normalizedQuery)
    setShowAdHocForm(true)
  }

  const resetAdHocForm = () => {
    setShowAdHocForm(false)
    setAdHocType('part')
    setAdHocName('')
    setAdHocPrice(0)
    setAdHocQuantity('1')
    setAdHocUnit('')
    setAdHocCost(0)
    setAdHocStandardHours('')
    setSaveToCatalog(false)
    setAdHocCode('')
  }

  const createCatalogMutation = useMutation({
    mutationFn: (vars: { input: CatalogItemInput; source: UpsertLineSource; quantity: number }) =>
      createCatalogItem(vars.input),
    onSuccess: (item, vars) => {
      void queryClient.invalidateQueries({ queryKey: ['catalog'] })
      void queryClient.invalidateQueries({ queryKey: ['catalog-management'] })
      toast.success('บันทึกสินค้าเข้าแคตตาล็อกแล้ว')
      // ใช้ onAddAdHoc (ที่จริงคือ "เพิ่มบรรทัดด้วย payload เต็ม") แทน onAdd ตรงๆ
      // เพราะ onAdd/addMutation เดิม hardcode quantity เป็น 1 เสมอ — จะทำให้จำนวนที่กรอกในฟอร์มนี้หายไปเงียบๆ
      onAddAdHoc({
        catalogCode: item.code,
        quantity: vars.quantity,
        unitPrice: item.price,
        discountPercent: 0,
        promotion: 0,
        source: vars.source,
      })
      resetAdHocForm()
    },
  })

  const nameMissing = !adHocName.trim()
  const priceMissing = !(adHocPrice > 0)
  const codeMissing = saveToCatalog && !adHocCode.trim()
  const disabledReason = nameMissing
    ? 'กรอกชื่อรายการก่อน'
    : priceMissing
      ? 'กรอกราคาก่อน'
      : codeMissing
        ? 'กรอกรหัสสินค้าก่อน'
        : undefined
  const adHocBusy = addingAdHoc || createCatalogMutation.isPending

  const handleAddAdHoc = (source: UpsertLineSource) => {
    if (disabledReason || adHocBusy) return
    const quantity = Number(adHocQuantity) > 0 ? Number(adHocQuantity) : 1

    if (saveToCatalog) {
      createCatalogMutation.mutate({
        input: {
          code: adHocCode.trim(),
          type: adHocType,
          name: adHocName.trim(),
          unit: adHocUnit.trim() || (adHocType === 'labor' ? 'งาน' : 'ชิ้น'),
          cost: adHocCost || 0,
          price: adHocPrice,
          standardHours: adHocType === 'labor' ? Number(adHocStandardHours) || undefined : undefined,
          onHand: 0,
          reserved: 0,
          onOrder: 0,
          damaged: 0,
        },
        source,
        quantity,
      })
      return
    }

    const payload: UpsertLine = {
      catalogCode: '',
      quantity,
      unitPrice: adHocPrice,
      discountPercent: 0,
      promotion: 0,
      source,
      name: adHocName.trim(),
      type: adHocType,
      unit: adHocUnit.trim() || undefined,
      unitCost: canManageCatalog && adHocCost > 0 ? adHocCost : undefined,
      standardHours: adHocType === 'labor' && adHocStandardHours ? Number(adHocStandardHours) : undefined,
    }
    onAddAdHoc(payload)
    resetAdHocForm()
  }

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

      <div className="catalog-adhoc-toggle">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={readOnly}
          onClick={() => (showAdHocForm ? resetAdHocForm() : openAdHocForm())}
        >
          {showAdHocForm ? <X aria-hidden="true" /> : <Plus aria-hidden="true" />}
          {showAdHocForm ? 'ยกเลิกเพิ่มรายการเอง' : 'เพิ่มรายการเอง (ไม่มีในแคตตาล็อก)'}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={readOnly}
          title={readOnly ? 'ใบเสนอราคานี้ไม่ได้อยู่ในสถานะร่าง — แก้ไขไม่ได้' : undefined}
          onClick={() => setShowTemplatePicker(true)}
        >
          <FileStack aria-hidden="true" /> เพิ่มรายการด้วยเทมเพลต
        </Button>
      </div>

      <QuotationTemplatePickerModal
        open={showTemplatePicker}
        onClose={() => { setShowTemplatePicker(false); applyTemplateMutation.reset() }}
        applying={applyTemplateMutation.isPending}
        applyError={applyTemplateMutation.error}
        onApply={(input) => applyTemplateMutation.mutate(input)}
      />

      {showAdHocForm ? (
        <Card className="catalog-adhoc-form">
          <div className="field field--wide">
            <span>ประเภท *</span>
            <div className="catalog-adhoc-type-toggle">
              <Button
                type="button"
                size="sm"
                variant={adHocType === 'part' ? 'default' : 'outline'}
                onClick={() => setAdHocType('part')}
              >
                <Package aria-hidden="true" /> อะไหล่
              </Button>
              <Button
                type="button"
                size="sm"
                variant={adHocType === 'labor' ? 'default' : 'outline'}
                onClick={() => setAdHocType('labor')}
              >
                <Wrench aria-hidden="true" /> ค่าแรง
              </Button>
            </div>
          </div>

          <Label className="field field--wide">
            <span>ชื่อรายการ *</span>
            <Input
              value={adHocName}
              maxLength={300}
              placeholder="เช่น ถอดล้างเทอร์โบ"
              onChange={(event) => setAdHocName(event.target.value)}
            />
          </Label>

          <div className="catalog-adhoc-grid">
            <Label className="field field--compact">
              <span>ราคา/หน่วย *</span>
              <MoneyInput value={adHocPrice} onValueChange={setAdHocPrice} />
            </Label>
            <Label className="field field--compact">
              <span>จำนวน</span>
              <Input
                type="number"
                min="0.01"
                step="0.01"
                value={adHocQuantity}
                onChange={(event) => setAdHocQuantity(event.target.value)}
              />
            </Label>
            <Label className="field field--compact">
              <span>หน่วย</span>
              <Input
                value={adHocUnit}
                maxLength={40}
                placeholder={adHocType === 'labor' ? 'งาน' : 'ชิ้น'}
                onChange={(event) => setAdHocUnit(event.target.value)}
              />
            </Label>
            {canManageCatalog ? (
              <Label className="field field--compact">
                <span>ต้นทุน/หน่วย</span>
                <MoneyInput value={adHocCost} onValueChange={setAdHocCost} />
              </Label>
            ) : null}
            {adHocType === 'labor' ? (
              <Label className="field field--compact">
                <span>ชั่วโมงมาตรฐาน</span>
                <Input
                  type="number"
                  min="0"
                  step="0.01"
                  value={adHocStandardHours}
                  onChange={(event) => setAdHocStandardHours(event.target.value)}
                />
              </Label>
            ) : null}
          </div>

          {canManageCatalog ? (
            <>
              <label className="filter-check">
                <input
                  type="checkbox"
                  checked={saveToCatalog}
                  onChange={(event) => {
                    const checked = event.target.checked
                    setSaveToCatalog(checked)
                    if (checked && !adHocCode.trim()) setAdHocCode(generateAdHocCode())
                  }}
                />
                บันทึกเข้าแคตตาล็อกเพื่อใช้ครั้งต่อไป
              </label>
              {saveToCatalog ? (
                <Label className="field field--wide">
                  <span>รหัสสินค้า *</span>
                  <div className="field-input-with-action">
                    <Input
                      value={adHocCode}
                      maxLength={60}
                      onChange={(event) => setAdHocCode(event.target.value)}
                    />
                    <Button type="button" size="sm" variant="secondary" onClick={() => setAdHocCode(generateAdHocCode())}>
                      <WandSparkles aria-hidden="true" /> สร้างรหัส
                    </Button>
                  </div>
                </Label>
              ) : null}
            </>
          ) : null}

          {createCatalogMutation.isError ? (
            <p className="field-error">
              {isApiError(createCatalogMutation.error) ? createCatalogMutation.error.messageTh : 'บันทึกสินค้าไม่สำเร็จ'}
            </p>
          ) : null}

          <div className="catalog-card__actions">
            <Button
              type="button"
              variant="outline"
              size="sm"
              title={readOnly ? undefined : disabledReason}
              disabled={readOnly || adHocBusy || Boolean(disabledReason)}
              onClick={() => handleAddAdHoc('Customer')}
            >
              <UserRound aria-hidden="true" /> ลูกค้าขอ
            </Button>
            <Button
              type="button"
              variant="soft"
              size="sm"
              title={readOnly ? undefined : disabledReason}
              disabled={readOnly || adHocBusy || Boolean(disabledReason)}
              onClick={() => handleAddAdHoc('Technician')}
            >
              {adHocBusy ? <LoaderCircle className="spin" aria-hidden="true" /> : <Wrench aria-hidden="true" />}
              ช่างแนะนำ
            </Button>
          </div>
        </Card>
      ) : null}

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
            <Button variant="outline" size="sm" disabled={readOnly} onClick={openAdHocForm}>
              <Plus aria-hidden="true" /> เพิ่มรายการเอง (ไม่มีในแคตตาล็อก)
            </Button>
          </div>
        )}
      </ScrollArea>
    </Card>
  )
}
