/** ชื่อผลิตภัณฑ์ที่แสดงต่อผู้ใช้ — rebrand จาก "GaragePro Auto Services" เป็น ServicePro (2026-10-05) */
export const BRAND_NAME = 'ServicePro'

/**
 * ตัวอักษร "ServicePro" แบบเดียวกับโลโก้ (service-pro-logo/logo-with-text.jpg): "Service" ตามสีข้อความ + "Pro" สีฟ้า
 * ใช้บนพื้นเข้ม (sidebar, หน้า landing) เท่านั้น — ฟ้าของ "Pro" ถูกยกให้อ่อนกว่าในไฟล์โลโก้ เพราะสีเดิม
 * (#2F65C0) บน navy-900 ได้ contrast แค่ ~2.8:1 ส่วน #4C9BFF ได้ ~5.5:1
 */
export function BrandWordmark() {
  return (
    <strong className="brand-wordmark" aria-label={BRAND_NAME}>
      Service<span className="brand-wordmark__accent">Pro</span>
    </strong>
  )
}

/** ตราหกเหลี่ยมบนหัวเอกสารพิมพ์ทุกใบ (แทนกล่องตัวอักษร "GP" เดิม) */
export function DocumentBrandMark() {
  return <img className="document-brand-mark" src="/servicepro-logo.png" alt="" aria-hidden="true" />
}
