import {
  BarChart3,
  CalendarClock,
  ClipboardCheck,
  Eye,
  FileText,
  Gauge,
  Menu,
  ShieldCheck,
  Smartphone,
  Users,
  Wrench,
  X,
} from 'lucide-react'
import { useEffect, useState, type MouseEvent } from 'react'
import { ContactSection } from './ContactSection'
import { LoginCard } from './LoginCard'
import './landing.css'

const NAV_ITEMS = [
  { id: 'login', label: 'เข้าสู่ระบบ' },
  { id: 'about', label: 'เกี่ยวกับเรา' },
  { id: 'contact', label: 'ติดต่อ' },
] as const

type SectionId = (typeof NAV_ITEMS)[number]['id']

const FEATURES = [
  { icon: ClipboardCheck, title: 'รับรถ + เช็คสภาพ', body: 'เปิดจ๊อบ ถ่ายรูป เช็คลิสต์สภาพรถขณะรับ และพิมพ์ใบรับรถพร้อมช่องเซ็น' },
  { icon: FileText, title: 'ใบเสนอราคาแบบมีฉบับ', body: 'แก้ไขเป็นฉบับใหม่ได้โดยไม่ทับของเดิม ลูกค้าอนุมัติรายบรรทัดพร้อมลายเซ็น' },
  { icon: Wrench, title: 'ติดตามงานซ่อม', body: 'สถานะจ๊อบตั้งแต่รับรถจนส่งมอบ เบิกอะไหล่แบบ FIFO และตรวจ QC ก่อนส่งมอบ' },
  { icon: CalendarClock, title: 'ปฏิทินนัดหมาย', body: 'เห็นรถนัดหมายรายวัน/รายเดือน และแปลงเป็นรถในอู่ได้ทันทีเมื่อรถเข้า' },
  { icon: Smartphone, title: 'แอปมือถือสำหรับหน้างาน', body: 'ช่างจับเวลาทำงาน แชทในจ๊อบ และส่งมอบรถให้ลูกค้าเซ็นรับได้ที่ข้างรถ' },
  { icon: BarChart3, title: 'รายงานผู้บริหาร', body: 'แดชบอร์ดวันนี้ รอบเวลาทำงาน ยอดขาย-ต้นทุน-กำไร และอายุสต็อก' },
] as const

const VALUES = [
  { icon: Eye, title: 'Visibility', body: 'เห็นสถานะทุกคันแบบเรียลไทม์ ไม่ต้องเดินถามหน้างาน' },
  { icon: Users, title: 'Performance', body: 'ทุกฝ่ายทำงานบนข้อมูลชุดเดียวกัน ส่งต่องานได้ไม่ตกหล่น' },
  { icon: Gauge, title: 'Growth Intelligence', body: 'เปลี่ยนข้อมูลหน้างานเป็นตัวเลขต้นทุนและประสิทธิภาพที่ตัดสินใจได้' },
] as const

