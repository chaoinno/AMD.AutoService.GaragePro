import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2, Check, ChevronDown, LoaderCircle, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { getSwitchableBranches, switchBranch } from '../api/auth'
import { isApiError } from '../api/client'
import { saveSession, useSession } from '../lib/session'
import { ConfirmModal } from './ConfirmModal'
import { StateBlock } from './StateBlock'
import { Alert, AlertDescription, AlertTitle } from './ui/alert'
import { Button } from './ui/button'
import { Input } from './ui/input'

/**
 * ชื่อสาขาบน topbar — ผู้ดูแลระบบคลิกเพื่อสลับอู่ได้ บทบาทอื่นเห็นเป็นป้ายเฉยๆ เหมือนเดิม
 *
 * [SECURITY] server เป็นผู้ตัดสินทั้งหมด (admin จริงไหม · บัญชีปลายทางคือใคร) — client ส่งแค่ branchId
 * การซ่อนปุ่มตาม `isAdministrator` ใน session เป็นแค่ความสะดวก ไม่ใช่การป้องกัน
 */
export function BranchSwitcher() {
  const { session } = useSession()
  const [open, setOpen] = useState(false)
  const branchName = session?.branchName ?? 'ไม่พบข้อมูลสาขา'

  if (!session?.user.isAdministrator) {
    return (
      <div className="branch-button" aria-label="สาขาปัจจุบัน">
        <Building2 aria-hidden="true" />
        <span><strong>{branchName}</strong></span>
      </div>
    )
  }

  return (
    <>
      <button
        type="button"
        className="branch-button branch-button--switchable"
        onClick={() => setOpen(true)}
        aria-haspopup="dialog"
        aria-label={`สาขาปัจจุบัน ${branchName} — คลิกเพื่อสลับสาขา`}
        title="สลับสาขา"
      >
        <Building2 aria-hidden="true" />
        <span>
          <strong>{branchName}</strong>
          <small>คลิกเพื่อสลับสาขา</small>
        </span>
        <ChevronDown className="branch-button__chevron" aria-hidden="true" />
      </button>
      {open ? <BranchSwitchDialog onClose={() => setOpen(false)} /> : null}
    </>
  )
}

