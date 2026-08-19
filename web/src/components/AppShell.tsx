import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  BarChart3,
  Building2,
  ChevronDown,
  CircleAlert,
  CircleHelp,
  FileText,
  LogOut,
  Package,
  Search,
  StopCircle,
  Users,
  Wrench,
} from 'lucide-react'
import { type ReactNode, useState } from 'react'
import { NavLink, useNavigate } from 'react-router'
import { toast } from 'sonner'
import { closeShiftSession } from '../api/auth'
import { isApiError } from '../api/client'
import { clearStoredSession, useSession } from '../lib/session'
import { ConfirmModal } from './ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from './ui/alert'
import { Avatar, AvatarFallback } from './ui/avatar'
import { Button } from './ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './ui/dropdown-menu'
import { Input } from './ui/input'

type AppShellProps = {
  children: ReactNode
  title?: string
  documentMode?: boolean
}

const navItems = [
  { to: '/quotations', icon: FileText, label: 'ใบเสนอราคา' },
  { to: '/jobs', icon: Wrench, label: 'งานซ่อม' },
  { to: '/customers', icon: Users, label: 'ลูกค้า' },
  { to: '/inventory', icon: Package, label: 'คลังอะไหล่' },
  { to: '/reports', icon: BarChart3, label: 'รายงาน' },
]

export function AppShell({ children, title = 'ใบเสนอราคา', documentMode = false }: AppShellProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { session } = useSession()
  const activeSession = session?.stage === 'active' ? session : null
  const [closeShiftOpen, setCloseShiftOpen] = useState(false)

  const closeMutation = useMutation({
    mutationFn: () => closeShiftSession(activeSession!.sessionId),
    onSuccess: () => {
      toast.success('ปิดกะเรียบร้อยแล้ว')
      queryClient.clear()
      clearStoredSession()
      navigate('/login', { replace: true })
    },
  })

  const logout = () => {
    queryClient.clear()
    clearStoredSession()
    navigate('/login', { replace: true })
  }

  const displayName = activeSession?.user.displayName ?? 'ผู้ใช้งาน'

  return (
    <div className={`app-shell ${documentMode ? 'app-shell--document' : ''}`}>
      <aside className="sidebar print-hidden">
        <div className="brand">
          <span className="brand__mark">GP</span>
          <span className="brand__copy">
            <strong>GaragePro</strong>
            <small>ระบบงานบริการ</small>
          </span>
        </div>
        <nav className="sidebar__nav" aria-label="เมนูหลัก">
          {navItems.map((item) => {
            const Icon = item.icon
            return (
              <NavLink
                key={item.to}
                to={item.to}
                className={({ isActive }) => `sidebar__link ${isActive ? 'sidebar__link--active' : ''}`}
              >
                <Icon className="sidebar__icon" aria-hidden="true" />
                <span className="sidebar__label">{item.label}</span>
              </NavLink>
            )
          })}
        </nav>
        <div className="sidebar__footer">
          <CircleHelp className="sidebar__icon" aria-hidden="true" />
          <span className="sidebar__label">ช่วยเหลือ</span>
        </div>
      </aside>

      <div className="app-shell__body">
        <header className="topbar print-hidden">
          <div>
            <span className="topbar__eyebrow">งานบริการ</span>
            <h1>{title}</h1>
          </div>
          <div className="topbar__actions">
            <label className="global-search">
              <Search aria-hidden="true" />
              <span className="sr-only">ค้นหาทั่วทั้งระบบ</span>
              <Input type="search" placeholder="ค้นหางาน ลูกค้า ทะเบียน" />
            </label>
            <div className="branch-button" aria-label="สาขาและกะปัจจุบัน">
              <Building2 aria-hidden="true" />
              <span>
                <strong>{activeSession?.branchName ?? 'ยังไม่เลือกสาขา'}</strong>
                <small>{activeSession?.shiftName ?? 'ยังไม่เลือกกะ'}</small>
              </span>
            </div>
            <DropdownMenu>
              <DropdownMenuTrigger className="user-menu" aria-label="เปิดเมนูผู้ใช้งาน">
                <Avatar>
                  <AvatarFallback>{getInitials(displayName)}</AvatarFallback>
                </Avatar>
                <span>
                  <strong>{displayName}</strong>
                  <small>{activeSession?.user.roleLabelTh ?? 'ไม่ระบุบทบาท'}</small>
                </span>
                <ChevronDown aria-hidden="true" />
              </DropdownMenuTrigger>
              <DropdownMenuContent className="user-dropdown">
                <DropdownMenuLabel>
                  <strong>{displayName}</strong>
                  <small>{activeSession?.branchName} · {activeSession?.shiftName}</small>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                {activeSession?.user.canCloseShift ? (
                  <DropdownMenuItem onClick={() => setCloseShiftOpen(true)}>
                    <StopCircle aria-hidden="true" /> ปิดกะ
                  </DropdownMenuItem>
                ) : null}
                <DropdownMenuItem onClick={logout}>
                  <LogOut aria-hidden="true" /> ออกจากระบบ
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </header>
        <main className="app-main">{children}</main>
      </div>

      <ConfirmModal
        open={closeShiftOpen}
        title="ยืนยันการปิดกะ"
        description={`ปิดกะ ${activeSession?.shiftName ?? ''} ที่ ${activeSession?.branchName ?? ''}`}
        size="small"
        onClose={() => {
          if (closeMutation.isPending) return
          closeMutation.reset()
          setCloseShiftOpen(false)
        }}
        footer={
          <>
            <Button variant="ghost" disabled={closeMutation.isPending} onClick={() => setCloseShiftOpen(false)}>
              ยกเลิก
            </Button>
            <Button variant="destructive" disabled={closeMutation.isPending} onClick={() => closeMutation.mutate()}>
              <StopCircle aria-hidden="true" />
              {closeMutation.isPending ? 'กำลังปิดกะ' : 'ยืนยันปิดกะ'}
            </Button>
          </>
        }
      >
        <p className="modal-confirm-copy">หลังปิดกะ ระบบจะออกจากระบบและต้องเข้าสู่ระบบใหม่ก่อนเริ่มกะถัดไป</p>
        {closeMutation.isError ? (
          <Alert variant="destructive">
            <CircleAlert aria-hidden="true" />
            <div>
              <AlertTitle>{isApiError(closeMutation.error) ? closeMutation.error.messageTh : 'ปิดกะไม่สำเร็จ'}</AlertTitle>
              <AlertDescription>
                <p>ตรวจสอบสถานะกะแล้วลองใหม่อีกครั้ง</p>
                <p className="trace-id">รหัสติดตาม (traceId): {isApiError(closeMutation.error) ? closeMutation.error.traceId : 'ไม่พบรหัสติดตาม'}</p>
              </AlertDescription>
            </div>
          </Alert>
        ) : null}
      </ConfirmModal>
    </div>
  )
}

function getInitials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((word) => word[0]).join('') || 'ผช'
}
