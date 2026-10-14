import { toast } from 'sonner'

/**
 * คัดลอกข้อความลงคลิปบอร์ด — `navigator.clipboard` ใช้ได้เฉพาะ secure context (https/localhost)
 * จึงมีทางสำรองแบบ textarea + execCommand สำหรับเครื่องที่เปิดผ่าน http ในวง LAN
 */
export async function copyText(text: string, label: string): Promise<boolean> {
  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(text)
    } else {
      const area = document.createElement('textarea')
      area.value = text
      area.setAttribute('readonly', '')
      area.style.position = 'fixed'
      area.style.opacity = '0'
      document.body.appendChild(area)
      area.select()
      const ok = document.execCommand('copy')
      area.remove()
      if (!ok) throw new Error('copy failed')
    }
    toast.success(`คัดลอก${label}แล้ว`)
    return true
  } catch {
    toast.error(`คัดลอก${label}ไม่สำเร็จ — เลือกข้อความแล้วคัดลอกเอง`)
    return false
  }
}
