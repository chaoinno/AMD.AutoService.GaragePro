import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleOff as ToggleLeft, Pencil, Plus, Search, WandSparkles, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { getCatalogItems } from '../../api/catalog'
import {
  createQuotationTemplate,
  getQuotationTemplate,
  getQuotationTemplates,
  setQuotationTemplateStatus,
  updateQuotationTemplate,
} from '../../api/masterData'
import type { QuotationTemplate, QuotationTemplateInput, QuotationTemplateLineInput, UpsertLineSource } from '../../api/types'
import { ManagementTable } from '../../components/ManagementTable'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Combobox } from '../../components/ui/combobox'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'
import { MoneyInput } from '../../components/MoneyInput'
import { useSession } from '../../lib/session'
import { Field, InlineError, PermissionNote, QueryState, StatusBadge } from './MasterDataCommon'
import './quotationTemplate.css'

type DraftLine = {
  key: string
  catalogCode: string // '' = นอกแคตตาล็อก
  name: string
  type: 'part' | 'labor'
  unit: string
  quantity: number
  useCatalogPrice: boolean // เฉพาะบรรทัดแคตตาล็อก — true = ไม่ส่ง unitPrice (ใช้ราคาสดตอน apply)
  unitPrice: number
  unitCost: number
  standardHours: number
  discountPercent: number
  source: UpsertLineSource
  note: string
}

let draftKeySeq = 0
const nextDraftKey = () => `draft-${++draftKeySeq}`

function emptyAdHocLine(): DraftLine {
  return {
    key: nextDraftKey(), catalogCode: '', name: '', type: 'part', unit: 'ชิ้น', quantity: 1,
    useCatalogPrice: false, unitPrice: 0, unitCost: 0, standardHours: 0, discountPercent: 0,
    source: 'Customer', note: '',
  }
}

function catalogLineFromItem(item: { code: string; name: string; type: 'part' | 'labor'; unit: string; price: number }): DraftLine {
  return {
    key: nextDraftKey(), catalogCode: item.code, name: item.name, type: item.type, unit: item.unit, quantity: 1,
    useCatalogPrice: true, unitPrice: item.price, unitCost: 0, standardHours: 0, discountPercent: 0,
    source: 'Customer', note: '',
  }
}

function toDraftLines(template: QuotationTemplate): DraftLine[] {
  return template.lines.map((l) => ({
    key: nextDraftKey(),
    catalogCode: l.catalogCode,
    name: l.name,
    type: l.type,
    unit: l.unit ?? (l.type === 'labor' ? 'งาน' : 'ชิ้น'),
    quantity: l.quantity,
    useCatalogPrice: !l.isAdHoc && l.unitPrice === null,
    unitPrice: l.unitPrice ?? l.catalogPrice ?? 0,
    unitCost: l.unitCost ?? 0,
    standardHours: l.standardHours ?? 0,
    discountPercent: l.discountPercent,
    source: l.source,
    note: l.note ?? '',
  }))
}

function toInput(code: string, name: string, description: string, lines: DraftLine[]): QuotationTemplateInput {
  const mappedLines: QuotationTemplateLineInput[] = lines.map((l) => {
    const isAdHoc = !l.catalogCode
    return {
      catalogCode: l.catalogCode,
      name: l.name || undefined,
      type: l.type,
      unit: l.unit || undefined,
      quantity: l.quantity,
      unitPrice: isAdHoc ? l.unitPrice : (l.useCatalogPrice ? undefined : l.unitPrice),
      unitCost: isAdHoc ? l.unitCost : undefined,
      standardHours: isAdHoc && l.type === 'labor' ? l.standardHours : undefined,
      discountPercent: l.discountPercent,
      promotion: 0,
      source: l.source,
      note: l.note || undefined,
    }
  })
  return { code, name, description: description || undefined, lines: mappedLines }
}

