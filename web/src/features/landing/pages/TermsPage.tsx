import { usePageMeta } from '../../../lib/seo'
import { LegalPlaceholder } from '../LegalPlaceholder'
import { PageHeading } from '../PageHeading'

export function TermsPage() {
  usePageMeta('/terms')
  return <><PageHeading path="/terms" /><LegalPlaceholder /></>
}
