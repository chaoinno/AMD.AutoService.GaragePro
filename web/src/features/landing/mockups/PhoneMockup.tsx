import {
  CalendarClock,
  Check,
  ChevronLeft,
  ClipboardList,
  MessageSquare,
  Pause,
  PenLine,
  Search,
  ShieldCheck,
  Timer,
  Wrench,
} from 'lucide-react'

type Variant = 'queue' | 'handover'

const LABELS: Record<Variant, string> = {
  queue:
    'ภาพตัวอย่างแอปมือถือ ServicePro หน้าคิวงาน แสดงรายการรถพร้อมสถานะ และแถบจับเวลางานซ่อมที่กำลังเดินอยู่ด้านล่าง',
  handover:
    'ภาพตัวอย่างแอปมือถือ ServicePro หน้าส่งมอบรถ แสดงเช็คลิสต์ของในรถ นัดบริการครั้งถัดไป และช่องให้ลูกค้าเซ็นรับรถ',
}

/** ภาพจำลองหน้าจอแอปมือถือ — ข้อมูลสมมติ ไม่มีส่วนที่กดได้ */
export function PhoneMockup({ variant, className }: { variant: Variant; className?: string }) {
  return (
    <figure className={className ? `mock-phone ${className}` : 'mock-phone'} role="img" aria-label={LABELS[variant]}>
      <div className="mock-phone__screen" aria-hidden="true">
        <div className="mock-phone__status">
          <span>9:41</span>
          <span className="mock-phone__notch" />
          <span>5G</span>
        </div>
        {variant === 'queue' ? <QueueScreen /> : <HandoverScreen />}
      </div>
    </figure>
  )
}

function QueueScreen() {
  return (
    <>
      <div className="mock-phone__bar">
        <strong>คิวงาน</strong>
        <span className="mock-phone__chat"><MessageSquare /><i /></span>
      </div>
      <div className="mock-phone__body">
        <span className="mock-phone__search"><Search /> ค้นหาทะเบียน</span>
        <div className="mock-phone__card mock-phone__card--pinned">
          <div><b>1กข 2468</b><small>Toyota Yaris · JB…5001</small></div>
          <em className="mock-chip job-status-inprogress"><Wrench />กำลังซ่อม</em>
        </div>
        <div className="mock-phone__card">
          <div><b>7กด 9013</b><small>Isuzu D-Max · JB…5003</small></div>
          <em className="mock-chip job-status-qc"><ShieldCheck />QC ตรวจสอบ</em>
          <span className="mock-phone__note"><MessageSquare /> มีข้อความใหม่ในแชท</span>
        </div>
        <div className="mock-phone__card">
          <div><b>5ศธ 7342</b><small>Ford Ranger · JB…5005</small></div>
          <em className="mock-chip job-status-waitinspect"><ClipboardList />รอตรวจเช็ค</em>
        </div>
      </div>
      <div className="mock-phone__timer">
        <Timer />
        <span><small>กำลังทำงาน · 1กข 2468</small><b className="mock-mono">00:42:18</b></span>
        <span className="mock-phone__pause"><Pause /></span>
      </div>
    </>
  )
}

const HANDOVER_ITEMS = ['กุญแจรถ', 'คู่มือรถ', 'ยางอะไหล่ + แม่แรง', 'ของใช้ส่วนตัว']

function HandoverScreen() {
  return (
    <>
      <div className="mock-phone__bar">
        <ChevronLeft />
        <strong>ส่งมอบรถ · 2ฒม 1180</strong>
      </div>
      <div className="mock-phone__body">
        <p className="mock-phone__label">ของในรถ</p>
        {HANDOVER_ITEMS.map((item) => (
          <div key={item} className="mock-phone__check">
            <span className="mock-phone__tick"><Check /></span>
            <span>{item}</span>
            <small>คืนแล้ว/ไม่มี</small>
          </div>
        ))}
        <div className="mock-phone__next">
          <CalendarClock />
          <span><small>นัดบริการครั้งถัดไป</small><b><span className="mock-mono">55,210</span> กม. หรือ 6 เดือน</b></span>
        </div>
        <p className="mock-phone__label">ลายเซ็นลูกค้า (ผู้รับรถ)</p>
        <div className="mock-phone__sign">
          <svg viewBox="0 0 200 60" fill="none">
            <path d="M12 42c14-22 22-30 28-26s-8 30 2 28 14-26 24-24-4 22 6 20 12-14 20-14 6 10 14 8 16-12 30-10 22 6 40 2" />
          </svg>
          <PenLine />
        </div>
      </div>
      <div className="mock-phone__cta">ยืนยันส่งมอบรถ</div>
    </>
  )
}
