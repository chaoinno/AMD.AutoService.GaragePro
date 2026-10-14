import assert from 'node:assert/strict'
import test from 'node:test'
import { accountSchema, consentSchema, emailSchema, garageNameSchema, otpSchema, passwordSchema, phoneSchema, workspaceSchema } from '../src/features/signup/signupSchema.ts'

test('password requires eight characters, a letter and a digit', () => {
  for (const password of ['abc12345', 'รหัสผ่าน123', 'space 12']) assert.equal(passwordSchema.safeParse(password).success, true, password)
  for (const password of ['abc123', 'abcdefgh', '12345678', '!!!!!!!!', '']) assert.equal(passwordSchema.safeParse(password).success, false, password)
})
test('Thai mobile and landline numbers normalize spaces and dashes', () => {
  for (const [raw, expected] of [['081-234-5678', '0812345678'], [' 069 123 4567 ', '0691234567'], ['0991234567', '0991234567'], ['02-123-4567', '021234567'], ['053 123456', '053123456'], ['071123456', '071123456']]) assert.equal(phoneSchema.parse(raw), expected)
  for (const phone of ['0112345678', '0212345678', '081234567', '+66812345678', '081abc5678', '']) assert.equal(phoneSchema.safeParse(phone).success, false, phone)
})
test('OTP accepts exactly six ASCII digits', () => {
  assert.equal(otpSchema.safeParse({ code: '012345' }).success, true)
  for (const code of ['12345', '1234567', '12345a', ' 123456', '๑๒๓๔๕๖', '']) assert.equal(otpSchema.safeParse({ code }).success, false, code)
})
test('garage names are trimmed and contain 2–100 characters', () => {
  assert.equal(garageNameSchema.parse('  อู่ช่างเอก  '), 'อู่ช่างเอก')
  assert.equal(garageNameSchema.safeParse('x'.repeat(100)).success, true)
  for (const name of [' ', ' x ', 'x'.repeat(101)]) assert.equal(garageNameSchema.safeParse(name).success, false)
})
test('email and consent validation use the required Thai messages', () => {
  assert.equal(emailSchema.parse(' owner@gmail.com '), 'owner@gmail.com')
  assert.equal(emailSchema.safeParse('invalid').success, false)
  assert.equal(consentSchema.safeParse(true).success, true)
  assert.equal(consentSchema.safeParse(false).error.issues[0].message, 'กรุณายอมรับข้อกำหนดและนโยบายความเป็นส่วนตัว')
  assert.equal(accountSchema.safeParse({ email: 'owner@gmail.com', password: 'valid123', consent: true, website: '' }).success, true)
})
test('workspace optional fields and default sample data follow the contract', () => {
  const input = { garageName: 'อู่ช่างเอก', phone: '0812345678' }
  assert.equal(workspaceSchema.parse(input).sampleData, true)
  assert.equal(workspaceSchema.parse({ ...input, sampleData: false, size: '10+', provinceId: 1 }).sampleData, false)
  assert.equal(workspaceSchema.safeParse({ ...input, size: 'invalid' }).success, false)
  assert.equal(workspaceSchema.safeParse({ ...input, provinceId: 0 }).success, false)
})
