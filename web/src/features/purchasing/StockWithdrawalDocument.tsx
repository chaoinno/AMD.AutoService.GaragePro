import type { Withdrawal } from '../../api/purchasing'
import { formatDateTime } from '../../lib/format'
import { money } from './PurchasingPage'

type StockWithdrawalDocumentProps = { withdrawal: Withdrawal }

/// เนื้อหาใบเบิกสินค้า — ใช้ซ้อนบน job card modal (StockWithdrawalDocumentModal) เมื่อเบิกผูกกับ job
/// และจากหน้าสต็อกเมื่อเบิกแบบไม่ผูกงาน (jobNo เป็น null)
export function StockWithdrawalDocument({ withdrawal }: StockWithdrawalDocumentProps) {
  return (
    <article className="quotation-document">
      <header className="document-header">
        <div className="document-branch">
          <div className="document-brand-mark" aria-hidden="true">GP</div>
          <div>
            <h1>GaragePro</h1>
            <p>{withdrawal.warehouseName}</p>
          </div>
        </div>
        <div className="document-title">
          <span>เอกสารเบิกสินค้าออกจากคลัง</span>
          <h2>ใบเบิกสินค้า</h2>
          <strong>{withdrawal.documentNumber}</strong>
        </div>
      </header>

      <section className="document-info-grid">
        <InfoBlock title="ข้อมูลการเบิก">
          <InfoRow label="วันที่" value={formatDateTime(withdrawal.occurredAt)} strong />
          <InfoRow label="คลังที่เบิก" value={withdrawal.warehouseName} />
          <InfoRow label="งานที่อ้างอิง" value={withdrawal.jobNo || 'ไม่ผูกกับงาน'} />
        </InfoBlock>
        <InfoBlock title="ผู้เกี่ยวข้อง">
          <InfoRow label="ผู้เบิก" value={withdrawal.requesterName} strong />
          <InfoRow label="ผู้จ่าย (ผู้ทำรายการในระบบ)" value={withdrawal.issuedByName} />
        </InfoBlock>
      </section>

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th className="document-col-code">รหัส</th>
              <th>รายการ</th>
              <th className="document-number">จำนวน</th>
              <th>หน่วย</th>
              {withdrawal.totalCost != null ? <th className="document-number">ต้นทุน/หน่วย</th> : null}
            </tr>
          </thead>
          <tbody>
            {withdrawal.lines.map((line) => (
              <tr key={line.catalogItemId}>
                <td>{line.code}</td>
                <td>{line.name}</td>
                <td className="document-number">{line.quantity}</td>
                <td>{line.unit}</td>
                {withdrawal.totalCost != null ? <td className="document-number">{money(line.unitCost ?? 0)}</td> : null}
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      {withdrawal.totalCost != null ? (
        <section className="document-summary-section">
          <div className="document-notes">
            <strong>เหตุผลการเบิก</strong>
            <p>{withdrawal.reason}</p>
          </div>
          <p className="purchase-total">
            ต้นทุนรวม <strong className="money">{money(withdrawal.totalCost)} บาท</strong>
          </p>
        </section>
      ) : (
        <section className="document-info-block">
          <h3>เหตุผลการเบิก</h3>
          <p>{withdrawal.reason}</p>
        </section>
      )}

      <footer className="document-footer">
        <div className="signature-slots">
          <div className="signature-slot">
            <span />
            <strong>ผู้เบิก</strong>
            <small>{withdrawal.requesterName}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
          <div className="signature-slot">
            <span />
            <strong>ผู้จ่าย</strong>
            <small>{withdrawal.issuedByName}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
        </div>
        <div className="document-footer__meta">
          <span>{withdrawal.warehouseName}</span>
          <span>{withdrawal.documentNumber}</span>
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
