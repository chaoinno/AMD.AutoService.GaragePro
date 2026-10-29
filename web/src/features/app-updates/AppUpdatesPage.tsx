import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { toast } from 'sonner'
import { compareVersions, getAppUpdateHistory, getAppUpdatePolicy, saveAppUpdatePolicy, type AppUpdatePolicy, type Platform } from '../../api/appUpdates'
import { AppShell } from '../../components/AppShell'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Button } from '../../components/ui/button'
import { Card } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { useSession } from '../../lib/session'
import { Field, InlineError } from '../master-data/MasterDataCommon'
import './app-updates.css'

export function AppUpdatesPage() {
  const { session } = useSession()
  const [platform, setPlatform] = useState<Platform>('android')
  const canManage = Boolean(session?.user.isAdministrator)
  return <AppShell title="เวอร์ชันแอป">
    <section className="page-heading"><div><p className="eyebrow">การตั้งค่าส่วนกลาง</p><h2>เวอร์ชันแอป</h2><p>นโยบายนี้มีผลกับทุกอู่ ผู้ดูแลระบบทุกบัญชีสามารถแก้ไขได้</p></div></section>
    {!canManage ? <Card className="app-update-card"><p>เฉพาะผู้ดูแลระบบเท่านั้นที่จัดการเวอร์ชันแอปได้</p></Card> : <>
      <div className="app-update-tabs" aria-label="ระบบปฏิบัติการ">{(['android', 'ios'] as const).map(p => <Button key={p} variant={platform === p ? 'default' : 'outline'} aria-pressed={platform === p} onClick={() => setPlatform(p)}>{p === 'ios' ? 'iOS' : 'Android'}</Button>)}</div>
      <PolicyPanel key={platform} platform={platform} />
    </>}
  </AppShell>
}
function PolicyPanel({ platform }: { platform: Platform }) {
  const cache = useQueryClient()
  const policy = useQuery({ queryKey: ['app-updates', platform], queryFn: () => getAppUpdatePolicy(platform) })
  const history = useQuery({ queryKey: ['app-updates', platform, 'history'], queryFn: () => getAppUpdateHistory(platform) })
  const [draft, setDraft] = useState<AppUpdatePolicy | null>(null)
  const [confirm, setConfirm] = useState(false)
  const [confirmedStore, setConfirmedStore] = useState(false)
  const [validation, setValidation] = useState('')
  const current = draft ?? policy.data
  const save = useMutation({ mutationFn: (value: { policy: AppUpdatePolicy; confirmed: boolean }) => saveAppUpdatePolicy(value.policy, value.confirmed),
    onSuccess: (value) => { cache.setQueryData(['app-updates', platform], value); setDraft(null); setConfirm(false); setConfirmedStore(false); toast.success('บันทึกนโยบายสำหรับทุกอู่แล้ว'); void cache.invalidateQueries({ queryKey: ['app-updates', platform, 'history'] }) } })
  const patch = (change: Partial<AppUpdatePolicy>) => { if (current) setDraft({ ...current, ...change }); setValidation(''); save.reset() }
  const needsConfirmation = Boolean(current?.enabled && compareVersions(current.minimumVersion, '0.0.0')! > 0 && (!policy.data?.enabled || compareVersions(current.minimumVersion, policy.data.minimumVersion)! > 0))
  const previewRequired = current && compareVersions(current.minimumVersion, '0.0.0')! > 0
  const submit = () => {
    if (!current) return
    const order = compareVersions(current.minimumVersion, current.latestVersion)
    if (order === null || order > 0 || !current.message.trim() || current.message.length > 1000) { setValidation('ระบุเวอร์ชันแบบ 1.2.3 ขั้นต่ำต้องไม่สูงกว่าล่าสุด และข้อความต้องมี 1–1,000 ตัวอักษร'); return }
    if (needsConfirmation) { setConfirmedStore(false); setConfirm(true) } else save.mutate({ policy: current, confirmed: false })
  }
  if (!current) return <Card className="app-update-card">{policy.isError ? <><InlineError error={policy.error} /><Button onClick={() => void policy.refetch()}>ลองใหม่</Button></> : <p role="status">กำลังโหลดนโยบาย…</p>}</Card>
  return <>
    <div className="app-update-grid">
      <Card className="app-update-card"><h3>นโยบาย {platform === 'ios' ? 'iOS' : 'Android'}</h3>
        <p>revision {current.revision} · ตั้งขั้นต่ำเป็น 0.0.0 เพื่อแจ้งเตือนอย่างเดียว</p>
        <fieldset disabled={save.isPending} className="app-update-fields">
          <label className="filter-check"><input type="checkbox" checked={current.enabled} onChange={e => patch({ enabled: e.target.checked })} /> เปิดแจ้งอัปเดต</label>
          <Field label="เวอร์ชันล่าสุด"><Input value={current.latestVersion} placeholder="1.4.0" onChange={e => patch({ latestVersion: e.target.value })} /></Field>
          <Field label="เวอร์ชันขั้นต่ำที่ใช้ได้"><Input value={current.minimumVersion} placeholder="0.0.0" onChange={e => patch({ minimumVersion: e.target.value })} /></Field>
          <Field label="ข้อความถึงผู้ใช้"><textarea className="app-update-message" maxLength={1000} rows={4} value={current.message} onChange={e => patch({ message: e.target.value })} /></Field>
          <a href={current.storeUrl} target="_blank" rel="noreferrer">เปิดหน้าดาวน์โหลดใน Store</a>
          {validation && <p role="alert">{validation}</p>}{save.isError && <InlineError error={save.error} />}
          <div className="app-update-actions"><Button onClick={submit}>{save.isPending ? 'กำลังบันทึก…' : 'บันทึกสำหรับทุกอู่'}</Button><Button variant="outline" onClick={() => { setDraft(null); save.reset(); void policy.refetch() }}>โหลดข้อมูลล่าสุด</Button>
            <Button variant="destructive" disabled={!policy.data?.enabled} onClick={() => policy.data && save.mutate({ policy: { ...policy.data, enabled: false }, confirmed: false })}>ปิดนโยบาย</Button></div>
        </fieldset>
      </Card>
      <Card className="app-update-card">
        <p className="eyebrow">{previewRequired ? 'ตัวอย่างสำหรับเครื่องที่ต่ำกว่าเวอร์ชันขั้นต่ำ' : 'ตัวอย่างข้อความ'}</p>
        <h3>{previewRequired ? 'กรุณาอัปเดตเพื่อใช้งานต่อ' : `มีเวอร์ชันใหม่ ${current.latestVersion}`}</h3>
        <p className="app-update-preview">{current.message}</p>
        <p>{previewRequired ? `รุ่นต่ำกว่า ${current.minimumVersion} ต้องอัปเดตก่อนใช้งาน` : 'แจ้งเตือนเมื่อมีเวอร์ชันใหม่ โดยยังใช้งานต่อได้'}</p>
        <div className="app-update-actions"><Button disabled>อัปเดต</Button>{previewRequired ? <Button variant="outline" disabled>ตรวจอีกครั้ง</Button> : <Button variant="outline" disabled>ไว้ภายหลัง</Button>}</div>
        <p>“ไว้ภายหลัง” แสดงเฉพาะรุ่นที่ยังถึงขั้นต่ำ และเลื่อนได้ 24 ชั่วโมง</p>
      </Card>
    </div>
    <Card className="app-update-card"><h3>ประวัติการเปลี่ยนแปลงล่าสุด</h3>{history.isError ? <InlineError error={history.error} /> : history.isPending ? <p>กำลังโหลด…</p> : !history.data?.length ? <p>ยังไม่มีการเปลี่ยนแปลง</p> : history.data.map(item => <details className="app-update-history" key={item.id}><summary>ครั้งที่ {item.revision} · {item.editedByName} · {new Date(item.editedAt).toLocaleString('th-TH')}</summary><p>บัญชี {item.editedByShard}/{item.editedByUserId}</p><p>สถานะ: {item.before.enabled ? 'เปิด' : 'ปิด'} → {item.after.enabled ? 'เปิด' : 'ปิด'}</p><p>ล่าสุด: {item.before.latestVersion} → {item.after.latestVersion} · ขั้นต่ำ: {item.before.minimumVersion} → {item.after.minimumVersion}</p><p>ข้อความเดิม: {item.before.message}</p><p>ข้อความใหม่: {item.after.message}</p></details>)}</Card>
    <ConfirmModal open={confirm} title="บังคับอัปเดตสำหรับทุกอู่" description={`ผู้ใช้ ${platform === 'ios' ? 'iOS' : 'Android'} ที่มีรุ่นต่ำกว่า ${current.minimumVersion} จะใช้แอปต่อไม่ได้จนกว่าจะอัปเดต`} onClose={() => { if (!save.isPending) setConfirm(false) }} footer={<><Button variant="ghost" disabled={save.isPending} onClick={() => setConfirm(false)}>ยกเลิก</Button><Button disabled={!confirmedStore || save.isPending} onClick={() => save.mutate({ policy: current, confirmed: true })}>{save.isPending ? 'กำลังบันทึก…' : 'ยืนยันสำหรับทุกอู่'}</Button></>}>
      <label className="filter-check"><input type="checkbox" checked={confirmedStore} onChange={e => setConfirmedStore(e.target.checked)} /> ตรวจแล้วว่าเวอร์ชันขั้นต่ำดาวน์โหลดจาก Store ได้จริง</label>{save.isError && <InlineError error={save.error} />}
    </ConfirmModal>
  </>
}
