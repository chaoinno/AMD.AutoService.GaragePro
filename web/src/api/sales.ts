import { apiRequest, isApiError } from './client'

export type SaleStatus = 'draft' | 'completed' | 'voided' | 'cancelled'
export type PaymentMethod = 'cash' | 'transfer' | 'card' | 'qr'
export type BillDiscountType = 'none' | 'percent' | 'amount'
/// API ตั้ง DefaultIgnoreCondition = WhenWritingNull — ฟิลด์ที่เป็น null จะไม่มาใน JSON เลย (ได้ undefined)
/// ผู้ใช้ type เหล่านี้จึงต้องเช็คด้วย truthiness/`??` ไม่ใช่ `=== null`
export type SaleLine = { id: string; catalogItemId: string; code: string; name: string; unit: string; quantity: number; unitPrice: number; discountPercent: number; promotionId: string | null; promotionCode: string | null; promotionName: string | null; discountAmount: number; promotionAmount: number; netAmount: number; costAmount: number | null; availableInWarehouse: number }
export type SalePayment = { id: string; method: PaymentMethod; amount: number; reference: string | null; receivedByName: string; receivedAt: string }
export type Sale = { id: string; status: SaleStatus; warehouseId: string; legacyCustomerId: number | null; customerName: string | null; customerPhone: string | null; vatIncluded: boolean; billDiscountType: BillDiscountType; billDiscountValue: number; billPromotionId: string | null; billPromotionCode: string | null; billPromotionName: string | null; grossAmount: number; lineDiscountAmount: number; linePromotionAmount: number; subtotalAmount: number; billDiscountAmount: number; billPromotionAmount: number; netAmount: number; vatRate: number; vatAmount: number; totalAmount: number; costTotal: number | null; receiptNo: string | null; createdAt: string; completedAt: string | null; voidedAt: string | null; voidReason: string | null; lines: SaleLine[]; payments: SalePayment[] }
export type SaleSearch = { items: Sale[]; total: number; page: number; pageSize: number }
export type Promotion = { id: string; code: string; name: string; kind: 'percent' | 'amount'; value: number; maxAmount: number | null; scope: 'line' | 'bill'; minSubtotal: number | null; startsAt: string | null; endsAt: string | null; isActive: boolean; createdAt: string; updatedAt: string }
export type UpdateSaleInput = { warehouseId: string; legacyCustomerId: number | null; customerName: string | null; customerPhone: string | null; billDiscountType: BillDiscountType; billDiscountValue: number; billPromotionId: string | null; vatIncluded: boolean }
export type SaleLineInput = { quantity: number; discountPercent: number; promotionId: string | null }
export type SalePaymentInput = { method: PaymentMethod; amount: number; reference: string | null }
export type CheckoutInput = { requestId: string; payments: SalePaymentInput[] }

export const sales = (query: Record<string, string | number | undefined>) => apiRequest<SaleSearch>(`/api/v1/sales?${new URLSearchParams(Object.entries(query).filter(([, v]) => v !== undefined).map(([k, v]) => [k, String(v)]))}`)
export const countDraftSales = () => apiRequest<number>('/api/v1/sales/count-drafts')
export const sale = (id: string) => apiRequest<Sale>(`/api/v1/sales/${id}`)
export const createSale = (input: { warehouseId: string; legacyCustomerId: number | null; customerName: string | null; customerPhone: string | null; vatIncluded: boolean }) => apiRequest<Sale>('/api/v1/sales', { method: 'POST', body: JSON.stringify(input) })
export const updateSale = (id: string, input: UpdateSaleInput) => apiRequest<Sale>(`/api/v1/sales/${id}`, { method: 'PUT', body: JSON.stringify(input) })
export const addSaleLine = (id: string, input: SaleLineInput & { catalogItemId: string }) => apiRequest<Sale>(`/api/v1/sales/${id}/lines`, { method: 'POST', body: JSON.stringify(input) })
export const updateSaleLine = (id: string, lineId: string, input: SaleLineInput) => apiRequest<Sale>(`/api/v1/sales/${id}/lines/${lineId}`, { method: 'PUT', body: JSON.stringify(input) })
export const deleteSaleLine = (id: string, lineId: string) => apiRequest<Sale>(`/api/v1/sales/${id}/lines/${lineId}`, { method: 'DELETE' })
export const checkoutSale = (id: string, input: CheckoutInput) => apiRequest<Sale>(`/api/v1/sales/${id}/checkout`, { method: 'POST', body: JSON.stringify(input) })
export const voidSale = (id: string, input: { requestId: string; reason: string }) => apiRequest<Sale>(`/api/v1/sales/${id}/void`, { method: 'POST', body: JSON.stringify(input) })
export const cancelSale = (id: string) => apiRequest<boolean>(`/api/v1/sales/${id}/cancel`, { method: 'POST' })
export const promotions = (scope?: 'line' | 'bill') => apiRequest<Promotion[]>(scope ? `/api/v1/promotions/applicable?scope=${scope}` : '/api/v1/promotions')
export const savePromotion = (id: string | null, input: unknown) => apiRequest<Promotion>(`/api/v1/promotions${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', body: JSON.stringify(input) })
export const setPromotionStatus = (id: string, isActive: boolean) => apiRequest<boolean>(`/api/v1/promotions/${id}/status`, { method: 'PATCH', body: JSON.stringify({ isActive }) })

export function pendingSaleCheckout(key: string): CheckoutInput | null { try { return JSON.parse(sessionStorage.getItem(key) || 'null') as CheckoutInput | null } catch { return null } }
/// เก็บคำขอไว้ก่อนส่ง — ถ้าเน็ตหลุด/5xx จะส่งซ้ำด้วย requestId เดิมได้ · ลบทิ้งเมื่อ server ตอบผลชัดเจน (2xx/4xx)
/// storage อาจใช้ไม่ได้ (private window/ถูกบล็อก) — ต้องไม่ทำให้การชำระเงินล้ม แค่เสียความสามารถกู้คำขอหลังรีโหลดหน้า
export async function saleCheckoutCommand(saleId: string, key: string, input: CheckoutInput) {
  const forget = () => { try { sessionStorage.removeItem(key) } catch { /* ignore */ } }
  try { sessionStorage.setItem(key, JSON.stringify(input)) } catch { /* ignore */ }
  try {
    const result = await checkoutSale(saleId, input)
    forget()
    return result
  } catch (error) {
    if (isApiError(error) && error.status && error.status < 500) forget()
    throw error
  }
}