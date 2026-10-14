import { useEffect } from 'react'
import { findPublicRoute } from '../routes/publicRoutes'
import { pageStructuredData } from './structuredData'

export const SITE_URL = (import.meta.env.VITE_SITE_URL || 'https://service.garage-pro.net').replace(/\/+$/, '')

/** DOM updates stay in an effect so the same pages can be prerendered without a browser. */
export function usePageMeta(path: string) {
  useEffect(() => {
    const route = findPublicRoute(path)
    if (!route) return

    function setMeta(attribute: 'name' | 'property', key: string, content: string) {
      let tag = document.head.querySelector<HTMLMetaElement>(`meta[${attribute}="${key}"]`)
      if (!tag) {
        tag = document.createElement('meta')
        tag.setAttribute(attribute, key)
        document.head.appendChild(tag)
      }
      tag.content = content
    }

    const url = `${SITE_URL}${path}`
    document.title = route.title
    setMeta('name', 'description', route.description)
    setMeta('property', 'og:title', route.title)
    setMeta('property', 'og:description', route.description)
    setMeta('property', 'og:url', url)
    setMeta('property', 'og:type', 'website')
    setMeta('property', 'og:locale', 'th_TH')
    setMeta('property', 'og:image', `${SITE_URL}/og-image.png`)
    setMeta('name', 'twitter:card', 'summary_large_image')

    let canonical = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')
    if (!canonical) {
      canonical = document.createElement('link')
      canonical.rel = 'canonical'
      document.head.appendChild(canonical)
    }
    canonical.href = url

    if (route.noindex) setMeta('name', 'robots', 'noindex')
    else document.head.querySelectorAll('meta[name="robots"]').forEach((tag) => tag.remove())

    document.head.querySelectorAll('script[data-page-jsonld]').forEach((tag) => tag.remove())
    for (const schema of pageStructuredData(route, SITE_URL)) {
      const tag = document.createElement('script')
      tag.type = 'application/ld+json'
      tag.dataset.pageJsonld = ''
      tag.textContent = JSON.stringify(schema).replace(/</g, '\\u003c')
      document.head.appendChild(tag)
    }

    return () => {
      canonical?.remove()
      document.head.querySelectorAll('script[data-page-jsonld], meta[property="og:url"], meta[name="robots"]').forEach((tag) => tag.remove())
      document.title = 'ServicePro'
      setMeta('name', 'description', 'ServicePro แพลตฟอร์มบริหารงานอู่บริการซ่อมบำรุงรถยนต์ รับรถ เสนอราคา ซ่อม QC ชำระเงิน และส่งมอบรถในระบบเดียว ทั้งบนเว็บและแอปมือถือ')
      setMeta('property', 'og:title', 'ServicePro | แพลตฟอร์มบริหารงานอู่บริการซ่อมบำรุงรถยนต์')
      setMeta('property', 'og:description', 'รับรถ เสนอราคา ซ่อม QC ชำระเงิน และส่งมอบรถในระบบเดียว ทั้งบนเว็บและแอปมือถือ')
    }
  }, [path])
}
