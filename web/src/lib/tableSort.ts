/** Thai-aware text ordering with numeric codes (WH-2 before WH-10). */
const collator = new Intl.Collator('th', { numeric: true, sensitivity: 'base' })

export function compareTableValues(a: unknown, b: unknown): number {
  if (a == null && b == null) return 0
  if (a == null) return 1
  if (b == null) return -1
  if (typeof a === 'number' && typeof b === 'number') return a - b
  return collator.compare(String(a), String(b))
}
