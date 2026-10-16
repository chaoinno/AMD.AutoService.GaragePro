import { ClipboardCheck, MessageSquare, PenLine, ShieldCheck, Timer, Wallet } from 'lucide-react'
import { APP_STORE_URL, PLAY_STORE_URL } from '../appStore'
import { PhoneMockup } from '../mockups/PhoneMockup'

// [BIZ] ความสามารถของแอป Flutter ที่ทำแล้วจริง (mobile/) — ไม่ใส่ offline/สแกน QR/push ที่ยังไม่ได้ทำ
const APP_FEATURES = [
  { icon: ClipboardCheck, text: 'เช็คลิสต์รับรถ พร้อมถ่ายรูปจากกล้องมือถือ' },
  { icon: Timer, text: 'จับเวลางานซ่อม พัก และสลับคันได้ในปุ่มเดียว' },
  { icon: MessageSquare, text: 'แชทในจ๊อบ แนบรูป และ @ เพื่อนร่วมทีม' },
  { icon: PenLine, text: 'ลูกค้าเซ็นอนุมัติราคาและเซ็นรับรถบนหน้าจอ' },
  { icon: ShieldCheck, text: 'ตรวจ QC และบันทึกผลทดลองขับ' },
  { icon: Wallet, text: 'รับชำระเงินตามสิทธิ์ของผู้ใช้' },
] as const

export function MobileAppSection() {
  return (
    <section id="mobile" className="landing-section landing-section--light" aria-labelledby="landing-mobile-title">
      <div className="landing-container landing-mobile">
        <div className="landing-mobile__visual">
          <PhoneMockup variant="queue" className="landing-mobile__phone landing-mobile__phone--back" />
          <PhoneMockup variant="handover" className="landing-mobile__phone landing-mobile__phone--front" />
        </div>
        <div className="landing-mobile__copy">
          <p className="landing-eyebrow">แอปมือถือ</p>
          <h2 id="landing-mobile-title" className="landing-h2">ทำงานได้จากข้างรถ ไม่ต้องเดินกลับมาที่คอมพิวเตอร์</h2>
          <p className="landing-section__intro">
            แอป ServicePro สำหรับช่างและพนักงานหน้างาน ใช้บัญชีพนักงานเดียวกับเว็บ ข้อมูลอัปเดตถึงกันทันที
          </p>
          <ul className="landing-mobile__list">
            {APP_FEATURES.map(({ icon: Icon, text }) => (
              <li key={text}>
                <span className="landing-feature__icon" aria-hidden="true"><Icon /></span>
                {text}
              </li>
            ))}
          </ul>

          <div className="landing-download" role="group" aria-labelledby="landing-download-title">
            <p id="landing-download-title" className="landing-download__title">ดาวน์โหลดแอป</p>
            <div className="landing-download__row">
              <div className="landing-download__stores">
                <a
                  className="landing-download__play"
                  href={PLAY_STORE_URL}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <img src="/store/google-play-badge-th.png" alt="ดาวน์โหลดบน Google Play (เปิดในแท็บใหม่)" width={646} height={250} />
                </a>
                <a
                  className="landing-download__apple"
                  href={APP_STORE_URL}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <img src="/store/app-store-badge-th.svg" alt="ดาวน์โหลดบน App Store (เปิดในแท็บใหม่)" width={120} height={40} />
                </a>
              </div>
              <div className="landing-download__qrs">
                <figure className="landing-download__qr">
                  <img src="/store/google-play-qr.svg" alt="QR code สำหรับเปิดหน้าดาวน์โหลดแอปบน Google Play" width={132} height={132} />
                  <figcaption>Android</figcaption>
                </figure>
                <figure className="landing-download__qr">
                  <img src="/store/app-store-qr.svg" alt="QR code สำหรับเปิดหน้าดาวน์โหลดแอปบน App Store" width={132} height={132} />
                  <figcaption>iPhone</figcaption>
                </figure>
              </div>
            </div>
            <p className="landing-download__note">สำหรับพนักงานของอู่ที่ใช้ ServicePro · เข้าสู่ระบบด้วยรหัสพนักงานเดียวกับเว็บ</p>
          </div>
        </div>
      </div>
    </section>
  )
}
