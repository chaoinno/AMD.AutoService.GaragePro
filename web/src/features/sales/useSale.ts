import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCallback } from 'react'
import { sale, type Sale } from '../../api/sales'

export const saleKey = (id: string) => ['sale', id] as const

export function useSaleQuery(id: string) {
  return useQuery({ queryKey: saleKey(id), queryFn: () => sale(id) })
}

/// ทุก endpoint ของบิลคืน SaleDto ที่คำนวณยอดใหม่แล้ว — วางลง cache ตรงๆ แทน refetch
/// (ยอดเงินคิดฝั่ง server เสมอ หน้าจอแสดงตามที่ server ส่งมาเท่านั้น — docs/11)
export function useApplySale() {
  const queryClient = useQueryClient()
  return useCallback((next: Sale) => {
    queryClient.setQueryData(saleKey(next.id), next)
    void queryClient.invalidateQueries({ queryKey: ['sales'] })
    void queryClient.invalidateQueries({ queryKey: ['sale-draft-count'] })
    // ขาย/ยกเลิกบิลเปลี่ยนยอดพร้อมใช้ → ตัวเลข "ถึงจุดต่ำสุด" ที่เมนูและรายการสต็อกต้องเปลี่ยนตาม
    void queryClient.invalidateQueries({ queryKey: ['purchase-count', 'low-stock'] })
    void queryClient.invalidateQueries({ queryKey: ['inventory'] })
  }, [queryClient])
}
