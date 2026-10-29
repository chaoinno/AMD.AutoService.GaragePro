import { apiRequest } from './client'
export type Platform = 'ios' | 'android'
export type AppUpdatePolicy = { platform: Platform; enabled: boolean; latestVersion: string; minimumVersion: string; message: string; revision: number; storeUrl: string }
export type AppUpdateHistory = { id: string; platform: Platform; revision: number; editedByShard: string; editedByUserId: number; editedByName: string; editedAt: string; before: AppUpdatePolicy; after: AppUpdatePolicy }
export const getAppUpdatePolicy = (platform: Platform) => apiRequest<AppUpdatePolicy>(`/api/v1/admin/app-updates/${platform}`)
export const getAppUpdateHistory = (platform: Platform) => apiRequest<AppUpdateHistory[]>(`/api/v1/admin/app-updates/${platform}/history`)
export const saveAppUpdatePolicy = (policy: AppUpdatePolicy, storeAvailableConfirmed: boolean) => apiRequest<AppUpdatePolicy>(`/api/v1/admin/app-updates/${policy.platform}`, { method: 'PUT', body: JSON.stringify({ ...policy, storeAvailableConfirmed }) })
export function compareVersions(a: string, b: string): number | null {
  const valid = /^(0|[1-9]\d{0,6})\.(0|[1-9]\d{0,6})\.(0|[1-9]\d{0,6})$/
  if (!valid.test(a) || !valid.test(b)) return null
  const left = a.split('.').map(Number), right = b.split('.').map(Number)
  for (let i = 0; i < 3; i++) if (left[i] !== right[i]) return left[i]! - right[i]!
  return 0
}
