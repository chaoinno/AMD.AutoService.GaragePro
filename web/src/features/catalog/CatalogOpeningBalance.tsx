import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, Layers } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { isApiError } from '../../api/client'
import { getWarehouses } from '../../api/masterData'
import { stockOpening } from '../../api/purchasing'
import type { CatalogManagementItem } from '../../api/types'
import { ConfirmModal } from '../../components/ConfirmModal'
import { Money } from '../../components/Money'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Button } from '../../components/ui/button'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Select } from '../../components/ui/select'
import { Textarea } from '../../components/ui/textarea'

/**
 * ตั้งยอดยกมา FIFO จากหน้าแก้ไขสินค้า (คำขอผู้ใช้ 2026-11-13) — เดิมต้องออกไปเมนูสต็อก FIFO
 * [BIZ] ใช้ endpoint เดิม `POST /inventory/opening-balances` กติกาเดิมทุกข้อ (ผู้จัดการ · ครั้งเดียว · ยอดต้องตรงกับที่บันทึกไว้)
 * · จำนวนที่ยกมา = คงคลัง/ชำรุดในฟอร์มนี้ — ถ้าแก้ฟอร์มค้างไว้ ระบบบันทึกข้อมูลสินค้าก่อนแล้วจึงตั้งยอด
 *   (server ตั้งยอดจากค่าที่บันทึกแล้วเท่านั้น และตรวจ ExpectedOnHand/Damaged กันยอดเปลี่ยนระหว่างทาง)
 * · ถ้าบันทึกสินค้าผ่านแต่ตั้งยอดไม่ผ่าน ข้อมูลสินค้าถูกบันทึกแล้ว กดตั้งยอดซ้ำได้ (ยังไม่เริ่ม FIFO จึงไม่มีอะไรซ้ำ)
 */
export function OpeningBalancePanel({ item, onHand, damaged, blockedReason, onOpen }: {
  item: CatalogManagementItem
  onHand: number
  damaged: number
  /** เหตุผลที่กดไม่ได้ — แสดงเป็นข้อความ ไม่ใช่ tooltip */
  blockedReason: string | null
  onOpen: () => void
}) {
  return <div className="field--wide catalog-opening">
    <div className="catalog-opening__text">
      <strong><Layers /> ยังไม่ได้ตั้งยอดยกมา FIFO</strong>
      <span>ตั้งยอดยกมาเพื่อเริ่มใช้สต็อกแบบล็อต (รับสินค้า เบิก FIFO และจุดต่ำสุด/สูงสุด) — ยกมาของดี {onHand.toLocaleString('th-TH')} {item.unit} · ชำรุด {damaged.toLocaleString('th-TH')} {item.unit} ตามจำนวนในฟอร์มนี้</span>
      {blockedReason ? <small className="field-hint">{blockedReason}</small> : null}
    </div>
    <Button type="button" variant="outline" disabled={Boolean(blockedReason)} onClick={onOpen}>ตั้งยอดยกมา FIFO</Button>
  </div>
}

