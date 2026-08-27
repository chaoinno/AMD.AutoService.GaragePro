import { CarFront } from 'lucide-react'
import { useState } from 'react'
import { legacyAssetBaseUrl } from '../lib/config'

export function resolveVehicleImage(path: string | null) {
  if (!path) return null
  if (/^https?:\/\//i.test(path)) return path
  const normalized = path.replace(/\\/g, '/').replace(/^~?\//, '')
  return new URL(normalized, legacyAssetBaseUrl).toString()
}

export function VehicleImage({ path, className = 'job-car-image' }: { path: string | null; className?: string }) {
  const [failed, setFailed] = useState(false)
  const src = resolveVehicleImage(path)

  if (!src || failed) {
    return <span className={`${className} ${className}--empty`}><CarFront aria-hidden="true" /></span>
  }

  return (
    <img
      className={className}
      src={src}
      alt="รูปรถ"
      loading="lazy"
      onError={() => setFailed(true)}
    />
  )
}