export function QuotationTemplatePage() {
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const [keyword, setKeyword] = useState('')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [formId, setFormId] = useState<string | 'new' | null>(null)
  const [statusTarget, setStatusTarget] = useState<QuotationTemplate | null>(null)
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: ['quotation-templates', keyword, includeInactive],
    queryFn: () => getQuotationTemplates({ keyword: keyword || undefined, includeInactive }),
  })

  const statusMutation = useMutation({
    mutationFn: (target: QuotationTemplate) => setQuotationTemplateStatus(target.id, !target.isActive),
    onSuccess: () => {
      toast.success(statusTarget?.isActive ? 'ปิดใช้งานเทมเพลตแล้ว' : 'เปิดใช้งานเทมเพลตแล้ว')
      setStatusTarget(null)
      void queryClient.invalidateQueries({ queryKey: ['quotation-templates'] })
    },
  })

  return (
    <AppShell title="เทมเพลตใบเสนอราคา">
      <section className="page-heading">
        <div>
          <p className="eyebrow">ข้อมูลหลัก / ใบเสนอราคา</p>
          <h2>จัดการเทมเพลตใบเสนอราคา</h2>
          <p>ชุดรายการมาตรฐาน (เช่น เช็คระยะ 10,000 กม.) ที่เพิ่มลงใบเสนอราคาได้ในคลิกเดียวจากการ์ดจ๊อบหรือหน้าต่างแก้ไขใบเสนอราคา</p>
        </div>
        <Button
          disabled={!canManage}
          title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มเทมเพลตได้' : undefined}
          onClick={() => setFormId('new')}
        >
          <Plus aria-hidden="true" /> เพิ่มเทมเพลต
        </Button>
      </section>
      <PermissionNote canManage={canManage} />
      <Card className="management-filters master-filters">
        <div className="filter-search input-with-icon">
          <Search aria-hidden="true" />
          <Input value={keyword} onChange={(e) => setKeyword(e.target.value)} placeholder="ค้นหารหัสหรือชื่อเทมเพลต" aria-label="ค้นหาเทมเพลต" />
        </div>
        <Select value={includeInactive ? 'all' : 'active'} onChange={(e) => setIncludeInactive(e.target.value === 'all')} aria-label="กรองสถานะ">
          <option value="active">ใช้งานอยู่</option>
          <option value="all">ทั้งหมด</option>
        </Select>
      </Card>
      <QueryState
        query={query}
        loadingTitle="กำลังโหลดเทมเพลต"
        emptyTitle="ยังไม่มีเทมเพลต"
        emptyReason="เพิ่มเทมเพลตแรก หรือเปลี่ยนตัวกรองสถานะเพื่อดูรายการอื่น"
        onRetry={() => void query.refetch()}
      >
        <Card className="management-table-card">
          <ManagementTable
            data={query.data ?? []}
            columns={[
              {
                id: 'template', header: 'เทมเพลต', value: (t) => t.name, size: 260,
                render: (t) => <div className="master-name"><strong>{t.name}</strong><small>{t.code}</small></div>,
              },
              {
                id: 'lines', header: 'รายการ', value: (t) => t.lineCount, size: 220,
                render: (t) => <>{t.lineCount} รายการ · {t.partCount} อะไหล่ · {t.laborCount} ค่าแรง</>,
              },
              {
                id: 'description', header: 'คำอธิบาย',
                render: (t) => t.description || <span className="muted">ไม่ระบุ</span>,
              },
              {
                id: 'status', header: 'สถานะ', value: (t) => t.isActive ? 'ใช้งาน' : 'ปิดใช้งาน', size: 140,
                render: (t) => <StatusBadge isActive={t.isActive} />,
              },
              {
                id: 'actions', header: '', size: 110,
                render: (t) => (
                  <div className="row-actions">
                    <Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขได้' : 'แก้ไขเทมเพลต'} aria-label={`แก้ไข ${t.name}`} onClick={() => setFormId(t.id)}><Pencil /></Button>
                    <Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เปลี่ยนสถานะได้' : t.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} aria-label={`${t.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${t.name}`} onClick={() => setStatusTarget(t)}><ToggleLeft /></Button>
                  </div>
                ),
              },
            ]}
          />
        </Card>
      </QueryState>

      <QuotationTemplateFormModal open={formId !== null} templateId={formId === 'new' ? null : formId} onClose={() => setFormId(null)} />
      <ConfirmModal
        open={Boolean(statusTarget)}
        title={statusTarget?.isActive ? 'ปิดใช้งานเทมเพลต' : 'เปิดใช้งานเทมเพลต'}
        description="ข้อมูลจะไม่ถูกลบและสามารถเปิดใช้งานกลับมาได้ — เทมเพลตที่ปิดใช้งานจะไม่แสดงในตัวเลือกเพิ่มรายการอีก"
        onClose={() => { setStatusTarget(null); statusMutation.reset() }}
        footer={
          <>
            <Button variant="ghost" onClick={() => setStatusTarget(null)}>ยกเลิก</Button>
            <Button variant={statusTarget?.isActive ? 'destructive' : 'default'} disabled={statusMutation.isPending} onClick={() => statusTarget && statusMutation.mutate(statusTarget)}>
              {statusMutation.isPending ? 'กำลังบันทึก…' : statusTarget?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}
            </Button>
          </>
        }
      >
        {statusMutation.isError ? <InlineError error={statusMutation.error} /> : null}
        <p>เทมเพลต: <strong>{statusTarget?.code} · {statusTarget?.name}</strong></p>
      </ConfirmModal>
    </AppShell>
  )
}