function BranchSwitchDialog({ onClose }: { onClose: () => void }) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const location = useLocation()
  const [keyword, setKeyword] = useState('')

  const branches = useQuery({
    queryKey: ['switchable-branches'],
    queryFn: getSwitchableBranches,
    staleTime: 0,
  })

  const mutation = useMutation({
    mutationFn: (branchId: number) => switchBranch(branchId),
    onSuccess: (result) => {
      // ข้อมูลที่แคชไว้เป็นของสาขาเดิมทั้งหมด — ต้องล้างก่อนบันทึก token ใหม่ ไม่งั้นหน้าจอโชว์ของสาขาเก่าค้าง
      queryClient.clear()
      saveSession(result)
      onClose()
      // ตัด query string (?job=, ?doc=) ทิ้ง — id ในนั้นเป็นของสาขาเดิม เปิดในสาขาใหม่ได้แค่ 404
      navigate(location.pathname, { replace: true })
      toast.success(`สลับไปสาขา ${result.branchName} แล้ว`, {
        description: `เข้าใช้งานในนามบัญชี ${result.user.userName}`,
      })
    },
  })

  const filtered = useMemo(() => {
    const term = keyword.trim().toLowerCase()
    const list = branches.data ?? []
    return term ? list.filter((b) => b.name.toLowerCase().includes(term)) : list
  }, [branches.data, keyword])

  const pendingId = mutation.isPending ? mutation.variables : undefined
  const close = () => { if (!mutation.isPending) onClose() }

  return (
    <ConfirmModal
      open
      onClose={close}
      title="สลับสาขา"
      description="แสดงเฉพาะสาขาที่เปิดใช้งานและมีบัญชีผู้ดูแลระบบชื่อเดียวกับคุณ — เลือกแล้วระบบจะเข้าสู่ระบบด้วยบัญชีของสาขานั้นให้อัตโนมัติ"
      size="medium"
      footer={<Button variant="outline" onClick={close} disabled={mutation.isPending}>ปิด</Button>}
    >
      {branches.isPending ? (
        <StateBlock variant="loading" title="กำลังโหลดรายชื่อสาขา" reason="กำลังค้นหาบัญชีของคุณในสาขาอื่น" actionLabel="ปิด" onAction={close} />
      ) : branches.isError ? (
        <StateBlock
          variant={isApiError(branches.error) && branches.error.status === 403 ? 'forbidden' : 'error'}
          title="โหลดรายชื่อสาขาไม่สำเร็จ"
          reason={isApiError(branches.error) ? branches.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
          traceId={isApiError(branches.error) ? branches.error.traceId : undefined}
          actionLabel="ลองใหม่"
          onAction={() => void branches.refetch()}
        />
      ) : branches.data.length === 0 ? (
        <StateBlock
          variant="empty"
          title="ไม่พบสาขาที่สลับไปได้"
          reason="ไม่มีสาขาที่เปิดใช้งานซึ่งมีบัญชีผู้ดูแลระบบชื่อเดียวกับคุณ — เพิ่มบัญชีได้ที่ระบบจัดการผู้ดูแลระบบ"
          actionLabel="ปิด"
          onAction={close}
        />
      ) : (
        <div className="branch-switcher">
          {mutation.isError ? (
            <Alert variant="destructive">
              <AlertTitle>สลับสาขาไม่สำเร็จ</AlertTitle>
              <AlertDescription>
                {isApiError(mutation.error) ? mutation.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
                <span className="trace-id">
                  รหัสติดตาม (traceId): <span>{isApiError(mutation.error) ? mutation.error.traceId : 'ไม่มี'}</span>
                </span>
              </AlertDescription>
            </Alert>
          ) : null}
          {branches.data.length > 6 ? (
            <label className="global-search branch-switcher__search">
              <Search aria-hidden="true" />
              <span className="sr-only">ค้นหาสาขา</span>
              <Input
                type="search"
                placeholder="ค้นหาชื่อสาขา"
                value={keyword}
                onChange={(event) => setKeyword(event.target.value)}
                data-dialog-autofocus
              />
            </label>
          ) : null}
          {filtered.length === 0 ? (
            <p className="branch-switcher__empty">ไม่พบสาขาที่ชื่อตรงกับ “{keyword.trim()}”</p>
          ) : (
            <ul className="branch-switcher__list" aria-label="สาขาที่สลับไปได้">
              {filtered.map((branch) => {
                const isPending = pendingId === branch.branchId
                return (
                  <li key={branch.branchId}>
                    <button
                      type="button"
                      className={`branch-switcher__item ${branch.isCurrent ? 'branch-switcher__item--current' : ''}`}
                      onClick={() => mutation.mutate(branch.branchId)}
                      disabled={branch.isCurrent || mutation.isPending}
                      aria-current={branch.isCurrent ? 'true' : undefined}
                    >
                      <Building2 className="branch-switcher__icon" aria-hidden="true" />
                      <span className="branch-switcher__text">
                        <strong>{branch.name}</strong>
                        {branch.address ? <small>{branch.address}</small> : null}
                      </span>
                      {branch.isCurrent ? (
                        <span className="branch-switcher__badge"><Check aria-hidden="true" /> สาขาปัจจุบัน</span>
                      ) : isPending ? (
                        <span className="branch-switcher__badge branch-switcher__badge--pending">
                          <LoaderCircle className="spin" aria-hidden="true" /> กำลังสลับ…
                        </span>
                      ) : (
                        <span className="branch-switcher__action">สลับไปสาขานี้</span>
                      )}
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      )}
    </ConfirmModal>
  )
}
