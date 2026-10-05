import type { Payment } from '../../../api/pos'
import { Money } from '../../../components/Money'
import { bahtText } from '../../../lib/bahtText'
import { formatDateTime, formatMoney, formatNumber } from '../../../lib/format'
import './billing.css'

export type BillingKind = 'statement' | 'receipt' | 'taxInvoice'
export type CopyLabel = 'original' | 'copy'

export type BillingParty = {
  name: string
  address?: string | null
  taxId?: string | null
  phone?: string | null
  branchNo?: string | null
}

export type BillingLine = {
  key: string
  description: string
  quantity: number
  unit: string
  unitPrice: number
  discountAmount: number
  netAmount: number
}

export type BillingGroup = { quotationCode: string; lines: BillingLine[] }

export type BillingDocumentData = {
  kind: BillingKind
  documentNo: string | null
  issuedAt: string
  issuedByName: string | null
  seller: BillingParty
  buyer: BillingParty
  jobNo: string
  vehicleRegistration: string
  /** เอกสารอ้างอิง เช่น เลขใบเสร็จบนใบกำกับภาษี */
  references: { label: string; value: string }[]
  groups: BillingGroup[]
  netAmount: number
  vatAmount: number
  totalAmount: number
  vatRate: number | null
  payments: Payment[]
  paidAmount: number | null
  remainingAmount: number | null
}

const TITLE: Record<BillingKind, { th: string; en: string }> = {
  statement: { th: 'ใบแจ้งยอด', en: 'STATEMENT' },
  receipt: { th: 'ใบเสร็จรับเงิน', en: 'RECEIPT' },
  taxInvoice: { th: 'ใบกำกับภาษี', en: 'TAX INVOICE' },
}

const METHOD_LABEL: Record<Payment['method'], string> = {
  cash: 'เงินสด', transfer: 'โอนเงิน', card: 'บัตรเครดิต/เดบิต', qr: 'พร้อมเพย์ (QR)',
}

const NOTE: Record<BillingKind, string> = {
  statement: 'ใบแจ้งยอดนี้แสดงรายการที่ลูกค้าอนุมัติและยอดที่ต้องชำระ ณ วันที่พิมพ์ — ไม่ใช่ใบเสร็จรับเงินหรือใบกำกับภาษี',
  receipt: 'ได้รับเงินตามรายการข้างต้นไว้ถูกต้องแล้ว — ใบเสร็จรับเงินนี้ไม่ใช่ใบกำกับภาษี',
  taxInvoice: 'ราคาสินค้า/บริการไม่รวมภาษีมูลค่าเพิ่ม · ภาษีมูลค่าเพิ่มแสดงแยกไว้ในยอดสรุป',
}

/** "00000" = สำนักงานใหญ่ ตามรูปแบบที่กรมสรรพากรใช้ */
export function branchLabel(branchNo: string | null | undefined): string | null {
  if (!branchNo) return null
  return branchNo === '00000' ? 'สำนักงานใหญ่' : `สาขาที่ ${branchNo}`
}

function formatTaxId(taxId: string): string {
  // 0-0000-00000-00-0 อ่านง่ายกว่าเลข 13 หลักติดกันตอนลูกค้าเทียบกับบัตร/ภ.พ.20
  return /^\d{13}$/.test(taxId)
    ? `${taxId[0]}-${taxId.slice(1, 5)}-${taxId.slice(5, 10)}-${taxId.slice(10, 12)}-${taxId[12]}`
    : taxId
}

