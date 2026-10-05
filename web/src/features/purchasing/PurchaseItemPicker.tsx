import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { PackagePlus } from 'lucide-react'
import { getCatalogItems } from '../../api/catalog'
import type { CatalogManagementItem } from '../../api/types'
import { Combobox } from '../../components/ui/combobox'
import { Button } from '../../components/ui/button'
import { InlineError } from '../master-data/MasterDataCommon'

export function PurchaseItemPicker({ rowId, label, excluded, onSelect, onCreate }: { rowId: string; label: string; excluded: string[]; onSelect: (item: CatalogManagementItem) => void; onCreate?: (name: string) => void }) {
  const [query, setQuery] = useState('')
  const [keyword, setKeyword] = useState('')
  useEffect(() => { const timer = setTimeout(() => setKeyword(query), 250); return () => clearTimeout(timer) }, [query])
  const catalog = useQuery({ queryKey: ['catalog', 'purchase-row', keyword], queryFn: () => getCatalogItems({ type: 'part', keyword, pageSize: 100 }) })
  const items = catalog.data?.items.filter(item => !excluded.includes(item.id)) ?? []
  const options = items.map(item => ({ value: item.id, label: `${item.code} · ${item.name}`, description: `หน่วย: ${item.unit}` }))
  if (onCreate && query.trim()) options.push({ value: 'create-new-part', label: `เพิ่มอะไหล่ใหม่ “${query.trim()}”`, description: 'ยังไม่มีในระบบ? เพิ่มแล้วเลือกสั่งซื้อได้ทันที' })
  return <div className="purchase-item-picker">
    <Combobox id={`purchase-item-${rowId}`} ariaLabel="ค้นหาสินค้าในแถว" placeholder="พิมพ์รหัสหรือชื่อสินค้า" selectedLabel={label} query={query} onQueryChange={setQuery} loading={catalog.isFetching || query !== keyword} options={options} onSelect={option => { if (option.value === 'create-new-part') { onCreate?.(query.trim()); return } const item = items.find(item => item.id === option.value); if (item) onSelect(item) }} />
    {onCreate && <Button className="purchase-item-create" type="button" variant="link" size="sm" onMouseDown={event => event.preventDefault()} onClick={() => onCreate(query.trim())}><PackagePlus data-icon="inline-start" />เพิ่มอะไหล่ใหม่</Button>}
    {catalog.isError && <InlineError error={catalog.error} />}
  </div>
}
