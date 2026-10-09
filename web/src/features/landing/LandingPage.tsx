import { LogIn, Menu, Phone, X } from 'lucide-react'
import { useEffect, useState, type MouseEvent } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { BrandWordmark } from '../../components/Brand'
import { LoginDialog } from '../auth/LoginDialog'
import { PLAY_STORE_URL } from './appStore'
import { ContactSection } from './ContactSection'
import { AboutSection } from './sections/AboutSection'
import { FeaturesSection } from './sections/FeaturesSection'
import { HeroSection } from './sections/HeroSection'
import { MobileAppSection } from './sections/MobileAppSection'
import { WorkflowSection } from './sections/WorkflowSection'
import './landing.css'

const NAV_ITEMS = [
  { id: 'features', label: 'ฟีเจอร์' },
  { id: 'workflow', label: 'ขั้นตอนการทำงาน' },
  { id: 'mobile', label: 'แอปมือถือ' },
  { id: 'about', label: 'เกี่ยวกับเรา' },
  { id: 'contact', label: 'ติดต่อ' },
] as const

type SectionId = (typeof NAV_ITEMS)[number]['id'] | 'top'

type Props = {
  /** true เมื่อ URL เป็น `/login` — modal เข้าสู่ระบบผูกกับ URL เพื่อให้ปุ่ม back ปิดได้ และ 401/ProtectedRoute ที่ส่งมา `/login` เด้ง modal เอง */
  loginOpen: boolean
  /** มีเซสชันอยู่แล้ว: ปุ่มหลักพาเข้าระบบงานตรงๆ ไม่ต้องเปิด modal */
  hasSession: boolean
}

/**
 * หน้าแรกสาธารณะ (`/` และ `/login`) — หน้านำเสนอแพลตฟอร์มเพื่อการขาย
 * [UI] หน้าเดียวที่รองรับจอ < 1024px (App.tsx แสดงนอก desktop-guard) เพราะผู้สนใจมักเปิดจากมือถือ
 */
