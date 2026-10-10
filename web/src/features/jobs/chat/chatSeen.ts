/// "เครื่องนี้เห็นข้อความแชทล่าสุดของจ๊อบถึง id ไหนแล้ว" — localStorage ต่อเครื่อง ไม่ sync ข้าม device/browser
/// ใช้ร่วมกันระหว่างจุดแดงของ JobChatWidget และป้าย "มีข้อความใหม่" บนบอร์ดรถในอู่ ให้ตัดสินตรงกันเสมอ

const lastSeenKey = (jobId: string) => `garagepro.jobchat.last-seen.${jobId}`

export function getLastSeenChatId(jobId: string): string | null {
  try {
    return window.localStorage.getItem(lastSeenKey(jobId))
  } catch {
    return null // private mode/ปิด storage — ไม่ใช่ข้อมูลสำคัญ ปล่อยผ่านได้
  }
}

export function setLastSeenChatId(jobId: string, messageId: string) {
  try {
    window.localStorage.setItem(lastSeenKey(jobId), messageId)
  } catch {
    // เช่นเดียวกับด้านบน
  }
}
