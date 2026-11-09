import { Printer } from 'lucide-react'
import { useEffect, useState } from 'react'
import type { CreditNote } from '../../../api/creditNotes'
import { REFUND_METHODS } from '../../../api/creditNotes'
import { DocumentBrandMark } from '../../../components/Brand'
import { ConfirmModal } from '../../../components/ConfirmModal'
import { Money } from '../../../components/Money'
import { Button } from '../../../components/ui/button'
import { Select } from '../../../components/ui/select'
import { bahtText } from '../../../lib/bahtText'
import { formatDateTime, formatMoney, formatNumber } from '../../../lib/format'
import { branchLabel, formatTaxId, type CopyLabel } from './BillingDocument'
import './billing.css'

/// พิมพ์ใบลดหนี้ — ใช้ print area เดียวกับเอกสารการเงินอื่น (body.printing-payment-receipt + .payment-receipt-print-area)
export function CreditNoteDocumentModal({ note, onClose }: { note: CreditNote | null; onClose: () => void }) {
  const [copyLabel, setCopyLabel] = useState<CopyLabel>('original')
  const open = note !== null

  useEffect(() => {
    if (!open) return
    setCopyLabel('original')
    document.body.classList.add('printing-payment-receipt')
    return () => document.body.classList.remove('printing-payment-receipt')
  }, [open])

  return (
    <ConfirmModal open={open} title={note ? `ใบลดหนี้ ${note.documentNo}` : 'ใบลดหนี้'} onClose={onClose} size="xlarge">
      {note ? (
        <div className="document-modal-body">
          <div className="job-card-panel-actions print-hidden">
            <label className="billing-copy-select">
              <span>พิมพ์เป็น</span>
              <Select value={copyLabel} onChange={(e) => setCopyLabel(e.target.value as CopyLabel)}>
                <option value="original">ต้นฉบับ</option>
                <option value="copy">สำเนา (พิมพ์ซ้ำ)</option>
              </Select>
            </label>
            <Button onClick={() => window.print()}>
              <Printer aria-hidden="true" /> พิมพ์
            </Button>
          </div>
          <div className="payment-receipt-print-area">
            <CreditNoteDocument note={note} copyLabel={copyLabel} />
          </div>
        </div>
      ) : null}
    </ConfirmModal>
  )
}

