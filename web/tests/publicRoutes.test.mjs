import assert from 'node:assert/strict'
import test from 'node:test'
import { PUBLIC_ROUTES, findPublicRoute, isPublicPath } from '../src/routes/publicRoutes.ts'

test('public route manifest has unique absolute paths with SEO-safe title/description lengths', () => {
  const paths = PUBLIC_ROUTES.map((route) => route.path)
  assert.equal(new Set(paths).size, paths.length)
  for (const route of PUBLIC_ROUTES) {
    assert.ok(route.path.startsWith('/'), route.path)
    assert.ok(route.path === '/' || !route.path.endsWith('/'), route.path)
    assert.ok(route.title.length > 0 && route.title.length <= 60, `${route.path} title ${route.title.length}`)
    assert.ok(
      route.description.length > 0 && route.description.length <= 160,
      `${route.path} description ${route.description.length}`,
    )
    assert.ok(route.priority >= 0 && route.priority <= 1, route.path)
    assert.ok(['weekly', 'monthly'].includes(route.changefreq), route.path)
    // หน้าที่ prerender ลง sitemap ต้องไม่ noindex (ขัดกันเอง)
    if (route.prerender) assert.notEqual(route.noindex, true, route.path)
  }
  assert.ok(PUBLIC_ROUTES.some((route) => route.path === '/' && route.prerender))
  assert.equal(findPublicRoute('/login')?.noindex, true)
  assert.equal(findPublicRoute('/login')?.prerender, false)
})

test('isPublicPath matches manifest paths exactly (same semantics as the old / and /login check)', () => {
  assert.equal(isPublicPath('/'), true)
  assert.equal(isPublicPath('/login'), true)
  assert.equal(isPublicPath('/jobs'), false)
  assert.equal(isPublicPath('/login/'), false)
  assert.equal(isPublicPath('/logins'), false)
  assert.equal(isPublicPath(''), false)
  assert.equal(findPublicRoute('/jobs'), undefined)
})
