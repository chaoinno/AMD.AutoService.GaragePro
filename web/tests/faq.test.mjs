import assert from 'node:assert/strict'
import test from 'node:test'
import { FAQ_ITEMS } from '../src/features/landing/faq.ts'

test('FAQ has 6–8 unique questions with non-empty answers for the page and JSON-LD', () => {
  assert.ok(FAQ_ITEMS.length >= 6 && FAQ_ITEMS.length <= 8)
  assert.equal(new Set(FAQ_ITEMS.map(({ q }) => q)).size, FAQ_ITEMS.length)
  for (const { q, a } of FAQ_ITEMS) {
    assert.equal(typeof q, 'string')
    assert.equal(typeof a, 'string')
    assert.ok(q.trim())
    assert.ok(a.trim())
  }
})
