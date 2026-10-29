import { QueryClientProvider, useQuery } from '@tanstack/react-query'
import { useEffect, useMemo, type ReactNode } from 'react'
import { createAppQueryClient } from './queryClient'
import { getMe } from '../api/auth'
import { updateSessionUser, useSession } from './session'

/**
 * heartbeat เดียวของทั้งแอปขณะล็อกอิน (docs/19) — /auth/me ทุก 60 วิเฉพาะแท็บที่มองอยู่ + ทันทีที่กลับมาที่แท็บ
 * ถูกเพิกถอน = 401 → client.ts ล้างเซสชันเอง · 503 AUTH_CHECK_UNAVAILABLE ไม่ใช่เหตุให้ออกจากระบบ
 * ตอบสำเร็จ = เติม plan/workspace/สถานะผู้ดูแลล่าสุดลงเซสชัน (เดิมอยู่ใน usePlan)
 */
export function SessionGuard({ children }: { children: ReactNode }) {
  const { session } = useSession()
  const token = session?.accessToken ?? null
  const me = useQuery({
    queryKey: ['me', session?.user.shardKey, session?.user.userId, session?.branchId, session?.expiresAt],
    queryFn: async () => ({ me: await getMe(), token }),
    enabled: Boolean(token),
    staleTime: 0,
    refetchInterval: 60_000,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: 'always',
    refetchOnReconnect: 'always',
    retry: false,
  })
  useEffect(() => {
    if (!me.data || me.data.token !== token || !token) return
    const user = me.data.me.user
    updateSessionUser({ plan: user?.plan ?? me.data.me.plan, workspace: user?.workspace ?? me.data.me.workspace,
      isAdministrator: user.isAdministrator }, token)
  }, [me.data, token])
  return children
}

/**
 * QueryClient + state ของหน้าจอแยกต่อการล็อกอิน (สร้างใหม่ทุกครั้งที่ token เปลี่ยน) — คำตอบที่มาช้าของบัญชีก่อนหน้า
 * ไม่มีทางไปเติม cache ของบัญชีถัดไป · Toaster จึงต้องอยู่นอก provider นี้ (ดู main.tsx)
 */
export function SessionQueryProvider({ children }: { children: ReactNode }) {
  const { session } = useSession()
  const token = session?.accessToken ?? null
  const client = useMemo(() => createAppQueryClient(), [token])
  useEffect(() => () => { void client.cancelQueries(); client.clear() }, [client])
  return <QueryClientProvider client={client}><SessionGuard key={token ?? 'anonymous'}>{children}</SessionGuard></QueryClientProvider>
}
