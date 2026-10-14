/**
 * ทะเบียนหน้าสาธารณะ (นอก desktop-guard · ไม่ต้องล็อกอิน) — แหล่งเดียวที่ App.tsx, meta/canonical, prerender
 * และ sitemap/robots อ่านร่วมกัน · pure TS ให้ `node --test` ตรวจความยาว title/description ได้
 * เพิ่มหน้าใหม่ที่นี่ที่เดียว แล้ว route จริงใน App.tsx ต้องมีให้ตรงกัน
 */
export type PublicRoute = {
  path: string
  /** `<title>` ≤ 60 ตัวอักษร (Google ตัดที่ราว 60) */
  title: string
  /** meta description ≤ 160 ตัวอักษร */
  description: string
  /** H1 ของหน้า — หน้าต้องใช้ข้อความเดียวกันเพื่อให้ตรงกับที่ prerender */
  h1?: string
  /** true = สร้างเป็น HTML จริงตอน build + อยู่ใน sitemap · false = render ฝั่ง client เท่านั้น */
  prerender: boolean
  changefreq: 'weekly' | 'monthly'
  priority: number
  /** ไม่ให้ search engine index (เช่น /login ซึ่งเป็นหน้าแรก + modal เข้าสู่ระบบ) */
  noindex?: boolean
  jsonLd?: ('Organization' | 'SoftwareApplication' | 'FAQPage')[]
}

export const SITE_NAME = 'ServicePro'

