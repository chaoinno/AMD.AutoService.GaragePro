import { Database, LogIn, MonitorSmartphone, ShieldCheck, Sparkles } from 'lucide-react'
import { PhoneMockup } from '../mockups/PhoneMockup'
import { WebAppMockup } from '../mockups/WebAppMockup'

// [BIZ] จุดเด่นต้องเป็นข้อเท็จจริงของระบบเท่านั้น — ห้ามใส่จำนวนลูกค้า/สถิติ/รีวิวที่ไม่มีจริง
const HIGHLIGHTS = [
  { icon: Database, title: 'ใช้ข้อมูลลูกค้าและรถชุดเดิม', body: 'ต่อยอดจาก GaragePro ไม่ต้องย้ายข้อมูลใหม่' },
  { icon: MonitorSmartphone, title: 'เว็บสำหรับออฟฟิศ แอปสำหรับหน้างาน', body: 'ทุกฝ่ายเห็นสถานะรถคันเดียวกันพร้อมกัน' },
  { icon: ShieldCheck, title: 'ทุกขั้นตอนมีหลักฐาน', body: 'รูปถ่าย ลายเซ็นลูกค้า และประวัติทุกการเปลี่ยนสถานะ' },
] as const

type Props = {
  startLabel: string
  onStart: () => void
  onRequestDemo: () => void
}

export function HeroSection({ startLabel, onStart, onRequestDemo }: Props) {
  return (
    <section id="top" className="landing-hero" aria-labelledby="landing-hero-title">
      <div className="auth-page__ambient auth-page__ambient--one" />
      <div className="auth-page__ambient auth-page__ambient--two" />
      <div className="landing-container landing-hero__inner">
        <div className="landing-hero__copy">
          <span className="login-intro__eyebrow">
            <Sparkles aria-hidden="true" /> แพลตฟอร์มบริหารงานบริการสำหรับอู่ซ่อมบำรุงรถยนต์
          </span>
          <h1 id="landing-hero-title">
            ทุกคันในอู่ <span className="landing-hero__accent">ชัดเจน</span>
            <br />
            ตั้งแต่รับรถจนส่งมอบ
          </h1>
          <p>
            ServicePro รวมงานรับรถ ใบเสนอราคา งานซ่อม QC ชำระเงิน และส่งมอบรถไว้ในระบบเดียว
            หน้าร้าน ช่าง แคชเชียร์ และผู้บริหารทำงานบนข้อมูลชุดเดียวกัน ทั้งบนเว็บและแอปมือถือ
          </p>
          <div className="landing-hero__actions">
            <button type="button" className="landing-btn landing-btn--primary" onClick={onStart}>
              <LogIn aria-hidden="true" /> {startLabel}
            </button>
            <button type="button" className="landing-btn landing-btn--ghost" onClick={onRequestDemo}>
              ขอ Demo
            </button>
          </div>
          <p className="landing-hero__staff-note">
            พนักงานอู่: กด “{startLabel}” แล้วเข้าสู่ระบบด้วยรหัสพนักงานและรหัสผ่านเดิมของ GaragePro
          </p>
        </div>
        <div className="landing-hero__visual">
          <WebAppMockup />
          <PhoneMockup variant="queue" className="landing-hero__phone" />
        </div>
      </div>
      <div className="landing-container">
        <ul className="landing-highlights">
          {HIGHLIGHTS.map(({ icon: Icon, title, body }) => (
            <li key={title}>
              <span className="landing-highlights__icon" aria-hidden="true"><Icon /></span>
              <span>
                <strong>{title}</strong>
                <small>{body}</small>
              </span>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}
