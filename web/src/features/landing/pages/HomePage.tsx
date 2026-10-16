import { Phone, Smartphone } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { usePageMeta } from '../../../lib/seo'
import { useSession } from '../../../lib/session'
import { LoginDialog } from '../../auth/LoginDialog'
import { FAQ_ITEMS } from '../faq'
import { FeaturesSection } from '../sections/FeaturesSection'
import { HeroSection } from '../sections/HeroSection'
import { WorkflowSection } from '../sections/WorkflowSection'

export function HomePage() {
  const location = useLocation()
  const navigate = useNavigate()
  const { session } = useSession()
  const [mounted, setMounted] = useState(false)
  usePageMeta(location.pathname)

  // Portals need a browser. Keep the dialog closed during SSR and the first hydration render.
  useEffect(() => setMounted(true), [])

  function closeLogin() {
    if ((location.state as { fromLanding?: boolean } | null)?.fromLanding) navigate(-1)
    else navigate('/', { replace: true })
  }

  if (location.pathname === '/login' && session) return <Navigate to="/jobs" replace />

  return (
    <>
      <HeroSection hasSession={Boolean(session)} />
      <FeaturesSection summary />
      <WorkflowSection />
      <section className="landing-section landing-section--light" aria-labelledby="mobile-teaser-title">
        <div className="landing-container">
          <p className="landing-eyebrow"><Smartphone aria-hidden="true" /> แอปช่าง</p>
          <h2 id="mobile-teaser-title" className="landing-h2">งานหน้างาน เชื่อมกับระบบอู่บริการ</h2>
          <p className="landing-section__intro">
            รับรถพร้อมถ่ายรูป จับเวลางานซ่อม แชทในจ๊อบ และให้ลูกค้าเซ็นรับรถผ่านแอป ServicePro
            ใช้บัญชีพนักงานเดียวกับเว็บ ดาวน์โหลดได้ทั้ง Android และ iPhone
          </p>
          <Link className="landing-text-link" to="/mobile-app">ดูแอปสำหรับช่าง →</Link>
        </div>
      </section>
      <section className="landing-section" aria-labelledby="faq-title">
        <div className="landing-container">
          <p className="landing-eyebrow">คำถามที่พบบ่อย</p>
          <h2 id="faq-title" className="landing-h2">รู้จักโปรแกรมสำหรับอู่บริการ ServicePro</h2>
          <div className="landing-faq">
            {FAQ_ITEMS.map(({ q, a }) => (
              <details key={q}>
                <summary>{q}</summary>
                <p>{a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>
      <section className="landing-cta" aria-labelledby="landing-cta-title">
        <div className="landing-container landing-cta__inner">
          <div>
            <h2 id="landing-cta-title">อยากเห็น ServicePro ทำงานกับอู่ของคุณ?</h2>
            <p>ทีมงานพร้อมสาธิตทุกขั้นตอน ตั้งแต่รับรถจนส่งมอบ ทั้งบนเว็บและแอปมือถือ</p>
          </div>
          <div className="landing-cta__actions">
            <Link className="landing-btn landing-btn--light" to="/signup">ทดลองใช้ฟรี</Link>
            <Link className="landing-btn landing-btn--ghost" to="/contact">ขอ Demo</Link>
            <a className="landing-btn landing-btn--ghost" href="tel:0909966446"><Phone aria-hidden="true" /> 090-996-6446</a>
          </div>
        </div>
      </section>
      <LoginDialog open={mounted && location.pathname === '/login'} onClose={closeLogin} />
    </>
  )
}
