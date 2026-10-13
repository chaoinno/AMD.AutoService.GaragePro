import { usePageMeta } from '../../../lib/seo'
import { ContactActions } from '../ContactActions'
import { PageHeading } from '../PageHeading'

export function SignupComingSoonPage() {
  usePageMeta('/signup')
  return (
    <>
      <PageHeading path="/signup">โปรแกรมสำหรับอู่บริการและอู่เซอร์วิส ทดลองใช้ 30 วันโดยไม่ต้องใส่บัตรเครดิต</PageHeading>
      <section className="landing-section" aria-labelledby="signup-contact-title">
        <div className="landing-container">
          <h2 id="signup-contact-title" className="landing-h2">สอบถามทีมงานระหว่างรอเปิดรับสมัคร</h2>
          <p className="landing-body">ติดต่อผ่าน LINE โทรศัพท์ หรือฝากข้อมูลเพื่อขอ Demo ได้</p>
          <ContactActions />
        </div>
      </section>
    </>
  )
}
