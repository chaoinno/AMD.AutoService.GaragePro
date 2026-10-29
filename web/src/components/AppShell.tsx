import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  AlarmClock,
  Boxes,
  ChevronRight,
  FileText,
  LayoutDashboard,
  PackageSearch,
  CarFront,
  ChevronDown,
  ChevronsLeft,
  ChevronsRight,
  Info,
  ClipboardList,
  Clock3,
  FileStack,
  FolderTree,
  History,
  Lock,
  LogOut,
  Package,
  Search,
  ShoppingCart,
  Timer,
  TrendingUp,
  Truck,
  UserCog,
  UserRound,
  Users,
  Wallet,
  Warehouse,
  Wrench,
} from 'lucide-react'
import { type ReactNode, useEffect, useState } from 'react'
import { NavLink, useLocation, useNavigate } from 'react-router'
import { countOpenJobs } from '../api/jobs'
import { getServiceDueCount } from '../api/reports'
import { NotificationBell } from './notifications/NotificationBell'
import { BranchSwitcher } from './BranchSwitcher'
import { countLowStock, countOpenPurchaseOrders, purchases, type PurchaseKind } from '../api/purchasing'
import { countDraftSales } from '../api/sales'
import { canUseRetailSale } from '../features/sales/saleFormat'
import { TrialBanner } from '../features/plan/TrialBanner'
import { TrialDialogProvider } from '../features/plan/TrialDialog'
import { lockedNavLabel } from '../features/plan/trialUx'
import { usePlan } from '../features/plan/usePlan'
import { isFeatureLocked, lockedFeatureForPath } from '../lib/plan'
import { clearStoredSession, useSession } from '../lib/session'
import { BRAND_NAME, BrandWordmark } from './Brand'
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

// ฟิลด์ตัวเลขท้ายเมนู (นับเอกสาร/จ๊อบ/บิลร่าง/รถใกล้ครบรอบ) เป็น optional ทุกตัว — ต้องระบุชนิดตรงๆ ไม่งั้น TS อนุมานเป็น union
// ที่แต่ละกลุ่มมีฟิลด์ไม่เหมือนกัน แล้ว item.purchaseKind พังเมื่อกลุ่มหนึ่งไม่มีฟิลด์นั้นเลย
type NavItem = {
  to: string
  icon: typeof Wrench
  label: string
  /** ชื่อเต็มสำหรับ tooltip/screen reader เมื่อ label ย่อเพราะอยู่ใต้หัวข้อย่อยอยู่แล้ว (เช่น "รายใบเบิก" ใต้ "การเบิกอะไหล่") */
  fullLabel?: string
  /** active เฉพาะ path ตรงตัว — ใช้เมื่อเมนูอื่นเป็น path ลูกของเมนูนี้ */
  end?: true
  jobsInShop?: true
  purchaseKind?: PurchaseKind
  saleDrafts?: boolean
  serviceDue?: true
}

/** หัวข้อย่อยในกลุ่มเมนู — พับ/กางได้ จำสถานะต่อเครื่อง · ตอนย่อ sidebar เป็นแถบไอคอนหัวข้อย่อยซ่อนและแสดงทุกเมนู */
type NavSection = { key: string; label: string; items: NavItem[] }

