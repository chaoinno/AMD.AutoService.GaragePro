import { useMutation } from '@tanstack/react-query'
import { CalendarClock, Gauge, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { saveHandoverServiceInfo, type Handover } from '../../api/handover'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { addMonthsToToday, formatDateOnly, formatKm } from '../../lib/format'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import { mileageInputError } from './mileage'

const NEXT_KM_STEPS = [5_000, 10_000] as const
const MONTH_PRESETS = [3, 6, 12] as const
// [ASSUME] ต้องตรงกับ ServiceSchedule.MinMonths/MaxMonths ฝั่ง backend
const MIN_MONTHS = 1
const MAX_MONTHS = 24

/// [BIZ] ข้อมูลส่งมอบที่บังคับก่อนเซ็น (HANDOVER_SERVICE_INFO_REQUIRED): ไมล์ตอนส่งมอบ + นัดครั้งถัดไป (ไมล์ + จำนวนเดือน)
/// ผู้ใช้กรอกเป็นเดือน ระบบคำนวณวันที่ให้ — ก่อนเซ็นวันที่เป็นพรีวิว server คำนวณใหม่จากวันส่งมอบจริงตอนเซ็น
export function handoverServiceInfoMissing(handover: Handover): boolean {
  return handover.mileageAtHandover == null || handover.nextServiceMileage == null || handover.nextServiceMonths == null
}

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
          <div>
            <dt>นัดเข้ารับบริการครั้งถัดไป</dt>
            <dd>{nextServiceText(handover.nextServiceMileage, handover.nextServiceDueOn)}</dd>
          </div>
        </dl>
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
  const [nextText, setNextText] = useState(() => toText(handover.nextServiceMileage))
  const [monthsText, setMonthsText] = useState(() => toText(handover.nextServiceMonths))

  const handoverKm = mileageInputError(handoverText)
  const nextKm = mileageInputError(nextText)
  const months = /^\d+$/.test(monthsText.trim()) ? Number(monthsText.trim()) : null

  // มิเรอร์ HandoverService.ValidateServiceInfo — server ตรวจซ้ำเสมอ
  const handoverError = handoverKm.error
    ?? (handoverKm.km != null && intakeKm != null && handoverKm.km < intakeKm
      ? `ต้องไม่น้อยกว่าเลขไมล์ขณะรับรถ (${formatKm(intakeKm)})`
      : undefined)
  const nextError = nextKm.error
    ?? (nextKm.km != null && handoverKm.km != null && nextKm.km <= handoverKm.km
      ? `ต้องมากกว่าเลขไมล์ตอนส่งมอบ (${formatKm(handoverKm.km)})`
      : undefined)
  const monthsError = monthsText.trim() && (months == null || months < MIN_MONTHS || months > MAX_MONTHS)
    ? `กรอกจำนวนเดือน ${MIN_MONTHS}–${MAX_MONTHS}`
    : undefined

  const dirty = handoverKm.km !== (handover.mileageAtHandover ?? null)
    || nextKm.km !== (handover.nextServiceMileage ?? null)
    || months !== (handover.nextServiceMonths ?? null)

  const blockedReason = intakeKm == null
    ? 'งานนี้ยังไม่มีเลขไมล์ขณะรับรถ — บันทึกที่ขั้น "รับรถ" ของการ์ดนี้ก่อน'
    : handoverKm.km == null ? 'กรอกเลขไมล์ตอนส่งมอบ'
      : nextKm.km == null ? 'กรอกเลขไมล์ที่นัดครั้งถัดไป'
        : months == null ? 'เลือกระยะเวลานัดครั้งถัดไป (เดือน)'
          : handoverError ?? nextError ?? monthsError ?? null

  const mutation = useMutation({
    mutationFn: () => saveHandoverServiceInfo(jobId, {
      mileageAtHandover: handoverKm.km!,
      nextServiceMileage: nextKm.km!,
      nextServiceMonths: months!,
    }),
    onSuccess: () => {
      toast.success('บันทึกเลขไมล์และนัดครั้งถัดไปแล้ว')
      onSaved()
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'บันทึกไม่สำเร็จ'),
  })

  const previewDue = months != null && !monthsError ? addMonthsToToday(months) : null

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
        <Field label="นัดครั้งถัดไปที่เลขไมล์ (กม.) *" error={nextError}>
          <Input inputMode="numeric" placeholder="เช่น 50000" value={nextText} onChange={(e) => setNextText(e.target.value)} />
          <div className="handover-service__presets">
            {NEXT_KM_STEPS.map((step) => (
              <Button
                key={step}
                type="button"
                variant="outline"
                size="sm"
                disabled={handoverKm.km == null}
                title={handoverKm.km == null ? 'กรอกเลขไมล์ตอนส่งมอบก่อน' : undefined}
                onClick={() => setNextText(String(handoverKm.km! + step))}
              >
                +{formatKm(step)}
              </Button>
            ))}
          </div>
        </Field>
        <Field label="นัดครั้งถัดไปอีก (เดือน) *" error={monthsError}>
          <Input inputMode="numeric" placeholder="เช่น 6" value={monthsText} onChange={(e) => setMonthsText(e.target.value)} />
          <div className="handover-service__presets">
            {MONTH_PRESETS.map((m) => (
              <Button
                key={m}
                type="button"
                variant={months === m ? 'default' : 'outline'}
                size="sm"
                onClick={() => setMonthsText(String(m))}
              >
                {m} เดือน
              </Button>
            ))}
          </div>
        </Field>
      </div>
      {previewDue ? (
        <p className="handover-service__preview">
          <CalendarClock aria-hidden="true" />
          นัดครั้งถัดไป: {nextServiceText(nextKm.km, previewDue)}
          <span className="section-help"> — วันที่นับจากวันที่ยืนยันส่งมอบจริง</span>
        </p>
      ) : null}
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
