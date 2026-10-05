import { useMutation } from '@tanstack/react-query'
import { CheckCircle2, CircleAlert, Clock, Globe, LoaderCircle, MessageCircle, Phone, Send } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { isApiError } from '../../api/client'
import { submitContactRequest, type ContactRequestInput } from '../../api/contact'
import { Alert, AlertDescription, AlertTitle } from '../../components/ui/alert'
import { Input } from '../../components/ui/input'
import { Label } from '../../components/ui/label'
import { Textarea } from '../../components/ui/textarea'

// ช่องทางติดต่อและตัวเลือกคัดมาจากหน้า https://gp.ipongs.com/ (#contact) ตามคำขอผู้ใช้ 2026-09-23
// ส่งเข้ากลุ่ม LINE ผ่าน POST /api/v1/public/contact-requests (ตัดตัวเลือกแพ็กเกจออกตามคำขอ)
const CONTACT_CHANNELS = [
  { icon: Phone, label: 'โทรศัพท์', value: '090-996-6446', href: 'tel:0909966446' },
  { icon: MessageCircle, label: 'LINE OA', value: '@garagepro', href: 'https://line.me/R/ti/p/@garagepro', hint: 'แอดเพื่อรับข่าวสารและสิทธิพิเศษ' },
  { icon: Globe, label: 'Facebook', value: 'GaragePro', href: 'https://www.facebook.com/GaragePro' },
  { icon: Globe, label: 'Instagram', value: 'garagepro.co', href: 'https://www.instagram.com/garagepro.co' },
  { icon: Globe, label: 'Website', value: 'garage-pro.net', href: 'https://garage-pro.net' },
] as const

const SIZE_OPTIONS = ['น้อยกว่า 30 คัน', '30–100 คัน', 'มากกว่า 100 คัน'] as const

type ContactForm = Omit<ContactRequestInput, 'requestId'>

const EMPTY_FORM: ContactForm = { name: '', garage: '', phone: '', lineId: '', garageSize: '', note: '', website: '' }

