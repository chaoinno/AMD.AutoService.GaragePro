import { useMutation, useQuery } from '@tanstack/react-query'
import {
  ArrowLeft,
  Building2,
  Check,
  ChevronRight,
  CircleAlert,
  Clock,
  FileClock,
  LoaderCircle,
  LogOut,
  MapPin,
  Phone,
  ShieldCheck,
  UserRoundCheck,
} from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { getBranchShifts, openShiftSession } from '../../api/auth'
import { isApiError, isForbiddenError } from '../../api/client'
import type { BranchOption } from '../../api/types'
import { StateBlock } from '../../components/StateBlock'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Badge } from '../../components/ui/badge'
import { Button } from '../../components/ui/button'
import { Card, CardContent } from '../../components/ui/card'
import { Skeleton } from '../../components/ui/skeleton'
import { clearStoredSession, saveActiveShiftSession, useSession } from '../../lib/session'
import { cn } from '../../lib/utils'

export function BranchPage() {
  const navigate = useNavigate()
  const { session } = useSession()
  const preSession = session?.stage === 'branch' ? session : null
  const branches = useMemo(() => preSession?.branches ?? [], [preSession?.branches])
  const [selectedBranchId, setSelectedBranchId] = useState<number | null>(() =>
    branches.length === 1 ? branches[0]?.branchId ?? null : null,
  )
  const [selectedShiftId, setSelectedShiftId] = useState<string | null>(null)
  const selectedBranch = branches.find((branch) => branch.branchId === selectedBranchId) ?? null

  useEffect(() => {
    if (!preSession) navigate(session?.stage === 'active' ? '/quotations' : '/login', { replace: true })
  }, [navigate, preSession, session?.stage])

  useEffect(() => {
    if (branches.length === 1) setSelectedBranchId(branches[0]?.branchId ?? null)
  }, [branches])

  const shiftsQuery = useQuery({
    queryKey: ['branch-shifts', selectedBranchId],
    queryFn: () => getBranchShifts(selectedBranchId!),
    enabled: Boolean(preSession && selectedBranchId),
  })

  useEffect(() => {
    const shifts = shiftsQuery.data
    if (!shifts?.length) return
    setSelectedShiftId((current) =>
      current && shifts.some((shift) => shift.shiftId === current)
        ? current
        : shifts.find((shift) => shift.isCurrent)?.shiftId ?? null,
    )
  }, [shiftsQuery.data])

  const openMutation = useMutation({
    mutationFn: () => openShiftSession(selectedBranchId!, selectedShiftId!),
    onSuccess: (result) => {
      saveActiveShiftSession(result)
      navigate('/quotations', { replace: true })
    },
  })

  if (!preSession) return null

  const logout = () => {
    clearStoredSession()
    navigate('/login', { replace: true })
  }

  const chooseBranch = (branch: BranchOption) => {
    setSelectedBranchId(branch.branchId)
    setSelectedShiftId(null)
    openMutation.reset()
  }

  return (
    <main className="branch-page">
      <header className="branch-topbar">
        <div className="auth-brand auth-brand--dark">
          <span className="auth-brand__mark">GP</span>
          <span>
            <strong>GaragePro</strong>
            <small>ระบบงานบริการ</small>
          </span>
        </div>
        <div className="branch-user">
          <span className="branch-user__avatar" aria-hidden="true">
            {getInitials(preSession.user.displayName)}
          </span>
          <span>
            <strong>{preSession.user.displayName}</strong>
            <small>{preSession.user.roleLabelTh}</small>
          </span>
          <Button variant="ghost" size="sm" onClick={logout}>
            <LogOut aria-hidden="true" /> ออกจากระบบ
          </Button>
        </div>
      </header>

      <div className="branch-page__content">
        <section className="branch-heading">
          <span className="branch-heading__icon" aria-hidden="true"><ShieldCheck /></span>
          <div>
            <p className="eyebrow">เตรียมพื้นที่ทำงาน</p>
            <h1>{selectedBranch ? 'เลือกกะที่กำลังปฏิบัติงาน' : 'เลือกสาขาที่ต้องการเข้าใช้งาน'}</h1>
            <p>ระบบจะผูกข้อมูลและกิจกรรมทั้งหมดกับสาขาและกะที่เลือก</p>
          </div>
        </section>

        <ol className="branch-steps" aria-label="ขั้นตอนเลือกพื้นที่ทำงาน">
          <li className={cn('branch-step', selectedBranch && 'branch-step--complete')}>
            <span>{selectedBranch ? <Check aria-hidden="true" /> : '1'}</span>
            <div><strong>เลือกสาขา</strong><small>{selectedBranch?.name ?? 'สาขาที่ปฏิบัติงาน'}</small></div>
          </li>
          <li className="branch-steps__line" />
          <li className={cn('branch-step', selectedBranch && 'branch-step--active')}>
            <span>2</span>
            <div><strong>เลือกกะ</strong><small>ช่วงเวลาปฏิบัติงาน</small></div>
          </li>
        </ol>

        {!selectedBranch ? (
          branches.length ? (
            <section className="branch-grid" aria-label="รายการสาขา">
              {branches.map((branch) => (
                <Button
                  variant="ghost"
                  className="branch-card"
                  key={branch.branchId}
                  onClick={() => chooseBranch(branch)}
                >
                  <span className="branch-card__icon" aria-hidden="true"><Building2 /></span>
                  <span className="branch-card__body">
                    <strong>{branch.name}</strong>
                    <small><MapPin aria-hidden="true" /> {branch.address || 'ยังไม่ระบุที่อยู่'}</small>
                    {branch.phone ? <small><Phone aria-hidden="true" /> {branch.phone}</small> : null}
                    <span className="branch-card__stats">
                      <span><FileClock aria-hidden="true" /><b className="money">{branch.pendingQuotationCount}</b><small>รอเสนอราคา</small></span>
                      <span><UserRoundCheck aria-hidden="true" /><b className="money">{branch.waitingApprovalCount}</b><small>รออนุมัติ</small></span>
                    </span>
                  </span>
                  <ChevronRight className="branch-card__arrow" aria-hidden="true" />
                </Button>
              ))}
            </section>
          ) : (
            <StateBlock
              variant="empty"
              title="ไม่พบสาขาที่เข้าใช้งานได้"
              reason="บัญชีนี้ยังไม่มีสาขาที่ผูกไว้ กรุณาติดต่อผู้ดูแลระบบ"
              traceId="คำขอเข้าสู่ระบบสำเร็จแต่ไม่มีข้อมูลสาขา"
              actionLabel="กลับไปเข้าสู่ระบบ"
              onAction={logout}
            />
          )
        ) : (
          <section className="shift-section">
            <div className="selected-branch-bar">
              <span className="selected-branch-bar__icon" aria-hidden="true"><Building2 /></span>
              <span><small>สาขาที่เลือก</small><strong>{selectedBranch.name}</strong></span>
              {branches.length > 1 ? (
                <Button variant="outline" size="sm" onClick={() => setSelectedBranchId(null)}>
                  <ArrowLeft aria-hidden="true" /> เปลี่ยนสาขา
                </Button>
              ) : null}
            </div>

            {shiftsQuery.isPending ? (
              <StateBlock
                variant="loading"
                title="กำลังโหลดรายการกะ"
                reason={`ระบบกำลังดึงกะที่เปิดใช้งานของ ${selectedBranch.name}`}
                traceId="ยังไม่มี traceId ระหว่างรอการตอบกลับ"
                actionLabel="โหลดรายการกะใหม่"
                onAction={() => void shiftsQuery.refetch()}
              >
                <div className="branch-loading-skeletons" aria-hidden="true">
                  {Array.from({ length: 3 }, (_, index) => <Skeleton key={index} />)}
                </div>
              </StateBlock>
            ) : shiftsQuery.isError ? (
              <StateBlock
                variant={isForbiddenError(shiftsQuery.error) ? 'forbidden' : 'error'}
                title={isForbiddenError(shiftsQuery.error) ? 'ไม่มีสิทธิ์ดูกะของสาขานี้' : 'โหลดรายการกะไม่สำเร็จ'}
                reason={isApiError(shiftsQuery.error) ? shiftsQuery.error.messageTh : 'ไม่สามารถโหลดข้อมูลกะได้'}
                traceId={isApiError(shiftsQuery.error) ? shiftsQuery.error.traceId : undefined}
                actionLabel="ลองโหลดใหม่"
                onAction={() => void shiftsQuery.refetch()}
              />
            ) : shiftsQuery.data?.length ? (
              <div className="shift-grid" role="radiogroup" aria-label="เลือกกะ">
                {shiftsQuery.data.map((shift) => {
                  const selected = shift.shiftId === selectedShiftId
                  return (
                    <Button
                      variant="ghost"
                      role="radio"
                      aria-checked={selected}
                      className={cn('shift-card', selected && 'shift-card--selected')}
                      key={shift.shiftId}
                      onClick={() => {
                        setSelectedShiftId(shift.shiftId)
                        openMutation.reset()
                      }}
                    >
                      <span className="shift-card__check" aria-hidden="true">
                        {selected ? <Check /> : <Clock />}
                      </span>
                      <span className="shift-card__title">
                        <strong>{shift.name}</strong>
                        {shift.isCurrent ? <Badge variant="success"><Clock aria-hidden="true" /> กะปัจจุบัน</Badge> : null}
                      </span>
                      <span className="shift-card__time money">{shift.startTime} – {shift.endTime}</span>
                      <span className="shift-card__supervisor">
                        <UserRoundCheck aria-hidden="true" /> หัวหน้ากะ: {shift.supervisorName || 'ยังไม่ระบุ'}
                      </span>
                    </Button>
                  )
                })}
              </div>
            ) : (
              <StateBlock
                variant="empty"
                title="ยังไม่มีกะที่เปิดให้เลือก"
                reason="สาขานี้ไม่มีข้อมูลกะในขณะนี้ กรุณาลองโหลดใหม่หรือติดต่อผู้ดูแลระบบ"
                traceId="คำขอนี้สำเร็จและไม่พบรายการกะ"
                actionLabel="โหลดรายการกะใหม่"
                onAction={() => void shiftsQuery.refetch()}
              />
            )}

            {openMutation.isError ? (
              <Alert variant="destructive" className="shift-error">
                <CircleAlert aria-hidden="true" />
                <div>
                  <AlertTitle>
                    {isApiError(openMutation.error) ? openMutation.error.messageTh : 'เข้าใช้งานกะไม่สำเร็จ'}
                  </AlertTitle>
                  <AlertDescription>
                    <p>เลือกกะแล้วลองเข้าใช้งานอีกครั้ง</p>
                    <p className="trace-id">รหัสติดตาม (traceId): {isApiError(openMutation.error) ? openMutation.error.traceId : 'ไม่พบรหัสติดตาม'}</p>
                  </AlertDescription>
                </div>
              </Alert>
            ) : null}

            <Card className="shift-action-card">
              <CardContent>
                <div>
                  <strong>{selectedShiftId ? 'พร้อมเข้าใช้งาน' : 'กรุณาเลือกกะก่อน'}</strong>
                  <p>{selectedShiftId ? `กิจกรรมใหม่จะบันทึกที่ ${selectedBranch.name}` : 'เลือกกะที่กำลังปฏิบัติงานจากรายการด้านบน'}</p>
                </div>
                <Button size="lg" disabled={!selectedShiftId || openMutation.isPending} onClick={() => openMutation.mutate()}>
                  {openMutation.isPending ? <LoaderCircle className="spin" aria-hidden="true" /> : <ShieldCheck aria-hidden="true" />}
                  {openMutation.isPending ? 'กำลังเปิดพื้นที่ทำงาน' : 'เข้าใช้งาน'}
                </Button>
              </CardContent>
            </Card>
          </section>
        )}
      </div>
    </main>
  )
}

function getInitials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((word) => word[0]).join('') || 'ผช'
}