function QuotationTemplateFormModal({ open, templateId, onClose }: { open: boolean; templateId: string | null; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([])
  const [itemQuery, setItemQuery] = useState('')
  const [errors, setErrors] = useState<{ code?: string; name?: string; lines?: string }>({})

  const details = useQuery({
    queryKey: ['quotation-template', templateId],
    queryFn: () => getQuotationTemplate(templateId!),
    enabled: open && Boolean(templateId),
  })

  const templatesForCode = useQuery({
    queryKey: ['quotation-templates', 'code-lookup'],
    queryFn: () => getQuotationTemplates({ includeInactive: true }),
    enabled: open && !templateId,
  })

  const catalogQuery = useQuery({
    queryKey: ['catalog', 'template-picker', itemQuery],
    queryFn: () => getCatalogItems({ keyword: itemQuery, pageSize: 20 }),
    enabled: open && itemQuery.trim().length >= 2,
  })

  useEffect(() => {
    if (!open) return
    setErrors({})
    setItemQuery('')
    if (!templateId) {
      setCode(''); setName(''); setDescription(''); setLines([])
    } else if (details.data) {
      setCode(details.data.code); setName(details.data.name); setDescription(details.data.description ?? '')
      setLines(toDraftLines(details.data))
    }
  }, [open, templateId, details.data])

  const save = useMutation({
    mutationFn: (input: QuotationTemplateInput) => templateId ? updateQuotationTemplate(templateId, input) : createQuotationTemplate(input),
    onSuccess: (saved) => {
      toast.success(templateId ? 'แก้ไขเทมเพลตเรียบร้อยแล้ว' : 'เพิ่มเทมเพลตเรียบร้อยแล้ว')
      void queryClient.invalidateQueries({ queryKey: ['quotation-templates'] })
      if (templateId) queryClient.setQueryData(['quotation-template', templateId], saved)
      close()
    },
  })

  const close = () => { save.reset(); onClose() }

  const generateCode = () => {
    const existing = templatesForCode.data ?? []
    const used = new Set(existing.map((t) => t.code.trim().toUpperCase()))
    let sequence = 1
    let candidate = `TPL-${String(sequence).padStart(3, '0')}`
    while (used.has(candidate)) { sequence += 1; candidate = `TPL-${String(sequence).padStart(3, '0')}` }
    setCode(candidate)
    setErrors((old) => ({ ...old, code: undefined }))
  }

  const updateLine = (key: string, patch: Partial<DraftLine>) =>
    setLines((old) => old.map((l) => l.key === key ? { ...l, ...patch } : l))
  const removeLine = (key: string) => setLines((old) => old.filter((l) => l.key !== key))

  const submit = () => {
    const nextErrors: typeof errors = {}
    if (!code.trim()) nextErrors.code = 'กรุณากรอกรหัสเทมเพลต'
    if (!name.trim()) nextErrors.name = 'กรุณากรอกชื่อเทมเพลต'
    if (lines.length === 0) nextErrors.lines = 'เพิ่มอย่างน้อย 1 รายการก่อนบันทึก'
    else if (lines.some((l) => !l.catalogCode && (!l.name.trim() || l.unitPrice <= 0)))
      nextErrors.lines = 'มีรายการนอกแคตตาล็อกที่ยังไม่ได้กรอกชื่อหรือราคา'
    setErrors(nextErrors)
    if (Object.keys(nextErrors).length) return
    save.mutate(toInput(code.trim().toUpperCase(), name.trim(), description.trim(), lines))
  }

  const codeOptions = (catalogQuery.data?.items ?? []).filter((x) => !lines.some((l) => l.catalogCode === x.code))

  return (
    <ConfirmModal
      open={open}
      title={templateId ? 'แก้ไขเทมเพลตใบเสนอราคา' : 'เพิ่มเทมเพลตใบเสนอราคา'}
      description="เทมเพลตใช้เพิ่มหลายรายการลงใบเสนอราคาในคลิกเดียวจากการ์ดจ๊อบหรือหน้าต่างแก้ไขใบเสนอราคา"
      onClose={close}
      size="xlarge"
      footer={
        <>
          <Button variant="ghost" onClick={close}>ยกเลิก</Button>
          <Button onClick={submit} disabled={save.isPending || (Boolean(templateId) && details.isPending)}>
            {save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}
          </Button>
        </>
      }
    >
      {details.isError ? <InlineError error={details.error} /> : (
        <form className="management-form master-form" onSubmit={(e) => { e.preventDefault(); submit() }}>
          <section>
            <h3>ข้อมูลเทมเพลต</h3>
            <div className="form-grid">
              <Field label="รหัสเทมเพลต *" error={errors.code}>
                <div className="field-input-with-action">
                  <Input value={code} maxLength={30} onChange={(e) => setCode(e.target.value)} />
                  <Button type="button" size="sm" variant="secondary" onClick={generateCode} disabled={Boolean(templateId) || templatesForCode.isPending} title={templateId ? 'สร้างรหัสอัตโนมัติเฉพาะเทมเพลตใหม่' : undefined}>
                    <WandSparkles aria-hidden="true" /> สร้างรหัส
                  </Button>
                </div>
              </Field>
              <Field label="ชื่อเทมเพลต *" error={errors.name}>
                <Input value={name} maxLength={200} onChange={(e) => setName(e.target.value)} />
              </Field>
              <Field label="คำอธิบาย" wide>
                <Textarea rows={2} value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
              </Field>
            </div>
          </section>

          <section>
            <h3>รายการในเทมเพลต</h3>
            <div className="form-grid">
              <Field label="ค้นหาสินค้าจากแคตตาล็อกเพื่อเพิ่มรายการ" wide>
                <Combobox
                  ariaLabel="ค้นหาและเพิ่มสินค้าจากแคตตาล็อก"
                  placeholder="พิมพ์รหัสหรือชื่อสินค้า (อย่างน้อย 2 ตัวอักษร)"
                  query={itemQuery}
                  onQueryChange={setItemQuery}
                  loading={catalogQuery.isFetching}
                  emptyLabel="ไม่พบสินค้าที่ค้นหา"
                  options={codeOptions.map((x) => ({ value: x.code, label: `${x.code} · ${x.name}`, description: `${formatType(x.type)} · ${x.price} บาท/${x.unit}` }))}
                  onSelect={(option) => {
                    const item = codeOptions.find((x) => x.code === option.value)
                    if (item) setLines((old) => [...old, catalogLineFromItem(item)])
                  }}
                />
              </Field>
            </div>
            <Button type="button" variant="outline" size="sm" onClick={() => setLines((old) => [...old, emptyAdHocLine()])}>
              <Plus aria-hidden="true" /> เพิ่มรายการนอกแคตตาล็อก
            </Button>
            {errors.lines ? <p className="field-error">{errors.lines}</p> : null}

            {lines.length === 0 ? (
              <p className="form-message">ยังไม่มีรายการ — ค้นหาสินค้าหรือเพิ่มรายการนอกแคตตาล็อกด้านบน</p>
            ) : (
              <div className="template-table-scroll">
                <table className="master-table template-lines">
                  <thead>
                    <tr>
                      <th>ลำดับ</th><th>รหัส/ชื่อ</th><th>จำนวน</th><th>หน่วย</th><th>ราคา/หน่วย</th><th>ส่วนลด %</th><th>ที่มา</th><th />
                    </tr>
                  </thead>
                  <tbody>
                    {lines.map((line, index) => {
                      const isAdHoc = !line.catalogCode
                      return (
                        <tr key={line.key}>
                          <td>{index + 1}</td>
                          <td>
                            {isAdHoc ? (
                              <div className="catalog-adhoc-type-toggle">
                                <Button type="button" size="sm" variant={line.type === 'part' ? 'default' : 'outline'} onClick={() => updateLine(line.key, { type: 'part', unit: line.unit === 'งาน' ? 'ชิ้น' : line.unit })}>อะไหล่</Button>
                                <Button type="button" size="sm" variant={line.type === 'labor' ? 'default' : 'outline'} onClick={() => updateLine(line.key, { type: 'labor', unit: line.unit === 'ชิ้น' ? 'งาน' : line.unit })}>ค่าแรง</Button>
                                <Input value={line.name} placeholder="ชื่อรายการ *" onChange={(e) => updateLine(line.key, { name: e.target.value })} />
                              </div>
                            ) : (
                              <div className="master-name">
                                <strong>{line.name}</strong>
                                <small>{line.catalogCode}</small>
                              </div>
                            )}
                          </td>
                          <td><Input type="number" min="0.01" step="0.01" value={line.quantity} onChange={(e) => updateLine(line.key, { quantity: Number(e.target.value) || 0 })} /></td>
                          <td>{isAdHoc ? <Input value={line.unit} onChange={(e) => updateLine(line.key, { unit: e.target.value })} /> : line.unit}</td>
                          <td>
                            {isAdHoc ? (
                              <MoneyInput value={line.unitPrice} onValueChange={(v) => updateLine(line.key, { unitPrice: v })} />
                            ) : (
                              <div className="template-price-cell">
                                <label>
                                  <input type="checkbox" checked={line.useCatalogPrice} onChange={(e) => updateLine(line.key, { useCatalogPrice: e.target.checked })} />
                                  ใช้ราคาแคตตาล็อก
                                </label>
                                {!line.useCatalogPrice ? <MoneyInput value={line.unitPrice} onValueChange={(v) => updateLine(line.key, { unitPrice: v })} /> : <span className="muted">{line.unitPrice} บาท (ราคาปัจจุบัน)</span>}
                              </div>
                            )}
                          </td>
                          <td><Input type="number" min="0" max="100" step="0.1" value={line.discountPercent} onChange={(e) => updateLine(line.key, { discountPercent: Number(e.target.value) || 0 })} /></td>
                          <td>
                            <Select value={line.source} onChange={(e) => updateLine(line.key, { source: e.target.value as UpsertLineSource })}>
                              <option value="Customer">ลูกค้าขอ</option>
                              <option value="Technician">ช่างแนะนำ</option>
                            </Select>
                          </td>
                          <td><Button type="button" size="icon" variant="ghost" aria-label="ลบรายการ" onClick={() => removeLine(line.key)}><X /></Button></td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </section>
          {save.isError ? <InlineError error={save.error} /> : null}
        </form>
      )}
    </ConfirmModal>
  )
}

function formatType(type: 'part' | 'labor') {
  return type === 'part' ? 'อะไหล่' : 'ค่าแรง'
}
