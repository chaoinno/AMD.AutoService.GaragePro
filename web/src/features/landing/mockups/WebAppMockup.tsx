import {
  BarChart3,
  Bell,
  CalendarClock,
  ClipboardList,
  FileEdit,
  LayoutGrid,
  PackageCheck,
  Search,
  ShieldCheck,
  Wrench,
  type LucideIcon,
} from 'lucide-react'
import { BrandWordmark } from '../../../components/Brand'

type Row = {
  jobNo: string
  plate: string
  model: string
  status: 'inprogress' | 'waitquote' | 'qc' | 'ready' | 'waitinspect'
  amount: string
}

// [UI] ข้อมูลสมมติล้วน — ทะเบียน/เลขจ๊อบไม่ได้มาจากฐานจริง
// คอลัมน์เลขจ๊อบอยู่ท้ายสุดเพราะภาพมือถือใน hero ซ้อนทับขอบขวา — สถานะต้องมองเห็นเสมอ
// เลือกสถานะที่สีไม่ซ้ำกัน (waitapprove กับ inprogress ใช้สีเดียวกันใน index.css จึงไม่ใส่คู่กัน)
const ROWS: Row[] = [
  { jobNo: 'JB2610090105001', plate: '1กข 2468', model: 'Toyota Yaris', status: 'inprogress', amount: '4,280.00' },
  { jobNo: 'JB2610090105002', plate: '3ขค 5521', model: 'Honda City', status: 'waitquote', amount: '—' },
  { jobNo: 'JB2610090105003', plate: '7กด 9013', model: 'Isuzu D-Max', status: 'qc', amount: '8,640.00' },
  { jobNo: 'JB2610090105004', plate: '2ฒม 1180', model: 'Mazda 2', status: 'ready', amount: '3,150.00' },
  { jobNo: 'JB2610090105005', plate: '5ศธ 7342', model: 'Ford Ranger', status: 'waitinspect', amount: '—' },
]

const STATUS: Record<Row['status'], { label: string; icon: LucideIcon }> = {
  inprogress: { label: 'กำลังซ่อม', icon: Wrench },
  waitquote: { label: 'รอเสนอราคา', icon: FileEdit },
  qc: { label: 'QC ตรวจสอบ', icon: ShieldCheck },
  ready: { label: 'พร้อมส่งมอบ', icon: PackageCheck },
  waitinspect: { label: 'รอตรวจเช็ค', icon: ClipboardList },
}

const MENU: { icon: LucideIcon; label: string; active?: boolean }[] = [
  { icon: LayoutGrid, label: 'จ๊อบ', active: true },
  { icon: CalendarClock, label: 'นัดหมาย' },
  { icon: PackageCheck, label: 'สต็อก' },
  { icon: BarChart3, label: 'รายงาน' },
]

/** ภาพจำลองหน้ารายการจ๊อบบนเว็บ — เป็นภาพประกอบ ไม่มีส่วนที่กดได้ */
export function WebAppMockup() {
  return (
    <figure
      className="mock-web"
      role="img"
      aria-label="ภาพตัวอย่างหน้ารายการจ๊อบบนเว็บ ServicePro แสดงรถแต่ละคันพร้อมสถานะ เช่น กำลังซ่อม รอเสนอราคา QC ตรวจสอบ และพร้อมส่งมอบ"
    >
      <div className="mock-web__screen" aria-hidden="true">
        <div className="mock-web__chrome">
          <span className="mock-web__dots"><i /><i /><i /></span>
          <span className="mock-web__url">service.garage-pro.net/jobs</span>
        </div>
        <div className="mock-web__app">
          <aside className="mock-web__side">
            <span className="mock-web__brand">
              <img src="/servicepro-logo.png" alt="" />
              <BrandWordmark />
            </span>
            {MENU.map(({ icon: Icon, label, active }) => (
              <span key={label} className={active ? 'mock-web__menu mock-web__menu--active' : 'mock-web__menu'}>
                <Icon /> {label}
              </span>
            ))}
          </aside>
          <div className="mock-web__main">
            <div className="mock-web__top">
              <strong>จ๊อบ · รถในอู่</strong>
              <span className="mock-web__search"><Search /> ค้นหาทะเบียน / ลูกค้า</span>
              <span className="mock-web__bell"><Bell /><i>3</i></span>
            </div>
            <div className="mock-web__stats">
              <span><small>รถในอู่</small><b>12</b></span>
              <span><small>รถนัดหมายวันนี้</small><b>4</b></span>
              <span><small>พร้อมส่งมอบ</small><b>3</b></span>
            </div>
            <div className="mock-web__table">
              <div className="mock-web__row mock-web__row--head">
                <span>ทะเบียน</span><span>รถ</span><span>สถานะ</span><span className="mock-num">ยอด (บาท)</span><span>เลขจ๊อบ</span>
              </div>
              {ROWS.map((row) => {
                const { label, icon: Icon } = STATUS[row.status]
                return (
                  <div key={row.jobNo} className="mock-web__row">
                    <span><b>{row.plate}</b></span>
                    <span>{row.model}</span>
                    <span><em className={`mock-chip job-status-${row.status}`}><Icon />{label}</em></span>
                    <span className="mock-num mock-mono">{row.amount}</span>
                    <span className="mock-mono mock-web__jobno">{row.jobNo}</span>
                  </div>
                )
              })}
            </div>
          </div>
        </div>
      </div>
    </figure>
  )
}
