// Pure logic of a sortable table header (no React, so `node --test` covers it). The order itself is done by the server before paging,
// see the `sort` and `dir` parameters of .spec/contracts/ledger.md and payouts.md; the table only asks for it.

export type SortDir = 'asc' | 'desc'

/** The column being sorted (its API name, not its label) and the direction. */
export interface SortState {
  key: string
  dir: SortDir
}

/** What a click on a sortable header asks for: a new column starts ascending, the same column flips the direction. */
export function nextSort(current: SortState | null, key: string): SortState {
  if (current !== null && current.key === key) return { key, dir: current.dir === 'asc' ? 'desc' : 'asc' }
  return { key, dir: 'asc' }
}

/** The query parameters of a sort; nothing at all when there is none, so the server keeps its own default order. */
export function sortParams(sort: SortState | null): { sort?: string; dir?: SortDir } {
  return sort === null ? {} : { sort: sort.key, dir: sort.dir }
}

export type AriaSort = 'ascending' | 'descending' | 'none'

/** The `aria-sort` value of a column header. */
export function ariaSort(sort: SortState | null, key: string): AriaSort {
  if (sort === null || sort.key !== key) return 'none'
  return sort.dir === 'asc' ? 'ascending' : 'descending'
}

/** What a screen reader and the tooltip say for a sortable header: the direction a click will ask for. */
export function sortHint(sort: SortState | null, key: string, label: string): string {
  const next = nextSort(sort, key)
  return `Sắp xếp theo ${label} ${next.dir === 'asc' ? 'tăng dần' : 'giảm dần'}`
}
