import { Dialog, DialogContent } from './ui/dialog'

/// พรีวิวรูปแบบเต็มจอในหน้าเดิม (ไม่เปิดแท็บใหม่) — ใช้ร่วมกับ AttachmentImage/VehicleImage
export function ImageLightbox({
  src,
  alt,
  open,
  onOpenChange,
}: {
  src: string | null
  alt: string
  open: boolean
  onOpenChange: (open: boolean) => void
}) {
  return (
    <Dialog open={open && Boolean(src)} onOpenChange={onOpenChange}>
      <DialogContent className="image-lightbox__content">
        {src ? <img className="image-lightbox__image" src={src} alt={alt} /> : null}
      </DialogContent>
    </Dialog>
  )
}