export function ContactSection() {
  const [form, setForm] = useState<ContactForm>(EMPTY_FORM)
  const [requestId, setRequestId] = useState(() => crypto.randomUUID())

  const mutation = useMutation({ mutationFn: submitContactRequest })

  // แก้ข้อมูลแล้วถือเป็นคำขอใหม่ (requestId ใหม่) — ถ้าคงค่าเดิม LINE จะมองว่าซ้ำแล้วทิ้งฉบับที่แก้
  const set = (field: keyof ContactForm) => (value: string) => {
    setForm((f) => ({ ...f, [field]: value }))
    setRequestId(crypto.randomUUID())
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    mutation.mutate({ ...form, requestId })
  }

  function reset() {
    mutation.reset()
    setForm(EMPTY_FORM)
    setRequestId(crypto.randomUUID())
  }

  return (
    <section id="contact" className="landing-section landing-section--dark" aria-labelledby="landing-contact-title">
      <div className="landing-container landing-contact">
        <div className="landing-contact__card">
          <p className="landing-eyebrow">ติดต่อทีมงาน</p>
          <h2 id="landing-contact-title" className="landing-h2">ขอ Demo หรือสอบถามข้อมูล</h2>
          <p className="landing-body">กรอกข้อมูลสั้นๆ ทีมงานจะติดต่อกลับภายใน 1 วันทำการ</p>

          {mutation.isSuccess ? (
            <div className="landing-contact__done" role="status">
              <CheckCircle2 aria-hidden="true" />
              <div>
                <p className="landing-contact__done-title">ได้รับข้อมูลแล้ว ขอบคุณครับ</p>
                <p>ทีมงานจะติดต่อกลับที่ {form.phone} ภายใน 1 วันทำการ</p>
                <div className="landing-contact__done-actions">
                  <button type="button" className="landing-btn landing-btn--ghost" onClick={reset}>
                    ส่งอีกครั้ง
                  </button>
                </div>
              </div>
            </div>
          ) : (
            <form className="landing-contact__form" onSubmit={handleSubmit}>
              <div className="form-field">
                <Label htmlFor="contact-name">ชื่อผู้ติดต่อ</Label>
                <Input id="contact-name" required maxLength={100} placeholder="เช่น คุณธนะรัชต์" value={form.name} onChange={(e) => set('name')(e.target.value)} />
              </div>
              <div className="form-field">
                <Label htmlFor="contact-garage">ชื่ออู่ / บริษัท</Label>
                <Input id="contact-garage" required maxLength={100} placeholder="เช่น อู่นำชัยรัตนาธิเบศร์" value={form.garage} onChange={(e) => set('garage')(e.target.value)} />
              </div>
              <div className="form-field">
                <Label htmlFor="contact-phone">เบอร์โทร</Label>
                <Input id="contact-phone" type="tel" required maxLength={20} autoComplete="tel" placeholder="08x-xxx-xxxx" value={form.phone} onChange={(e) => set('phone')(e.target.value)} />
              </div>
              <div className="form-field">
                <Label htmlFor="contact-line">
                  LINE ID <span className="landing-optional">(ถ้ามี)</span>
                </Label>
                <Input id="contact-line" maxLength={100} placeholder="@garage" value={form.lineId} onChange={(e) => set('lineId')(e.target.value)} />
              </div>
              <SegmentedField label="ขนาดอู่ (รถในอู่ต่อเดือน)" options={SIZE_OPTIONS} value={form.garageSize} onChange={set('garageSize')} />
              <div className="form-field landing-contact__wide">
                <Label htmlFor="contact-note">
                  รายละเอียดเพิ่มเติม <span className="landing-optional">(ไม่บังคับ)</span>
                </Label>
                <Textarea id="contact-note" rows={3} maxLength={1000} placeholder="เช่น ใช้ระบบเดิมอยู่ อยากย้ายข้อมูล / มีหลายสาขา" value={form.note} onChange={(e) => set('note')(e.target.value)} />
              </div>
              {/* [SECURITY] honeypot — ซ่อนจากคนและ screen reader บอทที่กรอกทุกช่องจะถูกกรองที่ server */}
              <div className="landing-honeypot" aria-hidden="true">
                <label htmlFor="contact-website">Website</label>
                <input id="contact-website" tabIndex={-1} autoComplete="off" value={form.website} onChange={(e) => set('website')(e.target.value)} />
              </div>

              {mutation.isError ? (
                <Alert variant="destructive" className="landing-contact__wide">
                  <CircleAlert aria-hidden="true" />
                  <div>
                    <AlertTitle>
                      {isApiError(mutation.error) ? mutation.error.messageTh : 'ส่งข้อมูลไม่สำเร็จ'}
                    </AlertTitle>
                    <AlertDescription>
                      <p>กดส่งอีกครั้งได้เลย ระบบจะไม่ส่งข้อมูลซ้ำ หรือติดต่อทาง LINE @garagepro / โทร 090-996-6446</p>
                      <p className="trace-id">
                        รหัสติดตาม (traceId): {isApiError(mutation.error) ? mutation.error.traceId : 'ไม่พบรหัสติดตาม'}
                      </p>
                    </AlertDescription>
                  </div>
                </Alert>
              ) : null}

              <div className="landing-contact__submit landing-contact__wide">
                <p>ข้อมูลใช้เพื่อติดต่อกลับเท่านั้น ไม่มีค่าใช้จ่ายในการขอ Demo</p>
                <button type="submit" className="landing-btn landing-btn--primary" disabled={mutation.isPending}>
                  {mutation.isPending ? (
                    <>
                      <LoaderCircle className="spin" aria-hidden="true" /> กำลังส่งข้อมูล
                    </>
                  ) : (
                    <>
                      ส่งข้อมูล ขอ Demo <Send aria-hidden="true" />
                    </>
                  )}
                </button>
              </div>
            </form>
          )}
        </div>

        <aside className="landing-contact__side">
          <div className="landing-contact__panel">
            <p className="landing-eyebrow">ช่องทางติดต่อ</p>
            <ul className="landing-contact__channels">
              {CONTACT_CHANNELS.map(({ icon: Icon, label, value, href, ...rest }) => (
                <li key={label}>
                  <a href={href} {...(href.startsWith('http') ? { target: '_blank', rel: 'noopener noreferrer' } : {})}>
                    <span className="landing-contact__ring" aria-hidden="true"><Icon /></span>
                    <span>
                      <small>{label}</small>
                      <strong>{value}</strong>
                      {'hint' in rest ? <small>{rest.hint}</small> : null}
                    </span>
                  </a>
                </li>
              ))}
            </ul>
          </div>
          <div className="landing-contact__panel">
            <p className="landing-eyebrow"><Clock aria-hidden="true" /> เวลาทำการ</p>
            <p className="landing-contact__hours">จันทร์ – เสาร์ 09:00 – 18:00</p>
            <p className="landing-body">ติดต่อกลับภายใน 1 วันทำการ · นอกเวลาฝากข้อความไว้ที่ LINE OA ได้ตลอด</p>
          </div>
        </aside>
      </div>
    </section>
  )
}

function SegmentedField({
  label,
  options,
  value,
  onChange,
}: {
  label: string
  options: readonly string[]
  value: string
  onChange: (value: string) => void
}) {
  return (
    <fieldset className="landing-segmented landing-contact__wide">
      <legend>{label}</legend>
      <div role="radiogroup" aria-label={label}>
        {options.map((option) => (
          <button
            key={option}
            type="button"
            role="radio"
            aria-checked={value === option}
            className="landing-segmented__option"
            // กดซ้ำเพื่อยกเลิก — ทั้งสองช่องไม่บังคับ
            onClick={() => onChange(value === option ? '' : option)}
          >
            {value === option ? <CheckCircle2 aria-hidden="true" /> : null}
            {option}
          </button>
        ))}
      </div>
    </fieldset>
  )
}
