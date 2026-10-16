import type { ReactNode } from 'react'

// [BIZ] ข้อกำหนดและนโยบายความเป็นส่วนตัว ฉบับแรก (2026-10-16) เขียนจากสิ่งที่ระบบเก็บจริงในโค้ด
// ผู้ใช้ขอให้ใช้ถ้อยคำกว้างไปก่อน: จุดที่ยังไม่ได้ข้อสรุป (ระยะเก็บบางรายการ · เพดานความรับผิด · ที่ตั้งเซิร์ฟเวอร์ ·
// ฐานส่งข้อมูลต่างประเทศ ฯลฯ) เขียนแบบทั่วไป ยังไม่ผ่านนักกฎหมาย รายการที่ต้องตัดสินใจอยู่ใน Claude Doc
// "ร่างข้อกำหนดการใช้งานและนโยบายความเป็นส่วนตัว ServicePro" (ดู CLAUDE.md) · แก้เนื้อหาแล้วเปลี่ยนวันที่ด้านล่างด้วย
export const LEGAL_UPDATED_AT = '16 ตุลาคม 2569'

/** ข้อมูลบริษัท: ชื่อ/เลขทะเบียน/ที่อยู่/เบอร์จากหน้าแรก garage-pro.net · ผู้ใช้ยืนยันที่อยู่ เบอร์ และอีเมล 2026-10-16 */
export const COMPANY = {
  nameTh: 'บริษัท อาร์มาดิลโล่ เทค จำกัด',
  nameEn: 'Armadillo Tech Co., Ltd.',
  registrationNo: '0135563009156',
  address: '88 หมู่ที่ 3 ตำบลบางบัวทอง อำเภอบางบัวทอง จังหวัดนนทบุรี 11110',
  phone: '090-996-6446',
  phoneHref: 'tel:0909966446',
  line: '@garagepro',
  lineHref: 'https://line.me/R/ti/p/@garagepro',
  // [RISK] 2026-10-16 service.garage-pro.net ยังไม่มี MX record — อีเมลนี้ยังรับไม่ได้ ต้องตั้ง Email Routing ก่อนเผยแพร่
  email: 'support@service.garage-pro.net',
  hours: 'จันทร์ถึงเสาร์ 09:00 ถึง 18:00',
} as const

export function LegalPage({ children }: { children: ReactNode }) {
  return (
    <div className="landing-section landing-section--light">
      <div className="landing-container landing-legal">
        {children}
        <p className="landing-legal__updated">ปรับปรุงล่าสุด {LEGAL_UPDATED_AT}</p>
      </div>
    </div>
  )
}

/** ตารางเลื่อนแนวนอนได้บนจอแคบ ไม่ให้หน้าเว็บล้น */
export function LegalTable({ head, rows }: { head: string[]; rows: ReactNode[][] }) {
  return (
    <div className="landing-legal__table-wrap">
      <table>
        <thead>
          <tr>{head.map((h) => <th key={h} scope="col">{h}</th>)}</tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i}>{row.map((cell, j) => <td key={j}>{cell}</td>)}</tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

export function ContactLines() {
  return (
    <ul>
      <li>
        {COMPANY.nameTh} ({COMPANY.nameEn}) เลขทะเบียนนิติบุคคล {COMPANY.registrationNo} สำนักงานใหญ่ {COMPANY.address}
      </li>
      <li>
        อีเมล <a href={`mailto:${COMPANY.email}`}>{COMPANY.email}</a>
      </li>
      <li>
        โทร <a href={COMPANY.phoneHref}>{COMPANY.phone}</a> · LINE <a href={COMPANY.lineHref}>{COMPANY.line}</a> ·{' '}
        {COMPANY.hours} ติดต่อกลับภายใน 1 วันทำการ
      </li>
    </ul>
  )
}
