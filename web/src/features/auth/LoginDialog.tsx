import { LockKeyhole } from 'lucide-react'
import { useState } from 'react'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '../../components/ui/dialog'
import { LoginForm } from './LoginForm'

/**
 * modal เข้าสู่ระบบบนหน้าแรก — การเปิด/ปิดผูกกับ URL `/login` (ดู LandingPage) จึงไม่มี state เปิดของตัวเอง
 * ระหว่างกำลังเข้าสู่ระบบไม่ยอมให้ปิด (Escape/คลิกพื้นหลัง/ปุ่ม X) — ถ้าปิดกลางทาง URL จะกลับเป็น `/`
 * แล้ว onSuccess ยัง navigate ไป /jobs ซ้อนอีกรอบ
 */
export function LoginDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [pending, setPending] = useState(false)

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && !pending) onClose()
      }}
    >
      <DialogContent className="ui-dialog__content--small login-dialog">
        <DialogHeader>
          <div className="login-dialog__icon" aria-hidden="true">
            <LockKeyhole />
          </div>
          <DialogTitle>เข้าสู่ระบบ ServicePro</DialogTitle>
          <DialogDescription>เจ้าของอู่ที่สมัครเอง: อีเมล · พนักงาน: รหัสจากเจ้าของอู่ · พนักงานอู่ GaragePro: รหัสพนักงานเดิม</DialogDescription>
        </DialogHeader>
        <div className="ui-dialog__body">
          <LoginForm onPendingChange={setPending} />
        </div>
      </DialogContent>
    </Dialog>
  )
}
