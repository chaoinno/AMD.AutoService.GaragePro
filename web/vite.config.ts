import { readFileSync } from 'node:fs'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { PUBLIC_ROUTES } from './src/routes/publicRoutes'

// เลขเวอร์ชันที่แสดงท้ายเมนู มาจาก package.json ที่เดียว — เลขกลาง = เลข plan ใน docs (เช่น docs/18 → 1.18.x)
const appVersion = (JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')) as { version: string }).version

export default defineConfig(({ isSsrBuild }) => ({
  define: {
    __APP_VERSION__: JSON.stringify(appVersion),
  },
  plugins: [
    react(),
    {
      name: 'prerender-preview',
      configurePreviewServer(server) {
        // Preview must serve the same documents as nginx without changing the browser URL.
        server.middlewares.use((request, _response, next) => {
          const url = new URL(request.url ?? '/', 'http://localhost')
          const route = PUBLIC_ROUTES.find(({ path }) => path === url.pathname)
          if (route?.prerender) {
            request.url = `${route.path === '/' ? '/index.html' : `${route.path}/index.html`}${url.search}`
          } else if (!url.pathname.split('/').pop()?.includes('.') && !url.pathname.startsWith('/assets/')) {
            request.url = `/app.html${url.search}`
          }
          next()
        })
      },
    },
  ],
  build: {
    rollupOptions: {
      output: {
        manualChunks: isSsrBuild ? undefined : {
          vendor: ['react', 'react-dom', 'react-router'],
          query: ['@tanstack/react-query'],
        },
      },
    },
  },
  resolve: {
    alias: {
      '@': '/src',
    },
  },
  server: {
    port: 5173,
  },
}))
