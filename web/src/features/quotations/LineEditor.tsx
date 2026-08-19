import { LoaderCircle, Plus, Trash2, UserRound, Wrench, type LucideIcon } from 'lucide-react'
import type { QuotationLine, Technician } from '../../api/types'
import { Money } from '../../components/Money'
import { MoneyInput } from '../../components/MoneyInput'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'

type LinePatch = Partial<
  Pick<
    QuotationLine,
    | 'quantity'
    | 'unitPrice'
    | 'discountPercent'
    | 'promotion'
    | 'assignedTechnicianId'
    | 'note'
  >
>

type LineEditorProps = {
  lines: QuotationLine[]
  technicians: Technician[]
  readOnly: boolean
  deletingLineId: string | null
  onPatch: (lineId: string, patch: LinePatch) => void
  onRequestDelete: (line: QuotationLine) => void
}

const promotionOptions = [
  { value: 0, label: 'ไม่มีโปรโมชัน' },
  { value: 1, label: 'ลูกค้าประจำ −5%' },
  { value: 2, label: 'โปรเบรกครบชุด −300' },
  { value: 3, label: 'ประกันคู่สัญญา −10%' },
] as const

export function LineEditor({
  lines,
  technicians,
  readOnly,
  deletingLineId,
  onPatch,
  onRequestDelete,
}: LineEditorProps) {
  const customerLines = lines.filter((line) => line.source === 'customer')
  const technicianLines = lines.filter((line) => line.source === 'technician')

  return (
    <Card className="line-panel">
      <div className="panel-heading panel-heading--line">
        <div>
          <span className="panel-heading__step">02</span>
          <h3>รายการในใบเสนอราคา</h3>
        </div>
        <span className="line-count">{lines.length} รายการ</span>
      </div>

      {lines.length === 0 ? (
        <div className="line-empty">
          <Plus aria-hidden="true" />
          <strong>ยังไม่มีรายการในใบเสนอราคา</strong>
          <p>ค้นหาอะไหล่หรือค่าแรงจากพาเนลซ้าย แล้วเลือกว่าจะเพิ่มเป็นรายการลูกค้าขอหรือช่างแนะนำ</p>
        </div>
      ) : null}

      <LineGroup
        title="ลูกค้าขอ"
        icon={UserRound}
        tone="customer"
        lines={customerLines}
        technicians={technicians}
        readOnly={readOnly}
        deletingLineId={deletingLineId}
        onPatch={onPatch}
        onRequestDelete={onRequestDelete}
      />
      <LineGroup
        title="ช่างแนะนำ"
        icon={Wrench}
        tone="technician"
        lines={technicianLines}
        technicians={technicians}
        readOnly={readOnly}
        deletingLineId={deletingLineId}
        onPatch={onPatch}
        onRequestDelete={onRequestDelete}
      />
    </Card>
  )
}

type LineGroupProps = Omit<LineEditorProps, 'lines'> & {
  title: string
  icon: LucideIcon
  tone: 'customer' | 'technician'
  lines: QuotationLine[]
}

function LineGroup({
  title,
  icon: Icon,
  tone,
  lines,
  technicians,
  readOnly,
  deletingLineId,
  onPatch,
  onRequestDelete,
}: LineGroupProps) {
  const total = lines.reduce((sum, line) => sum + line.netAmount, 0)

  return (
    <Card className={`line-group line-group--${tone}`}>
      <header className="line-group__header">
        <div>
          <span className="line-group__icon" aria-hidden="true"><Icon /></span>
          <strong>{title}</strong>
          <span>{lines.length} รายการ</span>
        </div>
        <Money value={total} suffix=" บาท" />
      </header>

      {lines.length ? (
        <div className="line-group__body">
          {lines.map((line) => (
            <Card className="line-editor" key={line.id}>
              <header className="line-editor__title">
                <span className="line-sequence">{line.sequence}</span>
                <span className="line-editor__name">
                  <strong>{line.name}</strong>
                  <small>
                    {line.catalogCode} · {line.type === 'part' ? 'อะไหล่' : 'ค่าแรง'} · {line.unit}
                  </small>
                </span>
                <span className="line-editor__net">
                  <small>สุทธิ</small>
                  <Money value={line.netAmount} suffix=" บาท" />
                </span>
                <Button
                  variant="ghost"
                  size="icon"
                  className="icon-button icon-button--danger"
                  aria-label={`ลบรายการ ${line.name}`}
                  disabled={readOnly || deletingLineId === line.id}
                  onClick={() => onRequestDelete(line)}
                >
                  {deletingLineId === line.id
                    ? <LoaderCircle className="spin" aria-hidden="true" />
                    : <Trash2 aria-hidden="true" />}
                </Button>
              </header>

              <div className="line-editor__grid">
                <Label className="field field--compact">
                  <span>จำนวน</span>
                  <Input
                    type="number"
                    min="0.01"
                    step="0.01"
                    value={line.quantity}
                    disabled={readOnly}
                    onChange={(event) => onPatch(line.id, { quantity: Number(event.target.value) })}
                  />
                </Label>
                <Label className="field field--compact">
                  <span>ราคา/หน่วย</span>
                  <MoneyInput
                    value={line.unitPrice}
                    disabled={readOnly}
                    onValueChange={(value) => onPatch(line.id, { unitPrice: value })}
                  />
                </Label>
                <Label className="field field--compact">
                  <span>ส่วนลด %</span>
                  <Input
                    className="money"
                    type="number"
                    min="0"
                    max="100"
                    step="0.01"
                    value={line.discountPercent}
                    disabled={readOnly}
                    onChange={(event) => onPatch(line.id, { discountPercent: Number(event.target.value) })}
                  />
                </Label>
                <Label className="field field--compact line-field--promotion">
                  <span>โปรโมชัน</span>
                  <Select
                    value={line.promotion}
                    disabled={readOnly}
                    onChange={(event) => onPatch(line.id, { promotion: Number(event.target.value) as QuotationLine['promotion'] })}
                  >
                    {promotionOptions.map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </Select>
                </Label>
                <Label className="field field--compact line-field--technician">
                  <span>ช่าง</span>
                  <Select
                    value={line.assignedTechnicianId ?? ''}
                    disabled={readOnly}
                    onChange={(event) => onPatch(line.id, {
                      assignedTechnicianId: event.target.value ? Number(event.target.value) : null,
                    })}
                  >
                    <option value="">ยังไม่ระบุ</option>
                    {technicians.map((technician) => (
                      <option key={technician.staffId} value={technician.staffId}>
                        {technician.name} · {technician.skillLevel || 'ไม่ระบุระดับ'}
                      </option>
                    ))}
                  </Select>
                </Label>
                <Label className="field field--compact line-field--note">
                  <span>หมายเหตุ</span>
                  <Input
                    value={line.note ?? ''}
                    disabled={readOnly}
                    placeholder="เพิ่มหมายเหตุ"
                    onChange={(event) => onPatch(line.id, { note: event.target.value })}
                  />
                </Label>
              </div>
            </Card>
          ))}
        </div>
      ) : (
        <div className="line-group__empty">ยังไม่มีรายการในกลุ่มนี้</div>
      )}
    </Card>
  )
}
