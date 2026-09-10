/// mention ฝังเป็น token ในข้อความเอง รูปแบบ "@[staffId:ชื่อ]" — ไม่ parse จาก plain text ฝั่ง server
/// (server รับ mentionedStaffIds แยกมาให้ validate ตรงๆ) client เป็นคนสร้าง/ตัด render เอง
/// ชื่อพนักงานถูก sanitize ตัดอักขระ [ ] : ออกก่อนฝัง กันชนกับ delimiter ของ token เอง

export type ChatBodySegment =
  | { type: 'text'; value: string }
  | { type: 'mention'; staffId: number; name: string }

const MENTION_PATTERN = /@\[(\d+):([^[\]]+)]/g

export function sanitizeMentionName(name: string): string {
  return name.replace(/[[\]:]/g, '').trim()
}

export function buildMentionToken(staffId: number, name: string): string {
  return `@[${staffId}:${sanitizeMentionName(name)}]`
}

export function parseChatBody(body: string): ChatBodySegment[] {
  const segments: ChatBodySegment[] = []
  let lastIndex = 0
  MENTION_PATTERN.lastIndex = 0
  let match: RegExpExecArray | null
  // eslint-disable-next-line no-cond-assign
  while ((match = MENTION_PATTERN.exec(body)) !== null) {
    if (match.index > lastIndex) segments.push({ type: 'text', value: body.slice(lastIndex, match.index) })
    segments.push({ type: 'mention', staffId: Number(match[1]), name: match[2] ?? '' })
    lastIndex = match.index + (match[0]?.length ?? 0)
  }
  if (lastIndex < body.length) segments.push({ type: 'text', value: body.slice(lastIndex) })
  return segments
}

export function extractMentionedStaffIds(body: string): number[] {
  const ids = parseChatBody(body)
    .filter((segment): segment is Extract<ChatBodySegment, { type: 'mention' }> => segment.type === 'mention')
    .map((segment) => segment.staffId)
  return Array.from(new Set(ids))
}

/// หาตำแหน่ง "@" ที่กำลังพิมพ์ค้นหาอยู่ก่อนตำแหน่ง caret (ไม่มีช่องว่าง/ไม่อยู่หลัง token ที่ปิดแล้ว)
/// คืน null ถ้าไม่ได้อยู่ระหว่างพิมพ์ mention
export function findMentionTriggerIndex(value: string, caret: number): number | null {
  for (let i = caret - 1; i >= 0; i--) {
    const ch = value[i]
    if (ch === '@') return i
    if (ch === undefined || /\s/.test(ch) || ch === ']') return null
  }
  return null
}
