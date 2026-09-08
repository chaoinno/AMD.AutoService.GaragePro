import test from 'node:test'
import assert from 'node:assert/strict'
import { createTable, getCoreRowModel, getSortedRowModel, getExpandedRowModel } from '@tanstack/react-table'
import { compareTableValues } from '../src/lib/tableSort.ts'

test('numbers and Thai/code strings sort by their actual values', () => {
  assert.deepEqual([100, 2, 10, 0].sort(compareTableValues), [0, 2, 10, 100])
  assert.deepEqual(['WH-10', 'WH-2', 'WH-1'].sort(compareTableValues), ['WH-1', 'WH-2', 'WH-10'])
  assert.deepEqual(['ขาว', 'แดง', 'กานต์'].sort(compareTableValues), ['กานต์', 'ขาว', 'แดง'])
  assert.equal(compareTableValues(null, null), 0)
  assert.ok(compareTableValues(null, 1) > 0)
})

test('table sorting toggles both directions and reset, excluding action columns', () => {
  const table = createTable({
    data: [{ id: 'a', amount: 100 }, { id: 'b', amount: 2 }, { id: 'c', amount: 10 }],
    columns: [{ id: 'amount', accessorKey: 'amount' }, { id: 'actions', enableSorting: false }],
    state: { sorting: [] },
    defaultColumn: { sortDescFirst: false, sortingFn: (a, b, id) => compareTableValues(a.getValue(id), b.getValue(id)) },
    onSortingChange: updater => table.setOptions(old => ({ ...old, state: { sorting: typeof updater === 'function' ? updater(old.state.sorting) : updater } })),
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  })
  const order = () => table.getRowModel().rows.map(row => row.original.id)
  table.getColumn('amount').toggleSorting()
  assert.deepEqual(order(), ['b', 'c', 'a'])
  table.getColumn('amount').toggleSorting()
  assert.deepEqual(order(), ['a', 'c', 'b'])
  table.getColumn('amount').toggleSorting()
  assert.deepEqual(order(), ['a', 'b', 'c'])
  assert.equal(table.getColumn('actions').getCanSort(), false)
})

test('sorting categories keeps children under their own parent', () => {
  const table = createTable({
    data: [
      { id: 'parent2', name: 'ข', children: [{ id: 'child2', name: 'ง' }, { id: 'child1', name: 'ก' }] },
      { id: 'parent1', name: 'ก' },
    ],
    columns: [{ accessorKey: 'name' }],
    state: { sorting: [{ id: 'name', desc: false }], expanded: { parent2: true } },
    getRowId: row => row.id,
    getSubRows: row => row.children,
    defaultColumn: { sortingFn: (a, b, id) => compareTableValues(a.getValue(id), b.getValue(id)) },
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getExpandedRowModel: getExpandedRowModel(),
  })
  assert.deepEqual(table.getRowModel().rows.map(row => row.id), ['parent1', 'parent2', 'child1', 'child2'])
})
