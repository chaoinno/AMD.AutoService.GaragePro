/**
 * ข้อความแจ้ง "ทำไมถึงหลุดออกจากระบบ" ส่งต่อจากจุดที่ API ตอบ 401 ไปยังหน้า login ข้ามการ redirect
 * (เช่น ถูกเพิกถอนสิทธิ์ · แพ็กเกจของอู่เปลี่ยน · ทดลองใช้ถูกระงับ) — ไม่งั้นผู้ใช้เห็นแค่ฟอร์ม login เปล่าๆ
 * เก็บใน sessionStorage (ต่อแท็บ หายเมื่อปิดแท็บ) · pure TS ไม่พึ่ง React/DOM โดยตรง ให้ทดสอบด้วย node:test ได้
 * และ storage ฉีดจากภายนอกได้
 */
export const LOGIN_NOTICE_KEY = 'garagepro.web.loginNotice'

/** notice ที่ค้างนานกว่านี้ถือว่าไม่เกี่ยวกับการเปิดหน้า login ครั้งนี้แล้ว (เช่น แท็บเปิดค้างแล้วกลับมาวันถัดไป) */
export const LOGIN_NOTICE_MAX_AGE_MS = 30 * 60 * 1000

export type LoginNotice = {
  code: string
  messageTh: string
  traceId?: string
  /** เวลาที่เก็บ (ISO) — ใช้ตัด notice เก่า */
  at?: string
}

export type NoticeStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

function defaultStorage(): NoticeStorage | null {
  // sessionStorage โยน SecurityError ได้เมื่อ browser บล็อก storage — ถือว่าไม่มีที่เก็บ ไม่ให้ flow logout ล้ม
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage
  } catch {
    return null
  }
}

export function stashLoginNotice(
  notice: LoginNotice,
  storage: NoticeStorage | null = defaultStorage(),
  now: Date = new Date(),
): void {
  if (!storage) return
  const payload: LoginNotice = {
    code: notice.code,
    messageTh: notice.messageTh,
    traceId: notice.traceId,
    at: now.toISOString(),
  }
  try {
    storage.setItem(LOGIN_NOTICE_KEY, JSON.stringify(payload))
  } catch {
    // storage เต็ม/ถูกบล็อก — ยอมเสียข้อความแจ้ง ดีกว่าทำให้การล้างเซสชันพัง
  }
}

/** อ่านแล้วลบทันที (แสดงได้ครั้งเดียว) — คืน null เมื่อไม่มี/อ่านไม่ออก/เก่าเกิน */
export function takeLoginNotice(
  storage: NoticeStorage | null = defaultStorage(),
  now: Date = new Date(),
): LoginNotice | null {
  if (!storage) return null
  let raw: string | null
  try {
    raw = storage.getItem(LOGIN_NOTICE_KEY)
    storage.removeItem(LOGIN_NOTICE_KEY)
  } catch {
    return null
  }
  return parseLoginNotice(raw, now)
}

export function parseLoginNotice(raw: string | null, now: Date = new Date()): LoginNotice | null {
  if (!raw) return null
  try {
    const value: unknown = JSON.parse(raw)
    if (!value || typeof value !== 'object') return null
    const notice = value as Partial<LoginNotice>
    if (typeof notice.code !== 'string' || typeof notice.messageTh !== 'string') return null
    if (typeof notice.at === 'string') {
      const at = Date.parse(notice.at)
      if (!Number.isNaN(at) && now.getTime() - at > LOGIN_NOTICE_MAX_AGE_MS) return null
    }
    return {
      code: notice.code,
      messageTh: notice.messageTh,
      traceId: typeof notice.traceId === 'string' ? notice.traceId : undefined,
      at: typeof notice.at === 'string' ? notice.at : undefined,
    }
  } catch {
    return null
  }
}
