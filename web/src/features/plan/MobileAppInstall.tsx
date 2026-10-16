import { ExternalLink, Smartphone } from 'lucide-react'
import { APP_STORE_URL, PLAY_STORE_URL } from '../landing/appStore'
import './plan.css'

/** QR + ลิงก์ store ของแอปช่าง — QR ไฟล์เดียวกับหน้าขาย (เข้ารหัส PLAY_STORE_URL/APP_STORE_URL ตรงตัว) */
export function MobileAppInstall({ compact = false }: { compact?: boolean }) {
  const qrSize = compact ? 76 : 112
  return (
    <div className={`mobile-app-install${compact ? ' mobile-app-install--compact' : ''}`}>
      <div className="mobile-app-install__qrs">
        <figure>
          <img className="mobile-app-install__qr" src="/store/google-play-qr.svg" alt="QR โค้ดดาวน์โหลดแอป ServicePro บน Google Play" width={qrSize} height={qrSize} />
          <figcaption>Android</figcaption>
        </figure>
        <figure>
          <img className="mobile-app-install__qr" src="/store/app-store-qr.svg" alt="QR โค้ดดาวน์โหลดแอป ServicePro บน App Store" width={qrSize} height={qrSize} />
          <figcaption>iPhone</figcaption>
        </figure>
      </div>
      <div className="mobile-app-install__copy">
        <strong><Smartphone aria-hidden="true" /> แอป ServicePro (Android · iPhone)</strong>
        <span>สแกน QR ด้วยมือถือช่าง หรือเปิดลิงก์ store</span>
        <a href={PLAY_STORE_URL} target="_blank" rel="noopener noreferrer">
          เปิด Google Play <ExternalLink aria-hidden="true" />
        </a>
        <a href={APP_STORE_URL} target="_blank" rel="noopener noreferrer">
          เปิด App Store <ExternalLink aria-hidden="true" />
        </a>
      </div>
    </div>
  )
}
