import { useQueryClient } from '@tanstack/react-query'
import {
  BarChart3,
  Building2,
  CarFront,
  ChevronDown,
  CircleHelp,
  LogOut,
  Package,
  Search,
  Users,
  Wrench,
} from 'lucide-react'
import { type ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router'
import { clearStoredSession, useSession } from '../lib/session'
import { Avatar, AvatarFallback } from './ui/avatar'
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
  { to: '/jobs', icon: Wrench, label: 'จ๊อบ' },
  { to: '/customers', icon: Users, label: 'ลูกค้า' },
  { to: '/vehicles', icon: CarFront, label: 'รถลูกค้า' },
  { to: '/inventory', icon: Package, label: 'คลังอะไหล่' },
  { to: '/reports', icon: BarChart3, label: 'รายงาน' },
]

export function AppShell({ children, title = 'จ๊อบ', documentMode = false }: AppShellProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { session } = useSession()

  const logout = () => {
    queryClient.clear()
    clearStoredSession()
    navigate('/login', { replace: true })
  }

  const displayName = session?.user.displayName ?? 'ผู้ใช้งาน'

  return (
    <div className={`app-shell ${documentMode ? 'app-shell--document' : ''}`}>
      <aside className="sidebar print-hidden">
        <div className="brand">
          <img className="brand__logo" src="/garagepro-logo.png" alt="" aria-hidden="true" />
          <span className="brand__copy">
            <strong>GaragePro</strong>
            <small>Auto Services</small>
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
            <span className="topbar__eyebrow">Auto Services</span>
            <h1>{title}</h1>
          </div>
          <div className="topbar__actions">
            <label className="global-search">
              <Search aria-hidden="true" />
              <span className="sr-only">ค้นหาทั่วทั้งระบบ</span>
              <Input type="search" placeholder="ค้นหางาน ลูกค้า ทะเบียน" />
            </label>
            <div className="branch-button" aria-label="สาขาปัจจุบัน">
              <Building2 aria-hidden="true" />
              <span>
                <strong>{session?.branchName ?? 'ไม่พบข้อมูลสาขา'}</strong>
              </span>
            </div>
            <DropdownMenu>
              <DropdownMenuTrigger className="user-menu" aria-label="เปิดเมนูผู้ใช้งาน">
                <Avatar>
                  <AvatarFallback>{getInitials(displayName)}</AvatarFallback>
                </Avatar>
                <span>
                  <strong>{displayName}</strong>
                  <small>{session?.user.roleLabelTh ?? 'ไม่ระบุบทบาท'}</small>
                </span>
                <ChevronDown aria-hidden="true" />
              </DropdownMenuTrigger>
              <DropdownMenuContent className="user-dropdown">
                <DropdownMenuLabel>
                  <strong>{displayName}</strong>
                  <small>{session?.branchName}</small>
                </DropdownMenuLabel>
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={logout}>
                  <LogOut aria-hidden="true" /> ออกจากระบบ
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </header>
        <main className="app-main">{children}</main>
      </div>

    </div>
  )
}

function getInitials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((word) => word[0]).join('') || 'ผช'
}
