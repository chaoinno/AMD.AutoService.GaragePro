import { usePageMeta } from '../../../lib/seo'
import { LegalPlaceholder } from '../LegalPlaceholder'
import { PageHeading } from '../PageHeading'

export function PrivacyPage() {
  usePageMeta('/privacy')
  return <><PageHeading path="/privacy" /><LegalPlaceholder /></>
}
