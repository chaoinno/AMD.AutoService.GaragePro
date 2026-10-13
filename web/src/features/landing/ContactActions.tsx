import { MessageCircle, Phone } from 'lucide-react'
import { Link } from 'react-router'

export function ContactActions() {
  return (
    <div className="landing-page-actions">
      <a className="landing-btn landing-btn--primary" href="https://line.me/R/ti/p/@garagepro" target="_blank" rel="noopener noreferrer">
        <MessageCircle aria-hidden="true" /> LINE @garagepro
      </a>
      <a className="landing-btn landing-btn--ghost" href="tel:0909966446">
        <Phone aria-hidden="true" /> 090-996-6446
      </a>
      <Link className="landing-btn landing-btn--ghost" to="/contact">ติดต่อทีมงาน / ขอ Demo</Link>
    </div>
  )
}
