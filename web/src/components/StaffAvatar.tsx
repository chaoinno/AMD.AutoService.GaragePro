import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { getStaffImage, getStaffs } from '../api/staffs'
import { Avatar, AvatarFallback, AvatarImage } from './ui/avatar'

export function StaffAvatar({ name, className = '' }: { name: string; className?: string }) {
  const normalizedName = name.trim()
  const staffQuery = useQuery({
    queryKey: ['staff-avatar-match', normalizedName],
    queryFn: () => getStaffs({ keyword: normalizedName, includeInactive: true, pageSize: 10 }),
    enabled: Boolean(normalizedName),
    staleTime: 5 * 60_000,
  })
  const exactMatches = staffQuery.data?.items.filter((staff) => staff.fullName.trim() === normalizedName) ?? []
  const staff = exactMatches.length === 1 ? exactMatches[0] : null
  const imageQuery = useQuery({
    queryKey: ['staff-avatar-image', staff?.id, staff?.lastUpdated],
    queryFn: () => getStaffImage(staff!.id),
    enabled: Boolean(staff?.pictureUrl),
    staleTime: 5 * 60_000,
  })
  const [imageUrl, setImageUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!imageQuery.data) { setImageUrl(null); return }
    const nextUrl = URL.createObjectURL(imageQuery.data)
    setImageUrl(nextUrl)
    return () => URL.revokeObjectURL(nextUrl)
  }, [imageQuery.data])

  return (
    <Avatar className={className} title={imageUrl ? `รูปพนักงาน ${name}` : `ผู้ใช้งาน ${name}`}>
      {imageUrl ? <AvatarImage src={imageUrl} alt={`รูปพนักงาน ${name}`} /> : <AvatarFallback>{initials(name)}</AvatarFallback>}
    </Avatar>
  )
}

function initials(name: string) {
  return name.trim().split(/\s+/).filter(Boolean).slice(0, 2).map((word) => word[0]).join('') || 'ผช'
}
