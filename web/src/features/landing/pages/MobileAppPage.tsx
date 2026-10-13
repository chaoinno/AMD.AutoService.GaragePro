import { usePageMeta } from '../../../lib/seo'
import { PageHeading } from '../PageHeading'
import { MobileAppSection } from '../sections/MobileAppSection'

export function MobileAppPage() {
  usePageMeta('/mobile-app')
  return (
    <>
      <PageHeading path="/mobile-app">เชื่อมงานข้างรถกับระบบอู่บริการ ServicePro ใช้บัญชีพนักงานเดียวกับเว็บ</PageHeading>
      <MobileAppSection />
    </>
  )
}
