import type { Handover } from '../../api/handover'
import type { Job } from '../../api/types'
import { formatDateTime } from '../../lib/format'

type HandoverDocumentProps = { job: Job; handover: Handover; signatureImageUrl: string | null }

/// เนื้อหาใบส่งมอบรถ — ใช้ CSS class เดิมร่วมกับใบเบิกสินค้า/ใบรับรถ (quotation-document/document-*/signature-slots)
/// ช่องลายเซ็นลูกค้าฝั่งซ้ายแสดงลายเซ็นดิจิทัลที่จับไว้แล้ว (SignaturePad ใน JobCardModal) ถ้ายังไม่ยืนยันส่งมอบ
/// จะเป็นเส้นว่างให้เซ็นด้วยปากกาแทน (fallback กระดาษ เหมือนใบรับรถ/ใบเบิกสินค้าที่พิมพ์ได้ทุกสถานะ)
/// signatureImageUrl เป็น blob URL ที่ parent (HandoverDocumentModal) โหลดมาแล้วด้วย Bearer token — endpoint
/// ไฟล์แนบต้อง auth เสมอ ใส่ตรงๆ ใน <img src> ไม่ได้ (ดู StaffAvatar.tsx/getStaffImage เป็น pattern เดียวกัน)
export function HandoverDocument({ job, handover, signatureImageUrl }: HandoverDocumentProps) {
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
          <span>เอกสารส่งมอบรถคืนลูกค้า</span>
          <h2>ใบส่งมอบรถ</h2>
          <strong>{job.jobNo}</strong>
        </div>
      </header>

      <section className="document-info-grid">
        <InfoBlock title="ข้อมูลลูกค้า/รถ">
          <InfoRow label="ลูกค้า" value={job.customerName || 'ไม่ระบุชื่อ'} strong />
          <InfoRow label="ทะเบียนรถ" value={job.vehicleRegistration || 'ไม่ระบุทะเบียน'} />
          <InfoRow label="เลขที่งาน" value={job.jobNo} />
        </InfoBlock>
        <InfoBlock title="ข้อมูลการส่งมอบ">
          <InfoRow
            label="วันที่ส่งมอบ"
            value={handover.submittedAt ? formatDateTime(handover.submittedAt) : 'ยังไม่ยืนยันส่งมอบ'}
            strong
          />
          <InfoRow label="ผู้ส่งมอบ" value={handover.submittedByUserName || 'ยังไม่ระบุ'} />
        </InfoBlock>
      </section>

      <section className="document-lines">
        <table>
          <thead>
            <tr>
              <th>ของในรถ</th>
              <th>สถานะ</th>
              <th>หมายเหตุ</th>
            </tr>
          </thead>
          <tbody>
            {handover.items.map((item) => (
              <tr key={item.id}>
                <td>{item.name}</td>
                <td>{item.updatedAt ? (item.isReturned ? 'คืนแล้ว' : 'ไม่คืน') : 'ยังไม่ตรวจ'}</td>
                <td>{item.note || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <footer className="document-footer">
        <div className="signature-slots">
          <div className="signature-slot">
            {signatureImageUrl ? (
              <img className="signature-slot__image" src={signatureImageUrl} alt="ลายเซ็นลูกค้า" />
            ) : (
              <span />
            )}
            <strong>ลายเซ็นลูกค้า (ผู้รับรถ)</strong>
            <small>{job.customerName || 'ไม่ระบุชื่อ'}</small>
            <small>{handover.submittedAt ? formatDateTime(handover.submittedAt) : 'วันที่ ____ / ____ / ______'}</small>
          </div>
          <div className="signature-slot">
            <span />
            <strong>ผู้ส่งมอบ</strong>
            <small>{handover.submittedByUserName || 'ไม่ระบุชื่อ'}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
        </div>
        <div className="document-footer__meta">
          <span>{job.branchName}</span>
          <span>{job.jobNo}</span>
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
