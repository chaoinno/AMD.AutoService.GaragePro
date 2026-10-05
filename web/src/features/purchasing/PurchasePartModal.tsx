import { useMutation, useQueryClient } from '@tanstack/react-query'
import { PackagePlus, Info } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { createPurchasePart, type PurchasePartInput } from '../../api/catalog'
import type { CatalogManagementItem } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Field, InlineError } from '../master-data/MasterDataCommon'

export function PurchasePartModal({ initialName, onClose, onSaved }: {
  initialName: string
  onClose: () => void
  onSaved: (item: CatalogManagementItem, unitCost: number) => void
}) {
  const [name, setName] = useState(initialName)
  const [code, setCode] = useState('')
  const [unit, setUnit] = useState('ชิ้น')
  const [compatibility, setCompatibility] = useState('')
  const [unitCost, setUnitCost] = useState('0')
  const client = useQueryClient()
  const save = useMutation({
    mutationFn: ({ input }: { input: PurchasePartInput; unitCost: number }) => createPurchasePart(input),
    onSuccess: (item, values) => {
      void client.invalidateQueries({ queryKey: ['catalog'] })
      void client.invalidateQueries({ queryKey: ['catalog-management'] })
      toast.success('เพิ่มอะไหล่และเลือกลงรายการสั่งซื้อแล้ว')
      onSaved(item, values.unitCost)
    },
  })
  const close = () => { if (!save.isPending) onClose() }
  return <ConfirmModal open title="เพิ่มอะไหล่ใหม่สำหรับสั่งซื้อ" description="เพิ่มได้จากหน้านี้ แล้วระบบจะเลือกอะไหล่ลงในเอกสารให้ทันที" onClose={close} size="medium" footer={<>
    <Button variant="ghost" disabled={save.isPending} onClick={close}>กลับไปที่เอกสาร</Button>
    <Button type="submit" form="purchase-new-part" disabled={save.isPending}><PackagePlus data-icon="inline-start" />{save.isPending ? 'กำลังเพิ่มอะไหล่…' : 'เพิ่มและเลือกอะไหล่'}</Button>
  </>}>
    <form id="purchase-new-part" className="management-form" onSubmit={event => {
      event.preventDefault()
      if (!Number.isFinite(Number(unitCost)) || Number(unitCost) < 0) return
      save.mutate({ input: { name: name.trim(), unit: unit.trim(), code: code.trim() || undefined, compatibility: compatibility.trim() || undefined }, unitCost: Number(unitCost) })
    }}>
      <fieldset className="purchase-fieldset" disabled={save.isPending}>
        <div className="form-grid">
          <Field label="ชื่ออะไหล่ *" wide><Input data-dialog-autofocus required maxLength={300} value={name} onChange={event => setName(event.target.value)} placeholder="เช่น ลูกปืนล้อหน้า Toyota Vios" /></Field>
          <Field label="รหัสอะไหล่ (ถ้ามี)"><Input maxLength={60} value={code} onChange={event => setCode(event.target.value)} placeholder="เว้นว่างเพื่อสร้างรหัสอัตโนมัติ" /></Field>
          <Field label="หน่วยนับ *"><Input required maxLength={40} value={unit} onChange={event => setUnit(event.target.value)} placeholder="ชิ้น / ชุด / ขวด" /></Field>
          <Field label="รุ่นรถที่รองรับ / รายละเอียด" wide><Input maxLength={500} value={compatibility} onChange={event => setCompatibility(event.target.value)} placeholder="ระบุรุ่นรถ ยี่ห้อ หรือรายละเอียดเพิ่มเติม" /></Field>
          <Field label="ราคาซื้อประมาณการ / หน่วย (บาท)" wide><Input type="number" min="0" max="100000000" step="0.01" required value={unitCost} onChange={event => setUnitCost(event.target.value)} /></Field>
        </div>
        <Alert className="purchase-new-part-note"><Info /><div><AlertTitle>บันทึกข้อมูลอะไหล่สำหรับสั่งซื้อ</AlertTitle><AlertDescription>อะไหล่จะอยู่ในรายการสินค้าของสาขา จำนวนคงเหลือเริ่มที่ 0 และเพิ่มสต็อกเมื่อรับสินค้า ราคาที่กรอกใช้ในเอกสารนี้</AlertDescription></div></Alert>
        {save.isError && <InlineError error={save.error} />}
      </fieldset>
    </form>
  </ConfirmModal>
}
