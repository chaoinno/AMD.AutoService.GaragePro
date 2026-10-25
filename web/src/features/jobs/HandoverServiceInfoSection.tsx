import { useMutation, useQuery } from '@tanstack/react-query'
import { CalendarClock, Gauge, Plus, Trash2, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { saveHandoverServiceInfo, type Handover, type HandoverServiceItem } from '../../api/handover'
import { getServiceReminderTemplates } from '../../api/serviceReminders'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { addMonthsToToday, formatDateOnly, formatKm } from '../../lib/format'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import { mileageInputError } from './mileage'
import {
  MAX_SERVICE_ITEMS,
  customRow,
  parseMonths,
  rowFromTemplate,
  rowMileageText,
  rowsFromHandover,
  rowsToInput,
  serviceInfoBlockedReason,
  serviceInfoSignature,
  validateRow,
  type ServiceRow,
} from './handoverServiceRows'

export { handoverServiceInfoMissing } from './handoverServiceRows'

/// [BIZ] ข้อมูลส่งมอบที่บังคับก่อนเซ็น (HANDOVER_SERVICE_INFO_REQUIRED): ไมล์ตอนส่งมอบ + นัดครั้งถัดไปอย่างน้อย 1 รายการ
/// นัดได้หลายรายการ (น้ำมันเครื่อง/กรอง/เบรก…) เลือกจากรายการตั้งต้นของสาขาแล้วแก้ไมล์/เดือนต่อคันได้ หรือพิมพ์เอง
/// ผู้ใช้กรอกเป็นเดือน ระบบคำนวณวันที่ให้ — ก่อนเซ็นวันที่เป็นพรีวิว server คำนวณใหม่จากวันส่งมอบจริงตอนเซ็น
export function HandoverServiceInfoSection({
  jobId,
  handover,
  onSaved,
}: {
  jobId: string
  handover: Handover
  onSaved: () => void
}) {
  const intakeKm = handover.mileageAtIntake ?? null

  if (handover.isLocked) {
    return (
      <section className="handover-service" aria-label="เลขไมล์และนัดครั้งถัดไป">
        <dl className="handover-service__summary">
          <div><dt>เลขไมล์ขณะรับรถ</dt><dd className="handover-service__compare">{formatKm(intakeKm)}</dd></div>
          <div><dt>เลขไมล์ตอนส่งมอบ</dt><dd className="handover-service__compare">{formatKm(handover.mileageAtHandover)}</dd></div>
        </dl>
        <ServiceItemList items={handover.serviceItems ?? []} />
      </section>
    )
  }

  return <ServiceInfoForm jobId={jobId} handover={handover} intakeKm={intakeKm} onSaved={onSaved} />
}

function ServiceInfoForm({
  jobId,
  handover,
  intakeKm,
  onSaved,
}: {
  jobId: string
  handover: Handover
  intakeKm: number | null
  onSaved: () => void
}) {
  const [handoverText, setHandoverText] = useState(() => toText(handover.mileageAtHandover))
  const [rows, setRows] = useState<ServiceRow[]>(() => rowsFromHandover(handover.serviceItems))
  const templates = useQuery({ queryKey: ['service-reminder-templates', 'active'], queryFn: () => getServiceReminderTemplates(false) })

  const handoverKm = mileageInputError(handoverText)
  // มิเรอร์ HandoverService.ValidateServiceInfo — server ตรวจซ้ำเสมอ
  const handoverError = handoverKm.error
    ?? (handoverKm.km != null && intakeKm != null && handoverKm.km < intakeKm
      ? `ต้องไม่น้อยกว่าเลขไมล์ขณะรับรถ (${formatKm(intakeKm)})`
      : undefined)

  const blockedReason = serviceInfoBlockedReason(intakeKm, handoverKm.km, handoverError, rows)

  const saved = serviceInfoSignature(
    handover.mileageAtHandover ?? null,
    (handover.serviceItems ?? []).map((i) => ({ name: i.name, nextServiceMileage: i.nextServiceMileage, nextServiceMonths: i.nextServiceMonths })),
  )
  const current = serviceInfoSignature(
    handoverKm.km,
    rows.map((r) => ({ name: r.name, nextServiceMileage: mileageInputError(rowMileageText(r, handoverKm.km)).km, nextServiceMonths: parseMonths(r.monthsText) })),
  )
  const dirty = saved !== current

  const mutation = useMutation({
    mutationFn: () => saveHandoverServiceInfo(jobId, { mileageAtHandover: handoverKm.km!, items: rowsToInput(rows, handoverKm.km) }),
    onSuccess: () => {
      toast.success('บันทึกเลขไมล์และนัดครั้งถัดไปแล้ว')
      onSaved()
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกไม่สำเร็จ'),
  })

  const usedTemplateIds = new Set(rows.map((r) => r.templateId).filter(Boolean))
  const availableTemplates = (templates.data ?? []).filter((t) => !usedTemplateIds.has(t.id))
  const atLimit = rows.length >= MAX_SERVICE_ITEMS
  const addBlockedReason = atLimit ? `เพิ่มได้ไม่เกิน ${MAX_SERVICE_ITEMS} รายการต่อการส่งมอบ` : null

  const updateRow = (key: string, patch: Partial<ServiceRow>) =>
    setRows((old) => old.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  return (
    <section className="handover-service" aria-label="เลขไมล์และนัดครั้งถัดไป">
      <h4><Gauge aria-hidden="true" /> เลขไมล์และนัดเข้ารับบริการครั้งถัดไป</h4>
      <p className="handover-service__intake">
        เลขไมล์ขณะรับรถ:{' '}
        {intakeKm != null ? (
          <strong className="handover-service__compare">{formatKm(intakeKm)}</strong>
        ) : (
          <span className="job-detail-overdue" role="status">
            <TriangleAlert aria-hidden="true" /> ยังไม่ได้บันทึก
          </span>
        )}
      </p>
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      <div className="handover-service__grid">
        <Field label="เลขไมล์ตอนส่งมอบ (กม.) *" error={handoverError}>
          <Input inputMode="numeric" placeholder="เช่น 45230" value={handoverText} onChange={(e) => setHandoverText(e.target.value)} />
        </Field>
      </div>

      <div className="handover-service__items" role="group" aria-label="รายการนัดเข้ารับบริการครั้งถัดไป">
        <p className="handover-service__items-title">
          <CalendarClock aria-hidden="true" /> นัดเข้ารับบริการครั้งถัดไป <span className="section-help">อย่างน้อย 1 รายการ</span>
        </p>
        {rows.length === 0 ? (
          <p className="section-help" role="status">ยังไม่มีรายการนัด — เลือกจากรายการตั้งต้นหรือเพิ่มรายการเองด้านล่าง</p>
        ) : null}
        {rows.map((row) => {
          const errors = validateRow(row, rows, handoverKm.km)
          const months = parseMonths(row.monthsText)
          const mileageText = rowMileageText(row, handoverKm.km)
          const dueOn = months != null && !errors.months ? addMonthsToToday(months) : null
          return (
            <div className="handover-service__row" key={row.key}>
              <Field label="รายการ *" error={errors.name}>
                {row.customName ? (
                  <Input value={row.name} maxLength={100} placeholder="เช่น ลูกปืนล้อหน้า" onChange={(e) => updateRow(row.key, { name: e.target.value })} />
                ) : (
                  <strong className="handover-service__row-name">{row.name}</strong>
                )}
              </Field>
              <Field label="นัดที่ไมล์ (กม.) *" error={errors.mileage}>
                <Input
                  inputMode="numeric"
                  placeholder={handoverKm.km == null ? 'กรอกไมล์ส่งมอบก่อน' : 'เช่น 50000'}
                  value={mileageText}
                  onChange={(e) => updateRow(row.key, { mileageText: e.target.value, autoInterval: null })}
                />
              </Field>
              <Field label="อีก (เดือน) *" error={errors.months}>
                <Input inputMode="numeric" placeholder="เช่น 6" value={row.monthsText} onChange={(e) => updateRow(row.key, { monthsText: e.target.value })} />
              </Field>
              <div className="handover-service__row-side">
                {dueOn ? <span className="section-help">ราว {formatDateOnly(dueOn)}</span> : null}
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label={`ลบรายการ ${row.name.trim() || 'ใหม่'}`}
                  title="ลบรายการนี้"
                  onClick={() => setRows((old) => old.filter((r) => r.key !== row.key))}
                >
                  <Trash2 aria-hidden="true" />
                </Button>
              </div>
            </div>
          )
        })}

        <div className="handover-service__add">
          <Select
            aria-label="เพิ่มจากรายการตั้งต้นของสาขา"
            value=""
            disabled={atLimit || templates.isPending || availableTemplates.length === 0}
            title={addBlockedReason ?? (templates.isPending ? 'กำลังโหลดรายการตั้งต้น' : availableTemplates.length === 0 ? 'เลือกครบทุกรายการตั้งต้นแล้ว' : undefined)}
            onChange={(e) => {
              const template = availableTemplates.find((t) => t.id === e.target.value)
              if (template) setRows((old) => [...old, rowFromTemplate(template)])
            }}
          >
            <option value="">+ เพิ่มจากรายการตั้งต้น…</option>
            {availableTemplates.map((t) => (
              <option key={t.id} value={t.id}>{t.name} ({formatKm(t.defaultMileageInterval)} / {t.defaultMonths} เดือน)</option>
            ))}
          </Select>
          <Button
            type="button"
            variant="outline"
            disabled={atLimit}
            title={addBlockedReason ?? undefined}
            onClick={() => setRows((old) => [...old, customRow()])}
          >
            <Plus aria-hidden="true" /> เพิ่มรายการเอง
          </Button>
          {templates.isError ? <span className="section-help" role="status">โหลดรายการตั้งต้นไม่ได้ — พิมพ์รายการเองแทนได้</span> : null}
        </div>
        <p className="section-help">วันที่นัดนับจากวันที่ยืนยันส่งมอบจริง</p>
      </div>

      <div className="job-card-panel-actions">
        {blockedReason ? <span className="section-help">{blockedReason}</span>
          : !dirty ? <span className="section-help">บันทึกแล้ว</span> : null}
        <Button
          variant={dirty ? 'default' : 'outline'}
          disabled={Boolean(blockedReason) || !dirty || mutation.isPending}
          title={blockedReason ?? (!dirty ? 'ไม่มีการเปลี่ยนแปลง' : undefined)}
          onClick={() => mutation.mutate()}
        >
          {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึกเลขไมล์และนัดครั้งถัดไป'}
        </Button>
      </div>
    </section>
  )
}

/// รายการนัดแบบอ่านอย่างเดียว (หลังเซ็นส่งมอบ)
function ServiceItemList({ items }: { items: readonly HandoverServiceItem[] }) {
  if (items.length === 0) return <p className="section-help">ไม่มีนัดเข้ารับบริการครั้งถัดไป</p>
  return (
    <div className="handover-service__list">
      <p className="handover-service__items-title"><CalendarClock aria-hidden="true" /> นัดเข้ารับบริการครั้งถัดไป</p>
      <ul>
        {items.map((i) => (
          <li key={i.id}><strong>{i.name}</strong> — {nextServiceText(i.nextServiceMileage, i.nextServiceDueOn)}</li>
        ))}
      </ul>
    </div>
  )
}

/// "ที่ 50,000 กม. หรือวันที่ 08 เม.ย. 2570 (ตามกำหนดที่ถึงก่อน)"
export function nextServiceText(km: number | null | undefined, dueOn: string | null | undefined): string {
  if (km == null && !dueOn) return 'ไม่ระบุ'
  const parts = [km != null ? `ที่ ${formatKm(km)}` : null, dueOn ? `วันที่ ${formatDateOnly(dueOn)}` : null]
    .filter(Boolean)
  return parts.length === 2 ? `${parts.join(' หรือ ')} (ตามกำหนดที่ถึงก่อน)` : parts[0]!
}

function toText(value: number | null | undefined): string {
  return value == null ? '' : String(value)
}
