import { apiRequest, isApiError } from './client'
import type { PagedResult } from './types'

export type PurchaseKind = 'PR' | 'PO'
export type PurchaseLine = { id: string; catalogItemId: string; code: string; name: string; unit: string; quantity: number; unitCost: number; receivedGood: number; receivedDamaged: number; outstanding: number }
export type Purchase = { id: string; kind: PurchaseKind; number: string; status: string; sourceRequestId: string | null; supplierId: string | null; supplierName: string | null; warehouseId: string; warehouseName: string; requiredDate: string | null; note: string | null; paymentTerms: string | null; cancelReason: string | null; createdByName: string; createdAt: string; updatedAt: string; total: number; version: string; lines: PurchaseLine[] }
export type PurchaseInput = { warehouseId: string; supplierId: string | null; requiredDate: string | null; note: string; paymentTerms: string; version?: string; lines: { catalogItemId: string; quantity: number; unitCost: number }[] }
export type ReceiptInput = { requestId: string; deliveryNumber: string; lines: { purchaseLineId: string; goodQuantity: number; damagedQuantity: number; unitCost: number; note: string }[] }
export type Receipt = { id: string; number: string; purchaseOrderId: string; deliveryNumber: string; receivedAt: string; receivedByName: string; lines: ReceiptInput['lines'] }
export type StockItem = { id: string; code: string; name: string; unit: string; stockManaged: boolean; onHand: number; reserved: number; available: number; onOrder: number; damaged: number; value: number | null }
export type StockLot = { id: string; warehouseId: string; warehouseName: string; documentNumber: string; receivedAt: string; receivedQuantity: number; remainingQuantity: number; unitCost: number | null; value: number | null }
export type StockMovement = { id: string; operationId: string; documentNumber: string; type: string; warehouseId: string; quantity: number; damagedQuantity: number; balanceBefore: number; balanceAfter: number; unitCost: number | null; reason: string; performedByName: string; occurredAt: string }
export type StockDetail = { item: StockItem; lots: StockLot[]; movements: StockMovement[] }
export type IssueInput = { requestId: string; catalogItemId: string; warehouseId: string; quantity: number; reason: string }
const path = (kind: PurchaseKind) => `/api/v1/${kind === 'PR' ? 'purchase-requests' : 'purchase-orders'}`
export const purchases = (kind: PurchaseKind, q: string, status: string, page: number) => apiRequest<PagedResult<Purchase>>(`${path(kind)}?${new URLSearchParams({ q, status, page: String(page), pageSize: '25' })}`)
export const purchase = (kind: PurchaseKind, id: string) => apiRequest<Purchase>(`${path(kind)}/${id}`)
export const savePurchase = (kind: PurchaseKind, id: string | null, input: PurchaseInput) => apiRequest<Purchase>(`${path(kind)}${id ? `/${id}` : ''}`, { method: id ? 'PUT' : 'POST', body: JSON.stringify(input) })
export const purchaseAction = (doc: Purchase, action: string, reason: string) => apiRequest<Purchase>(`${path(doc.kind)}/${doc.id}/${action}`, { method: 'POST', body: JSON.stringify({ version: doc.version, reason }) })
export const convertPurchase = (doc: Purchase, supplierId: string) => apiRequest<Purchase>(`${path('PR')}/${doc.id}/convert`, { method: 'POST', body: JSON.stringify({ version: doc.version, supplierId }) })
export const approvalThreshold = () => apiRequest<number>('/api/v1/purchase-orders/approval-threshold')
export const receipts = (id: string) => apiRequest<Receipt[]>(`${path('PO')}/${id}/receipts`)
export const receive = (id: string, input: ReceiptInput) => apiRequest<Receipt>(`${path('PO')}/${id}/receipts`, { method: 'POST', body: JSON.stringify(input) })
export const stockItems = (q: string) => apiRequest<StockItem[]>(`/api/v1/inventory/items?q=${encodeURIComponent(q)}`)
export const stockDetail = (id: string) => apiRequest<StockDetail>(`/api/v1/inventory/items/${id}`)
export const stockOpening = (input: { catalogItemId: string; warehouseId: string; unitCost: number; expectedOnHand: number; expectedDamaged: number; reason: string }) => apiRequest<boolean>('/api/v1/inventory/opening-balances', { method: 'POST', body: JSON.stringify(input) })
export const stockIssue = (input: IssueInput) => apiRequest<StockMovement[]>('/api/v1/inventory/issues', { method: 'POST', body: JSON.stringify(input) })

// Retain an uncertain command across modal closes/reloads so retry uses the same UUID and payload.
export function pendingCommand<T>(key: string): T | null {
  try { return JSON.parse(sessionStorage.getItem(key) || 'null') as T | null } catch { return null }
}
export async function stockCommand<T, R>(key: string, input: T, send: (input: T) => Promise<R>) {
  sessionStorage.setItem(key, JSON.stringify(input))
  try { const result = await send(input); sessionStorage.removeItem(key); return result }
  catch (error) { if (isApiError(error) && error.status && error.status < 500) sessionStorage.removeItem(key); throw error }
}