const navGroups: { label: string; items?: NavItem[]; sections?: NavSection[] }[] = [
  {
    label: 'Workplace',
    items: [
      { to: '/jobs', icon: Wrench, label: 'จ๊อบ', jobsInShop: true as const },
      { to: '/purchasing/pr', icon: ClipboardList, label: 'ใบขอซื้อ (PR)', purchaseKind: 'PR' as PurchaseKind },
      { to: '/purchasing/po', icon: ShoppingCart, label: 'ใบสั่งซื้อ (PO)', purchaseKind: 'PO' as PurchaseKind },
      { to: '/inventory', icon: Boxes, label: 'สต็อก FIFO' },
      { to: '/sales', icon: ShoppingCart, label: 'ขายสินค้า (POS)', saleDrafts: true },
    ],
  },
  {
    label: 'Reports',
    items: [
      { to: '/reports/dashboard', icon: LayoutDashboard, label: 'แดชบอร์ดวันนี้' },
    ],
    sections: [
      {
        key: 'sales',
        label: 'ยอดขาย',
        items: [
          { to: '/reports/sales-margin', icon: TrendingUp, label: 'ยอดขาย-ต้นทุน-กำไร' },
          { to: '/reports/retail-sales', icon: ShoppingCart, label: 'ขายหน้าร้าน' },
        ],
      },
      {
        key: 'stock',
        label: 'สต็อก & การเบิกอะไหล่',
        items: [
          { to: '/reports/stock', icon: Boxes, label: 'มูลค่า/อายุสต็อก', fullLabel: 'รายงานสต็อกสินค้า (มูลค่าและอายุสต็อก)' },
          { to: '/reports/stock-usage', end: true, icon: FileText, label: 'เบิกอะไหล่ · รายใบเบิก', fullLabel: 'การเบิกอะไหล่ รายใบเบิก' },
          { to: '/reports/stock-usage/items', icon: PackageSearch, label: 'เบิกอะไหล่ · ตามสินค้า', fullLabel: 'การเบิกอะไหล่ สรุปตามสินค้า' },
          { to: '/reports/stock-usage/requesters', icon: UserRound, label: 'เบิกอะไหล่ · ตามช่าง', fullLabel: 'การเบิกอะไหล่ สรุปตามช่างผู้เบิก' },
        ],
      },
      {
        key: 'service',
        label: 'งานซ่อม & ช่าง',
        items: [
          { to: '/reports/cycle-time', icon: Clock3, label: 'รอบเวลาทำงาน (SLA)' },
          // ยังไม่ใช่รายงานประเมินประสิทธิภาพ — เป็นข้อมูลดิบให้หัวหน้าช่าง/ผู้จัดการตรวจและแก้คาบที่ผิด
          // ระหว่างช่วงเก็บข้อมูล (บทบาทอื่นกดแล้วได้ 403 พร้อมเหตุผลจาก QueryState ตามคอนเวนชันของเมนู)
          { to: '/reports/work-intervals', icon: Timer, label: 'เวลาทำงานของช่าง' },
        ],
      },
      {
        key: 'vehicles',
        label: 'รถ & ลูกค้า',
        items: [
          // เปิดทุกบทบาท (หน้าร้าน/ช่างค้นตอนลูกค้ามาที่เคาน์เตอร์) — ช่าง/หัวหน้าช่างไม่เห็นยอดเงิน
          { to: '/reports/vehicle-history', icon: History, label: 'ประวัติรถ' },
          // ตัวเลขข้างเมนูเห็นเฉพาะบทบาทที่เปิดรายงานได้ (ผู้จัดการ/ธุรการ/หน้าร้าน)
          { to: '/reports/service-due', icon: AlarmClock, label: 'รถใกล้ครบรอบบริการ', serviceDue: true as const },
        ],
      },
    ],
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
      { to: '/quotation-templates', icon: FileStack, label: 'เทมเพลตใบเสนอราคา' },
      { to: '/service-reminder-templates', icon: AlarmClock, label: 'รายการนัดบริการ' },
      { to: '/promotions', icon: TrendingUp, label: 'โปรโมชัน' },
    ],
  },
]

const SECTIONS_CLOSED_KEY = 'garagepro.sidebar.sections.closed'

function readClosedSections(): Set<string> {
  try {
    const raw = JSON.parse(window.localStorage.getItem(SECTIONS_CLOSED_KEY) ?? '[]')
    return new Set(Array.isArray(raw) ? raw.filter((x): x is string => typeof x === 'string') : [])
  } catch {
    return new Set()
  }
}

const pathActive = (item: NavItem, pathname: string) =>
  pathname === item.to || (!item.end && pathname.startsWith(`${item.to}/`))

