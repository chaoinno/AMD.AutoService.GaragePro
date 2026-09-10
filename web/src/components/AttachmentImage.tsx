import { useQuery } from '@tanstack/react-query'
import { ImageOff, Loader2 } from 'lucide-react'
import { useEffect, useState, type ReactNode } from 'react'
import { downloadAttachment } from '../api/attachments'
import { ImageLightbox } from './ImageLightbox'

/// รูปแนบทุกจุด (checklist รับรถ/ใบรับรถพิมพ์/แกลเลอรีเอกสารแนบ) ต้องผ่านตัวนี้แทน <img src={attachmentFileUrl(...)}>
/// ตรงๆ เพราะ GET /api/v1/attachments/file ต้อง Bearer token เสมอ — <img src> เพียวๆ ไม่แนบ header ให้ browser
/// เลยโดน 401 เงียบๆ กลายเป็นรูปพัง (ดู HandoverDocumentModal.tsx/StaffAvatar.tsx ที่ใช้ pattern เดียวกันนี้อยู่แล้ว)
export function AttachmentImage({
  relativePath,
  alt,
  className = '',
  linkClassName = '',
  linkToFullImage = true,
  children,
}: {
  relativePath: string
  alt: string
  className?: string
  linkClassName?: string
  /** true = คลิกแล้วเปิดพรีวิวเต็มจอในหน้าเดิม (ไม่เปิดแท็บใหม่) */
  linkToFullImage?: boolean
  children?: ReactNode
}) {
  const query = useQuery({
    queryKey: ['attachment-blob', relativePath],
    queryFn: () => downloadAttachment(relativePath),
    staleTime: 5 * 60_000,
  })
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [previewOpen, setPreviewOpen] = useState(false)

  useEffect(() => {
    if (!query.data) {
      setImageUrl(null)
      return
    }
    const nextUrl = URL.createObjectURL(query.data)
    setImageUrl(nextUrl)
    return () => URL.revokeObjectURL(nextUrl)
  }, [query.data])

  let content: ReactNode
  if (query.isPending) {
    content = (
      <span className={`attachment-image attachment-image--loading ${className}`} role="status">
        <Loader2 className="status-chip__icon intake-item__spin" aria-hidden="true" />
      </span>
    )
  } else if (query.isError || !imageUrl) {
    content = (
      <span
        className={`attachment-image attachment-image--error ${className}`}
        title="โหลดรูปไม่สำเร็จ — ไฟล์อาจถูกลบหรือหมดสิทธิ์เข้าถึง"
      >
        <ImageOff className="status-chip__icon" aria-hidden="true" />
      </span>
    )
  } else {
    content = <img className={className} src={imageUrl} alt={alt} loading="lazy" />
  }

  if (!linkToFullImage) {
    return children ? <>{content}{children}</> : content
  }

  if (imageUrl) {
    return (
      <>
        <button type="button" className={`image-preview-trigger ${linkClassName}`} onClick={() => setPreviewOpen(true)}>
          {content}
          {children}
        </button>
        <ImageLightbox src={imageUrl} alt={alt} open={previewOpen} onOpenChange={setPreviewOpen} />
      </>
    )
  }

  return (
    <span className={linkClassName}>
      {content}
      {children}
    </span>
  )
}
