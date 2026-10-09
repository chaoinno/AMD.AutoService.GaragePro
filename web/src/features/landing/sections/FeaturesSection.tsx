import {
  BarChart3,
  Bell,
  Boxes,
  CalendarClock,
  ClipboardCheck,
  FileText,
  Receipt,
  Timer,
  Wrench,
} from 'lucide-react'

// [BIZ] ทุกข้อต้องเป็นความสามารถที่มีอยู่ในระบบจริงแล้ว (อ้างอิงสถานะใน CLAUDE.md) — อย่าเพิ่มสิ่งที่ยังไม่ได้ทำ
const FEATURES = [
  { icon: ClipboardCheck, title: 'รับรถไว ไม่มีข้อโต้แย้งภายหลัง', body: 'เช็คลิสต์สภาพรถพร้อมรูปถ่ายและเลขไมล์ พิมพ์ใบรับรถให้ลูกค้าเซ็นได้ทันที' },
  { icon: FileText, title: 'ใบเสนอราคาที่ลูกค้าอนุมัติรายรายการ', body: 'ลูกค้าเลือกอนุมัติทีละรายการพร้อมลายเซ็น แก้ราคาเป็นฉบับใหม่โดยประวัติเดิมไม่หาย และใช้เทมเพลตงานที่ทำบ่อยได้' },
  { icon: Wrench, title: 'เห็นงานซ่อมทุกคันในจอเดียว', body: 'สถานะจ๊อบตั้งแต่รับรถจนปิดงาน เบิกอะไหล่ตามรายการที่อนุมัติ และตรวจ QC ก่อนส่งมอบ' },
  { icon: Timer, title: 'รู้เวลาทำงานจริงของช่าง', body: 'ช่างกดเริ่ม พัก หรือสลับคันจากแอป ผู้จัดการเห็นชั่วโมงทำงานต่อจ๊อบโดยไม่ต้องจดเอง' },
  { icon: Receipt, title: 'ชำระเงินและเอกสารครบในที่เดียว', body: 'ใบแจ้งยอด ใบเสร็จ ใบกำกับภาษีเต็มรูป และขายสินค้าหน้าร้านที่ตัดสต็อกในระบบเดียวกัน' },
  { icon: Boxes, title: 'คลังและจัดซื้อแบบ FIFO', body: 'ขอซื้อ สั่งซื้อ รับสินค้า คิดต้นทุนตามล็อตจริง และเห็นจำนวนพร้อมใช้ก่อนเบิก' },
  { icon: CalendarClock, title: 'นัดหมายและนัดบริการครั้งถัดไป', body: 'ปฏิทินลากวางเลื่อนนัด บันทึกนัดเข้ารับบริการตามระยะทาง/เดือน และรายชื่อรถใกล้ครบรอบให้โทรตาม' },
  { icon: Bell, title: 'ทีมรู้ทันทีเมื่อมีเรื่องต้องทำ', body: 'แจ้งเตือนเมื่อถูกกล่าวถึงในแชท ลูกค้าอนุมัติราคา หรือเอกสารจัดซื้อรออนุมัติ' },
  { icon: BarChart3, title: 'ตัวเลขที่ผู้บริหารใช้ตัดสินใจได้', body: 'แดชบอร์ดวันนี้ รอบเวลาทำงาน ยอดขาย-ต้นทุน-กำไร อายุสต็อก และประวัติรถรายคัน' },
] as const

export function FeaturesSection() {
  return (
    <section id="features" className="landing-section landing-section--light" aria-labelledby="landing-features-title">
      <div className="landing-container">
        <p className="landing-eyebrow">ฟีเจอร์</p>
        <h2 id="landing-features-title" className="landing-h2">ครอบคลุมงานบริการทุกขั้นตอน</h2>
        <p className="landing-section__intro">
          ออกแบบจากงานจริงของอู่บริการ — ลดการจดซ้ำ ลดงานตกหล่นระหว่างฝ่าย และเก็บหลักฐานทุกจุดที่ลูกค้าอาจถามย้อนหลัง
        </p>
        <ul className="landing-feature-grid">
          {FEATURES.map(({ icon: Icon, title, body }) => (
            <li key={title} className="landing-feature">
              <span className="landing-feature__icon" aria-hidden="true"><Icon /></span>
              <h3>{title}</h3>
              <p>{body}</p>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}
