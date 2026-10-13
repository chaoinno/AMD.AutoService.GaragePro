import { usePageMeta } from '../../../lib/seo'
import { PageHeading } from '../PageHeading'
import { AboutSection } from '../sections/AboutSection'

export function AboutPage() {
  usePageMeta('/about')
  return (
    <>
      <PageHeading path="/about">ผู้พัฒนาโปรแกรมสำหรับอู่บริการและอู่เซอร์วิส ที่เชื่อมข้อมูลออฟฟิศกับงานหน้างาน</PageHeading>
      <AboutSection />
    </>
  )
}
