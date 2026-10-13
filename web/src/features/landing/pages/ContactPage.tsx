import { usePageMeta } from '../../../lib/seo'
import { ContactSection } from '../ContactSection'
import { PageHeading } from '../PageHeading'

export function ContactPage() {
  usePageMeta('/contact')
  return (
    <>
      <PageHeading path="/contact">สอบถามโปรแกรมสำหรับอู่บริการและอู่เซอร์วิส หรือขอให้ทีมงานสาธิตการใช้งาน</PageHeading>
      <ContactSection />
    </>
  )
}
