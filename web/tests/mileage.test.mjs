import assert from 'node:assert/strict'
import test from 'node:test'
import { addMonthsToToday, parseKmInput } from '../src/lib/format.ts'

test('addMonthsToToday clamps to the last day of a shorter month like DateOnly.AddMonths', () => {
  assert.equal(addMonthsToToday(1, new Date(2027, 0, 31)), '2027-02-28')
  assert.equal(addMonthsToToday(1, new Date(2028, 0, 31)), '2028-02-29')
  assert.equal(addMonthsToToday(6, new Date(2026, 9, 8)), '2027-04-08')
  assert.equal(addMonthsToToday(12, new Date(2026, 11, 15)), '2027-12-15')
})

test('parseKmInput accepts commas and spaces but rejects decimals and negatives', () => {
  assert.equal(parseKmInput('45,210'), 45210)
  assert.equal(parseKmInput(' 45 210 '), 45210)
  assert.equal(parseKmInput('0'), 0)
  assert.equal(parseKmInput('12.5'), null)
  assert.equal(parseKmInput('-5'), null)
  assert.equal(parseKmInput(''), null)
})
