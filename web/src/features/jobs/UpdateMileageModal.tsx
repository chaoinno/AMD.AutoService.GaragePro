import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { toast } from 'sonner'
import { updateJobMileage } from '../../api/jobs'
import type { Job } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import { mileageInputError } from './mileage'

/// เลขไมล์ขณะรับรถ — แก้ได้จนกว่าจะส่งมอบรถ (server ตอบ JOB_MILEAGE_LOCKED หลังส่งมอบ) และบันทึกประวัติทุกครั้ง
export function UpdateMileageModal({ job, onClose }: { job: Job; onClose: () => void }) {
  const queryClient = useQueryClient()
  const [text, setText] = useState(() => (job.mileageAtIntake != null ? String(job.mileageAtIntake) : ''))
  const { km, error } = mileageInputError(text)

  const mutation = useMutation({
    mutationFn: () => updateJobMileage(job.jobId, { mileageAtIntake: km! }),
    onSuccess: () => {
      toast.success('บันทึกเลขไมล์แล้ว')
      void queryClient.invalidateQueries({ queryKey: ['job-detail', job.jobId] })
      void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
      void queryClient.invalidateQueries({ queryKey: ['handover', job.jobId] })
      onClose()
    },
  })

  return (
    <ConfirmModal
      open
      title={job.mileageAtIntake != null ? 'แก้ไขเลขไมล์ขณะรับรถ' : 'บันทึกเลขไมล์ขณะรับรถ'}
      description="อ่านจากหน้าปัดรถตอนรับรถ — แก้ไขได้จนกว่าจะส่งมอบรถ ระบบบันทึกประวัติการแก้ไขไว้ในจ๊อบนี้"
      onClose={onClose}
      size="small"
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>ยกเลิก</Button>
          <Button
            disabled={km == null || mutation.isPending}
            title={km == null ? (error ?? 'กรุณากรอกเลขไมล์ก่อน') : undefined}
            onClick={() => mutation.mutate()}
          >
            {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึก'}
          </Button>
        </>
      }
    >
      {mutation.isError ? <InlineError error={mutation.error} /> : null}
      <Field label="เลขไมล์ (กม.) *" error={error}>
        <Input inputMode="numeric" autoFocus value={text} onChange={(e) => setText(e.target.value)} placeholder="เช่น 45210" />
      </Field>
    </ConfirmModal>
  )
}
