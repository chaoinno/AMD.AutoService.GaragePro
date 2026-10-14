import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot, hydrateRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import App from './App'
import { ClientOnly } from './components/ClientOnly'
import { Toaster } from './components/ui/sonner'
import { createAppQueryClient } from './lib/queryClient'
import { SessionProvider } from './lib/session'
import './index.css'

const queryClient = createAppQueryClient()

const app = (
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <BrowserRouter>
          <App />
          <ClientOnly><Toaster /></ClientOnly>
        </BrowserRouter>
      </SessionProvider>
    </QueryClientProvider>
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
