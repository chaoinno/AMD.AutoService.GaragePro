import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { CircleOff as ToggleLeft, Pencil, Plus } from 'lucide-react'
import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import {
  createServiceReminderTemplate,
  getServiceReminderTemplates,
  setServiceReminderTemplateStatus,
  updateServiceReminderTemplate,
  type ServiceReminderTemplate,
  type ServiceReminderTemplateInput,
} from '../../api/serviceReminders'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { ManagementTable } from '../../components/ManagementTable'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { formatKm, MAX_ODOMETER_KM, parseKmInput } from '../../lib/format'
import { useSession } from '../../lib/session'
import { MAX_MONTHS, MIN_MONTHS, SERVICE_ITEM_NAME_MAX } from '../jobs/handoverServiceRows'
import { Field, InlineError, PermissionNote, QueryState, StatusBadge } from './MasterDataCommon'

/// รายการนัดเข้ารับบริการตั้งต้นของสาขา — ตอนส่งมอบรถพนักงานเลือกจากรายการนี้แล้วแก้ไมล์/เดือนต่อคันได้
/// อ่านได้ทุกบทบาท · เพิ่ม/แก้/เปิดปิด = ผู้จัดการเท่านั้น (บังคับที่ API) · ไม่มีลบ ใช้ปิดใช้งานแทน
type FormState = { name: string; mileage: string; months: string; sortOrder: string }
const emptyForm: FormState = { name: '', mileage: '10000', months: '6', sortOrder: '0' }

export function ServiceReminderTemplatePage() {
  const { session } = useSession()
  const canManage = Boolean(session?.user.canSeeCost)
  const [includeInactive, setIncludeInactive] = useState(false)
  const [formTarget, setFormTarget] = useState<ServiceReminderTemplate | 'new' | null>(null)
  const [statusTarget, setStatusTarget] = useState<ServiceReminderTemplate | null>(null)
  const queryClient = useQueryClient()
  const query = useQuery({
    queryKey: ['service-reminder-templates', includeInactive ? 'all' : 'active'],
    queryFn: () => getServiceReminderTemplates(includeInactive),
  })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['service-reminder-templates'] })
  const status = useMutation({
    mutationFn: (target: ServiceReminderTemplate) => setServiceReminderTemplateStatus(target.id, !target.isActive),
    onSuccess: () => {
      toast.success(statusTarget?.isActive ? 'ปิดใช้งานรายการแล้ว' : 'เปิดใช้งานรายการแล้ว')
      setStatusTarget(null)
      refresh()
    },
  })
  const lockedTitle = 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขได้'

  return (
    <AppShell title="รายการนัดบริการ">
      <section className="page-heading">
        <div>
          <p className="eyebrow">ข้อมูลหลัก / งานบริการ</p>
          <h2>รายการนัดบริการ</h2>
          <p>รายการตั้งต้นที่ให้พนักงานเลือกตอนส่งมอบรถ เพื่อนัดลูกค้ากลับมาเข้ารับบริการครั้งถัดไป (เช่น น้ำมันเครื่อง กรอง เบรก) — ไมล์/เดือนที่ตั้งไว้เป็นค่าเริ่มต้น แก้ได้ต่อคัน</p>
        </div>
        <Button disabled={!canManage} title={!canManage ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่มรายการได้' : undefined} onClick={() => setFormTarget('new')}>
          <Plus aria-hidden="true" /> เพิ่มรายการ
        </Button>
      </section>
      <PermissionNote canManage={canManage} />
      <Card className="management-filters master-filters">
        <label className="filter-check">
          <input type="checkbox" checked={includeInactive} onChange={(e) => setIncludeInactive(e.target.checked)} /> รวมรายการปิดใช้งาน
        </label>
      </Card>
      <QueryState query={query} loadingTitle="กำลังโหลดรายการนัดบริการ" emptyTitle="ยังไม่มีรายการนัดบริการ" emptyReason="เพิ่มรายการเพื่อให้พนักงานเลือกตอนส่งมอบรถ" onRetry={() => void query.refetch()}>
        <Card className="management-table-card">
          <ManagementTable data={query.data ?? []} columns={[
            { id: 'name', header: 'รายการ', value: (t) => t.name, size: 260, render: (t) => <strong>{t.name}</strong> },
            { id: 'km', header: 'นัดห่างจากไมล์ส่งมอบ', value: (t) => t.defaultMileageInterval, size: 200, render: (t) => <span className="report-num">{formatKm(t.defaultMileageInterval)}</span> },
            { id: 'months', header: 'หรือภายใน', value: (t) => t.defaultMonths, size: 140, render: (t) => <span className="report-num">{t.defaultMonths} เดือน</span> },
            { id: 'order', header: 'ลำดับ', value: (t) => t.sortOrder, size: 90, render: (t) => <span className="report-num">{t.sortOrder}</span> },
            { id: 'status', header: 'สถานะ', value: (t) => (t.isActive ? 'ใช้งาน' : 'ปิดใช้งาน'), render: (t) => <StatusBadge isActive={t.isActive} /> },
            {
              id: 'actions', header: '', size: 110, render: (t) => (
                <div className="row-actions">
                  <Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? lockedTitle : 'แก้ไขรายการ'} aria-label={`แก้ไข ${t.name}`} onClick={() => setFormTarget(t)}><Pencil /></Button>
                  <Button size="icon" variant="ghost" disabled={!canManage} title={!canManage ? lockedTitle : t.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'} aria-label={`${t.isActive ? 'ปิด' : 'เปิด'}ใช้งาน ${t.name}`} onClick={() => setStatusTarget(t)}><ToggleLeft /></Button>
                </div>
              ),
            },
          ]} />
        </Card>
      </QueryState>
      <TemplateFormModal target={formTarget} onClose={() => setFormTarget(null)} onSaved={refresh} />
      <ConfirmModal
        open={Boolean(statusTarget)}
        title={statusTarget?.isActive ? 'ปิดใช้งานรายการ' : 'เปิดใช้งานรายการ'}
        description="ใบส่งมอบที่บันทึกไปแล้วไม่เปลี่ยน — รายการที่ปิดจะไม่ขึ้นให้เลือกตอนส่งมอบครั้งต่อไป และเปิดกลับมาได้"
        onClose={() => { setStatusTarget(null); status.reset() }}
        footer={<>
          <Button variant="ghost" onClick={() => setStatusTarget(null)}>ยกเลิก</Button>
          <Button variant={statusTarget?.isActive ? 'destructive' : 'default'} disabled={status.isPending} onClick={() => statusTarget && status.mutate(statusTarget)}>
            {status.isPending ? 'กำลังบันทึก…' : statusTarget?.isActive ? 'ปิดใช้งาน' : 'เปิดใช้งาน'}
          </Button>
        </>}
      >
        {status.isError ? <InlineError error={status.error} /> : null}
        <p>รายการ: <strong>{statusTarget?.name}</strong></p>
      </ConfirmModal>
    </AppShell>
  )
}

