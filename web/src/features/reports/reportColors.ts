// สีต่อสถานะจ๊อบ — คัดจาก .job-status-* ใน web/src/index.css (สีเดียวกับป้ายสถานะทั้งระบบ)
// เพื่อให้แท่งกราฟใน "แดชบอร์ด" และ "รอบเวลาทำงาน" หมายถึงสถานะเดียวกันด้วยสีเดียวกันเสมอ ไม่ใช้สีใหม่แยกต่างหาก
export const jobStatusColors: Record<string, string> = {
  waitinspect: '#8a5a00',
  waitquote: '#1552b3',
  waitapprove: '#c1691f',
  approved: '#0b6d5e',
  inprogress: '#c1691f',
  waitparts: '#a31d1d',
  qc: '#6d28d9',
  ready: '#1a7f37',
  completed: '#0f2440',
  cancelled: '#a31d1d',
}

export const lineTypeColors: Record<string, string> = {
  Part: '#1B6BE3',
  Labor: '#0E9F8C',
}

// ไล่เฉดเดียว (amber) จากอ่อนไปเข้มตามอายุสต็อก — sequential ไม่ใช่ categorical จึงไม่ต้องแยกเฉด
export const agingBucketColors = ['#F5C77E', '#E0930C', '#B5730A', '#7A4A05']
