import assert from 'node:assert/strict'
import test from 'node:test'
import { formatStatusAge } from '../src/lib/format.ts'

const now = new Date('2026-10-10T08:00:00Z')

test('formatStatusAge steps from minutes to hours to days with remaining hours', () => {
  assert.equal(formatStatusAge('2026-10-10T07:59:40Z', now), 'ไม่ถึง 1 นาที')
  assert.equal(formatStatusAge('2026-10-10T07:45:00Z', now), '15 นาที')
  assert.equal(formatStatusAge('2026-10-10T05:00:00Z', now), '3 ชม.')
  assert.equal(formatStatusAge('2026-10-08T04:00:00Z', now), '2 วัน 4 ชม.')
  assert.equal(formatStatusAge('2026-10-03T08:00:00Z', now), '7 วัน')
})

test('formatStatusAge treats zone-less API values as UTC and clamps clock skew to zero', () => {
  assert.equal(formatStatusAge('2026-10-10T05:00:00', now), '3 ชม.')
  assert.equal(formatStatusAge('2026-10-10T08:05:00Z', now), 'ไม่ถึง 1 นาที')
  assert.equal(formatStatusAge(null, now), 'ไม่ทราบ')
})
