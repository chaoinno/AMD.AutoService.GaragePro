import { FAQ_ITEMS } from '../features/landing/faq.ts'
import type { PublicRoute } from '../routes/publicRoutes.ts'

export function pageStructuredData(route: PublicRoute, siteUrl: string) {
  const url = siteUrl.replace(/\/+$/, '')
  const schemas = {
    Organization: {
      '@context': 'https://schema.org',
      '@type': 'Organization',
      name: 'ServicePro',
      legalName: 'Armadillo Tech Co., Ltd.',
      url,
      logo: `${url}/servicepro-logo.png`,
      contactPoint: { '@type': 'ContactPoint', telephone: '+66-90-996-6446', contactType: 'customer service' },
      sameAs: ['https://line.me/R/ti/p/@garagepro', 'https://www.facebook.com/GaragePro'],
    },
    SoftwareApplication: {
      '@context': 'https://schema.org',
      '@type': 'SoftwareApplication',
      name: 'ServicePro',
      applicationCategory: 'BusinessApplication',
      operatingSystem: 'Web, Android, iOS',
      description: route.description,
      offers: { '@type': 'Offer', price: '0', priceCurrency: 'THB', description: 'ทดลองใช้ฟรี 30 วัน' },
    },
    FAQPage: {
      '@context': 'https://schema.org',
      '@type': 'FAQPage',
      mainEntity: FAQ_ITEMS.map(({ q, a }) => ({
        '@type': 'Question', name: q, acceptedAnswer: { '@type': 'Answer', text: a },
      })),
    },
  }
  return (route.jsonLd ?? []).map((type) => schemas[type])
}