export function OpeningBalanceModal({ item, onHand, damaged, formDirty, saveForm, onClose }: {
  item: CatalogManagementItem
  onHand: number
  damaged: number
  formDirty: boolean
  /** บันทึกฟอร์มสินค้าแล้วคืนค่าที่บันทึก · null = ฟอร์มไม่ผ่าน validation (ปิดหน้าต่างให้เห็นช่องที่ผิด) */
  saveForm: () => Promise<CatalogManagementItem | null>
  onClose: () => void
}) {
  const queryClient = useQueryClient()
  const warehouses = useQuery({ queryKey: ['warehouses', 'purchase-picker'], queryFn: () => getWarehouses() })
  const [warehouseId, setWarehouseId] = useState(item.warehouseId || '')
  const [cost, setCost] = useState(String(item.cost ?? 0))
  const [reason, setReason] = useState('ตั้งยอดยกมาจากหน้าแก้ไขสินค้า')
  const activeWarehouses = (warehouses.data ?? []).filter((warehouse) => warehouse.isActive)
  const selectedWarehouse = activeWarehouses.some((warehouse) => warehouse.id === warehouseId) ? warehouseId : ''
  const unitCost = Number(cost)
  const costValid = cost.trim() !== '' && Number.isFinite(unitCost) && unitCost >= 0 && unitCost <= 100000000 && Math.round(unitCost * 100) / 100 === unitCost

  const save = useMutation({
    mutationFn: async () => {
      let saved: CatalogManagementItem = item
      if (formDirty) {
        const result = await saveForm()
        if (!result) return 'invalid' as const
        saved = result
      }
      await stockOpening({ catalogItemId: saved.id, warehouseId: selectedWarehouse, unitCost, expectedOnHand: saved.onHand, expectedDamaged: saved.damaged, reason: reason.trim() })
      return 'opened' as const
    },
    onSuccess: async (outcome) => {
      if (outcome === 'invalid') {
        toast.error('ข้อมูลสินค้าในฟอร์มยังไม่ถูกต้อง กรุณาแก้ช่องที่แจ้งก่อนตั้งยอดยกมา')
        onClose()
        return
      }
      await Promise.all(['catalog', 'catalog-management', 'catalog-management-item', 'inventory', 'stock-detail'].map((key) => queryClient.invalidateQueries({ queryKey: [key] })))
      toast.success('ตั้งยอดยกมา FIFO เรียบร้อยแล้ว')
      onClose()
    },
    // บันทึกสินค้าผ่านแล้วแต่ตั้งยอดไม่ผ่าน → ให้ฟอร์มโหลดค่าที่บันทึกจริงกลับมา
    onError: () => { void queryClient.invalidateQueries({ queryKey: ['catalog-management-item', item.id] }) },
  })

  const blocked = !selectedWarehouse ? 'เลือกคลังที่เก็บสินค้ายกมา'
    : !costValid ? 'ต้นทุนต่อหน่วยต้องเป็น 0–100,000,000 บาท ทศนิยมไม่เกิน 2 ตำแหน่ง'
      : !reason.trim() ? 'กรอกเหตุผล / เลขอ้างอิง'
        : null

  return <ConfirmModal open size="small" title={`ตั้งยอดยกมา · ${item.name}`} description="นำจำนวนในฟอร์มเข้าสู่ล็อต FIFO ครั้งเดียว ยอดรวมสินค้าไม่เพิ่มซ้ำ หลังตั้งยอดจะแก้จำนวนในหน้าสินค้าไม่ได้อีก" onClose={() => { if (!save.isPending) onClose() }}
    footer={<><Button variant="ghost" disabled={save.isPending} onClick={onClose}>กลับ</Button><Button type="submit" form="catalog-opening-form" disabled={save.isPending || Boolean(blocked)}>{save.isPending ? 'กำลังบันทึก…' : formDirty ? 'บันทึกสินค้าและตั้งยอดยกมา' : 'ยืนยันตั้งยอดยกมา'}</Button></>}>
    <form id="catalog-opening-form" className="management-form" onSubmit={(event) => { event.preventDefault(); if (!blocked) save.mutate() }}>
      <fieldset className="catalog-opening-form" disabled={save.isPending}>
        <dl className="catalog-opening__summary">
          <div><dt>ของดียกมา</dt><dd>{onHand.toLocaleString('th-TH')} {item.unit}</dd></div>
          <div><dt>ชำรุดยกมา</dt><dd>{damaged.toLocaleString('th-TH')} {item.unit}</dd></div>
          <div><dt>มูลค่ายกมา</dt><dd>{costValid ? <Money value={onHand * unitCost} /> : '—'}</dd></div>
        </dl>
        {formDirty ? <p className="section-help">มีการแก้ไขในฟอร์มสินค้าที่ยังไม่ได้บันทึก — ระบบจะบันทึกข้อมูลสินค้าก่อน แล้วจึงตั้งยอดยกมาตามจำนวนข้างบน</p> : null}
        <Label className="field"><span>คลังสินค้า *</span>
          <Select required value={selectedWarehouse} onChange={(event) => setWarehouseId(event.target.value)} disabled={warehouses.isPending}>
            <option value="">เลือกคลัง</option>
            {activeWarehouses.map((warehouse) => <option key={warehouse.id} value={warehouse.id}>{warehouse.code} · {warehouse.name}</option>)}
          </Select>
          {warehouses.isError ? <small className="field-error">โหลดคลังไม่สำเร็จ</small> : !warehouses.isPending && !activeWarehouses.length ? <small className="field-error">ยังไม่มีคลังที่เปิดใช้งาน — เพิ่มคลังที่เมนูคลังสินค้าก่อน</small> : null}
        </Label>
        <Label className="field"><span>ต้นทุนต่อหน่วยของยอดยกมา (บาท) *</span>
          <Input className="money" required type="number" min="0" max="100000000" step="0.01" value={cost} onChange={(event) => setCost(event.target.value)} />
          <small className="field-hint">เริ่มจากต้นทุนในฟอร์มสินค้า · ใช้กับจำนวนของดียกมาทั้งหมด</small>
        </Label>
        <Label className="field"><span>เหตุผล / เลขอ้างอิง *</span><Textarea required rows={2} maxLength={1000} value={reason} onChange={(event) => setReason(event.target.value)} /></Label>
        {blocked && !save.isPending ? <p className="section-help">{blocked}</p> : null}
      </fieldset>
      {save.isError ? <Alert variant="destructive"><AlertTriangle /><div><AlertTitle>{isApiError(save.error) ? save.error.messageTh : 'ตั้งยอดยกมาไม่สำเร็จ'}</AlertTitle><AlertDescription>รหัสติดตาม (traceId): {isApiError(save.error) ? save.error.traceId || 'ไม่พบรหัสติดตาม' : 'ไม่พบรหัสติดตาม'}</AlertDescription></div></Alert> : null}
    </form>
  </ConfirmModal>
}
