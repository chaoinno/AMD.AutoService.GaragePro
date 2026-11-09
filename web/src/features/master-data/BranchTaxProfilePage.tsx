import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Building2 } from 'lucide-react'
import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { getBranchTaxProfile, updateBranchTaxProfile } from '../../api/creditNotes'
import { AppShell } from '../../components/AppShell'
import { Button } from '../../components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card'
import { Input } from '../../components/ui/input'
import { Select } from '../../components/ui/select'
import { formatDateTime } from '../../lib/format'
import { branchLabel } from '../jobs/billing/BillingDocument'
import { Field, InlineError, PermissionNote, QueryState } from './MasterDataCommon'

/// สถานประกอบการของสาขาสำหรับเอกสารภาษี (docs/20 Q6) — Garage DB เดิมไม่มีค่านี้
/// อ่านได้ทุกบทบาท · แก้ได้เฉพาะผู้จัดการ (บังคับที่ API) · มีผลกับใบกำกับ/ใบลดหนี้ที่ออกหลังจากนี้ ใบเดิมคงค่าเดิม
export function BranchTaxProfilePage() {
  const queryClient = useQueryClient()
  const query = useQuery({ queryKey: ['branch-tax-profile'], queryFn: getBranchTaxProfile })
  const profile = query.data
  const [kind, setKind] = useState<'head' | 'branch'>('head')
  const [branchNo, setBranchNo] = useState('')

  useEffect(() => {
    if (!profile) return
    setKind(profile.sellerBranchNo === '00000' ? 'head' : 'branch')
    setBranchNo(profile.sellerBranchNo === '00000' ? '' : profile.sellerBranchNo)
  }, [profile])

  const value = kind === 'head' ? '00000' : branchNo.trim()
  const error = kind === 'branch' && !/^\d{5}$/.test(value)
    ? 'เลขที่สาขาต้องเป็นตัวเลข 5 หลัก (เช่น 00001)'
    : kind === 'branch' && value === '00000'
      ? '00000 คือสำนักงานใหญ่ ให้เลือก "สำนักงานใหญ่" แทน'
      : null
  const unchanged = profile ? profile.sellerBranchNo === value && !profile.isDefault : false

  const mutation = useMutation({
    mutationFn: () => updateBranchTaxProfile(value),
    onSuccess: (saved) => {
      toast.success(`บันทึกแล้ว: ${branchLabel(saved.sellerBranchNo)}`)
      queryClient.setQueryData(['branch-tax-profile'], saved)
    },
  })

  return (
    <AppShell title="ตั้งค่าภาษีสาขา">
      <section className="page-heading">
        <div>
          <p className="eyebrow">ข้อมูลหลัก / เอกสารภาษี</p>
          <h2>ตั้งค่าภาษีของสาขา</h2>
          <p>สถานประกอบการ (สำนักงานใหญ่/สาขาที่) ที่พิมพ์บนใบกำกับภาษีและใบลดหนี้ของสาขานี้</p>
        </div>
      </section>
      <PermissionNote canManage={Boolean(profile?.canEdit)} />
      <QueryState query={query} loadingTitle="กำลังโหลดค่าตั้งภาษี" emptyTitle="ไม่พบค่าตั้ง" emptyReason="ลองโหลดใหม่" onRetry={() => void query.refetch()}>
        {profile ? (
          <Card>
            <CardHeader><CardTitle><Building2 aria-hidden="true" style={{ width: 18, verticalAlign: -3, marginRight: 6 }} />สถานประกอบการของผู้ขาย</CardTitle></CardHeader>
            <CardContent>
              <p className="form-message">
                ปัจจุบัน: <strong>{branchLabel(profile.sellerBranchNo)}</strong>
                {profile.isDefault
                  ? ' (ค่าเริ่มต้น ยังไม่เคยตั้ง)'
                  : profile.updatedAt ? ` · แก้ล่าสุดโดย ${profile.updatedByName ?? '-'} เมื่อ ${formatDateTime(profile.updatedAt)}` : ''}
              </p>
              <section className="form-grid">
                <Field label="ประเภทสถานประกอบการ">
                  <Select value={kind} disabled={!profile.canEdit} onChange={(e) => setKind(e.target.value as 'head' | 'branch')}>
                    <option value="head">สำนักงานใหญ่ (00000)</option>
                    <option value="branch">สาขา</option>
                  </Select>
                </Field>
                {kind === 'branch' ? (
                  <Field label="เลขที่สาขา (5 หลัก ตาม ภ.พ.20)" error={error ?? undefined}>
                    <Input inputMode="numeric" maxLength={5} disabled={!profile.canEdit} value={branchNo} onChange={(e) => setBranchNo(e.target.value)} />
                  </Field>
                ) : null}
              </section>
              <p className="form-message">มีผลกับใบกำกับภาษีและใบลดหนี้ที่ออกหลังบันทึกเท่านั้น เอกสารที่ออกไปแล้วคงค่าเดิม</p>
              {mutation.isError ? <InlineError error={mutation.error} /> : null}
              <div className="heading-actions" style={{ marginTop: 12 }}>
                <Button
                  disabled={!profile.canEdit || Boolean(error) || unchanged || mutation.isPending}
                  title={!profile.canEdit ? 'เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ได้' : error ?? (unchanged ? 'ค่านี้บันทึกไว้แล้ว' : undefined)}
                  onClick={() => mutation.mutate()}
                >
                  {mutation.isPending ? 'กำลังบันทึก…' : 'บันทึก'}
                </Button>
                {!profile.canEdit ? <span className="section-help">เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ได้</span>
                  : error ? <span className="section-help">{error}</span>
                    : unchanged ? <span className="section-help">ค่านี้บันทึกไว้แล้ว</span> : null}
              </div>
            </CardContent>
          </Card>
        ) : null}
      </QueryState>
    </AppShell>
  )
}
