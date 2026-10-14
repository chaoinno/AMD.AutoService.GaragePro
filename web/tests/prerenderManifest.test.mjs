import assert from 'node:assert/strict'
import test from 'node:test'
import { PUBLIC_ROUTES } from '../src/routes/publicRoutes.ts'
import { FAQ_ITEMS } from '../src/features/landing/faq.ts'
import { pageStructuredData } from '../src/lib/structuredData.ts'
import { generateSitemap, generateRobots } from '../scripts/seo-files.mjs'

test('every prerendered page has a title, description and H1 within SEO limits', () => {
  for (const route of PUBLIC_ROUTES.filter((route) => route.prerender)) {
    assert.ok(route.title.trim() && route.title.length <= 60, `${route.path} title`)
    assert.ok(route.description.trim() && route.description.length <= 160, `${route.path} description`)
    assert.ok(route.h1?.trim(), `${route.path} H1`)
  }
})

test('sitemap excludes noindex/client pages and uses absolute canonical URLs and build dates', () => {
  const routes = [...PUBLIC_ROUTES, { ...PUBLIC_ROUTES[0], path: '/hidden', noindex: true }]
  const xml = generateSitemap(routes, 'https://canonical.example/', '2026-10-14')
  assert.ok(!xml.includes('/login') && !xml.includes('/hidden'))
  const locations = [...xml.matchAll(/<loc>(.*?)<\/loc>/g)].map(([, location]) => location)
  assert.deepEqual(locations, PUBLIC_ROUTES.filter((route) => route.prerender && !route.noindex).map((route) => `https://canonical.example${route.path}`))
  assert.equal([...xml.matchAll(/<lastmod>2026-10-14<\/lastmod>/g)].length, locations.length)
  for (const route of PUBLIC_ROUTES.filter((route) => route.prerender)) {
    assert.ok(xml.includes(`<changefreq>${route.changefreq}</changefreq><priority>${route.priority}</priority>`))
  }
  assert.throws(() => generateSitemap(routes, '/relative'), /Invalid URL/)
})

test('robots permits public pages and disallows jobs/login with the canonical sitemap', () => {
  const robots = generateRobots('https://canonical.example/')
  assert.ok(robots.startsWith('User-agent: *\nAllow: /\n'))
  assert.ok(robots.includes('Disallow: /jobs\n') && robots.includes('Disallow: /login\n'))
  assert.ok(robots.includes('Sitemap: https://canonical.example/sitemap.xml'))
})

test('structured data contains the shared FAQ, verified trial offer and contact details', () => {
  const [organization, software, faq] = pageStructuredData(PUBLIC_ROUTES[0], 'https://canonical.example/')
  assert.equal(organization.legalName, 'Armadillo Tech Co., Ltd.')
  assert.equal(organization.contactPoint.telephone, '+66-90-996-6446')
  assert.equal(organization.logo, 'https://canonical.example/servicepro-logo.png')
  assert.deepEqual(software.offers, { '@type': 'Offer', price: '0', priceCurrency: 'THB', description: 'ทดลองใช้ฟรี 30 วัน' })
  assert.equal(software.operatingSystem, 'Web, Android')
  assert.equal(software.description, PUBLIC_ROUTES[0].description)
  assert.ok(!('aggregateRating' in software) && !('review' in software))
  assert.deepEqual(faq.mainEntity.map((item) => ({ q: item.name, a: item.acceptedAnswer.text })), FAQ_ITEMS)
  for (const path of ['/features', '/pricing']) {
    assert.deepEqual(pageStructuredData(PUBLIC_ROUTES.find((route) => route.path === path), 'https://canonical.example').map((schema) => schema['@type']), ['SoftwareApplication'])
  }
})
