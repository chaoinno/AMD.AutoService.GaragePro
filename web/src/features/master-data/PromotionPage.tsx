import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Plus } from 'lucide-react'
import { useState } from 'react'
import { promotions, savePromotion, setPromotionStatus, type Promotion } from '../../api/promotions'
import { AppShell } from '../../components/AppShell'
import { ManagementTable } from '../../components/ManagementTable'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { useSession } from '../../lib/session'

export function PromotionPage() {
  const client = useQueryClient(); const { session } = useSession(); const manager = Boolean(session?.user.canSeeCost); const query = useQuery({ queryKey: ['promotions'], queryFn: () => promotions() }); const [name, setName] = useState(''); const [code, setCode] = useState(''); const [value, setValue] = useState('')
  const save = useMutation({ mutationFn: () => savePromotion(null, { code, name, kind: 'percent', value: Number(value), maxAmount: null, scope: 'bill', minSubtotal: null, startsAt: null, endsAt: null }), onSuccess: () => { setName(''); setCode(''); setValue(''); void client.invalidateQueries({ queryKey: ['promotions'] }) } })
  const status = useMutation({ mutationFn: (p: Promotion) => setPromotionStatus(p.id, !p.isActive), onSuccess: () => void client.invalidateQueries({ queryKey: ['promotions'] }) })
  return <AppShell title="โปรโมชัน"><section className="management-heading"><div><span className="page-eyebrow">MASTER DATA</span><h2>โปรโมชัน</h2><p>กำหนดส่วนลดระดับรายการหรือท้ายบิลตามช่วงเวลา</p></div><Button disabled={!manager} title={!manager ? 'เฉพาะผู้จัดการเท่านั้นที่แก้ไขข้อมูลหลักได้' : undefined}><Plus /> เพิ่มโปรโมชัน</Button></section><Card className="management-form"><div className="form-grid"><Input placeholder="รหัสโปรโมชัน" value={code} onChange={e => setCode(e.target.value)} disabled={!manager} /><Input placeholder="ชื่อโปรโมชัน" value={name} onChange={e => setName(e.target.value)} disabled={!manager} /><Input type="number" placeholder="ส่วนลด %" value={value} onChange={e => setValue(e.target.value)} disabled={!manager} /><Button disabled={!manager || save.isPending || !code || !name || !value} onClick={() => save.mutate()}>บันทึก</Button></div></Card><Card className="management-table-card"><ManagementTable data={query.data ?? []} columns={[{ id: 'code', header: 'รหัส', value: x => x.code, render: x => <strong>{x.code}</strong> }, { id: 'name', header: 'ชื่อ', value: x => x.name, render: x => x.name }, { id: 'value', header: 'ส่วนลด', value: x => x.value, render: x => `${x.value}${x.kind === 'percent' ? '%' : ' บาท'}` }, { id: 'scope', header: 'ขอบเขต', value: x => x.scope, render: x => x.scope === 'bill' ? 'ท้ายบิล' : 'รายบรรทัด' }, { id: 'status', header: 'สถานะ', value: x => x.isActive, render: x => <Button variant="outline" size="sm" disabled={!manager} title={!manager ? 'เฉพาะผู้จัดการเท่านั้นที่แก้ไขได้' : undefined} onClick={() => status.mutate(x)}>{x.isActive ? 'ใช้งานอยู่' : 'ปิดใช้งาน'}</Button> }]} /></Card></AppShell>
}