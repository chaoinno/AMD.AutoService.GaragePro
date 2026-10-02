import test from 'node:test'
import assert from 'node:assert/strict'
import { bahtText } from '../src/lib/bahtText.ts'

test('bahtText reads whole baht amounts', () => {
  assert.equal(bahtText(0), 'ศูนย์บาทถ้วน')
  assert.equal(bahtText(1), 'หนึ่งบาทถ้วน')
  assert.equal(bahtText(11), 'สิบเอ็ดบาทถ้วน')
  assert.equal(bahtText(21), 'ยี่สิบเอ็ดบาทถ้วน')
  assert.equal(bahtText(101), 'หนึ่งร้อยเอ็ดบาทถ้วน')
  assert.equal(bahtText(1070), 'หนึ่งพันเจ็ดสิบบาทถ้วน')
  assert.equal(bahtText(1_000_000), 'หนึ่งล้านบาทถ้วน')
  assert.equal(bahtText(1_000_001), 'หนึ่งล้านเอ็ดบาทถ้วน')
  assert.equal(bahtText(21_000_000), 'ยี่สิบเอ็ดล้านบาทถ้วน')
})

test('bahtText reads satang and rounds to two decimals', () => {
  assert.equal(bahtText(2514.5), 'สองพันห้าร้อยสิบสี่บาทห้าสิบสตางค์')
  assert.equal(bahtText(0.25), 'ยี่สิบห้าสตางค์')
  assert.equal(bahtText(8838.2), 'แปดพันแปดร้อยสามสิบแปดบาทยี่สิบสตางค์')
  assert.equal(bahtText(0.1 + 0.2), 'สามสิบสตางค์')
  assert.equal(bahtText(1.01), 'หนึ่งบาทหนึ่งสตางค์')
})
