import type { ReactNode } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { DataTable } from './DataTable'

type ManagementColumn<T> = {
  id: string
  header: string
  value?: (item: T) => string | number | boolean | null | undefined
  render: (item: T) => ReactNode
  hidden?: boolean
  size?: number
}

/** Use the Jobs table's keyboard-accessible sorting for management lists. */
export function ManagementTable<T>({ data, columns, sortScope }: {
  data: T[]
  columns: ManagementColumn<T>[]
  sortScope?: 'page' | 'loaded'
}) {
  return <DataTable<T> sortable sortScope={sortScope} data={data} columns={columns.filter(column => !column.hidden).map<ColumnDef<T, unknown>>(column => ({
    id: column.id, header: column.header, accessorFn: column.value,
    enableSorting: Boolean(column.value), size: column.size ?? 180,
    cell: ({ row }) => column.render(row.original),
  }))} />
}
