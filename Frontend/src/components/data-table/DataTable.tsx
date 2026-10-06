import type { ReactNode } from 'react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { Pagination, type PaginationProps } from './Pagination'

export interface Column<T> {
  id: string
  header: ReactNode
  cell: (row: T) => ReactNode
  align?: 'left' | 'center' | 'right'
  /** Extra classes for the th/td, e.g. 'min-w-48' or 'w-32'. */
  className?: string
}

export interface DataTableProps<T> {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string | number
  loading?: boolean
  /** Message of a failed load; shows the error state with a retry button. */
  error?: string | null
  onRetry?: () => void
  emptyMessage?: ReactNode
  onRowClick?: (row: T) => void
  /** Search, filters and export button, rendered above the table. */
  toolbar?: ReactNode
  /** Server-side paging; omit for an unpaged table. */
  pagination?: PaginationProps
  skeletonRows?: number
}

const ALIGN = { left: 'text-left', center: 'text-center', right: 'text-right' } as const

/**
 * Presentational table: the caller owns the data, paging and filters (they are server-side,
 * see the { items, page, pageSize, total } shape in .spec/contracts), the table only shows them.
 * Many columns scroll horizontally inside the card instead of squeezing.
 */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  loading = false,
  error = null,
  onRetry,
  emptyMessage = 'Không có dữ liệu',
  onRowClick,
  toolbar,
  pagination,
  skeletonRows = 5,
}: DataTableProps<T>) {
  const colSpan = columns.length
  let body: ReactNode

  if (error) {
    body = (
      <tr>
        <td colSpan={colSpan} className="px-4 py-10 text-center">
          <p className="font-medium">Không tải được dữ liệu</p>
          <p className="mt-1 text-sm text-muted-foreground">{error}</p>
          {onRetry && (
            <Button className="mt-4" variant="outline" size="sm" onClick={onRetry}>
              Thử lại
            </Button>
          )}
        </td>
      </tr>
    )
  } else if (loading) {
    body = Array.from({ length: skeletonRows }, (_, r) => (
      <tr key={`s${r}`} aria-hidden="true" className="border-t border-border">
        {columns.map((c) => (
          <td key={c.id} className="px-4 py-3">
            <div className="h-4 w-3/4 animate-pulse rounded bg-muted" />
          </td>
        ))}
      </tr>
    ))
  } else if (rows.length === 0) {
    body = (
      <tr>
        <td colSpan={colSpan} className="px-4 py-10 text-center text-sm text-muted-foreground">
          {emptyMessage}
        </td>
      </tr>
    )
  } else {
    body = rows.map((row) => (
      <tr
        key={rowKey(row)}
        onClick={onRowClick ? () => onRowClick(row) : undefined}
        className={cn('border-t border-border', onRowClick && 'cursor-pointer hover:bg-muted/50')}
      >
        {columns.map((c) => (
          <td key={c.id} className={cn('px-4 py-3 align-middle', ALIGN[c.align ?? 'left'], c.className)}>
            {c.cell(row)}
          </td>
        ))}
      </tr>
    ))
  }

  return (
    <div className="flex flex-col gap-3">
      {toolbar}
      <div className="overflow-x-auto rounded-xl border border-border bg-card text-card-foreground">
        <table className="w-full min-w-max border-collapse text-sm" aria-busy={loading}>
          <thead>
            <tr className="text-xs tracking-wide text-muted-foreground uppercase">
              {columns.map((c) => (
                <th key={c.id} scope="col" className={cn('px-4 py-3 font-medium', ALIGN[c.align ?? 'left'], c.className)}>
                  {c.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>{body}</tbody>
        </table>
      </div>
      {pagination && !error && <Pagination {...pagination} />}
    </div>
  )
}
