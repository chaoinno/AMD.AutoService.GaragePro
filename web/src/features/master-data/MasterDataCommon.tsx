import { AlertTriangle, CheckCircle2, CircleOff, LockKeyhole } from 'lucide-react'
import type { ReactNode } from 'react'
import { isApiError, isForbiddenError } from '../../api/client'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { SkeletonRows, StateBlock } from '../../components/StateBlock'

export function StatusBadge({ isActive }: { isActive: boolean }) {
  return isActive
    ? <Badge className="active-badge"><CheckCircle2 aria-hidden="true" /> ใช้งานอยู่</Badge>
    : <Badge variant="outline" className="inactive-badge"><CircleOff aria-hidden="true" /> ปิดใช้งาน</Badge>
}

export function PermissionNote({ canManage }: { canManage: boolean }) {
  return canManage ? null : <Alert className="master-permission-note"><LockKeyhole aria-hidden="true" /><div><AlertTitle>ดูข้อมูลได้อย่างเดียว</AlertTitle><AlertDescription>การเพิ่ม แก้ไข และเปลี่ยนสถานะ เปิดให้เฉพาะผู้จัดการสาขา</AlertDescription></div></Alert>
}

export function InlineError({ error }: { error: unknown }) {
  return <Alert variant="destructive"><AlertTriangle aria-hidden="true" /><div><AlertTitle>{errorMessage(error)}</AlertTitle><AlertDescription>รหัสติดตาม (traceId): {traceId(error) || 'ไม่พบรหัสติดตาม'}</AlertDescription></div></Alert>
}

export function QueryState({ query, loadingTitle, emptyTitle, emptyReason, onRetry, children }: {
  query: { isPending: boolean; isError: boolean; error: unknown; data?: unknown; isFetching?: boolean }
  loadingTitle: string
  emptyTitle: string
  emptyReason: string
  onRetry: () => void
  children: ReactNode
}) {
  if (query.isPending) return <StateBlock variant="loading" title={loadingTitle} reason="ระบบกำลังอ่านข้อมูลจากฐานข้อมูลบริการ" actionLabel="โหลดใหม่" onAction={onRetry}><SkeletonRows /></StateBlock>
  if (query.isError && isForbiddenError(query.error)) return <StateBlock variant="forbidden" title="ไม่มีสิทธิ์ดูข้อมูลนี้" reason={errorMessage(query.error)} traceId={traceId(query.error)} actionLabel="ลองใหม่" onAction={onRetry} />
  if (query.isError) return <StateBlock variant="error" title="โหลดข้อมูลไม่สำเร็จ" reason={errorMessage(query.error)} traceId={traceId(query.error)} actionLabel="ลองใหม่" onAction={onRetry} />
  if (!query.data || (Array.isArray(query.data) && query.data.length === 0)) return <StateBlock variant="empty" title={emptyTitle} reason={emptyReason} traceId="คำขอสำเร็จและไม่พบรายการ" actionLabel="ลองใหม่" onAction={onRetry} />
  return <>{query.isFetching ? <div className="master-stale" role="status">กำลังปรับข้อมูลล่าสุด… <Button size="sm" variant="link" onClick={onRetry}>โหลดใหม่</Button></div> : null}{children}</>
}

export function errorMessage(error: unknown) { return isApiError(error) ? error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ' }
export function traceId(error: unknown) { return isApiError(error) ? error.traceId : undefined }

export function Field({ label, error, wide, children }: { label: string; error?: string; wide?: boolean; children: ReactNode }) {
  return <label className={`field ${wide ? 'field--wide' : ''}`}><span>{label}</span>{children}{error ? <small className="field-error">{error}</small> : null}</label>
}