export function LoginPage() {
  const [menuOpen, setMenuOpen] = useState(false)
  const [active, setActive] = useState<SectionId>('login')

  useEffect(() => {
    document.title = 'GaragePro Auto Services | แพลตฟอร์มจัดการงานอู่บริการซ่อมบำรุงรถยนต์'
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
    NAV_ITEMS.forEach(({ id }) => {
      const el = document.getElementById(id)
      if (el) observer.observe(el)
    })
    return () => observer.disconnect()
  }, [])

  // ใช้ scrollIntoView แทน hash navigation เพื่อไม่ให้ URL เปลี่ยนเป็น /login#about
  // (router จะมองว่าเป็นคนละ location และปุ่ม back ของเบราว์เซอร์จะกระโดดไปมาใน section)
  function goTo(event: MouseEvent, id: SectionId) {
    event.preventDefault()
    setMenuOpen(false)
    // จอแคบ (< 1024px) การ์ดเข้าสู่ระบบถูกดันลงไปใต้ข้อความ hero — เลื่อนไปที่การ์ดโดยตรงไม่ใช่หัว section
    const target =
      id === 'login' && window.matchMedia('(max-width: 1023px)').matches
        ? document.getElementById('login-card')
        : document.getElementById(id)
    target?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    if (id === 'login') {
      // ผู้ใช้ที่กด "เข้าสู่ระบบ" ต้องการพิมพ์รหัสต่อทันที — preventScroll กันไม่ให้โฟกัสกระตุกการเลื่อนแบบ smooth
      window.setTimeout(() => document.getElementById('userName')?.focus({ preventScroll: true }), 350)
    }
  }

  return (
    <div className="landing">
      <header className="landing-nav">
        <div className="landing-container landing-nav__inner">
          <a className="auth-brand" href="#login" onClick={(e) => goTo(e, 'login')}>
            <img className="auth-brand__logo" src="/garagepro-logo.png" alt="" aria-hidden="true" />
            <span>
              <strong>GaragePro</strong>
              <small>Auto Services</small>
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
                className={item.id === 'login' ? 'landing-nav__link landing-nav__link--cta' : 'landing-nav__link'}
                aria-current={active === item.id ? 'true' : undefined}
                onClick={(e) => goTo(e, item.id)}
              >
                {item.label}
              </a>
            ))}
          </nav>
        </div>
      </header>

      <main>
        <section id="login" className="landing-hero" aria-labelledby="landing-hero-title">
          <div className="auth-page__ambient auth-page__ambient--one" />
          <div className="auth-page__ambient auth-page__ambient--two" />
          <div className="landing-container landing-hero__inner">
            <div className="landing-hero__copy">
              <span className="login-intro__eyebrow">
                <ShieldCheck aria-hidden="true" /> ระบบปฏิบัติการงานบริการสำหรับอู่ซ่อมรถ
              </span>
              <h1 id="landing-hero-title">
                จัดการงานอู่บริการซ่อมบำรุงรถยนต์
                <br />
                ได้ในแพลตฟอร์มเดียว
              </h1>
              <p>
                ตั้งแต่รับรถ เสนอราคา ซ่อม ตรวจ QC ชำระเงิน จนส่งมอบรถ — ทุกฝ่ายเห็นข้อมูลชุดเดียวกัน
                ทั้งบนเว็บและแอปมือถือ
              </p>
              <div className="landing-hero__actions">
                <a className="landing-btn landing-btn--primary" href="#contact" onClick={(e) => goTo(e, 'contact')}>
                  ขอ Demo
                </a>
                <a className="landing-btn landing-btn--ghost" href="#about" onClick={(e) => goTo(e, 'about')}>
                  รู้จัก GaragePro
                </a>
              </div>
              <p className="landing-hero__staff-note">พนักงานอู่: เข้าสู่ระบบด้วยรหัสพนักงานและรหัสผ่านเดิมของ GaragePro</p>
            </div>
            <LoginCard />
          </div>
        </section>

        <section className="landing-section landing-section--light" aria-labelledby="landing-features-title">
          <div className="landing-container">
            <p className="landing-eyebrow">สิ่งที่ระบบทำได้</p>
            <h2 id="landing-features-title" className="landing-h2">ครอบคลุมงานบริการทุกขั้นตอน</h2>
            <ul className="landing-feature-grid">
              {FEATURES.map(({ icon: Icon, title, body }) => (
                <li key={title} className="landing-feature">
                  <span className="landing-feature__icon" aria-hidden="true"><Icon /></span>
                  <h3>{title}</h3>
                  <p>{body}</p>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section id="about" className="landing-section" aria-labelledby="landing-about-title">
          <div className="landing-container landing-about">
            <div>
              <p className="landing-eyebrow">เกี่ยวกับเรา</p>
              <h2 id="landing-about-title" className="landing-h2">Armadillo Tech Co., Ltd.</h2>
              <p className="landing-lead">
                ผู้พัฒนา GaragePro — ระบบบริหารอู่ที่เข้าใจธุรกิจของคุณ
                และเปลี่ยนทุกข้อมูลหน้างานให้เป็นแรงขับเคลื่อนการเติบโต
              </p>
              <p className="landing-body">
                GaragePro Auto Services ต่อยอดจากระบบ GaragePro ที่อู่ซ่อมสีและตัวถังใช้งานอยู่ มาสู่งานบริการ
                ซ่อมบำรุงรถยนต์ โดยใช้ข้อมูลลูกค้าและรถชุดเดิม ไม่ต้องย้ายข้อมูลใหม่
              </p>
            </div>
            <ul className="landing-values">
              {VALUES.map(({ icon: Icon, title, body }) => (
                <li key={title}>
                  <span className="landing-feature__icon" aria-hidden="true"><Icon /></span>
                  <div>
                    <h3>{title}</h3>
                    <p>{body}</p>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <ContactSection />
      </main>

      <footer className="landing-footer">
        <div className="landing-container landing-footer__inner">
          <p>ระบบบริหารอู่ที่เข้าใจธุรกิจของคุณ และเปลี่ยนทุกข้อมูลหน้างานให้เป็นแรงขับเคลื่อนการเติบโต</p>
          <p>© {new Date().getFullYear()} GaragePro by Armadillo Tech Co., Ltd.</p>
        </div>
      </footer>
    </div>
  )
}
