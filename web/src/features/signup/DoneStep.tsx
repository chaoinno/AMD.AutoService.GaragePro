import { CheckCircle2, Copy, Monitor } from 'lucide-react'
import { Link } from 'react-router'
import { useEffect, useRef } from 'react'
import { toast } from 'sonner'
import type { SignupResult } from '../../api/signup'
import { PLAY_STORE_URL } from '../landing/appStore'

export function DoneStep({ result }: { result: SignupResult }) {
  const headingRef = useRef<HTMLHeadingElement>(null)
  useEffect(() => { headingRef.current?.focus() }, [])
  async function copy(value: string) {
    try {
      await navigator.clipboard.writeText(value)
      toast.success('คัดลอกแล้ว')
    } catch {
      toast.error('คัดลอกไม่สำเร็จ กรุณาเลือกข้อความแล้วคัดลอกเอง', { description: 'รหัสติดตาม (traceId): ไม่ใช่คำขอ API' })
    }
  }
  return (
    <div className="signup-done">
      <CheckCircle2 className="signup-done__icon" aria-hidden="true" />
      <h1 ref={headingRef} tabIndex={-1}>เปิดอู่ {result.workspaceNo} สำเร็จ</h1>
      <p><Monitor size={20} aria-hidden="true" /> ระบบงานบนเว็บใช้บนคอมพิวเตอร์ (จอกว้าง 1024px ขึ้นไป)</p>
      <a className="signup-play" href={PLAY_STORE_URL} target="_blank" rel="noopener noreferrer">
        <img src="/store/google-play-badge-th.png" alt="ดาวน์โหลดแอปช่างบน Google Play (เปิดในแท็บใหม่)" width={646} height={250} />
      </a>
      {result.sampleTechnicians?.length ? (
        <section className="signup-credentials" aria-labelledby="signup-credentials-title">
          <h2 id="signup-credentials-title">บัญชีช่างตัวอย่าง</h2>
          <p>แสดงครั้งเดียว เก็บไว้ให้ช่างใช้เข้าแอป</p>
          {result.sampleTechnicians.map((technician) => (
            <div className="signup-credential" key={technician.userName}>
              <div><span>รหัสเข้าแอป</span><code>{technician.userName}</code><button type="button" className="signup-copy" aria-label={`คัดลอกรหัสเข้าแอป ${technician.userName}`} onClick={() => void copy(technician.userName)}><Copy size={18} aria-hidden="true" /> คัดลอก</button></div>
              <div><span>รหัสผ่าน</span><code>{technician.password}</code><button type="button" className="signup-copy" aria-label={`คัดลอกรหัสผ่านของ ${technician.userName}`} onClick={() => void copy(technician.password)}><Copy size={18} aria-hidden="true" /> คัดลอก</button></div>
            </div>
          ))}
        </section>
      ) : null}
      <Link className="landing-btn landing-btn--primary" to="/jobs?welcome=1">ไปหน้าระบบงาน</Link>
    </div>
  )
}
