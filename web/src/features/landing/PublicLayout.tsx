import { LogIn, Menu, X } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router'
import { BrandWordmark } from '../../components/Brand'
import { useSession } from '../../lib/session'
import { PLAY_STORE_URL } from './appStore'
import './landing.css'

const NAV_ITEMS = [
  { path: '/', label: 'หน้าแรก' },
  { path: '/features', label: 'ฟีเจอร์' },
  { path: '/mobile-app', label: 'แอปช่าง' },
  { path: '/pricing', label: 'แพ็กเกจ' },
  { path: '/about', label: 'เกี่ยวกับเรา' },
  { path: '/contact', label: 'ติดต่อเรา' },
] as const

export function PublicLayout() {
  const { session } = useSession()
  const { pathname } = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const toggleRef = useRef<HTMLButtonElement>(null)
  const previousPath = useRef(pathname)

  useEffect(() => {
    setMenuOpen(false)
    // Opening/closing login preserves the underlying page's scroll position.
    if (pathname !== '/login' && previousPath.current !== '/login') window.scrollTo(0, 0)
    previousPath.current = pathname
  }, [pathname])

  return (
    <div className="landing">
      <header className="landing-nav">
        <div className="landing-container landing-nav__inner" onKeyDown={(event) => {
          if (event.key === 'Escape' && menuOpen) {
            setMenuOpen(false)
            toggleRef.current?.focus()
          }
        }}>
          <Link className="auth-brand" to="/" onClick={() => setMenuOpen(false)} aria-label="ServicePro หน้าแรก">
            <img className="auth-brand__logo" src="/servicepro-logo.png" alt="" aria-hidden="true" />
            <span><BrandWordmark /></span>
          </Link>
          <button
            ref={toggleRef}
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
            {NAV_ITEMS.map(({ path, label }) => (
              <NavLink key={path} to={path} end className="landing-nav__link" onClick={() => setMenuOpen(false)}>
                {label}
              </NavLink>
            ))}
            <Link
              className="landing-nav__link landing-nav__link--login"
              to={session ? '/jobs' : '/login'}
              state={session ? undefined : { fromLanding: true }}
              onClick={() => setMenuOpen(false)}
            >
              <LogIn aria-hidden="true" /> {session ? 'เข้าสู่ระบบงาน' : 'เข้าสู่ระบบ'}
            </Link>
            <Link className="landing-nav__link landing-nav__link--cta" to="/signup" onClick={() => setMenuOpen(false)}>
              ทดลองใช้ฟรี
            </Link>
          </nav>
        </div>
      </header>

      <main><Outlet /></main>

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
            {NAV_ITEMS.map(({ path, label }) => <Link key={path} to={path}>{label}</Link>)}
            <Link to="/privacy">นโยบายความเป็นส่วนตัว</Link>
            <Link to="/terms">ข้อกำหนดการใช้งาน</Link>
            <a href={PLAY_STORE_URL} target="_blank" rel="noopener noreferrer">แอป Android บน Google Play</a>
          </nav>
        </div>
        <div className="landing-container landing-footer__legal">
          <p>© ServicePro by Armadillo Tech Co., Ltd.</p>
          <p>Google Play และโลโก้ Google Play เป็นเครื่องหมายการค้าของ Google LLC</p>
        </div>
      </footer>
    </div>
  )
}
