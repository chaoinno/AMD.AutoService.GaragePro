import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ShieldCheck } from 'lucide-react'
import { type ReactNode, useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { toast } from 'sonner'
import { z } from 'zod'
import { isApiError } from '../../api/client'
import { createStaffQuick } from '../../api/staffs'
import type { StaffDetail } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'
import { useSession } from '../../lib/session'
import { STAFF_ROLE_OPTIONS, type StaffRoleValue } from '../plan/trialUx'
import '../plan/plan.css'

const ROLE_VALUES = STAFF_ROLE_OPTIONS.map((option) => option.value) as [StaffRoleValue, ...StaffRoleValue[]]

const schema = z.object({
  firstName: z.string().trim().min(1, 'กรุณากรอกชื่อ').max(100, 'ชื่อยาวเกินไป'),
  lastName: z.string().trim().min(1, 'กรุณากรอกนามสกุล').max(100, 'นามสกุลยาวเกินไป'),
  phoneNumber1: z.string().trim().regex(/^[0-9+()\-\s]{8,20}$/, 'รูปแบบเบอร์โทรไม่ถูกต้อง'),
  role: z.enum(ROLE_VALUES, { message: 'กรุณาเลือกบทบาท' }),
})
type FormValues = z.infer<typeof schema>

const blank: FormValues = { firstName: '', lastName: '', phoneNumber1: '', role: 'Technician' }

/**
 * เพิ่มพนักงานแบบง่ายสำหรับอู่ที่สมัครเอง (ชื่อ/นามสกุล/เบอร์/บทบาท) — server เติมเพศ/ตำแหน่ง/แผนกตามบทบาท
 * และตั้งชื่อผู้ใช้ + รหัสผ่านให้เอง (คืนรหัสผ่านครั้งเดียว) · ลูกค้าเดิมยังใช้ฟอร์มเต็มเหมือนเดิม
 */
export function SimpleStaffCreateDialog({
  open,
  onClose,
  onCreated,
}: {
  open: boolean
  onClose: () => void
  onCreated: (detail: StaffDetail) => void
}) {
  const { session } = useSession()
  const queryClient = useQueryClient()
  const { register, handleSubmit, reset, watch, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: blank,
  })
  const save = useMutation({
    mutationFn: (values: FormValues) => createStaffQuick({
      branchId: session?.branchId || 0,
      firstName: values.firstName,
      lastName: values.lastName,
      phoneNumber1: values.phoneNumber1,
      role: values.role,
    }),
    onSuccess: (detail) => {
      toast.success('เพิ่มพนักงานและสร้างบัญชีเข้าแอปแล้ว')
      void queryClient.invalidateQueries({ queryKey: ['staffs'] })
      void queryClient.invalidateQueries({ queryKey: ['selfserve-status'] })
      onCreated(detail)
      onClose()
    },
  })
  const { reset: resetMutation } = save

  useEffect(() => {
    if (!open) return
    reset(blank)
    resetMutation()
  }, [open, reset, resetMutation])

  const close = () => {
    if (save.isPending) return
    onClose()
  }

  const roleHint = STAFF_ROLE_OPTIONS.find((option) => option.value === watch('role'))?.hint

  return (
    <ConfirmModal
      open={open}
      title="เพิ่มพนักงาน"
      description="กรอกแค่ชื่อ เบอร์โทร และบทบาท ระบบสร้างชื่อผู้ใช้และรหัสผ่านเข้าแอปให้"
      onClose={close}
      size="medium"
      footer={
        <>
          <Button variant="ghost" disabled={save.isPending} onClick={close}>ยกเลิก</Button>
          <Button type="submit" form="simple-staff-form" disabled={save.isPending}>
            {save.isPending ? 'กำลังบันทึก…' : 'เพิ่มพนักงาน'}
          </Button>
        </>
      }
    >
      <form
        id="simple-staff-form"
        className="simple-staff-form"
        onSubmit={handleSubmit((values) => save.mutate(values), () => toast.error('กรุณาตรวจสอบข้อมูลที่ระบุไม่ครบหรือรูปแบบไม่ถูกต้อง'))}
      >
        <div className="form-grid">
          <Field label="ชื่อ *" error={errors.firstName?.message}>
            <Input autoComplete="off" data-dialog-autofocus {...register('firstName')} />
          </Field>
          <Field label="นามสกุล *" error={errors.lastName?.message}>
            <Input autoComplete="off" {...register('lastName')} />
          </Field>
          <Field label="เบอร์โทร *" error={errors.phoneNumber1?.message}>
            <Input inputMode="tel" autoComplete="off" placeholder="08x-xxx-xxxx" {...register('phoneNumber1')} />
          </Field>
          <Field label="บทบาท *" error={errors.role?.message}>
            <Select {...register('role')}>
              {STAFF_ROLE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>{option.label}</option>
              ))}
            </Select>
          </Field>
        </div>
        {roleHint ? <p className="simple-staff-form__hint">บทบาทนี้: {roleHint}</p> : null}
        <div className="credential-preview">
          <ShieldCheck aria-hidden="true" />
          <div>
            <strong>ระบบสร้างบัญชีเข้าแอปให้อัตโนมัติ</strong>
            <p>หลังบันทึกจะแสดงชื่อผู้ใช้และรหัสผ่านครั้งเดียว ให้คัดลอกส่งให้พนักงานใช้เข้าแอป ServicePro บนมือถือ</p>
          </div>
        </div>
        {save.isError ? (
          <Alert variant="destructive">
            <AlertTriangle aria-hidden="true" />
            <div>
              <AlertTitle>{isApiError(save.error) ? save.error.messageTh : 'เพิ่มพนักงานไม่สำเร็จ'}</AlertTitle>
              <AlertDescription>
                รหัสติดตาม (traceId): {isApiError(save.error) ? save.error.traceId : 'ไม่พบรหัสติดตาม'}
              </AlertDescription>
            </div>
          </Alert>
        ) : null}
      </form>
    </ConfirmModal>
  )
}

function Field({ label, error, children }: { label: string; error?: string; children: ReactNode }) {
  return (
    <Label className="field">
      <span>{label}</span>
      {children}
      {error ? <small className="field-error">{error}</small> : null}
    </Label>
  )
}
