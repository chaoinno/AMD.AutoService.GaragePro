import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../../api/client'
import { getTaxInvoiceState, issueTaxInvoice } from '../../../api/pos'
import type { Job } from '../../../api/types'
import { ConfirmModal } from '../../../components/ConfirmModal'
import { SkeletonRows, StateBlock } from '../../../components/StateBlock'
import { Button } from '../../../components/ui/button'
import { Input } from '../../../components/ui/input'
import { Select } from '../../../components/ui/select'
import { Textarea } from '../../../components/ui/textarea'
import { Field, InlineError } from '../../master-data/MasterDataCommon'

type Props = { open: boolean; job: Job; onClose: () => void; onIssued: () => void }

type BuyerKind = 'individual' | 'headOffice' | 'branch'

const digits = (value: string) => value.replace(/\D/g, '')

/// ฟอร์มยืนยันข้อมูลผู้ซื้อก่อนออกใบกำกับภาษีเต็มรูป — เติมจากข้อมูลลูกค้าให้ แก้ได้ (เช่น ออกในนามบริษัท)
/// ข้อมูลที่แก้ตรงนี้เก็บกับใบกำกับภาษีใบนี้เท่านั้น ไม่เขียนกลับไปที่ข้อมูลลูกค้า
/// [BIZ] ออกแล้วแก้ไม่ได้ (ยังไม่มี void/ออกใหม่ — OQ#7) จึงต้องยืนยันก่อนกดออก · server ตรวจทุกช่องซ้ำเสมอ
export function TaxInvoiceIssueModal({ open, job, onClose, onIssued }: Props) {
  const queryClient = useQueryClient()
  const stateQuery = useQuery({
    queryKey: ['tax-invoice', job.jobId],
    queryFn: () => getTaxInvoiceState(job.jobId),
    enabled: open,
  })
  const state = stateQuery.data

  const [name, setName] = useState('')
  const [address, setAddress] = useState('')
  const [taxId, setTaxId] = useState('')
  const [buyerKind, setBuyerKind] = useState<BuyerKind>('individual')
  const [branchNo, setBranchNo] = useState('')

  // เติมค่าครั้งเดียวต่อการเปิด — refetch ระหว่างกรอกต้องไม่ทับสิ่งที่แคชเชียร์แก้ไว้
  const [filledFor, setFilledFor] = useState<string | null>(null)
  useEffect(() => {
    if (!open) { setFilledFor(null); return }
    if (!state || filledFor === job.jobId) return
    const p = state.prefill
    setName(p.buyerName ?? '')
    setAddress(p.buyerAddress ?? '')
    setTaxId(p.buyerTaxId ?? '')
    setBuyerKind(!p.buyerBranchNo ? 'individual' : p.buyerBranchNo === '00000' ? 'headOffice' : 'branch')
    setBranchNo(p.buyerBranchNo && p.buyerBranchNo !== '00000' ? p.buyerBranchNo : '')
    setFilledFor(job.jobId)
  }, [open, state, filledFor, job.jobId])

  const taxIdDigits = digits(taxId)
  const branchDigits = buyerKind === 'headOffice' ? '00000' : buyerKind === 'branch' ? digits(branchNo) : null

  // มิเรอร์ TaxInvoiceService.ValidateBuyer เพื่อบอกเหตุผลปุ่มที่ปิด — server ตรวจซ้ำเสมอ
  const formError = !name.trim()
    ? 'กรุณาระบุชื่อผู้ซื้อ'
    : !address.trim()
      ? 'กรุณาระบุที่อยู่ผู้ซื้อ — ใบกำกับภาษีเต็มรูปต้องมีที่อยู่ผู้ซื้อเสมอ'
      : taxIdDigits && taxIdDigits.length !== 13
        ? 'เลขประจำตัวผู้เสียภาษีต้องเป็นตัวเลข 13 หลัก'
        : buyerKind !== 'individual' && !taxIdDigits
          ? 'ผู้ซื้อที่เป็นนิติบุคคล/จด VAT ต้องระบุเลขประจำตัวผู้เสียภาษี'
          : buyerKind === 'branch' && branchDigits?.length !== 5
            ? 'เลขที่สาขาต้องเป็นตัวเลข 5 หลัก'
            : null

  const issueMutation = useMutation({
    mutationFn: () => issueTaxInvoice(job.jobId, {
      buyerName: name.trim(),
      buyerAddress: address.trim(),
      buyerTaxId: taxIdDigits || null,
      buyerBranchNo: branchDigits,
    }),
    onSuccess: (invoice) => {
      toast.success(`ออกใบกำกับภาษี ${invoice.documentNo} แล้ว`)
      void queryClient.invalidateQueries({ queryKey: ['tax-invoice', job.jobId] })
      onIssued()
    },
    onError: (error) => toast.error(isApiError(error) ? error.messageTh : 'ออกใบกำกับภาษีไม่สำเร็จ'),
  })

  const blockedReason = state?.blockedReasonTh || null
  const disabledReason = blockedReason ?? formError

  let body
  if (stateQuery.isPending) {
    body = (
      <StateBlock variant="loading" title="กำลังโหลดข้อมูลผู้ซื้อ" reason="ระบบกำลังอ่านข้อมูลลูกค้าและสาขา"
        actionLabel="โหลดใหม่" onAction={() => void stateQuery.refetch()}>
        <SkeletonRows count={4} />
      </StateBlock>
    )
  } else if (stateQuery.isError) {
    body = (
      <StateBlock
        variant="error"
        title="โหลดข้อมูลไม่สำเร็จ"
        reason={isApiError(stateQuery.error) ? stateQuery.error.messageTh : 'เกิดข้อผิดพลาดที่ไม่ทราบสาเหตุ'}
        traceId={isApiError(stateQuery.error) ? stateQuery.error.traceId : undefined}
        actionLabel="ลองใหม่"
        onAction={() => void stateQuery.refetch()}
      />
    )
  } else {
    body = (
      <div className="billing-issue-form">
        {state?.seller ? (
          <p className="form-message">
            ผู้ขาย: {state.seller.name} · เลขประจำตัวผู้เสียภาษี {state.seller.taxId || 'ไม่ระบุ'}
          </p>
        ) : null}
        {blockedReason ? <div className="form-error-panel" role="alert"><p>{blockedReason}</p></div> : null}
        <section className="form-grid">
          <Field label="ชื่อผู้ซื้อ (บุคคล/บริษัท)" wide>
            <Input maxLength={200} value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label="ที่อยู่ผู้ซื้อ" wide>
            <Textarea maxLength={500} rows={3} value={address} onChange={(e) => setAddress(e.target.value)} />
          </Field>
          <Field label="ประเภทผู้ซื้อ">
            <Select value={buyerKind} onChange={(e) => setBuyerKind(e.target.value as BuyerKind)}>
              <option value="individual">บุคคลธรรมดา (ไม่ระบุสาขา)</option>
              <option value="headOffice">นิติบุคคล — สำนักงานใหญ่</option>
              <option value="branch">นิติบุคคล — สาขา</option>
            </Select>
          </Field>
          <Field label={buyerKind === 'individual' ? 'เลขประจำตัวผู้เสียภาษี / บัตรประชาชน (ถ้ามี)' : 'เลขประจำตัวผู้เสียภาษี'}>
            <Input inputMode="numeric" maxLength={17} value={taxId} onChange={(e) => setTaxId(e.target.value)} />
          </Field>
          {buyerKind === 'branch' ? (
            <Field label="เลขที่สาขา (5 หลัก)">
              <Input inputMode="numeric" maxLength={5} value={branchNo} onChange={(e) => setBranchNo(e.target.value)} />
            </Field>
          ) : null}
        </section>
        <p className="form-message">
          ข้อมูลนี้ใช้กับใบกำกับภาษีใบนี้เท่านั้น ไม่เปลี่ยนข้อมูลลูกค้า · ออกแล้วแก้ไขหรือออกใหม่ไม่ได้ ตรวจให้ถูกต้องก่อนยืนยัน
        </p>
        {issueMutation.isError ? <InlineError error={issueMutation.error} /> : null}
      </div>
    )
  }

  return (
    <ConfirmModal
      open={open}
      title="ออกใบกำกับภาษี"
      description="ยืนยันข้อมูลผู้ซื้อที่จะพิมพ์บนใบกำกับภาษีเต็มรูป"
      onClose={onClose}
      size="large"
      footer={
        <div className="billing-issue-footer">
          {disabledReason && !stateQuery.isPending ? <p className="billing-issue-footer__reason">{disabledReason}</p> : null}
          <Button variant="outline" onClick={onClose}>ยกเลิก</Button>
          <Button
            onClick={() => issueMutation.mutate()}
            disabled={stateQuery.isPending || Boolean(disabledReason) || issueMutation.isPending}
          >
            {issueMutation.isPending ? 'กำลังออกใบกำกับภาษี…' : 'ยืนยันออกใบกำกับภาษี'}
          </Button>
        </div>
      }
    >
      {body}
    </ConfirmModal>
  )
}
