// จำนวนเงินเป็นตัวอักษรภาษาไทย (เช่น "หนึ่งพันเจ็ดสิบบาทถ้วน") — ใช้บนใบเสร็จ/ใบกำกับภาษี
// ยึดแบบเดียวกับ BAHTTEXT ของ Excel: หลักหน่วยเป็น 1 และมีหลักที่สูงกว่า → "เอ็ด" (101 = หนึ่งร้อยเอ็ด, 1,000,001 = หนึ่งล้านเอ็ด)
const DIGITS: readonly string[] = ['ศูนย์', 'หนึ่ง', 'สอง', 'สาม', 'สี่', 'ห้า', 'หก', 'เจ็ด', 'แปด', 'เก้า']
const PLACES: readonly string[] = ['', 'สิบ', 'ร้อย', 'พัน', 'หมื่น', 'แสน']

/** แปลงจำนวนเต็มไม่ติดลบ — hasHigher = มีหลักที่สูงกว่าอยู่ข้างหน้า (ตัดสินว่าหลักหน่วย 1 อ่าน "เอ็ด") */
function chunkText(digits: string, hasHigher: boolean): string {
  let text = ''
  const len = digits.length
  for (let i = 0; i < len; i += 1) {
    const d = Number(digits[i])
    const place = len - 1 - i
    if (d === 0) continue
    const digit = DIGITS[d] ?? ''
    if (place === 0) {
      text += d === 1 && (hasHigher || text !== '') ? 'เอ็ด' : digit
    } else if (place === 1) {
      text += (d === 1 ? '' : d === 2 ? 'ยี่' : digit) + 'สิบ'
    } else {
      text += digit + (PLACES[place] ?? '')
    }
  }
  return text
}

function integerText(digits: string): string {
  const trimmed = digits.replace(/^0+/, '')
  if (trimmed === '') return ''
  if (trimmed.length <= 6) return chunkText(trimmed, false)
  const head = trimmed.slice(0, -6)
  const tail = trimmed.slice(-6)
  return integerText(head) + 'ล้าน' + chunkText(tail.replace(/^0+/, ''), true)
}

export function bahtText(amount: number): string {
  if (!Number.isFinite(amount)) return ''
  const negative = amount < 0
  // ปัดเป็นสตางค์ก่อนแยกส่วน (ทำงานกับจำนวนเต็มสตางค์ กันทศนิยมลอยตัว เช่น 0.1 + 0.2)
  const satangTotal = Math.round(Math.abs(amount) * 100)
  const baht = Math.floor(satangTotal / 100)
  const satang = satangTotal % 100

  const bahtPart = integerText(String(baht))
  const satangPart = satang === 0 ? '' : chunkText(String(satang), false) + 'สตางค์'

  let text: string
  if (!bahtPart && !satangPart) text = 'ศูนย์บาทถ้วน'
  else if (!satangPart) text = `${bahtPart}บาทถ้วน`
  else if (!bahtPart) text = satangPart
  else text = `${bahtPart}บาท${satangPart}`

  return negative ? `ลบ${text}` : text
}
