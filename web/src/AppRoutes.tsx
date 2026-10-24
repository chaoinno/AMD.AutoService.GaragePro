import type { ReactElement } from 'react'
import { Monitor } from 'lucide-react'
import { Navigate, Route, Routes, useNavigate } from 'react-router'
import { AppShell } from './components/AppShell'
import { StateBlock } from './components/StateBlock'
import { IntakeDocumentPage } from './features/jobs/IntakeDocumentPage'
import { JobsPage } from './features/jobs/JobsPage'
import { CustomerPage } from './features/customers/CustomerPage'
import { VehiclePage } from './features/customers/VehiclePage'
import { StaffPage } from './features/staff/StaffPage'
import { CatalogPage } from './features/catalog/CatalogPage'
import { SupplierPage } from './features/master-data/SupplierPage'
import { WarehousePage } from './features/master-data/WarehousePage'
import { CatalogCategoryPage } from './features/master-data/CatalogCategoryPage'
import { QuotationTemplatePage } from './features/master-data/QuotationTemplatePage'
import { PurchasingPage } from './features/purchasing/PurchasingPage'
import { InventoryPage } from './features/purchasing/InventoryPage'
import { DashboardReportPage } from './features/reports/DashboardReportPage'
import { CycleTimeReportPage } from './features/reports/CycleTimeReportPage'
import { SalesMarginReportPage } from './features/reports/SalesMarginReportPage'
import { StockReportPage } from './features/reports/StockReportPage'
import { RetailSalesReportPage } from './features/reports/RetailSalesReportPage'
import { ServiceDueReportPage } from './features/reports/ServiceDueReportPage'
import { StockUsageReportPage } from './features/reports/StockUsageReportPage'
import { VehicleHistoryReportPage } from './features/reports/VehicleHistoryReportPage'
import { WorkIntervalsPage } from './features/reports/WorkIntervalsPage'
import { SalesPage } from './features/sales/SalesPage'
import { PromotionPage } from './features/master-data/PromotionPage'
import { FeatureGate } from './features/plan/LockedFeaturePage'
import { PackagePage } from './features/plan/PackagePage'
import { useSession } from './lib/session'

export default function AppRoutes() {
  return (
    <>
      <div className="desktop-guard" role="alert">
        <div>
          <span aria-hidden="true"><Monitor /></span>
          <h1>กรุณาเปิดบนเดสก์ท็อป</h1>
          <p>ระบบงานบริการรองรับหน้าจอกว้างตั้งแต่ 1024 พิกเซลขึ้นไป</p>
        </div>
      </div>
      <div className="desktop-app">
        <Routes>
          <Route path="/jobs" element={<ProtectedRoute><JobsPage /></ProtectedRoute>} />
          <Route path="/jobs/:jobId/intake-document" element={<ProtectedRoute><IntakeDocumentPage /></ProtectedRoute>} />
          <Route path="/customers" element={<ProtectedRoute><CustomerPage /></ProtectedRoute>} />
          <Route path="/vehicles" element={<ProtectedRoute><VehiclePage /></ProtectedRoute>} />
          <Route path="/staffs" element={<ProtectedRoute><StaffPage /></ProtectedRoute>} />
          <Route path="/products" element={<ProtectedRoute><CatalogPage /></ProtectedRoute>} />
          <Route path="/suppliers" element={<ProtectedRoute><FeatureGate><SupplierPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/warehouses" element={<ProtectedRoute><FeatureGate><WarehousePage /></FeatureGate></ProtectedRoute>} />
          <Route path="/catalog-categories" element={<ProtectedRoute><CatalogCategoryPage /></ProtectedRoute>} />
          <Route path="/quotation-templates" element={<ProtectedRoute><QuotationTemplatePage /></ProtectedRoute>} />
          <Route path="/purchasing" element={<ProtectedRoute><Navigate to="/purchasing/pr" replace /></ProtectedRoute>} />
          <Route path="/purchasing/:kind" element={<ProtectedRoute><FeatureGate><PurchasingPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/inventory" element={<ProtectedRoute><FeatureGate><InventoryPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/sales" element={<ProtectedRoute><FeatureGate><SalesPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/sales/:id" element={<ProtectedRoute><FeatureGate><SalesPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/promotions" element={<ProtectedRoute><FeatureGate><PromotionPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/reports" element={<ProtectedRoute><Navigate to="/reports/dashboard" replace /></ProtectedRoute>} />
          <Route path="/reports/dashboard" element={<ProtectedRoute><DashboardReportPage /></ProtectedRoute>} />
          <Route path="/reports/cycle-time" element={<ProtectedRoute><FeatureGate><CycleTimeReportPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/reports/sales-margin" element={<ProtectedRoute><FeatureGate><SalesMarginReportPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/reports/retail-sales" element={<ProtectedRoute><FeatureGate><RetailSalesReportPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/reports/vehicle-history" element={<ProtectedRoute><VehicleHistoryReportPage /></ProtectedRoute>} />
          <Route path="/reports/stock-usage" element={<ProtectedRoute><StockUsageReportPage /></ProtectedRoute>} />
          <Route path="/reports/service-due" element={<ProtectedRoute><ServiceDueReportPage /></ProtectedRoute>} />
          <Route path="/reports/stock" element={<ProtectedRoute><FeatureGate><StockReportPage /></FeatureGate></ProtectedRoute>} />
          <Route path="/reports/work-intervals" element={<ProtectedRoute><FeatureGate><WorkIntervalsPage /></FeatureGate></ProtectedRoute>} />
          {/* แพ็กเกจและการชำระเงิน (docs/17): ไม่ผ่าน FeatureGate: ต้องเปิดได้แม้อ่านอย่างเดียว เพื่อแจ้งชำระต่ออายุ */}
          <Route path="/package" element={<ProtectedRoute><PackagePage /></ProtectedRoute>} />
          <Route path="*" element={<ProtectedRoute><NotFoundPage /></ProtectedRoute>} />
        </Routes>
      </div>
    </>
  )
}

// FeatureGate: route ของฟีเจอร์ที่แพ็กเกจทดลองใช้ล็อก (path ตรงกับ lib/plan.ts FEATURES) → หน้าอธิบาย + ติดต่อเรา
// ลูกค้าเดิมผ่านตลอด จึงไม่มีอะไรเปลี่ยน
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
