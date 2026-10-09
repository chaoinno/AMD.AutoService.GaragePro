import {
  ClipboardCheck,
  FileText,
  Monitor,
  PackageCheck,
  PenLine,
  Receipt,
  ShieldCheck,
  Smartphone,
  Wrench,
  type LucideIcon,
} from 'lucide-react'

type Step = {
  icon: LucideIcon
  title: string
  who: string
  body: string
  web: boolean
  app: boolean
}

// [BIZ] ลำดับตรงกับ state machine ของจ๊อบ (รับรถ → … → ส่งมอบ) และบทบาทที่ทำได้จริงตาม JobStateMachine/สิทธิ์ของแต่ละ service
const STEPS: Step[] = [
  { icon: ClipboardCheck, title: 'รับรถ', who: 'หน้าร้าน', body: 'เปิดจ๊อบ เช็คสภาพรถ ถ่ายรูป และบันทึกเลขไมล์', web: true, app: true },
  { icon: FileText, title: 'เสนอราคา', who: 'ธุรการ / ผู้จัดการ', body: 'สร้างใบเสนอราคาจากแคตตาล็อกหรือเทมเพลต ระบุช่างของแต่ละรายการ', web: true, app: false },
  { icon: PenLine, title: 'ลูกค้าอนุมัติ', who: 'ลูกค้า', body: 'เลือกอนุมัติรายรายการและเซ็นบนหน้าจอ', web: true, app: true },
  { icon: Wrench, title: 'ซ่อม', who: 'ช่าง', body: 'เบิกอะไหล่ตามที่อนุมัติ จับเวลาทำงาน และคุยกับทีมในแชทของจ๊อบ', web: true, app: true },
  { icon: ShieldCheck, title: 'ตรวจ QC', who: 'หัวหน้าช่าง / ธุรการ', body: 'ติ๊กผ่านทุกรายการที่ซ่อมและบันทึกผลทดลองขับ', web: true, app: true },
  { icon: Receipt, title: 'ชำระเงิน', who: 'แคชเชียร์', body: 'รับชำระ ออกใบเสร็จและใบกำกับภาษี', web: true, app: true },
  { icon: PackageCheck, title: 'ส่งมอบรถ', who: 'ทุกคนในทีม', body: 'ลูกค้าเซ็นรับรถ บันทึกไมล์ส่งมอบและนัดบริการครั้งถัดไป', web: true, app: true },
]

export function WorkflowSection() {
  return (
    <section id="workflow" className="landing-section landing-section--dark" aria-labelledby="landing-workflow-title">
      <div className="landing-container">
        <p className="landing-eyebrow">ขั้นตอนการทำงาน</p>
        <h2 id="landing-workflow-title" className="landing-h2">เส้นทางของรถหนึ่งคัน ตั้งแต่เข้าจนออกจากอู่</h2>
        <p className="landing-section__intro landing-section__intro--dark">
          ทุกขั้นตอนส่งต่อกันในระบบ ระบบไม่ยอมให้ข้ามขั้นที่สำคัญ เช่น ส่งมอบรถก่อนออกใบเสร็จ
          หรือซ่อมรายการที่ลูกค้ายังไม่อนุมัติ
        </p>
        <ol className="landing-flow">
          {STEPS.map(({ icon: Icon, title, who, body, web, app }, index) => (
            <li key={title} className="landing-flow__step">
              <span className="landing-flow__marker" aria-hidden="true">
                <Icon />
                <small>{index + 1}</small>
              </span>
              <div className="landing-flow__copy">
                <h3>{title}</h3>
                <p className="landing-flow__who">{who}</p>
                <p>{body}</p>
                <p className="landing-flow__where">
                  {web ? <span><Monitor aria-hidden="true" /> เว็บ</span> : null}
                  {app ? <span><Smartphone aria-hidden="true" /> แอป</span> : null}
                </p>
              </div>
            </li>
          ))}
        </ol>
      </div>
    </section>
  )
}
