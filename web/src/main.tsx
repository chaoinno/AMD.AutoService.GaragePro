import { StrictMode } from 'react'
import { createRoot, hydrateRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import App from './App'
import { ClientOnly } from './components/ClientOnly'
import { Toaster } from './components/ui/sonner'
import { SessionProvider } from './lib/session'
import { SessionQueryProvider } from './lib/SessionGuard'
import './index.css'

const app = (
  <StrictMode>
    <SessionProvider>
      <SessionQueryProvider>
        <BrowserRouter>
          <App />
        </BrowserRouter>
      </SessionQueryProvider>
      {/* อยู่นอก SessionQueryProvider โดยตั้งใจ — ต้นไม้ข้างในสร้างใหม่ทุกครั้งที่ token เปลี่ยน
          toast ที่ยิงพร้อมกับเปลี่ยน token (เช่น "สลับไปสาขา … แล้ว") จะหายไปกับ Toaster ตัวเก่า */}
      <ClientOnly><Toaster /></ClientOnly>
    </SessionProvider>
  </StrictMode>
)

const root = document.getElementById('root')!
if (root.hasChildNodes() && root.dataset.prerenderPath === window.location.pathname) {
  hydrateRoot(root, app)
} else {
  root.replaceChildren()
  delete root.dataset.prerenderPath
  createRoot(root).render(app)
}
