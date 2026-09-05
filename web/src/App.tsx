import { Component, type ErrorInfo, type ReactElement, type ReactNode } from 'react'
import { Monitor, RefreshCw, TriangleAlert } from 'lucide-react'
import { Navigate, Route, Routes, useNavigate } from 'react-router'
import { AppShell } from './components/AppShell'
import { StateBlock } from './components/StateBlock'
import { Button } from './components/ui/button'
import { LoginPage } from './features/auth/LoginPage'
import { IntakeDocumentPage } from './features/jobs/IntakeDocumentPage'
import { JobsPage } from './features/jobs/JobsPage'
import { CustomerPage } from './features/customers/CustomerPage'
import { VehiclePage } from './features/customers/VehiclePage'
import { StaffPage } from './features/staff/StaffPage'
import { CatalogPage } from './features/catalog/CatalogPage'
import { SupplierPage } from './features/master-data/SupplierPage'
import { WarehousePage } from './features/master-data/WarehousePage'
import { CatalogCategoryPage } from './features/master-data/CatalogCategoryPage'
import { PurchasingPage } from './features/purchasing/PurchasingPage'
import { InventoryPage } from './features/purchasing/InventoryPage'
import { useSession } from './lib/session'

export default function App() {
  return (
    <AppErrorBoundary>
      <div className="desktop-guard" role="alert">
        <div>
          <span aria-hidden="true"><Monitor /></span>
          <h1>กรุณาเปิดบนเดสก์ท็อป</h1>
          <p>ระบบงานบริการรองรับหน้าจอกว้างตั้งแต่ 1024 พิกเซลขึ้นไป</p>
        </div>
      </div>
      <div className="desktop-app">
        <Routes>
          <Route path="/" element={<RootRedirect />} />
          <Route path="/login" element={<LoginRoute />} />
          <Route path="/jobs" element={<ProtectedRoute><JobsPage /></ProtectedRoute>} />
          <Route path="/jobs/:jobId/intake-document" element={<ProtectedRoute><IntakeDocumentPage /></ProtectedRoute>} />
          <Route path="/customers" element={<ProtectedRoute><CustomerPage /></ProtectedRoute>} />
          <Route path="/vehicles" element={<ProtectedRoute><VehiclePage /></ProtectedRoute>} />
          <Route path="/staffs" element={<ProtectedRoute><StaffPage /></ProtectedRoute>} />
          <Route path="/products" element={<ProtectedRoute><CatalogPage /></ProtectedRoute>} />
          <Route path="/suppliers" element={<ProtectedRoute><SupplierPage /></ProtectedRoute>} />
          <Route path="/warehouses" element={<ProtectedRoute><WarehousePage /></ProtectedRoute>} />
          <Route path="/catalog-categories" element={<ProtectedRoute><CatalogCategoryPage /></ProtectedRoute>} />
          <Route path="/purchasing" element={<ProtectedRoute><Navigate to="/purchasing/pr" replace /></ProtectedRoute>} />
          <Route path="/purchasing/:kind" element={<ProtectedRoute><PurchasingPage /></ProtectedRoute>} />
          <Route path="/inventory" element={<ProtectedRoute><InventoryPage /></ProtectedRoute>} />
          <Route path="*" element={<ProtectedRoute><NotFoundPage /></ProtectedRoute>} />
        </Routes>
      </div>
    </AppErrorBoundary>
  )
}

function RootRedirect() {
  const { session } = useSession()
  return <Navigate to={session ? '/jobs' : '/login'} replace />
}

function LoginRoute() {
  const { session } = useSession()
  if (session) return <Navigate to="/jobs" replace />
  return <LoginPage />
}

function ProtectedRoute({ children }: { children: ReactElement }) {
  const { session } = useSession()
  if (!session) return <Navigate to="/login" replace />
  return children
}

function NotFoundPage() {
  const navigate = useNavigate()
  return (
    <AppShell title="ไม่พบหน้า">
      <StateBlock
        variant="empty"
        title="ไม่พบหน้าที่ต้องการ"
        reason="ที่อยู่นี้ไม่มีอยู่ในระบบงานบริการ"
        traceId="ไม่ใช่คำขอ API"
        actionLabel="กลับไปหน้าจ๊อบ"
        onAction={() => navigate('/jobs')}
      />
    </AppShell>
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
