import { formatKm, MAX_ODOMETER_KM, parseKmInput } from '../../lib/format'

/// ตรวจช่องกรอกเลขไมล์ — ว่างคืน km=null ไม่มี error (ให้ปุ่มบอกเหตุผลเอง) · กรอกผิดคืนข้อความบอกวิธีแก้
/// API ตรวจซ้ำเสมอ (Odometer.IsValid) ที่นี่แค่ให้ผู้ใช้รู้ก่อนกดบันทึก
export function mileageInputError(text: string): { km: number | null; error?: string } {
  if (!text.trim()) return { km: null }
  const km = parseKmInput(text)
  if (km == null || km > MAX_ODOMETER_KM) {
    return { km: null, error: `กรอกเป็นตัวเลขจำนวนเต็ม 0 ถึง ${formatKm(MAX_ODOMETER_KM)}` }
  }
  return { km }
}
