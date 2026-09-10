import { useQuery } from '@tanstack/react-query'
import { CarFront } from 'lucide-react'
import { useEffect, useState } from 'react'
import { apiDownload } from '../api/client'
import { ImageLightbox } from './ImageLightbox'

/// รูปรถต้องผ่าน endpoint /api/v1/vehicles/{id}/image ที่ต้อง Bearer token เสมอ — ใช้ apiDownload + blob URL
/// แบบเดียวกับ AttachmentImage/StaffAvatar ไม่ใช่ <img src> ตรงๆ (เดิมต่อ URL กับ legacy media host ตรงๆ ซึ่งพังทุก
/// จ๊อบที่สร้างผ่านระบบใหม่ เพราะ vehicle.ImageUrl ที่เก็บไว้เป็น path ของ endpoint นี้ ไม่ใช่ path ไฟล์ legacy)
export function VehicleImage({
  vehicleId,
  className = 'job-car-image',
  clickToPreview = false,
}: {
  vehicleId: number | null
  className?: string
  clickToPreview?: boolean
}) {
  const query = useQuery({
    queryKey: ['vehicle-image', vehicleId],
    queryFn: () => apiDownload(`/api/v1/vehicles/${vehicleId}/image`),
    enabled: Boolean(vehicleId),
    staleTime: 60_000,
    retry: false,
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

  if (!imageUrl) {
    return <span className={`${className} ${className}--empty`}><CarFront aria-hidden="true" /></span>
  }

  if (!clickToPreview) {
    return <img className={className} src={imageUrl} alt="รูปรถ" loading="lazy" />
  }

  return (
    <>
      <button type="button" className={`image-preview-trigger ${className}`} onClick={() => setPreviewOpen(true)}>
        <img className="image-preview-trigger__img" src={imageUrl} alt="รูปรถ" loading="lazy" />
      </button>
      <ImageLightbox src={imageUrl} alt="รูปรถ" open={previewOpen} onOpenChange={setPreviewOpen} />
    </>
  )
}
