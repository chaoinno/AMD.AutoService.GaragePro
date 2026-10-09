import { Eye, Gauge, Users } from 'lucide-react'

const VALUES = [
  { icon: Eye, title: 'Visibility', body: 'เห็นสถานะทุกคันแบบเรียลไทม์ ไม่ต้องเดินถามหน้างาน' },
  { icon: Users, title: 'Performance', body: 'ทุกฝ่ายทำงานบนข้อมูลชุดเดียวกัน ส่งต่องานได้ไม่ตกหล่น' },
  { icon: Gauge, title: 'Growth Intelligence', body: 'เปลี่ยนข้อมูลหน้างานเป็นตัวเลขต้นทุนและประสิทธิภาพที่ตัดสินใจได้' },
] as const

export function AboutSection() {
  return (
    <section id="about" className="landing-section" aria-labelledby="landing-about-title">
      <div className="landing-container landing-about">
        <div>
          <p className="landing-eyebrow">เกี่ยวกับเรา</p>
          <h2 id="landing-about-title" className="landing-h2">Armadillo Tech Co., Ltd.</h2>
          <p className="landing-lead">
            ผู้พัฒนา ServicePro — ระบบบริหารอู่ที่เข้าใจธุรกิจของคุณ
            และเปลี่ยนทุกข้อมูลหน้างานให้เป็นแรงขับเคลื่อนการเติบโต
          </p>
          <p className="landing-body">
            ServicePro ต่อยอดจากระบบ GaragePro ที่อู่ซ่อมสีและตัวถังใช้งานอยู่ มาสู่งานบริการ
            ซ่อมบำรุงรถยนต์ โดยใช้ข้อมูลลูกค้าและรถชุดเดิม ไม่ต้องย้ายข้อมูลใหม่
          </p>
        </div>
        <ul className="landing-values">
          {VALUES.map(({ icon: Icon, title, body }) => (
            <li key={title}>
              <span className="landing-feature__icon" aria-hidden="true"><Icon /></span>
              <div>
                <h3>{title}</h3>
                <p>{body}</p>
              </div>
            </li>
          ))}
        </ul>
      </div>
    </section>
  )
}