/// เอกสารการเงินของจ๊อบ 3 แบบใช้โครงเดียวกัน (หัวผู้ขาย · ผู้ซื้อ · รายการ · ยอดสรุป) ต่างกันที่หัวเรื่อง/ส่วนท้าย
/// [BIZ] ห้ามแสดงต้นทุน/กำไร (กฎข้อ 10) — BillingLine ไม่มีฟิลด์ต้นทุนเลยโดยตั้งใจ
/// ใช้ CSS class เดิมของใบเสนอราคา (quotation-document/document-*) ให้หน้าตาเหมือนเอกสารอื่นของระบบ
export function BillingDocument({ data, copyLabel }: { data: BillingDocumentData; copyLabel: CopyLabel | null }) {
  const title = TITLE[data.kind]
  const isTaxInvoice = data.kind === 'taxInvoice'
  const vatPercent = data.vatRate ? `${formatNumber(data.vatRate * 100)}%` : null
  // ลำดับวิ่งต่อเนื่องข้ามกลุ่มใบเสนอราคา — คำนวณล่วงหน้า (ห้ามนับระหว่าง render ของลูก StrictMode render ซ้ำได้)
  const groupOffsets = data.groups.map((_, i) =>
    data.groups.slice(0, i).reduce((sum, g) => sum + g.lines.length, 0))

  return (
    <article className="quotation-document billing-document">
      <header className="document-header">
        <div className="document-branch">
          <div className="document-brand-mark" aria-hidden="true">GP</div>
          <div>
            <h1>{data.seller.name}</h1>
            <p>{data.seller.address || 'ไม่ระบุที่อยู่'}</p>
            <p>
              โทร {data.seller.phone || 'ไม่ระบุ'}
              {data.seller.taxId || isTaxInvoice
                ? ` · เลขประจำตัวผู้เสียภาษี ${data.seller.taxId ? formatTaxId(data.seller.taxId) : 'ไม่ระบุ'}`
                : null}
            </p>
          </div>
        </div>
        <div className="document-title">
          <span>{title.en}</span>
          <h2>{title.th}</h2>
          {data.documentNo ? <strong>{data.documentNo}</strong> : null}
          {copyLabel ? (
            <div>
              <span className={`billing-copy-chip billing-copy-chip--${copyLabel}`}>
                {copyLabel === 'original' ? 'ต้นฉบับ' : 'สำเนา'}
              </span>
            </div>
          ) : null}
        </div>
      </header>

      <section className="document-info-grid">
        <section className="document-info-block">
          <h3>{isTaxInvoice ? 'ผู้ซื้อ/ผู้รับบริการ' : 'ข้อมูลลูกค้า'}</h3>
          <InfoRow label="ชื่อ" value={data.buyer.name || 'ไม่ระบุชื่อ'} strong />
          {data.buyer.address || isTaxInvoice ? <InfoRow label="ที่อยู่" value={data.buyer.address || 'ไม่ระบุ'} /> : null}
          {data.buyer.taxId ? <InfoRow label="เลขผู้เสียภาษี" value={formatTaxId(data.buyer.taxId)} /> : null}
          {branchLabel(data.buyer.branchNo) ? <InfoRow label="สาขา" value={branchLabel(data.buyer.branchNo)!} /> : null}
          {data.buyer.phone ? <InfoRow label="โทร" value={data.buyer.phone} /> : null}
        </section>
        <section className="document-info-block">
          <h3>ข้อมูลเอกสาร</h3>
          <InfoRow label={data.kind === 'statement' ? 'วันที่พิมพ์' : 'วันที่'} value={formatDateTime(data.issuedAt)} strong />
          <InfoRow label="เลขที่งาน" value={data.jobNo} />
          <InfoRow label="ทะเบียนรถ" value={data.vehicleRegistration || 'ไม่ระบุทะเบียน'} />
          {data.references.map((r) => <InfoRow key={r.label} label={r.label} value={r.value} />)}
        </section>
      </section>

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th className="document-col-sequence">ลำดับ</th>
              <th>รายการ</th>
              <th className="document-number">จำนวน</th>
              <th className="document-number">ราคา/หน่วย</th>
              <th className="document-number">ส่วนลด</th>
              <th className="document-number">จำนวนเงิน</th>
            </tr>
          </thead>
          <tbody>
            {data.groups.length === 0 ? (
              <tr className="document-group-empty"><td colSpan={6}>ยังไม่มีรายการที่ลูกค้าอนุมัติ</td></tr>
            ) : null}
            {data.groups.map((group, i) => (
              <GroupRows
                key={group.quotationCode}
                group={group}
                showHeader={data.groups.length > 1}
                firstSequence={(groupOffsets[i] ?? 0) + 1}
              />
            ))}
          </tbody>
        </table>
      </section>

      <section className="document-summary-section">
        <div className="document-notes">
          <strong>จำนวนเงินรวม (ตัวอักษร)</strong>
          <p className="billing-baht-text">({bahtText(data.totalAmount)})</p>
          <strong>หมายเหตุ</strong>
          <p>{NOTE[data.kind]}</p>
        </div>
        <div className="money-summary document-money-summary">
          <div className="money-summary__row">
            <span>{isTaxInvoice ? 'มูลค่าสินค้า/บริการ' : 'ยอดก่อนภาษี'}</span>
            <Money value={data.netAmount} />
          </div>
          <div className="money-summary__row">
            <span>ภาษีมูลค่าเพิ่ม{vatPercent && data.vatAmount > 0 ? ` ${vatPercent}` : ''}</span>
            <Money value={data.vatAmount} />
          </div>
          <div className="money-summary__total">
            <span>ยอดรวมทั้งสิ้น</span>
            <Money value={data.totalAmount} />
          </div>
          {data.paidAmount !== null ? (
            <div className="money-summary__row">
              <span>ชำระแล้ว</span>
              <Money value={data.paidAmount} />
            </div>
          ) : null}
          {data.remainingAmount !== null ? (
            <div className="money-summary__row money-summary__grand">
              <span>คงเหลือที่ต้องชำระ</span>
              <Money value={data.remainingAmount} />
            </div>
          ) : null}
        </div>
      </section>

      {data.payments.length > 0 && data.kind !== 'taxInvoice' ? (
        <section className="document-lines billing-payments">
          <table>
            <thead>
              <tr>
                <th>{data.kind === 'receipt' ? 'ชำระโดย' : 'รายการชำระเงินที่ได้รับแล้ว'}</th>
                <th>อ้างอิง</th>
                <th>เวลา</th>
                <th className="document-number">จำนวนเงิน</th>
              </tr>
            </thead>
            <tbody>
              {data.payments.map((p) => (
                <tr key={p.id}>
                  <td>{METHOD_LABEL[p.method]}</td>
                  <td>{p.reference || '—'}</td>
                  <td>{formatDateTime(p.receivedAt)}</td>
                  <td className="document-number">{formatMoney(p.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      ) : null}

      <footer className="document-footer">
        {data.kind === 'statement' ? null : (
          <div className="signature-slots">
            <div className="signature-slot">
              <span />
              <strong>{data.kind === 'receipt' ? 'ผู้รับเงิน' : 'ผู้ออกใบกำกับภาษี'}</strong>
              <small>{data.issuedByName || ''}</small>
              <small>วันที่ ____ / ____ / ______</small>
            </div>
            <div className="signature-slot">
              <span />
              <strong>ผู้รับเอกสาร</strong>
              <small>{data.buyer.name}</small>
              <small>วันที่ ____ / ____ / ______</small>
            </div>
          </div>
        )}
        <div className="document-footer__meta">
          <span>{data.seller.name}</span>
          <span>{data.documentNo ?? `${title.th} · ${data.jobNo}`}</span>
        </div>
      </footer>
    </article>
  )
}

function GroupRows({
  group, showHeader, firstSequence,
}: { group: BillingGroup; showHeader: boolean; firstSequence: number }) {
  return (
    <>
      {showHeader ? (
        <tr className="document-group-row">
          <td colSpan={6}>ตามใบเสนอราคา {group.quotationCode}</td>
        </tr>
      ) : null}
      {group.lines.map((line, i) => (
        <tr key={line.key}>
          <td className="document-center">{firstSequence + i}</td>
          <td>{line.description}</td>
          <td className="document-number">{formatNumber(line.quantity)} {line.unit}</td>
          <td className="document-number">{formatMoney(line.unitPrice)}</td>
          <td className="document-number">{line.discountAmount > 0 ? formatMoney(line.discountAmount) : '—'}</td>
          <td className="document-number">{formatMoney(line.netAmount)}</td>
        </tr>
      ))}
    </>
  )
}

function InfoRow({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <div className="document-info-row">
      <span>{label}</span>
      {strong ? <strong>{value}</strong> : <p>{value}</p>}
    </div>
  )
}
