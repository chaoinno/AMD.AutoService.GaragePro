import type { QueryClient } from '@tanstack/react-query'

/// วันนัดเข้า/วันนัดส่งมอบถูกแสดงหลายที่ (การ์ดจ๊อบ · ตาราง · ปฏิทิน · ประวัติการเปลี่ยนวันนัด)
/// — หลังแก้จากจุดไหนก็ตาม (modal ในการ์ดจ๊อบ หรือลากวางในปฏิทิน) ต้อง invalidate ชุดเดียวกันเสมอ
export function invalidateJobSchedule(queryClient: QueryClient, jobId: string) {
  void queryClient.invalidateQueries({ queryKey: ['job-detail', jobId] })
  void queryClient.invalidateQueries({ queryKey: ['job-schedule-history', jobId] })
  void queryClient.invalidateQueries({ queryKey: ['jobs-table'] })
  void queryClient.invalidateQueries({ queryKey: ['jobs-calendar'] })
}