export function LandingPage({ loginOpen, hasSession }: Props) {
  const navigate = useNavigate()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const [active, setActive] = useState<SectionId>('top')
  const startLabel = hasSession ? 'เข้าสู่ระบบงาน' : 'เริ่มใช้งาน'

  useEffect(() => {
    document.title = 'ServicePro | แพลตฟอร์มบริหารงานอู่บริการซ่อมบำรุงรถยนต์'
  }, [])

  // [UI] ไฮไลต์เมนูตาม section ที่อยู่ในจอ — ใช้ IntersectionObserver แทนการฟัง scroll ทุกเฟรม
  useEffect(() => {
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries.filter((e) => e.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0]
        if (visible) setActive(visible.target.id as SectionId)
      },
      { rootMargin: '-40% 0px -50% 0px', threshold: [0, 0.25, 0.5] },
    )
    ;(['top', ...NAV_ITEMS.map((item) => item.id)] as SectionId[]).forEach((id) => {
      const el = document.getElementById(id)
      if (el) observer.observe(el)
    })
    return () => observer.disconnect()
  }, [])

  // ใช้ scrollIntoView แทน hash navigation เพื่อไม่ให้ URL เปลี่ยนเป็น /#about
  // (router จะมองว่าเป็นคนละ location และปุ่ม back ของเบราว์เซอร์จะกระโดดไปมาใน section)
  function scrollToSection(id: SectionId) {
    setMenuOpen(false)
    if (id === 'top') window.scrollTo({ top: 0, behavior: 'smooth' })
    else document.getElementById(id)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  function goTo(event: MouseEvent, id: SectionId) {
    event.preventDefault()
    scrollToSection(id)
  }

  function start() {
    setMenuOpen(false)
    if (hasSession) navigate('/jobs')
    else navigate('/login', { state: { fromLanding: true } })
  }

  // เปิดจากปุ่มบนหน้านี้ → ย้อน history (ไม่ทิ้ง `/` ซ้ำสองรายการ) · เปิดจากลิงก์ตรง/401 → แทนที่ด้วย `/`
  function closeLogin() {
    if ((location.state as { fromLanding?: boolean } | null)?.fromLanding) navigate(-1)
    else navigate('/', { replace: true })
  }

  return (
    <div className="landing">
      <header className="landing-nav">
        <div className="landing-container landing-nav__inner">
          <a className="auth-brand" href="/" onClick={(e) => goTo(e, 'top')}>
            <img className="auth-brand__logo" src="/servicepro-logo.png" alt="" aria-hidden="true" />
            <span>
              <BrandWordmark />
            </span>
          </a>
          <button
            type="button"
            className="landing-nav__toggle"
            aria-label={menuOpen ? 'ปิดเมนู' : 'เปิดเมนู'}
            aria-expanded={menuOpen}
            aria-controls="landing-menu"
            onClick={() => setMenuOpen((open) => !open)}
          >
            {menuOpen ? <X aria-hidden="true" /> : <Menu aria-hidden="true" />}
          </button>
          <nav id="landing-menu" className="landing-nav__menu" data-open={menuOpen} aria-label="เมนูหลัก">
            {NAV_ITEMS.map((item) => (
              <a
                key={item.id}
                href={`#${item.id}`}
                className="landing-nav__link"
                aria-current={active === item.id ? 'true' : undefined}
                onClick={(e) => goTo(e, item.id)}
              >
                {item.label}
              </a>
            ))}
            <button type="button" className="landing-nav__link landing-nav__link--cta" onClick={start}>
              <LogIn aria-hidden="true" /> {startLabel}
            </button>
          </nav>
        </div>
      </header>

      <main>
        <HeroSection startLabel={startLabel} onStart={start} onRequestDemo={() => scrollToSection('contact')} />
        <FeaturesSection />
        <WorkflowSection />
        <MobileAppSection />
        <AboutSection />

        <section className="landing-cta" aria-labelledby="landing-cta-title">
          <div className="landing-container landing-cta__inner">
            <div>
              <h2 id="landing-cta-title">อยากเห็น ServicePro ทำงานกับอู่ของคุณ?</h2>
              <p>ทีมงานพร้อมสาธิตทุกขั้นตอน ตั้งแต่รับรถจนส่งมอบ ทั้งบนเว็บและแอปมือถือ</p>
            </div>
            <div className="landing-cta__actions">
              <button type="button" className="landing-btn landing-btn--light" onClick={() => scrollToSection('contact')}>
                ขอ Demo
              </button>
              <a className="landing-btn landing-btn--ghost" href="tel:0909966446">
                <Phone aria-hidden="true" /> 090-996-6446
              </a>
            </div>
          </div>
        </section>

        <ContactSection />
      </main>

      <footer className="landing-footer">
        <div className="landing-container landing-footer__inner">
          <div className="landing-footer__brand">
            <span className="auth-brand">
              <img className="auth-brand__logo" src="/servicepro-logo.png" alt="" aria-hidden="true" />
              <span><BrandWordmark /></span>
            </span>
            <p>ระบบบริหารอู่ที่เข้าใจธุรกิจของคุณ และเปลี่ยนทุกข้อมูลหน้างานให้เป็นแรงขับเคลื่อนการเติบโต</p>
          </div>
          <nav className="landing-footer__links" aria-label="ลิงก์ท้ายหน้า">
            {NAV_ITEMS.map((item) => (
              <a key={item.id} href={`#${item.id}`} onClick={(e) => goTo(e, item.id)}>{item.label}</a>
            ))}
            <a href={PLAY_STORE_URL} target="_blank" rel="noopener noreferrer">แอป Android บน Google Play</a>
          </nav>
        </div>
        <div className="landing-container landing-footer__legal">
          <p>© {new Date().getFullYear()} ServicePro by Armadillo Tech Co., Ltd.</p>
          <p>Google Play และโลโก้ Google Play เป็นเครื่องหมายการค้าของ Google LLC</p>
        </div>
      </footer>

      <LoginDialog open={loginOpen} onClose={closeLogin} />
    </div>
  )
}
