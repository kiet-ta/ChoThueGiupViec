import { Button } from '@/components/ui/button'
import { pageItems, pageRange, totalPages } from './paging'

export interface PaginationProps {
  /** 1-based, as in the API contracts. */
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  /** When given, a page-size select is shown. */
  pageSizeOptions?: number[]
  onPageSizeChange?: (pageSize: number) => void
}

export function Pagination({
  page,
  pageSize,
  total,
  onPageChange,
  pageSizeOptions,
  onPageSizeChange,
}: PaginationProps) {
  const pages = totalPages(total, pageSize)
  const { from, to } = pageRange(page, pageSize, total)

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground">
      <span>{total === 0 ? 'Không có bản ghi' : `Hiển thị ${from}–${to} trên tổng ${total}`}</span>
      <div className="flex items-center gap-3">
        {pageSizeOptions && onPageSizeChange && (
          <label className="flex items-center gap-2">
            Số dòng
            <select
              className="h-8 rounded-lg border border-input bg-background px-2 text-foreground"
              value={pageSize}
              onChange={(e) => onPageSizeChange(Number(e.target.value))}
            >
              {pageSizeOptions.map((n) => (
                <option key={n} value={n}>
                  {n}
                </option>
              ))}
            </select>
          </label>
        )}
        <nav aria-label="Phân trang" className="flex items-center gap-1">
          <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
            Trước
          </Button>
          {pageItems(page, pages).map((item, i) =>
            item === 'ellipsis' ? (
              <span key={`e${i}`} className="px-1">
                …
              </span>
            ) : (
              <Button
                key={item}
                size="sm"
                variant={item === page ? 'default' : 'outline'}
                aria-current={item === page ? 'page' : undefined}
                onClick={() => onPageChange(item)}
              >
                {item}
              </Button>
            ),
          )}
          <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => onPageChange(page + 1)}>
            Sau
          </Button>
        </nav>
      </div>
    </div>
  )
}
