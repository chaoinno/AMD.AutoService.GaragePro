import { apiRequest } from './client'

export type ContactRequestInput = {
  /** สร้างใหม่ทุกครั้งที่แก้ฟอร์ม — กด "ลองใหม่" โดยไม่แก้อะไรจะใช้ค่าเดิม ทำให้กลุ่ม LINE ไม่ได้ข้อความซ้ำ */
  requestId: string
  name: string
  garage: string
  phone: string
  lineId: string
  garageSize: string
  note: string
  /** honeypot — ต้องว่างเสมอสำหรับคนจริง */
  website: string
}

export function submitContactRequest(input: ContactRequestInput) {
  return apiRequest<{ requestId: string }>('/api/v1/public/contact-requests', {
    method: 'POST',
    body: JSON.stringify(input),
    skipUnauthorizedRedirect: true,
  })
}
