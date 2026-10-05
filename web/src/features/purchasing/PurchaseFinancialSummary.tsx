const money = (value: number) => value.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
export const vatLabel = (hasVat: boolean, rate = 0.07) => hasVat ? `มี VAT ${(rate * 100).toLocaleString('th-TH')}%` : 'ไม่มี VAT'

// Keep the preview in satang, matching the server's document-level rounding.
export function purchaseTotals(lines: { quantity: number; unitCost: number }[], hasVat: boolean, vatRate = 0.07) {
  const subtotalSatang = lines.reduce((sum, line) => sum + line.quantity * Math.round(line.unitCost * 100), 0)
  const vatSatang = hasVat ? Math.round(subtotalSatang * Math.round(vatRate * 10000) / 10000) : 0
  return { subtotal: subtotalSatang / 100, vatAmount: vatSatang / 100, total: (subtotalSatang + vatSatang) / 100 }
}

export function PurchaseFinancialSummary({ subtotal, vatAmount, total, hasVat, vatRate = 0.07 }: {
  subtotal: number; vatAmount: number; total: number; hasVat: boolean; vatRate?: number
}) {
  return <dl className="purchase-financial-summary" aria-label="สรุปยอดเอกสาร">
    <div><dt>มูลค่าก่อนภาษี</dt><dd>{money(subtotal)} <span>บาท</span></dd></div>
    <div><dt>{hasVat ? `ภาษีมูลค่าเพิ่ม ${(vatRate * 100).toLocaleString('th-TH')}%` : 'ภาษีมูลค่าเพิ่ม (ไม่มี VAT)'}</dt><dd>{money(vatAmount)} <span>บาท</span></dd></div>
    <div className="purchase-financial-summary__total"><dt>ยอดสุทธิ</dt><dd>{money(total)} <span>บาท</span></dd></div>
  </dl>
}

export function PurchaseVatSelector({ value, onChange }: { value: boolean; onChange: (value: boolean) => void }) {
  return <fieldset className="purchase-vat-selector">
    <legend>ภาษีมูลค่าเพิ่ม</legend>
    <p>ราคาต่อหน่วยเป็นราคาก่อน VAT</p>
    <div className="purchase-vat-selector__options">
      {[false, true].map(hasVat => <label key={String(hasVat)} data-selected={value === hasVat || undefined}>
        <input type="radio" name="purchase-vat" checked={value === hasVat} onChange={() => onChange(hasVat)} />
        <span>{vatLabel(hasVat)}</span>
      </label>)}
    </div>
    <small>เลือกตามเงื่อนไขราคาของผู้ขาย</small>
  </fieldset>
}
