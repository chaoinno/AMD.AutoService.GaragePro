import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { renderToString } from 'react-dom/server'
import { StaticRouter } from 'react-router'
import App from './App'
import { ClientOnly } from './components/ClientOnly'
import { Toaster } from './components/ui/sonner'
import { createAppQueryClient } from './lib/queryClient'
import { SessionProvider } from './lib/session'

export function render(url: string): { html: string } {
  const queryClient = createAppQueryClient()
  try {
    return {
      html: renderToString(
        <StrictMode>
          <QueryClientProvider client={queryClient}>
            <SessionProvider>
              <StaticRouter location={url}>
                <App />
                <ClientOnly><Toaster /></ClientOnly>
              </StaticRouter>
            </SessionProvider>
          </QueryClientProvider>
        </StrictMode>,
      ),
    }
  } finally {
    queryClient.clear()
  }
}
