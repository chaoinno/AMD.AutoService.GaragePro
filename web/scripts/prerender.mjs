import { mkdir, readFile, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { PUBLIC_ROUTES } from '../src/routes/publicRoutes.ts'
import { pageStructuredData } from '../src/lib/structuredData.ts'
import { render } from '../dist-ssr/entry-server.js'
import { DEFAULT_SITE_URL, escapeHtml, generateRobots, generateSitemap, normalizeSiteUrl } from './seo-files.mjs'

const dist = new URL('../dist/', import.meta.url)
const siteUrl = normalizeSiteUrl(process.env.VITE_SITE_URL || DEFAULT_SITE_URL)
const template = await readFile(new URL('index.html', dist), 'utf8')
const rootPlaceholder = '<div id="root"></div>'
const headPlaceholder = '<!--app-head-->'
if (!template.includes(rootPlaceholder) || !template.includes(headPlaceholder)) {
  throw new Error('Expected a fresh client build with an empty #root and <!--app-head-->')
}

function pageHead(route) {
  const canonical = `${siteUrl}${route.path}`
  const meta = (attribute, key, content) => `<meta ${attribute}="${key}" content="${escapeHtml(content)}" />`
  return [
    `<title>${escapeHtml(route.title)}</title>`,
    meta('name', 'description', route.description),
    `<link rel="canonical" href="${escapeHtml(canonical)}" />`,
    meta('property', 'og:site_name', 'ServicePro'),
    meta('property', 'og:title', route.title),
    meta('property', 'og:description', route.description),
    meta('property', 'og:url', canonical),
    meta('property', 'og:type', 'website'),
    meta('property', 'og:locale', 'th_TH'),
    meta('property', 'og:image', `${siteUrl}/og-image.png`),
    meta('name', 'twitter:card', 'summary_large_image'),
    ...(route.noindex ? [meta('name', 'robots', 'noindex')] : []),
    ...pageStructuredData(route, siteUrl).map((schema) => (
      `<script type="application/ld+json" data-page-jsonld>${JSON.stringify(schema).replace(/</g, '\\u003c')}</script>`
    )),
  ].join('\n    ')
}

// Remove template defaults before inserting route metadata; crawlers must see one value per tag.
const pageTemplate = template
  .replace(/<title>[\s\S]*?<\/title>\s*/g, '')
  .replace(/<meta\s+(?:name="(?:description|twitter:card|robots)"|property="og:[^"]+")[^>]*>\s*/g, '')

const pages = PUBLIC_ROUTES.filter((route) => route.prerender).map((route) => {
  const { html } = render(route.path)
  const headings = [...html.matchAll(/<h1\b[^>]*>([\s\S]*?)<\/h1>/g)]
  if (!route.h1 || !headings.some(([, heading]) => heading.includes(escapeHtml(route.h1)))) {
    throw new Error(`Prerender failed: ${route.path} is missing its manifest H1: ${route.h1}`)
  }
  return {
    route,
    html: pageTemplate.replace(headPlaceholder, () => pageHead(route)).replace(rootPlaceholder, () => (
      `<div id="root" data-prerender-path="${escapeHtml(route.path)}">${html}</div>`
    )),
  }
})

// Keep the untouched client template as the protected/unknown route fallback.
await writeFile(new URL('app.html', dist), template)
for (const { route, html } of pages) {
  const output = new URL(route.path === '/' ? 'index.html' : `${route.path.slice(1)}/index.html`, dist)
  await mkdir(new URL('.', output), { recursive: true })
  await writeFile(output, html)
  console.log(`Prerendered ${route.path} → ${fileURLToPath(output)}`)
}
await writeFile(new URL('sitemap.xml', dist), generateSitemap(PUBLIC_ROUTES, siteUrl))
await writeFile(new URL('robots.txt', dist), generateRobots(siteUrl))
console.log(`Generated sitemap.xml and robots.txt for ${siteUrl}`)
