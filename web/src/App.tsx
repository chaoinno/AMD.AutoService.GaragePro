import { Component, lazy, Suspense, type ErrorInfo, type ReactElement, type ReactNode } from 'react'
import { RefreshCw, TriangleAlert } from 'lucide-react'
import { Route, Routes, useLocation } from 'react-router'
import { StateBlock } from './components/StateBlock'
import { Button } from './components/ui/button'
import { PublicLayout } from './features/landing/PublicLayout'
import { HomePage } from './features/landing/pages/HomePage'
import { FeaturesPage } from './features/landing/pages/FeaturesPage'
import { MobileAppPage } from './features/landing/pages/MobileAppPage'
import { PricingPage } from './features/landing/pages/PricingPage'
import { AboutPage } from './features/landing/pages/AboutPage'
import { ContactPage } from './features/landing/pages/ContactPage'
import { PrivacyPage } from './features/landing/pages/PrivacyPage'
import { TermsPage } from './features/landing/pages/TermsPage'
import { SignupPage } from './features/signup/SignupPage'
import { isPublicPath, PUBLIC_ROUTES } from './routes/publicRoutes'

// Do not import protected pages here: public visits must not download the internal app.
const AppRoutes = lazy(() => import('./AppRoutes'))

const PUBLIC_PAGES: Record<string, ReactElement> = {
  '/': <HomePage />,
  '/login': <HomePage />,
  '/features': <FeaturesPage />,
  '/mobile-app': <MobileAppPage />,
  '/pricing': <PricingPage />,
  '/about': <AboutPage />,
  '/contact': <ContactPage />,
  '/privacy': <PrivacyPage />,
  '/terms': <TermsPage />,
  '/signup': <SignupPage />,
}

export default function App() {
  const { pathname } = useLocation()
  return (
    <AppErrorBoundary>
      {isPublicPath(pathname) ? (
        <Routes>
          {/* Public pages work on phones; the protected tree retains its desktop guard. */}
          <Route element={<PublicLayout />}>
            {PUBLIC_ROUTES.map(({ path }) => (
              <Route key={path} path={path} element={PUBLIC_PAGES[path]} />
            ))}
          </Route>
        </Routes>
      ) : (
        <Suspense fallback={
          <StateBlock
            variant="loading"
            title="กำลังโหลดระบบงาน"
            reason="กำลังเตรียมหน้าจอสำหรับพนักงานอู่"
            traceId="ไม่ใช่คำขอ API"
            actionLabel="โหลดหน้าใหม่"
            onAction={() => window.location.reload()}
          />
        }>
          <AppRoutes />
        </Suspense>
      )}
    </AppErrorBoundary>
  )
}

type BoundaryState = { error: Error | null }

class AppErrorBoundary extends Component<{ children: ReactNode }, BoundaryState> {
  state: BoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): BoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Application render error', error, info)
  }

  render() {
    if (this.state.error) {
      return (
        <div className="fatal-error" role="alert">
          <div>
            <span aria-hidden="true"><TriangleAlert /></span>
            <h1>หน้าจอทำงานไม่สำเร็จ</h1>
            <p>{this.state.error.message || 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}</p>
            <p className="trace-id">รหัสติดตาม (traceId): ไม่ใช่ข้อผิดพลาดจาก API</p>
            <Button onClick={() => window.location.reload()}>
              <RefreshCw aria-hidden="true" /> โหลดหน้าใหม่
            </Button>
          </div>
        </div>
      )
    }
    return this.props.children
  }
}
