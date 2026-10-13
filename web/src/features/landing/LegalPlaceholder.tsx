const SECTION_TITLES = ['ข้อมูลที่เก็บ', 'วัตถุประสงค์', 'การเปิดเผย', 'สิทธิของเจ้าของข้อมูล', 'ติดต่อ']

/** Placeholder only: the system owner supplies all legal wording. */
export function LegalPlaceholder() {
  return (
    <div className="landing-section landing-section--light">
      <div className="landing-container landing-legal">
        {SECTION_TITLES.map((title) => (
          <section key={title}>
            <h2>{title}</h2>
            <p>[รอเจ้าของระบบระบุ]</p>
          </section>
        ))}
      </div>
    </div>
  )
}
