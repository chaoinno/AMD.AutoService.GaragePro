import type { Job } from '../../api/types'
import type { PaymentReceipt, Payment } from '../../api/pos'
import { Money } from '../../components/Money'
import { formatDateTime, formatMoney } from '../../lib/format'

type PaymentReceiptDocumentProps = { job: Job; receipt: PaymentReceipt; payments: Payment[] }

const METHOD_LABEL: Record<Payment['method'], string> = {
  cash: 'เงินสด', transfer: 'โอนเงิน', card: 'บัตรเครดิต/เดบิต', qr: 'พร้อมเพย์ (QR)',
}

/// เนื้อหาใบเสร็จรับเงิน — MVP: ไม่มีใบกำกับภาษีเต็มรูป (ดู [ASSUME] ใน CLAUDE.md/docs/06-purchasing-fifo.md ที่คล้ายกัน)
/// ใช้ CSS class เดิมร่วมกับใบเบิกสินค้า/ใบเสนอราคา (quotation-document/document-*) ไม่ต้องมี signature-slots
/// เพราะใบเสร็จเป็นแค่บันทึกยอดชำระ ไม่ใช่เอกสารที่ต้องเซ็น
export function PaymentReceiptDocument({ job, receipt, payments }: PaymentReceiptDocumentProps) {
  return (
    <article className="quotation-document">
      <header className="document-header">
        <div className="document-branch">
          <div className="document-brand-mark" aria-hidden="true">GP</div>
          <div>
            <h1>GaragePro</h1>
            <p>{job.branchName || 'ไม่ระบุสาขา'}</p>
          </div>
        </div>
        <div className="document-title">
          <span>ใบเสร็จรับเงิน</span>
          <h2>Receipt</h2>
          <strong>{receipt.documentNo}</strong>
        </div>
      </header>

      <section className="document-info-grid">
        <InfoBlock title="ข้อมูลลูกค้า/รถ">
          <InfoRow label="ลูกค้า" value={job.customerName || 'ไม่ระบุชื่อ'} strong />
          <InfoRow label="ทะเบียนรถ" value={job.vehicleRegistration || 'ไม่ระบุทะเบียน'} />
          <InfoRow label="เลขที่งาน" value={job.jobNo} />
        </InfoBlock>
        <InfoBlock title="ข้อมูลเอกสาร">
          <InfoRow label="วันที่ออกใบเสร็จ" value={formatDateTime(receipt.issuedAt)} strong />
          <InfoRow label="ผู้ออกเอกสาร" value={receipt.issuedByName} />
        </InfoBlock>
      </section>

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th>ช่องทางชำระเงิน</th>
              <th className="document-number">จำนวนเงิน</th>
              <th>อ้างอิง</th>
              <th>ผู้รับเงิน</th>
              <th>เวลา</th>
            </tr>
          </thead>
          <tbody>
            {payments.map((p) => (
              <tr key={p.id}>
                <td>{METHOD_LABEL[p.method]}</td>
                <td className="document-number">{formatMoney(p.amount)}</td>
                <td>{p.reference || '—'}</td>
                <td>{p.receivedByName}</td>
                <td>{formatDateTime(p.receivedAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="document-summary-section">
        <div className="document-notes">
          <strong>หมายเหตุ</strong>
          <p>ใบเสร็จรับเงิน — ยังไม่รองรับใบกำกับภาษีเต็มรูปแบบ/วางบิลนิติบุคคล</p>
        </div>
        <div className="money-summary document-money-summary">
          <div className="money-summary__row">
            <span>ยอดก่อนภาษี</span>
            <Money value={receipt.netAmount} />
          </div>
          <div className="money-summary__row">
            <span>ภาษีมูลค่าเพิ่ม</span>
            <Money value={receipt.vatAmount} />
          </div>
          <div className="money-summary__row money-summary__grand">
            <span>ยอดรวมทั้งสิ้น</span>
            <Money value={receipt.totalAmount} />
          </div>
        </div>
      </section>

      <footer className="document-footer">
        <div className="document-footer__meta">
          <span>{job.branchName}</span>
          <span>{receipt.documentNo}</span>
        </div>
      </footer>
    </article>
  )
}

function InfoBlock({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="document-info-block">
      <h3>{title}</h3>
      {children}
    </section>
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
