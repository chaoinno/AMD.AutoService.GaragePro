import test from 'node:test'
import assert from 'node:assert/strict'
import { buildMonthGrid, dayKey, isSameDay } from '../src/features/jobs/calendarMonth.ts'

test('buildMonthGrid returns 42 days starting on a Sunday', () => {
  // กันยายน 2569 (2026) เริ่มวันอังคาร — ต้องมีวันจากเดือนสิงหาคมนำหน้าจนกว่าจะถึงวันอาทิตย์
  const grid = buildMonthGrid(2026, 8) // month index 8 = กันยายน
  assert.equal(grid.days.length, 42)
  assert.equal(grid.days[0].getDay(), 0, 'วันแรกของตารางต้องเป็นวันอาทิตย์เสมอ')
  assert.equal(grid.days[0].getMonth(), 7) // สิงหาคม (เดือนก่อนหน้า)
  assert.ok(grid.days.some((d) => d.getMonth() === 8 && d.getDate() === 1))
})

test('buildMonthGrid covers a month that starts exactly on Sunday', () => {
  // มีนาคม 2026 เริ่มวันอาทิตย์ (1 มี.ค. 2026 = Sunday)
  const grid = buildMonthGrid(2026, 2)
  assert.equal(grid.days[0].getMonth(), 2)
  assert.equal(grid.days[0].getDate(), 1)
})

test('buildMonthGrid covers a 31-day month fully', () => {
  const grid = buildMonthGrid(2026, 0) // มกราคม — 31 วัน
  const daysInMonth = grid.days.filter((d) => d.getMonth() === 0)
  assert.equal(daysInMonth.length, 31)
})

test('buildMonthGrid handles February of a leap year', () => {
  const grid = buildMonthGrid(2028, 1) // 2028 เป็นปีอธิกสุรทิน — ก.พ. มี 29 วัน
  const daysInMonth = grid.days.filter((d) => d.getMonth() === 1)
  assert.equal(daysInMonth.length, 29)
})

test('dayKey groups dates on the same local day identically', () => {
  const a = new Date(2026, 8, 20, 9, 30)
  const b = new Date(2026, 8, 20, 23, 59)
  const c = new Date(2026, 8, 21, 0, 1)
  assert.equal(dayKey(a), dayKey(b))
  assert.notEqual(dayKey(a), dayKey(c))
})

test('isSameDay compares only the calendar date, not the time', () => {
  assert.ok(isSameDay(new Date(2026, 8, 20, 1, 0), new Date(2026, 8, 20, 23, 0)))
  assert.ok(!isSameDay(new Date(2026, 8, 20), new Date(2026, 8, 21)))
})

test('rangeStart/rangeEnd bound exactly the 42 rendered days', () => {
  const grid = buildMonthGrid(2026, 8)
  assert.ok(isSameDay(grid.rangeStart, grid.days[0]))
  const dayAfterLast = new Date(grid.days[41].getFullYear(), grid.days[41].getMonth(), grid.days[41].getDate() + 1)
  assert.ok(isSameDay(grid.rangeEnd, dayAfterLast))
})
