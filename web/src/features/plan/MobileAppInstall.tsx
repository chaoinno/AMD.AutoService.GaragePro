import { ExternalLink, Smartphone } from 'lucide-react'
import { PLAY_STORE_URL } from '../landing/appStore'
import './plan.css'

/** QR + ลิงก์ Google Play ของแอปช่าง — QR ไฟล์เดียวกับหน้าขาย (เข้ารหัส PLAY_STORE_URL ตรงตัว) */
export function MobileAppInstall({ compact = false }: { compact?: boolean }) {
  return (
    <div className={`mobile-app-install${compact ? ' mobile-app-install--compact' : ''}`}>
      <img
        className="mobile-app-install__qr"
        src="/store/google-play-qr.svg"
        alt="QR โค้ดดาวน์โหลดแอป ServicePro บน Google Play"
        width={compact ? 76 : 112}
        height={compact ? 76 : 112}
      />
      <div className="mobile-app-install__copy">
        <strong><Smartphone aria-hidden="true" /> แอป ServicePro (Android)</strong>
        <span>สแกน QR ด้วยมือถือช่าง หรือเปิดลิงก์ Google Play</span>
        <a href={PLAY_STORE_URL} target="_blank" rel="noopener noreferrer">
          เปิด Google Play <ExternalLink aria-hidden="true" />
        </a>
      </div>
    </div>
  )
}
