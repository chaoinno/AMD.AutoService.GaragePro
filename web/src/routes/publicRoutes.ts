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
  },
  {
    // หน้าเดียวกับ `/` แต่เปิด modal เข้าสู่ระบบ — ไม่ควรถูก index ซ้ำกับหน้าแรก
    path: '/login',
    title: 'เข้าสู่ระบบ | ServicePro',
    description: 'เข้าสู่ระบบงานบริการ ServicePro ด้วยรหัสพนักงานของอู่',
    prerender: false,
    changefreq: 'monthly',
    priority: 0.1,
    noindex: true,
  },
]

export function findPublicRoute(pathname: string): PublicRoute | undefined {
  return PUBLIC_ROUTES.find((route) => route.path === pathname)
}

/** จับคู่แบบตรงตัว (ไม่ตัด trailing slash) — พฤติกรรมเดียวกับการเช็ค `/`/`/login` เดิมใน App.tsx */
export function isPublicPath(pathname: string): boolean {
  return findPublicRoute(pathname) !== undefined
}
