import { useEffect } from 'react'
import { findPublicRoute } from '../routes/publicRoutes'

export const SITE_URL = import.meta.env.VITE_SITE_URL ?? 'https://service.garage-pro.net'

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

    let canonical = document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]')
    if (!canonical) {
      canonical = document.createElement('link')
      canonical.rel = 'canonical'
      document.head.appendChild(canonical)
    }
    canonical.href = url

    if (route.noindex) setMeta('name', 'robots', 'noindex')
    else document.head.querySelectorAll('meta[name="robots"]').forEach((tag) => tag.remove())
  }, [path])
}
