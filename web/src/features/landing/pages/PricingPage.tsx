import { Check } from 'lucide-react'
import { Link } from 'react-router'
import { usePageMeta } from '../../../lib/seo'
import { ContactActions } from '../ContactActions'
import { PageHeading } from '../PageHeading'

const FULL_PACKAGE = ['จัดซื้อและสต็อก FIFO', 'ขายหน้าร้าน POS และใบกำกับภาษี', 'รายงานขั้นสูง', 'ส่งออก Excel', 'หลายสาขา']

export function PricingPage() {
  usePageMeta('/pricing')
  return (
    <>
      <PageHeading path="/pricing">เลือกแพ็กเกจโปรแกรมสำหรับอู่บริการให้เหมาะกับงานของทีมคุณ</PageHeading>
      <section className="landing-section landing-section--light" aria-label="แพ็กเกจโปรแกรมอู่บริการ">
        <div className="landing-container landing-pricing">
          <article className="landing-feature landing-pricing__card">
            <p className="landing-eyebrow">เริ่มต้นทดลองใช้</p>
            <h2 className="landing-h2">ทดลองใช้ฟรี 30 วัน ไม่ต้องใส่บัตรเครดิต</h2>
            <p>ทดลองโปรแกรมสำหรับอู่เซอร์วิส ServicePro เพื่อทำความรู้จักขั้นตอนงานของระบบ</p>
            <p>เปิดรับสมัครเร็วๆ นี้</p>
            <div className="landing-page-actions"><Link className="landing-btn landing-btn--primary" to="/signup">ทดลองใช้ฟรี</Link></div>
          </article>
          <article className="landing-feature landing-pricing__card">
            <p className="landing-eyebrow">สำหรับงานที่ครบขึ้น</p>
            <h2 className="landing-h2">แพ็กเกจเต็ม — ติดต่อสอบถาม</h2>
            <p>เพิ่มความสามารถสำหรับการจัดการอู่บริการ:</p>
            <ul className="landing-package-list">
              {FULL_PACKAGE.map((feature) => <li key={feature}><Check aria-hidden="true" /> {feature}</li>)}
            </ul>
            <ContactActions />
          </article>
        </div>
      </section>
    </>
  )
}
