import { useQuery, useQueryClient } from '@tanstack/react-query'
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
  UserCog,
  Truck,
  Warehouse,
  FolderTree,
  ClipboardList,
  ShoppingCart,
  Boxes,
} from 'lucide-react'
import { type ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router'
import { countOpenJobs } from '../api/jobs'
import { purchases, type PurchaseKind } from '../api/purchasing'
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

const navGroups = [
  {
    label: 'Workplace',
    items: [
      { to: '/jobs', icon: Wrench, label: 'จ๊อบ', jobsInShop: true as const },
      { to: '/purchasing/pr', icon: ClipboardList, label: 'ใบขอซื้อ (PR)', purchaseKind: 'PR' as PurchaseKind },
      { to: '/purchasing/po', icon: ShoppingCart, label: 'ใบสั่งซื้อ (PO)', purchaseKind: 'PO' as PurchaseKind },
      { to: '/inventory', icon: Boxes, label: 'สต็อก FIFO' },
    ],
  },
  {
    label: 'Reports',
    items: [{ to: '/reports', icon: BarChart3, label: 'รายงาน' }],
  },
  {
    label: 'Master Data',
    items: [
      { to: '/customers', icon: Users, label: 'ลูกค้า' },
      { to: '/vehicles', icon: CarFront, label: 'รถลูกค้า' },
      { to: '/staffs', icon: UserCog, label: 'พนักงาน' },
      { to: '/products', icon: Package, label: 'สินค้า' },
      { to: '/suppliers', icon: Truck, label: 'ซัพพลายเออร์' },
      { to: '/warehouses', icon: Warehouse, label: 'คลัง' },
      { to: '/catalog-categories', icon: FolderTree, label: 'หมวดหมู่สินค้า' },
    ],
  },
]

export function AppShell({ children, title = 'จ๊อบ', documentMode = false }: AppShellProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { session } = useSession()
  const canUsePurchasing = ['manager', 'office'].includes(session?.user.role.toLowerCase() || '')
  const prCount = useQuery({
    queryKey: ['purchase-count', 'PR'],
    queryFn: () => purchases('PR', '', '', 1, 100),
    enabled: canUsePurchasing,
    staleTime: 30_000,
  })
  const poCount = useQuery({
    queryKey: ['purchase-count', 'PO'],
    queryFn: () => purchases('PO', '', '', 1, 100),
    enabled: canUsePurchasing,
    staleTime: 30_000,
  })
  const jobsInShopCount = useQuery({
    queryKey: ['jobs-count-open', 9],
    queryFn: () => countOpenJobs(9),
    staleTime: 30_000,
  })

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
          {navGroups.map((group) => (
            <div className="sidebar__group" key={group.label}>
              <span className="sidebar__group-label">{group.label}</span>
              {group.items.map((item) => {
                const Icon = item.icon
                const count = item.purchaseKind === 'PR'
                  ? prCount.data?.totalItems
                  : item.purchaseKind === 'PO' ? poCount.data?.totalItems
                  : 'jobsInShop' in item && item.jobsInShop ? jobsInShopCount.data
                  : undefined
                const countTitle = 'jobsInShop' in item && item.jobsInShop
                  ? `จ๊อบรถในอู่ที่ยังไม่ปิดงาน ${count} รายการ`
                  : `เอกสารทั้งหมด ${count} รายการ`
                return (
                  <NavLink
                    key={item.to}
                    to={item.to}
                    className={({ isActive }) => `sidebar__link ${isActive ? 'sidebar__link--active' : ''}`}
                  >
                    <Icon className="sidebar__icon" aria-hidden="true" />
                    <span className="sidebar__label">{item.label}</span>
                    {count !== undefined ? (
                      <span className="sidebar__count" title={countTitle} aria-label={`${count} รายการ`}>
                        {count > 999 ? '999+' : count}
                      </span>
                    ) : null}
                  </NavLink>
                )
              })}
            </div>
          ))}
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
