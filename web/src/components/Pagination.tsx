import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from './ui/button'

export function Pagination({ page, totalPages, totalItems, onPageChange }: {
  page: number
  totalPages: number
  totalItems: number
  onPageChange: (page: number) => void
}) {
  return (
    <footer className="pagination-bar">
      <span>ทั้งหมด {totalItems.toLocaleString('th-TH')} รายการ</span>
      <div>
        <Button variant="outline" size="sm" disabled={page <= 1} title={page <= 1 ? 'อยู่หน้าแรกแล้ว' : 'ไปหน้าก่อนหน้า'} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft aria-hidden="true" /> ก่อนหน้า
        </Button>
        <strong>หน้า {page} / {Math.max(totalPages, 1)}</strong>
        <Button variant="outline" size="sm" disabled={page >= totalPages} title={page >= totalPages ? 'อยู่หน้าสุดท้ายแล้ว' : 'ไปหน้าถัดไป'} onClick={() => onPageChange(page + 1)}>
          ถัดไป <ChevronRight aria-hidden="true" />
        </Button>
      </div>
    </footer>
  )
}
