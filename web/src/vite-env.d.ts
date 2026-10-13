/// <reference types="vite/client" />

// ตัวแปร build-time ของหน้าสาธารณะ/สมัครทดลองใช้ — ไม่ตั้ง = ซ่อนปุ่ม/widget ที่พึ่งค่านั้น (ดูแผน self-serve §3)
interface ImportMetaEnv {
  readonly VITE_GOOGLE_CLIENT_ID?: string
  readonly VITE_TURNSTILE_SITE_KEY?: string
  readonly VITE_SITE_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