function TemplateFormModal({ target, onClose, onSaved }: { target: ServiceReminderTemplate | 'new' | null; onClose: () => void; onSaved: () => void }) {
  const open = target !== null
  const editing = target && target !== 'new' ? target : null
  const [form, setForm] = useState<FormState>(emptyForm)
  const [errors, setErrors] = useState<Partial<Record<keyof FormState, string>>>({})

  useEffect(() => {
    if (!open) return
    setErrors({})
    setForm(editing
      ? { name: editing.name, mileage: String(editing.defaultMileageInterval), months: String(editing.defaultMonths), sortOrder: String(editing.sortOrder) }
      : { ...emptyForm })
    // เปิดแต่ละครั้งเริ่มจากค่าของรายการที่เลือก — ไม่ผูกกับ object ที่ refetch เปลี่ยน reference
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editing?.id])

  const save = useMutation({
    mutationFn: (input: ServiceReminderTemplateInput) => editing ? updateServiceReminderTemplate(editing.id, input) : createServiceReminderTemplate(input),
    onSuccess: () => {
      toast.success(editing ? 'แก้ไขรายการเรียบร้อยแล้ว' : 'เพิ่มรายการเรียบร้อยแล้ว')
      onSaved()
      onClose()
    },
  })

  const set = (key: keyof FormState, value: string) => setForm((old) => ({ ...old, [key]: value }))
  const close = () => { save.reset(); onClose() }

  const submit = () => {
    const next: typeof errors = {}
    const name = form.name.trim()
    const km = parseKmInput(form.mileage)
    const months = /^\d+$/.test(form.months.trim()) ? Number(form.months.trim()) : null
    const order = /^\d+$/.test(form.sortOrder.trim()) ? Number(form.sortOrder.trim()) : null
    if (!name) next.name = 'กรุณากรอกชื่อรายการ'
    else if (name.length > SERVICE_ITEM_NAME_MAX) next.name = `ชื่อต้องยาวไม่เกิน ${SERVICE_ITEM_NAME_MAX} ตัวอักษร`
    if (km == null || km < 1 || km > MAX_ODOMETER_KM) next.mileage = `กรอกระยะทาง 1 ถึง ${formatKm(MAX_ODOMETER_KM)}`
    if (months == null || months < MIN_MONTHS || months > MAX_MONTHS) next.months = `กรอกจำนวนเดือน ${MIN_MONTHS}–${MAX_MONTHS}`
    if (order == null || order > 9999) next.sortOrder = 'กรอกลำดับ 0–9,999'
    setErrors(next)
    if (!Object.keys(next).length) save.mutate({ name, defaultMileageInterval: km!, defaultMonths: months!, sortOrder: order! })
  }

  return (
    <ConfirmModal
      open={open}
      title={editing ? 'แก้ไขรายการนัดบริการ' : 'เพิ่มรายการนัดบริการ'}
      description="ค่าที่ตั้งเป็นค่าเริ่มต้น พนักงานแก้ได้ต่อคันตอนส่งมอบรถ"
      onClose={close}
      size="medium"
      footer={<>
        <Button variant="ghost" onClick={close}>ยกเลิก</Button>
        <Button onClick={submit} disabled={save.isPending}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกข้อมูล'}</Button>
      </>}
    >
      <form className="management-form master-form" onSubmit={(e) => { e.preventDefault(); submit() }}>
        <section>
          <div className="form-grid">
            <Field label="ชื่อรายการ *" error={errors.name} wide>
              <Input value={form.name} maxLength={SERVICE_ITEM_NAME_MAX} placeholder="เช่น น้ำมันเครื่อง" onChange={(e) => set('name', e.target.value)} data-dialog-autofocus />
            </Field>
            <Field label="นัดห่างจากไมล์ส่งมอบ (กม.) *" error={errors.mileage}>
              <Input inputMode="numeric" value={form.mileage} onChange={(e) => set('mileage', e.target.value)} />
            </Field>
            <Field label="หรือภายใน (เดือน) *" error={errors.months}>
              <Input inputMode="numeric" value={form.months} onChange={(e) => set('months', e.target.value)} />
            </Field>
            <Field label="ลำดับที่แสดง" error={errors.sortOrder}>
              <Input inputMode="numeric" value={form.sortOrder} onChange={(e) => set('sortOrder', e.target.value)} />
            </Field>
          </div>
        </section>
        {save.isError ? <InlineError error={save.error} /> : null}
      </form>
    </ConfirmModal>
  )
}