// [BIZ] ข้อความต้องเป็นความสามารถที่มีจริง — title/description หน้าแรกตาม docs/12 §8.2
export const PUBLIC_ROUTES: PublicRoute[] = [
  {
    path: '/',
    title: 'ServicePro | โปรแกรมสำหรับอู่บริการ ทดลองใช้ฟรี 30 วัน',
    description:
      'โปรแกรมบริหารงานอู่บริการ/อู่เซอร์วิส รับรถ ใบเสนอราคา งานซ่อม QC ชำระเงิน ส่งมอบรถ บนเว็บและแอปช่าง ทดลองใช้ฟรี 30 วัน',
    h1: 'โปรแกรมสำหรับอู่บริการ ครบตั้งแต่รับรถจนส่งมอบ — ทดลองใช้ฟรี 30 วัน',
    prerender: true,
    changefreq: 'weekly',
    priority: 1,
    jsonLd: ['Organization', 'SoftwareApplication', 'FAQPage'],
  },
  {
    // หน้าเดียวกับ `/` แต่เปิด modal เข้าสู่ระบบ — ไม่ควรถูก index ซ้ำกับหน้าแรก
    path: '/login',
    title: 'เข้าสู่ระบบ | ServicePro',
    description: 'เข้าสู่ระบบสำหรับอู่บริการ ServicePro ด้วยอีเมลเจ้าของอู่ บัญชี Google หรือรหัสพนักงานของอู่',
    h1: 'โปรแกรมสำหรับอู่บริการ ครบตั้งแต่รับรถจนส่งมอบ — ทดลองใช้ฟรี 30 วัน',
    prerender: false,
    changefreq: 'monthly',
    priority: 0.1,
    noindex: true,
  },
  {
    path: '/features',
    title: 'ฟีเจอร์โปรแกรมอู่เซอร์วิส ServicePro',
    description: 'โปรแกรมสำหรับอู่เซอร์วิส ตั้งแต่รับรถ เสนอราคา งานซ่อม QC จนส่งมอบ พร้อมจัดซื้อ สต็อก FIFO ขายหน้าร้าน และรายงาน',
    h1: 'ฟีเจอร์โปรแกรมอู่เซอร์วิส ServicePro',
    jsonLd: ['SoftwareApplication'],
    prerender: true,
    changefreq: 'monthly',
    priority: 0.8,
  },
  {
    path: '/mobile-app',
    title: 'แอปช่างในระบบอู่บริการ | ServicePro',
    description: 'แอปช่าง ServicePro เชื่อมกับระบบอู่บริการ รับรถ ถ่ายรูป จับเวลางานซ่อม แชท และเซ็นรับรถ ดาวน์โหลดบน Google Play ส่วน iOS เร็วๆ นี้',
    h1: 'แอปสำหรับช่างในอู่',
    prerender: true,
    changefreq: 'monthly',
    priority: 0.8,
  },
  {
    path: '/pricing',
    title: 'ราคาโปรแกรมอู่บริการและแพ็กเกจ | ServicePro',
    description: 'โปรแกรมสำหรับอู่บริการ ทดลองใช้ฟรี 30 วัน ไม่ต้องใส่บัตรเครดิต แพ็กเกจเต็มพร้อมจัดซื้อ สต็อก POS รายงานขั้นสูง และหลายสาขา ติดต่อสอบถาม',
    h1: 'ราคาและแพ็กเกจ ServicePro',
    jsonLd: ['SoftwareApplication'],
    prerender: true,
    changefreq: 'monthly',
    priority: 0.8,
  },
  {
    path: '/about',
    title: 'เกี่ยวกับผู้พัฒนาโปรแกรมอู่บริการ | ServicePro',
    description: 'รู้จัก Armadillo Tech ผู้พัฒนา ServicePro โปรแกรมสำหรับอู่บริการและอู่เซอร์วิส ต่อยอดจาก GaragePro เชื่อมงานออฟฟิศกับงานช่าง',
    h1: 'เกี่ยวกับ ServicePro และ Armadillo Tech',
    prerender: true,
    changefreq: 'monthly',
    priority: 0.5,
  },
  {
    path: '/contact',
    title: 'ติดต่อทีมงานโปรแกรมอู่บริการ | ServicePro',
    description: 'สอบถามโปรแกรมสำหรับอู่บริการ ServicePro หรือขอ Demo ผ่านแบบฟอร์ม LINE @garagepro หรือโทร 090-996-6446',
    h1: 'ติดต่อทีมงาน ServicePro',
    prerender: true,
    changefreq: 'monthly',
    priority: 0.5,
  },
  {
    path: '/privacy',
    title: 'ความเป็นส่วนตัว ระบบอู่บริการ | ServicePro',
    description: 'หน้าข้อมูลความเป็นส่วนตัวของระบบอู่บริการ ServicePro อยู่ระหว่างรอเจ้าของระบบระบุเนื้อหา',
    h1: 'นโยบายความเป็นส่วนตัวของระบบอู่บริการ ServicePro',
    prerender: true,
    changefreq: 'monthly',
    priority: 0.2,
  },
  {
    path: '/terms',
    title: 'ข้อกำหนดการใช้ระบบอู่บริการ | ServicePro',
    description: 'หน้าข้อกำหนดการใช้งานโปรแกรมสำหรับอู่บริการ ServicePro อยู่ระหว่างรอเจ้าของระบบระบุเนื้อหา',
    h1: 'ข้อกำหนดการใช้งานระบบอู่บริการ ServicePro',
    prerender: true,
    changefreq: 'monthly',
    priority: 0.2,
  },
  {
    path: '/signup',
    title: 'ทดลองใช้โปรแกรมอู่บริการฟรี 30 วัน | ServicePro',
    description: 'สมัครทดลองใช้โปรแกรมสำหรับอู่บริการ ServicePro ฟรี 30 วัน ด้วย Google หรืออีเมล ไม่ต้องใส่บัตรเครดิต ตั้งค่าอู่และเริ่มใช้งานได้ในไม่กี่นาที',
    h1: 'ทดลองใช้ ServicePro ฟรี 30 วัน',
    prerender: true,
    changefreq: 'weekly',
    priority: 0.8,
  },
]

export function findPublicRoute(pathname: string): PublicRoute | undefined {
  return PUBLIC_ROUTES.find((route) => route.path === pathname)
}

/** จับคู่แบบตรงตัว (ไม่ตัด trailing slash) — พฤติกรรมเดียวกับการเช็ค `/`/`/login` เดิมใน App.tsx */
export function isPublicPath(pathname: string): boolean {
  return findPublicRoute(pathname) !== undefined
}
