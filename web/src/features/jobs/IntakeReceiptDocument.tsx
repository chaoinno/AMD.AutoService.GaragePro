import { AlertTriangle, CheckCircle2, Circle, MinusCircle } from 'lucide-react'
import { attachmentFileUrl } from '../../api/attachments'
import type { Attachment, IntakeCheckResult, IntakeChecklist, IntakeChecklistItem, Job } from '../../api/types'
import { formatDate, formatDateTime } from '../../lib/format'

const CATEGORY_LABEL_TH: Record<string, string> = {
  exterior: 'ตรวจสอบภายนอกรอบคัน',
  wheels: 'ล้อและยาง',
  interior: 'ภายในห้องโดยสาร',
  underhood: 'ห้องเครื่องยนต์เบื้องต้น',
}

const RESULT_LABEL: Record<IntakeCheckResult, { label: string; icon: typeof CheckCircle2; className: string }> = {
  pending: { label: 'ยังไม่ได้ตรวจ', icon: Circle, className: 'intake-print-result--pending' },
  ok: { label: 'ปกติ', icon: CheckCircle2, className: 'intake-print-result--ok' },
  issue: { label: 'พบปัญหา', icon: AlertTriangle, className: 'intake-print-result--issue' },
  na: { label: 'ไม่เกี่ยวข้อง', icon: MinusCircle, className: 'intake-print-result--na' },
}

type IntakeReceiptDocumentProps = {
  job: Job
  checklist: IntakeChecklist
  photos: Attachment[]
}

/// เนื้อหาใบรับรถ A4 — ใช้ร่วมกันทั้งหน้าเอกสารเต็มจอ (IntakeDocumentPage) และ modal ที่ซ้อนบน job card (IntakeReceiptModal)
export function IntakeReceiptDocument({ job, checklist, photos }: IntakeReceiptDocumentProps) {
  const categories = groupByCategory(checklist.items)
  const issueCount = checklist.items.filter((i) => i.result === 'issue').length

  return (
    <article className="quotation-document intake-document">
      <header className="document-header">
        <div className="document-branch">
          <div className="document-brand-mark" aria-hidden="true">GP</div>
          <div>
            <h1>{job.branchName}</h1>
            <p>สาขา {job.branchName}</p>
          </div>
        </div>
        <div className="document-title">
          <span>เอกสารรับรถเข้าอู่</span>
          <h2>ใบรับรถ</h2>
          <strong>{job.jobNo}</strong>
        </div>
      </header>

      <section className="document-info-grid">
        <InfoBlock title="ข้อมูลลูกค้า">
          <InfoRow label="ชื่อ" value={job.customerName || 'ไม่ระบุชื่อ'} strong />
          <InfoRow label="โทร" value={job.customerPhone || 'ไม่ระบุ'} />
        </InfoBlock>
        <InfoBlock title="ข้อมูลรถและงาน">
          <div className="document-info-columns">
            <div>
              <InfoRow label="ทะเบียน" value={job.vehicleRegistration || 'ไม่ระบุ'} strong />
              <InfoRow label="รุ่น" value={job.vehicleModel || 'ไม่ระบุ'} />
              <InfoRow label="เลขตัวถัง" value={job.vehicleVin || 'ไม่ระบุ'} />
            </div>
            <div>
              <InfoRow label="เลขงาน" value={job.jobNo} strong />
              <InfoRow label="วันที่รับรถ" value={formatDate(job.createdAt)} />
              <InfoRow label="วันนัดรับรถคืน" value={job.promiseAt ? formatDate(job.promiseAt) : 'ไม่ระบุ'} />
            </div>
          </div>
        </InfoBlock>
      </section>

      <section className="intake-document-checklist">
        <div className="intake-document-checklist__heading">
          <h3>ตรวจสภาพรถขณะรับ</h3>
          {checklist.isLocked ? (
            <span>ส่งแล้วเมื่อ {formatDateTime(checklist.submittedAt!)} โดย {checklist.submittedByUserName}</span>
          ) : (
            <span>ยังไม่ส่ง checklist — ข้อมูลนี้เป็นร่างล่าสุด ณ เวลาที่พิมพ์</span>
          )}
        </div>
        {issueCount > 0 ? (
          <p className="intake-document-checklist__warning">
            <AlertTriangle aria-hidden="true" /> พบสภาพที่ต้องบันทึกไว้ {issueCount} รายการ — ดูรายละเอียดด้านล่าง
          </p>
        ) : null}

        {categories.map((category) => (
          <div key={category.key} className="intake-document-checklist__category document-lines">
            <h4>{category.labelTh}</h4>
            <table>
              <thead>
                <tr>
                  <th>รายการ</th>
                  <th className="document-col-approval">ผลตรวจ</th>
                  <th>หมายเหตุ</th>
                </tr>
              </thead>
              <tbody>
                {category.items.map((item) => {
                  const meta = RESULT_LABEL[item.result]
                  const Icon = meta.icon
                  return (
                    <tr key={item.itemCode}>
                      <td>
                        <strong>{item.labelTh}</strong>
                        {item.hintTh ? <small className="document-line-note">{item.hintTh}</small> : null}
                      </td>
                      <td>
                        <span className={`intake-print-result ${meta.className}`}>
                          <Icon aria-hidden="true" /> {meta.label}
                        </span>
                      </td>
                      <td>{item.note || '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        ))}
      </section>

      {photos.length ? (
        <section className="intake-document-photos">
          <h3>รูปถ่ายประกอบการรับรถ</h3>
          <div className="job-detail-photo-grid">
            {photos.map((p) => (
              <div key={p.id} className="job-detail-photo-grid__item">
                <img src={attachmentFileUrl(p.relativePath)} alt={p.fileName} />
              </div>
            ))}
          </div>
        </section>
      ) : null}

      <footer className="document-footer">
        <div className="signature-slots">
          <div className="signature-slot">
            <span />
            <strong>ลูกค้าผู้นำรถเข้ารับบริการ</strong>
            <small>{job.customerName || 'ไม่ระบุชื่อ'}</small>
            <small>วันที่ ____ / ____ / ______</small>
          </div>
          <div className="signature-slot">
            <span />
            <strong>พนักงานผู้รับรถ</strong>
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

function groupByCategory(items: IntakeChecklistItem[]) {
  const order: string[] = []
  const byKey = new Map<string, { key: string; labelTh: string; items: IntakeChecklistItem[] }>()
  for (const item of items) {
    if (!byKey.has(item.categoryKey)) {
      byKey.set(item.categoryKey, { key: item.categoryKey, labelTh: CATEGORY_LABEL_TH[item.categoryKey] ?? item.categoryKey, items: [] })
      order.push(item.categoryKey)
    }
    byKey.get(item.categoryKey)!.items.push(item)
  }
  return order.map((key) => byKey.get(key)!)
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
