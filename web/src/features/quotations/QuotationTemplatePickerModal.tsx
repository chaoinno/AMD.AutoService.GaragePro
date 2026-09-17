import { useQuery } from '@tanstack/react-query'
import { AlertTriangle } from 'lucide-react'
import { useState } from 'react'
import { getQuotationTemplate, getQuotationTemplates } from '../../api/masterData'
import type { UpsertLineSource } from '../../api/types'
import { isApiError } from '../../api/client'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { formatMoney } from '../../lib/format'
import './templatePicker.css'

type SourceChoice = '' | UpsertLineSource

export type QuotationTemplatePickerModalProps = {
  open: boolean
  onClose: () => void
  onApply: (input: { templateId: string; source: UpsertLineSource | null }) => void
  applying: boolean
  applyError?: unknown
}

/// หน้าต่างเลือกเทมเพลตใบเสนอราคา — ใช้ร่วมกันทั้งจาก CatalogPanel (ในหน้าต่างแก้ไขใบเสนอราคา)
/// และจากการ์ดจ๊อบ (ขั้นเสนอราคา/งานซ่อม) — docs/08-quotation-template.md
export function QuotationTemplatePickerModal({
  open, onClose, onApply, applying, applyError,
}: QuotationTemplatePickerModalProps) {
  const [keyword, setKeyword] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [source, setSource] = useState<SourceChoice>('')

  const listQuery = useQuery({
    queryKey: ['quotation-templates', 'picker', keyword],
    queryFn: () => getQuotationTemplates({ keyword: keyword || undefined, includeInactive: false }),
    enabled: open,
  })

  const detailQuery = useQuery({
    queryKey: ['quotation-template', selectedId],
    queryFn: () => getQuotationTemplate(selectedId!),
    enabled: open && Boolean(selectedId),
  })

  const close = () => {
    setSelectedId(null)
    setKeyword('')
    setSource('')
    onClose()
  }

  const template = detailQuery.data
  const hasMissingCatalogItem = template?.lines.some((l) => !l.isAdHoc && l.catalogResolved === false) ?? false

  return (
    <ConfirmModal
      open={open}
      title="เพิ่มรายการด้วยเทมเพลต"
      description="เลือกเทมเพลตที่ต้องการ ระบบจะเพิ่มทุกรายการในเทมเพลตต่อท้ายรายการปัจจุบัน"
      onClose={close}
      size="large"
      footer={
        <>
          <Button variant="ghost" onClick={close}>ยกเลิก</Button>
          <Button
            disabled={!selectedId || !template || template.lineCount === 0 || hasMissingCatalogItem || applying}
            title={
              !selectedId
                ? 'เลือกเทมเพลตก่อน'
                : template && template.lineCount === 0
                  ? 'เทมเพลตนี้ยังไม่มีรายการ'
                  : hasMissingCatalogItem
                    ? 'เทมเพลตนี้มีรหัสที่ไม่พบในแคตตาล็อกแล้ว — แก้ที่เมนูข้อมูลหลักก่อน'
                    : undefined
            }
            onClick={() => selectedId && onApply({ templateId: selectedId, source: source || null })}
          >
            {applying ? 'กำลังเพิ่มรายการ…' : template ? `เพิ่ม ${template.lineCount} รายการ` : 'เพิ่มรายการ'}
          </Button>
        </>
      }
    >
      {applyError ? (
        <div className="form-error-panel" role="alert">
          {isApiError(applyError) ? applyError.messageTh : 'เพิ่มรายการจากเทมเพลตไม่สำเร็จ'}
        </div>
      ) : null}

      <div className="template-picker">
        <div className="template-picker__list">
          <Input
            value={keyword}
            onChange={(e) => setKeyword(e.target.value)}
            placeholder="ค้นหาชื่อหรือรหัสเทมเพลต"
            aria-label="ค้นหาเทมเพลต"
          />
          {listQuery.isPending ? (
            <p className="form-message">กำลังโหลดเทมเพลต…</p>
          ) : listQuery.isError ? (
            <div className="form-error-panel" role="alert">
              {isApiError(listQuery.error) ? listQuery.error.messageTh : 'โหลดเทมเพลตไม่สำเร็จ'}
            </div>
          ) : !listQuery.data?.length ? (
            <p className="form-message">ยังไม่มีเทมเพลตที่ใช้งานอยู่ — สร้างได้ที่เมนู "เทมเพลตใบเสนอราคา"</p>
          ) : (
            <ul className="template-picker__cards">
              {listQuery.data.map((t) => (
                <li key={t.id}>
                  <button
                    type="button"
                    className={`template-picker__card${selectedId === t.id ? ' template-picker__card--selected' : ''}`}
                    onClick={() => setSelectedId(t.id)}
                  >
                    <strong>{t.name}</strong>
                    <small>{t.code}</small>
                    <span>{t.lineCount} รายการ · {t.partCount} อะไหล่ · {t.laborCount} ค่าแรง</span>
                    {t.description ? <p>{t.description}</p> : null}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="template-picker__preview">
          {!selectedId ? (
            <p className="form-message">เลือกเทมเพลตทางซ้ายเพื่อดูรายการที่จะถูกเพิ่ม</p>
          ) : detailQuery.isPending ? (
            <p className="form-message">กำลังโหลดรายละเอียด…</p>
          ) : detailQuery.isError ? (
            <div className="form-error-panel" role="alert">
              {isApiError(detailQuery.error) ? detailQuery.error.messageTh : 'โหลดรายละเอียดไม่สำเร็จ'}
            </div>
          ) : template ? (
            <>
              <label className="field">
                <span>ที่มาของรายการ</span>
                <Select value={source} onChange={(e) => setSource(e.target.value as SourceChoice)}>
                  <option value="">ตามที่กำหนดในเทมเพลต (ค่าเริ่มต้นต่อบรรทัด)</option>
                  <option value="Customer">ลูกค้าขอ (ทุกบรรทัด)</option>
                  <option value="Technician">ช่างแนะนำ (ทุกบรรทัด)</option>
                </Select>
              </label>
              <ul className="template-picker__lines">
                {template.lines.map((line) => (
                  <li key={line.id}>
                    <div>
                      <strong>{line.name}</strong>
                      {line.isAdHoc ? <Badge variant="outline">นอกแคตตาล็อก</Badge> : null}
                      {!line.isAdHoc && line.catalogResolved === false ? (
                        <Badge variant="destructive"><AlertTriangle aria-hidden="true" /> ไม่พบรหัสนี้ในแคตตาล็อกแล้ว</Badge>
                      ) : null}
                      {!line.isAdHoc && line.catalogActive === false ? (
                        <Badge variant="warning"><AlertTriangle aria-hidden="true" /> สินค้าถูกปิดใช้งาน</Badge>
                      ) : null}
                    </div>
                    <span>
                      {line.quantity} {line.unit ?? ''} ×{' '}
                      {formatMoney(line.isAdHoc ? line.unitPrice ?? 0 : line.catalogPrice ?? line.unitPrice ?? 0)} บาท
                    </span>
                  </li>
                ))}
              </ul>
            </>
          ) : null}
        </div>
      </div>
    </ConfirmModal>
  )
}
