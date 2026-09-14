import { useQuery } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { getCatalogItems } from '../../api/catalog'
import type { CatalogManagementItem } from '../../api/types'
import { Combobox } from '../../components/ui/combobox'
import { InlineError } from '../master-data/MasterDataCommon'

export function PurchaseItemPicker({ rowId, label, excluded, onSelect }: { rowId: string; label: string; excluded: string[]; onSelect: (item: CatalogManagementItem) => void }) {
  const [query, setQuery] = useState('')
  const [keyword, setKeyword] = useState('')
  useEffect(() => { const timer = setTimeout(() => setKeyword(query), 250); return () => clearTimeout(timer) }, [query])
  const catalog = useQuery({ queryKey: ['catalog', 'purchase-row', keyword], queryFn: () => getCatalogItems({ type: 'part', keyword, pageSize: 100 }) })
  const items = catalog.data?.items.filter(item => !excluded.includes(item.id)) ?? []
  return <div className="purchase-item-picker">
    <Combobox id={`purchase-item-${rowId}`} ariaLabel="ค้นหาสินค้าในแถว" placeholder="พิมพ์รหัสหรือชื่อสินค้า" selectedLabel={label} query={query} onQueryChange={setQuery} loading={catalog.isFetching || query !== keyword} options={items.map(item => ({ value: item.id, label: `${item.code} · ${item.name}`, description: `หน่วย: ${item.unit}` }))} onSelect={option => { const item = items.find(item => item.id === option.value); if (item) onSelect(item) }} />
    {catalog.isError && <InlineError error={catalog.error} />}
  </div>
}
