import {
  flexRender,
  getCoreRowModel,
  getExpandedRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type SortingState,
  type ExpandedState,
} from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react'
import { useState } from 'react'
import { compareTableValues } from '../lib/tableSort'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from './ui/table'

type DataTableProps<T> = {
  data: T[]
  columns: ColumnDef<T, unknown>[]
  onRowClick?: (row: T) => void
  getRowLabel?: (row: T) => string
  sortable?: boolean
  sortScope?: 'page' | 'loaded'
  getSubRows?: (row: T) => T[] | undefined
  getRowId?: (row: T) => string
}

export function DataTable<T>({ data, columns, onRowClick, getRowLabel, sortable = false, sortScope, getSubRows, getRowId }: DataTableProps<T>) {
  const [sorting, setSorting] = useState<SortingState>([])
  const [expanded, setExpanded] = useState<ExpandedState>({})
  const table = useReactTable({
    data,
    columns,
    state: { sorting, expanded },
    onExpandedChange: setExpanded,
    getSubRows,
    getRowId,
    getExpandedRowModel: getExpandedRowModel(),
    onSortingChange: setSorting,
    enableSorting: sortable,
    defaultColumn: {
      sortingFn: (a, b, id) => compareTableValues(a.getValue(id), b.getValue(id)),
      sortDescFirst: false,
    },
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  })

  return (
    <div className="data-table-wrap">
      {sortable && sortScope && <p className="data-table__scope">กดหัวคอลัมน์เพื่อเรียงข้อมูล{sortScope === 'page' ? 'ในหน้าปัจจุบัน' : 'ที่โหลดแล้ว'}</p>}
      <Table className="data-table">
        <TableHeader>
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow key={headerGroup.id}>
              {headerGroup.headers.map((header) => (
                <TableHead
                  key={header.id}
                  style={{ width: header.getSize() }}
                  aria-sort={header.column.getIsSorted() === 'asc'
                    ? 'ascending'
                    : header.column.getIsSorted() === 'desc'
                      ? 'descending'
                      : header.column.getCanSort() ? 'none' : undefined}
                >
                  {header.isPlaceholder ? null : header.column.getCanSort() ? (
                    <button
                      type="button"
                      className="data-table__sort"
                      title={header.column.getNextSortingOrder() === 'asc' ? 'เรียงจากน้อยไปมาก' : header.column.getNextSortingOrder() === 'desc' ? 'เรียงจากมากไปน้อย' : 'ยกเลิกการเรียง'}
                      onClick={header.column.getToggleSortingHandler()}
                    >
                      {flexRender(header.column.columnDef.header, header.getContext())}
                      {header.column.getIsSorted() === 'asc'
                        ? <ArrowUp aria-hidden="true" />
                        : header.column.getIsSorted() === 'desc'
                          ? <ArrowDown aria-hidden="true" />
                          : <ArrowUpDown aria-hidden="true" />}
                    </button>
                  ) : (
                    flexRender(header.column.columnDef.header, header.getContext())
                  )}
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {table.getRowModel().rows.map((row) => (
            <TableRow
              key={row.id}
              className={onRowClick ? 'data-table__clickable-row' : undefined}
              tabIndex={onRowClick ? 0 : undefined}
              aria-label={getRowLabel?.(row.original)}
              onClick={() => onRowClick?.(row.original)}
              onKeyDown={(event) => {
                if (onRowClick && (event.key === 'Enter' || event.key === ' ')) {
                  event.preventDefault()
                  onRowClick(row.original)
                }
              }}
            >
              {row.getVisibleCells().map((cell) => (
                <TableCell key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}
