export const DEFAULT_SITE_URL = 'https://service.garage-pro.net'

export const ROBOTS_DISALLOW_PATHS = [
  '/jobs', '/customers', '/vehicles', '/staffs', '/products', '/suppliers', '/warehouses',
  '/catalog-categories', '/quotation-templates', '/purchasing', '/inventory', '/sales',
  '/promotions', '/reports', '/login',
]

export function normalizeSiteUrl(siteUrl = DEFAULT_SITE_URL) {
  const url = new URL(siteUrl)
  if (!['http:', 'https:'].includes(url.protocol) || url.pathname !== '/' || url.search || url.hash || url.username || url.password) {
    throw new Error('VITE_SITE_URL must be an absolute HTTP(S) origin')
  }
  return url.origin
}

export function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[character])
}

export function generateSitemap(routes, siteUrl = DEFAULT_SITE_URL, buildDate = new Date().toISOString().slice(0, 10)) {
  const origin = normalizeSiteUrl(siteUrl)
  const entries = routes.filter((route) => route.prerender && !route.noindex).map((route) => (
    `  <url><loc>${escapeHtml(new URL(route.path, origin).href)}</loc><lastmod>${escapeHtml(buildDate)}</lastmod><changefreq>${escapeHtml(route.changefreq)}</changefreq><priority>${route.priority}</priority></url>`
  ))
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${entries.join('\n')}\n</urlset>\n`
}

export function generateRobots(siteUrl = DEFAULT_SITE_URL) {
  return ['User-agent: *', 'Allow: /', ...ROBOTS_DISALLOW_PATHS.map((path) => `Disallow: ${path}`),
    `Sitemap: ${normalizeSiteUrl(siteUrl)}/sitemap.xml`, ''].join('\n')
}