const SIDEBAR_COLLAPSED_KEY = 'garagepro.sidebar.collapsed'

export function AppShell({ children, title = 'จ๊อบ', documentMode = false }: AppShellProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { session } = useSession()
  const [sidebarCollapsed, setSidebarCollapsed] = useState(
    () => window.localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === '1',
  )
  useEffect(() => {
    document.title = `${title} | ServicePro`
  }, [title])
  const { pathname } = useLocation()
  const [closedSections, setClosedSections] = useState(readClosedSections)
  const toggleSection = (key: string) => setClosedSections((old) => {
    const next = new Set(old)
    if (next.has(key)) next.delete(key); else next.add(key)
    try { window.localStorage.setItem(SECTIONS_CLOSED_KEY, JSON.stringify([...next])) } catch { /* จำไม่ได้ก็แค่กางทุกครั้ง */ }
    return next
  })
  // เปิดหน้าที่อยู่ในหัวข้อที่พับไว้ (ลิงก์ตรง/แจ้งเตือน/ปุ่มในหน้า) → กางหัวข้อนั้นให้ ไม่งั้นไม่รู้ว่าอยู่เมนูไหน
  // ผู้ใช้ยังพับหัวข้อของหน้าปัจจุบันเองได้ — จะกางกลับเมื่อเปลี่ยนหน้าครั้งถัดไปที่อยู่ในหัวข้อนั้นเท่านั้น
  useEffect(() => {
    const key = navGroups.flatMap((g) => g.sections ?? [])
      .find((section) => section.items.some((item) => pathActive(item, pathname)))?.key
    if (key) setClosedSections((old) => {
      if (!old.has(key)) return old
      const next = new Set(old)
      next.delete(key)
      return next
    })
  }, [pathname])
  const toggleSidebar = () => {
    const next = !sidebarCollapsed
    setSidebarCollapsed(next)
    window.localStorage.setItem(SIDEBAR_COLLAPSED_KEY, next ? '1' : '0')
  }
  // แพ็กเกจของอู่ (อ่านสดจาก /auth/me) — ลูกค้าเดิมไม่มี plan: ไม่มีกุญแจ/แถบ/ลายน้ำ พฤติกรรมเดิมทุกอย่าง
  const { plan } = usePlan()
  // ฟีเจอร์ที่ล็อกไม่ต้องนับเอกสาร — ไม่งั้นทุกหน้าจะยิงคำขอที่รู้อยู่แล้วว่าได้ 403 PLAN_FEATURE_LOCKED
  const canUsePurchasing = ['manager', 'office'].includes(session?.user.role.toLowerCase() || '')
    && !isFeatureLocked(plan, 'purchasing')
  const prCount = useQuery({
    queryKey: ['purchase-count', 'PR'],
    queryFn: () => purchases('PR', '', '', 1, 100),
    enabled: canUsePurchasing,
    staleTime: 30_000,
  })
  // PO's badge tracks documents that still need attention (not yet fully received/closed),
  // unlike PR's which counts every document — see countOpenPurchaseOrders on the backend.
  const poCount = useQuery({
    queryKey: ['purchase-count', 'PO', 'open'],
    queryFn: () => countOpenPurchaseOrders(),
    enabled: canUsePurchasing,
    staleTime: 30_000,
  })
  // อะไหล่ที่ถึงจุดต่ำสุด (min-max) — key ขึ้นต้น 'purchase-count' ให้ usePurchasingRefresh ครอบคลุม
  // (รับของ/เบิก/ตั้งยอดยกมา) ส่วนขายหน้าร้านกับใบเบิกผูกจ๊อบ invalidate key นี้เองตรงๆ
  const canUseInventory = canUsePurchasing && !isFeatureLocked(plan, 'inventory')
  const lowStockCount = useQuery({
    queryKey: ['purchase-count', 'low-stock'],
    queryFn: () => countLowStock(),
    enabled: canUseInventory,
    staleTime: 30_000,
  })
  const jobsInShopCount = useQuery({
    queryKey: ['jobs-count-open', 9],
    queryFn: () => countOpenJobs(9),
    staleTime: 30_000,
  })

  // บิลขายหน้าร้านที่ร่างค้างไว้ — key เดียวกับที่หน้า /sales invalidate หลังสร้าง/ยกเลิก/ชำระเงิน
  const saleDraftCount = useQuery({
    queryKey: ['sale-draft-count'],
    queryFn: countDraftSales,
    enabled: canUseRetailSale(session?.user.role) && !isFeatureLocked(plan, 'pos'),
    staleTime: 30_000,
  })

  // รถใกล้ครบรอบบริการ — เฉพาะบทบาทที่เปิดรายงานได้ ไม่งั้นช่าง/แคชเชียร์จะยิงคำขอที่ได้ 403 ทุกหน้า
  const canSeeServiceDue = ['manager', 'office', 'frontdesk'].includes(session?.user.role.toLowerCase() || '')
  const serviceDueCount = useQuery({
    queryKey: ['service-due-count'],
    queryFn: getServiceDueCount,
    enabled: canSeeServiceDue,
    staleTime: 60_000,
  })

  const logout = () => {
    queryClient.clear()
    clearStoredSession()
    navigate('/login', { replace: true })
  }

  const displayName = session?.user.displayName ?? 'ผู้ใช้งาน'

  const renderLink = (item: NavItem) => {
    const Icon = item.icon
    // path ของฟีเจอร์มาจาก lib/plan.ts ที่เดียว (ชุดเดียวกับ FeatureGate ใน AppRoutes)
    const lockedFeature = lockedFeatureForPath(plan, item.to)
    const count = lockedFeature ? undefined : item.purchaseKind === 'PR'
      ? prCount.data?.totalItems
      : item.purchaseKind === 'PO' ? poCount.data
      : item.jobsInShop ? jobsInShopCount.data
      : item.saleDrafts ? saleDraftCount.data || undefined
      : item.to === '/inventory' ? lowStockCount.data || undefined
      : item.serviceDue ? serviceDueCount.data?.count || undefined
      : undefined
    const countTitle = item.jobsInShop
      ? `จ๊อบรถในอู่ที่ยังไม่ปิดงาน ${count} รายการ`
      : item.purchaseKind === 'PO'
      ? `ใบสั่งซื้อที่ยังไม่รับครบ ${count} รายการ`
      : item.saleDrafts
      ? `บิลขายร่างที่ยังไม่ชำระเงิน ${count} บิล`
      : item.to === '/inventory'
      ? `อะไหล่ที่ถึงจุดต่ำสุด ${count} รายการ`
      : item.serviceDue
      ? `รถที่ถึงรอบบริการแล้ว (เลยกำหนดไม่เกิน 30 วัน) หรือจะครบใน 7 วัน ${count} คัน`
      : `เอกสารทั้งหมด ${count} รายการ`
    const name = item.fullLabel ?? item.label
    const linkLabel = lockedFeature ? lockedNavLabel(name) : name
    return (
      <NavLink
        key={item.to}
        to={item.to}
        end={item.end}
        className={({ isActive }) => `sidebar__link ${isActive ? 'sidebar__link--active' : ''}${lockedFeature ? ' sidebar__link--locked' : ''}`}
        aria-label={linkLabel}
        title={sidebarCollapsed || lockedFeature || item.fullLabel ? linkLabel : undefined}
      >
        <Icon className="sidebar__icon" aria-hidden="true" />
        <span className="sidebar__label">{item.label}</span>
        {lockedFeature ? (
          <span className="sidebar__lock" aria-hidden="true">
            <Lock />
          </span>
        ) : count !== undefined ? (
          <span className={`sidebar__count${item.to === '/inventory' ?' sidebar__count--warn' : ''}`} title={countTitle} aria-label={countTitle}>
            {count > 999 ? '999+' : count}
          </span>
        ) : null}
      </NavLink>
    )
  }


  return (
    <TrialDialogProvider>
    <div className={`app-shell ${documentMode ? 'app-shell--document' : ''} ${sidebarCollapsed ? 'app-shell--sidebar-collapsed' : ''}`}>
      <aside className="sidebar print-hidden">
        <div className="brand">
          <img className="brand__logo" src="/servicepro-logo.png" alt="" aria-hidden="true" />
          <span className="brand__copy">
            <BrandWordmark />
          </span>
        </div>
        <nav className="sidebar__nav" aria-label="เมนูหลัก">
          {navGroups.map((group) => (
            <div className="sidebar__group" key={group.label}>
              <span className="sidebar__group-label">{group.label}</span>
              {group.items?.map(renderLink)}
              {group.sections?.map((section) => {
                const open = !closedSections.has(section.key)
                const listId = `sidebar-section-${section.key}`
                return (
                  <div className={`sidebar__section${open ? '' : ' sidebar__section--closed'}`} key={section.key}>
                    <button
                      type="button"
                      className="sidebar__section-toggle"
                      aria-expanded={open}
                      aria-controls={listId}
                      title={open ? `พับหัวข้อ ${section.label}` : `กางหัวข้อ ${section.label}`}
                      onClick={() => toggleSection(section.key)}
                    >
                      <ChevronRight className="sidebar__section-chevron" aria-hidden="true" />
                      <span>{section.label}</span>
                    </button>
                    <div className="sidebar__section-items" id={listId}>
                      {section.items.map(renderLink)}
                    </div>
                  </div>
                )
              })}
            </div>
          ))}
        </nav>
        <button
          type="button"
          className="sidebar__toggle"
          onClick={toggleSidebar}
          aria-label={sidebarCollapsed ? 'ขยายเมนู' : 'ย่อเมนู'}
          title={sidebarCollapsed ? 'ขยายเมนู' : 'ย่อเมนู'}
        >
          {sidebarCollapsed
            ? <ChevronsRight className="sidebar__icon" aria-hidden="true" />
            : <ChevronsLeft className="sidebar__icon" aria-hidden="true" />}
          <span className="sidebar__label">ย่อเมนู</span>
        </button>
        <div className="sidebar__footer" title={`${BRAND_NAME} เวอร์ชัน ${__APP_VERSION__}`} aria-label={`เวอร์ชัน ${__APP_VERSION__}`}>
          <Info className="sidebar__icon" aria-hidden="true" />
          <span className="sidebar__label">v{__APP_VERSION__}</span>
        </div>
      </aside>

      <div className="app-shell__body">
        <TrialBanner />
        <header className="topbar print-hidden">
          <div>
            <span className="topbar__eyebrow">{BRAND_NAME}</span>
            <h1>{title}</h1>
          </div>
          <div className="topbar__actions">
            <label className="global-search">
              <Search aria-hidden="true" />
              <span className="sr-only">ค้นหาทั่วทั้งระบบ</span>
              <Input type="search" placeholder="ค้นหางาน ลูกค้า ทะเบียน" />
            </label>
            <BranchSwitcher />
            <NotificationBell />
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
                {/* ทุกผู้ใช้ ServicePro (docs/17 §8.7 · D9): อู่ที่สมัครเอง (sp1) และสาขา GaragePro กลุ่ม Service (db2)
                    · login ของ ServicePro รับเฉพาะสาขากลุ่ม Service อยู่แล้ว · สาขาที่ server ไม่รองรับ หน้าแพ็กเกจบอกทางต่อเอง */}
                <DropdownMenuItem onClick={() => navigate('/package')}>
                  <Wallet aria-hidden="true" /> แพ็กเกจและการชำระเงิน
                </DropdownMenuItem>
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
    </TrialDialogProvider>
  )
}

function getInitials(name: string) {
  const words = name.trim().split(/\s+/).filter(Boolean)
  return words.slice(0, 2).map((word) => word[0]).join('') || 'ผช'
}
