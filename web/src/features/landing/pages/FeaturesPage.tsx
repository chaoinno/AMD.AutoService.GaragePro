import { usePageMeta } from '../../../lib/seo'
import { PageHeading } from '../PageHeading'
import { FeaturesSection } from '../sections/FeaturesSection'

export function FeaturesPage() {
  usePageMeta('/features')
  return (
    <>
      <PageHeading path="/features">โปรแกรมสำหรับอู่บริการที่ให้หน้าร้าน ช่าง แคชเชียร์ และผู้บริหารทำงานบนข้อมูลชุดเดียวกัน</PageHeading>
      <FeaturesSection />
    </>
  )
}