/// [BIZ] อ้างใบกำกับ = "ใบลดหนี้/ใบกำกับภาษี" มีช่องตาม ม.86/10 (อ้างเลข/วันที่ใบกำกับเดิม · มูลค่าเดิม · ที่ถูกต้อง · ผลต่าง · VAT)
/// อ้างใบเสร็จ = "ใบลดหนี้" พร้อมข้อความว่าไม่ใช่เอกสารทางภาษี · ไม่แสดงต้นทุน/กำไร (กฎข้อ 10)
export function CreditNoteDocument({ note, copyLabel }: { note: CreditNote; copyLabel: CopyLabel | null }) {
  const title = note.isTaxDocument ? { th: 'ใบลดหนี้ / ใบกำกับภาษี', en: 'CREDIT NOTE / TAX INVOICE' } : { th: 'ใบลดหนี้', en: 'CREDIT NOTE' }
  const sourceLabel = note.sourceType === 'taxInvoice' ? 'อ้างอิงใบกำกับภาษี' : 'อ้างอิงใบเสร็จรับเงิน'
  const seller = note.seller
  const vatPercent = note.vatRate > 0 ? ` ${formatNumber(note.vatRate * 100)}%` : ''

  return (
    <article className="quotation-document billing-document">
      <header className="document-header">
        <div className="document-branch">
          <DocumentBrandMark />
          <div>
            <h1>{seller.name}</h1>
            <p>{seller.address || 'ไม่ระบุที่อยู่'}</p>
            <p>
              {seller.taxId || note.isTaxDocument ? `เลขประจำตัวผู้เสียภาษี ${seller.taxId ? formatTaxId(seller.taxId) : 'ไม่ระบุ'}` : null}
              {branchLabel(seller.branchNo) && note.isTaxDocument ? ` · ${branchLabel(seller.branchNo)}` : null}
            </p>
          </div>
        </div>
        <div className="document-title">
          <span>{title.en}</span>
          <h2>{title.th}</h2>
          <strong>{note.documentNo}</strong>
          {copyLabel ? (
            <div>
              <span className={`billing-copy-chip billing-copy-chip--${copyLabel}`}>{copyLabel === 'original' ? 'ต้นฉบับ' : 'สำเนา'}</span>
            </div>
          ) : null}
        </div>
      </header>

      <section className="document-info-grid">
        <section className="document-info-block">
          <h3>{note.isTaxDocument ? 'ผู้ซื้อ/ผู้รับบริการ' : 'ข้อมูลลูกค้า'}</h3>
          <Info label="ชื่อ" value={note.buyer.name} strong />
          {note.buyer.address ? <Info label="ที่อยู่" value={note.buyer.address} /> : null}
          {note.buyer.taxId ? <Info label="เลขผู้เสียภาษี" value={formatTaxId(note.buyer.taxId)} /> : null}
          {branchLabel(note.buyer.branchNo) ? <Info label="สาขา" value={branchLabel(note.buyer.branchNo)!} /> : null}
        </section>
        <section className="document-info-block">
          <h3>ข้อมูลเอกสาร</h3>
          <Info label="วันที่" value={formatDateTime(note.issuedAt)} strong />
          <Info label={sourceLabel} value={`${note.sourceDocumentNo} ลงวันที่ ${formatDateTime(note.sourceIssuedAt)}`} />
          <Info label="เลขที่งาน" value={note.jobNo} />
          <Info label="ทะเบียนรถ" value={note.vehicleRegistration || 'ไม่ระบุทะเบียน'} />
          <Info label="เหตุผล" value={note.reasonText ? `${note.reasonLabelTh} — ${note.reasonText}` : note.reasonLabelTh} />
        </section>
      </section>

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th className="document-col-sequence">ลำดับ</th>
              <th>รายการ</th>
              <th className="document-number">จำนวนเดิม</th>
              <th className="document-number">มูลค่าเดิม</th>
              <th className="document-number">จำนวนที่ลด</th>
              <th className="document-number">มูลค่าที่ลด</th>
            </tr>
          </thead>
          <tbody>
            {note.lines.map((l) => (
              <tr key={l.sequence}>
                <td className="document-center">{l.sequence}</td>
                <td>{l.description}{l.quotationCode ? <small className="billing-line-ref"> ({l.quotationCode})</small> : null}</td>
                <td className="document-number">{formatNumber(l.originalQuantity)} {l.unit}</td>
                <td className="document-number">{formatMoney(l.originalNetAmount)}</td>
                <td className="document-number">{l.quantity > 0 ? `${formatNumber(l.quantity)} ${l.unit}` : 'ลดราคา'}</td>
                <td className="document-number">{formatMoney(l.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="document-summary-section">
        <div className="document-notes">
          <strong>จำนวนเงินลดหนี้รวม (ตัวอักษร)</strong>
          <p className="billing-baht-text">({bahtText(note.totalAmount)})</p>
          <strong>หมายเหตุ</strong>
          <p>
            {note.isTaxDocument
              ? 'ใบลดหนี้นี้ออกเพื่อลดมูลค่าตามใบกำกับภาษีที่อ้างอิง ราคาไม่รวมภาษีมูลค่าเพิ่ม ภาษีมูลค่าเพิ่มแสดงแยกไว้ในยอดสรุป'
              : 'ใบลดหนี้นี้อ้างอิงใบเสร็จรับเงิน ไม่ใช่เอกสารทางภาษี'}
          </p>
        </div>
        <div className="money-summary document-money-summary">
          <div className="money-summary__row"><span>มูลค่าตาม{note.isTaxDocument ? 'ใบกำกับภาษี' : 'ใบเสร็จ'}เดิม</span><Money value={note.originalNetAmount} /></div>
          <div className="money-summary__row"><span>มูลค่าที่ถูกต้อง</span><Money value={note.correctNetAmount} /></div>
          <div className="money-summary__row"><span>ผลต่าง</span><Money value={note.diffNetAmount} /></div>
          <div className="money-summary__row"><span>ภาษีมูลค่าเพิ่ม{vatPercent}</span><Money value={note.vatAmount} /></div>
          <div className="money-summary__total"><span>รวมลดหนี้ทั้งสิ้น</span><Money value={note.totalAmount} /></div>
        </div>
      </section>

      {note.refunds.length > 0 ? (
        <section className="document-lines billing-payments">
          <table>
            <thead>
              <tr><th>คืนเงินโดย</th><th>อ้างอิง</th><th>เวลา</th><th className="document-number">จำนวนเงิน</th></tr>
            </thead>
            <tbody>
              {note.refunds.map((r) => (
                <tr key={r.id}>
                  <td>{REFUND_METHODS.find((m) => m.value === r.method)?.label ?? r.method}</td>
                  <td>{r.reference || '-'}</td>
                  <td>{formatDateTime(r.refundedAt)}</td>
                  <td className="document-number">{formatMoney(r.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ) : null}

      <footer className="document-footer">
        <div className="signature-slots">
          <div className="signature-slot">
            <span />
            <strong>ผู้อนุมัติ/ผู้ออกใบลดหนี้</strong>
            <small>{note.issuedByName}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
          <div className="signature-slot">
            <span />
            <strong>ผู้รับเอกสาร</strong>
            <small>{note.buyer.name}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
        </div>
        <div className="document-footer__meta">
          <span>{seller.name}</span>
          <span>{note.documentNo}</span>
        </div>
      </footer>
    </article>
  )
}

function Info({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="document-info-row">
      <span>{label}</span>
      {strong ? <strong>{value}</strong> : <p>{value}</p>}
    </div>
  )
}
