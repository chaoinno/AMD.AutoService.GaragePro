import assert from 'node:assert/strict'
import test from 'node:test'
import {
  customRow,
  handoverServiceInfoMissing,
  rowFromTemplate,
  rowMileageText,
  rowsFromHandover,
  rowsToInput,
  serviceInfoBlockedReason,
  serviceInfoSignature,
  validateRow,
} from '../src/features/jobs/handoverServiceRows.ts'

const oil = { id: 't-oil', name: 'น้ำมันเครื่อง', defaultMileageInterval: 10_000, defaultMonths: 6, sortOrder: 1, isActive: true }
const gear = { id: 't-gear', name: 'น้ำมันเกียร์', defaultMileageInterval: 40_000, defaultMonths: 24, sortOrder: 3, isActive: true }

test('a row from a template follows the handover mileage until the user edits the mileage', () => {
  const row = rowFromTemplate(oil)
  assert.equal(rowMileageText(row, 45_230), '55230')
  assert.equal(rowMileageText(row, 46_000), '56000', 'changing the handover km moves the auto mileage')
  assert.equal(rowMileageText(row, null), '', 'nothing to add to yet')
  const edited = { ...row, mileageText: '60000', autoInterval: null }
  assert.equal(rowMileageText(edited, 46_000), '60000', 'manual value is kept')
})

test('rows saved on the server are real values, not auto sums', () => {
  const rows = rowsFromHandover([{ id: 'a', templateId: 't-oil', name: 'น้ำมันเครื่อง', nextServiceMileage: 52_000, nextServiceMonths: 6, nextServiceDueOn: '2027-04-25' }])
  assert.equal(rowMileageText(rows[0], 45_230), '52000')
  assert.equal(rows[0].customName, false)
  assert.equal(rowsFromHandover(undefined).length, 0)
})

test('validateRow mirrors the server: name required and unique, mileage above handover, months 1-24', () => {
  const a = rowFromTemplate(oil)
  const b = { ...customRow(), name: ' น้ำมันเครื่อง ' }
  const rows = [a, b]
  assert.equal(validateRow(a, rows, 45_000).name, 'ชื่อรายการซ้ำกัน')
  assert.equal(validateRow(b, rows, 45_000).name, 'ชื่อรายการซ้ำกัน')
  assert.equal(validateRow({ ...b, name: '  ' }, [{ ...b, name: '  ' }], 45_000).name, 'กรอกชื่อรายการ')

  const low = { ...customRow(), name: 'x', mileageText: '45000', autoInterval: null }
  assert.match(validateRow(low, [low], 45_000).mileage, /มากกว่าเลขไมล์ตอนส่งมอบ/)
  assert.equal(validateRow({ ...low, mileageText: '45001' }, [low], 45_000).mileage, undefined)

  assert.match(validateRow({ ...a, monthsText: '0' }, [a], 45_000).months, /1–24/)
  assert.match(validateRow({ ...a, monthsText: '25' }, [a], 45_000).months, /1–24/)
  assert.equal(validateRow({ ...a, monthsText: '24' }, [a], 45_000).months, undefined)
})

test('rowsToInput trims names, keeps the template id and resolves auto mileage', () => {
  const a = rowFromTemplate(gear)
  const b = { ...customRow(), name: ' ลูกปืนล้อ ', mileageText: '70000', autoInterval: null, monthsText: '12' }
  assert.deepEqual(rowsToInput([a, b], 45_000), [
    { templateId: 't-gear', name: 'น้ำมันเกียร์', nextServiceMileage: 85_000, nextServiceMonths: 24 },
    { templateId: null, name: 'ลูกปืนล้อ', nextServiceMileage: 70_000, nextServiceMonths: 12 },
  ])
})

test('save is blocked with a readable reason in the order the user can fix things', () => {
  const ok = { ...rowFromTemplate(oil) }
  assert.match(serviceInfoBlockedReason(null, 45_000, undefined, [ok]), /ยังไม่มีเลขไมล์ขณะรับรถ/)
  assert.equal(serviceInfoBlockedReason(40_000, null, undefined, [ok]), 'กรอกเลขไมล์ตอนส่งมอบ')
  assert.equal(serviceInfoBlockedReason(40_000, 45_000, 'ต้องไม่น้อยกว่า', [ok]), 'ต้องไม่น้อยกว่า')
  assert.equal(serviceInfoBlockedReason(40_000, 45_000, undefined, []), 'เพิ่มรายการนัดบริการอย่างน้อย 1 รายการ')
  assert.match(serviceInfoBlockedReason(40_000, 45_000, undefined, [customRow()]), /กรอกชื่อรายการ/)
  assert.match(serviceInfoBlockedReason(40_000, 45_000, undefined, [{ ...ok, monthsText: '' }]), /เลือกระยะเวลานัด/)
  assert.equal(serviceInfoBlockedReason(40_000, 45_000, undefined, [ok]), null)
})

test('signature ignores name padding so an untouched form is not dirty', () => {
  const saved = serviceInfoSignature(45_000, [{ name: 'น้ำมันเครื่อง', nextServiceMileage: 55_000, nextServiceMonths: 6 }])
  const same = serviceInfoSignature(45_000, [{ name: ' น้ำมันเครื่อง ', nextServiceMileage: 55_000, nextServiceMonths: 6 }])
  const changed = serviceInfoSignature(45_000, [{ name: 'น้ำมันเครื่อง', nextServiceMileage: 55_000, nextServiceMonths: 12 }])
  assert.equal(saved, same)
  assert.notEqual(saved, changed)
})

test('handover needs the handover mileage and at least one item before signing', () => {
  assert.equal(handoverServiceInfoMissing({ mileageAtHandover: 45_000, serviceItems: [{}] }), false)
  assert.equal(handoverServiceInfoMissing({ mileageAtHandover: 45_000, serviceItems: [] }), true)
  assert.equal(handoverServiceInfoMissing({ mileageAtHandover: 45_000 }), true)
  assert.equal(handoverServiceInfoMissing({ serviceItems: [{}] }), true)
})
