import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { toCsv, type CsvColumn } from './csv'

export interface FilterOption {
  value: string
  label: string
  /** Shown as "Tất cả · 214". */
  count?: number
}

export interface DataTableToolbarProps<T> {
  /** Controlled search box; the caller debounces and queries the server. */
  search?: { value: string; onChange: (value: string) => void; placeholder?: string }
  /** Controlled chip filter (e.g. Tất cả / Ưu tiên cao / Trung bình / Thấp). */
  filter?: { options: FilterOption[]; value: string; onChange: (value: string) => void; label?: string }
  /** Downloads the rows currently shown as .csv (the real bank files come from the server). */
  exportCsv?: { fileName: string; columns: CsvColumn<T>[]; rows: T[] }
}

function download(fileName: string, text: string) {
  const url = URL.createObjectURL(new Blob([text], { type: 'text/csv;charset=utf-8' }))
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}

export function DataTableToolbar<T>({ search, filter, exportCsv }: DataTableToolbarProps<T>) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      {search && (
        <input
          type="search"
          value={search.value}
          onChange={(e) => search.onChange(e.target.value)}
          placeholder={search.placeholder ?? 'Tìm kiếm…'}
          aria-label={search.placeholder ?? 'Tìm kiếm'}
          className="h-9 w-72 rounded-lg border border-input bg-background px-3 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
        />
      )}
      {filter && (
        <div role="group" aria-label={filter.label ?? 'Bộ lọc'} className="flex flex-wrap gap-1">
          {filter.options.map((o) => (
            <button
              key={o.value}
              type="button"
              aria-pressed={o.value === filter.value}
              onClick={() => filter.onChange(o.value)}
              className={cn(
                'rounded-full border px-3 py-1 text-sm',
                o.value === filter.value
                  ? 'border-primary bg-primary text-primary-foreground'
                  : 'border-border bg-background hover:bg-muted',
              )}
            >
              {o.label}
              {o.count !== undefined && <span className="ml-1 opacity-70">· {o.count}</span>}
            </button>
          ))}
        </div>
      )}
      {exportCsv && (
        <Button
          className="ml-auto"
          variant="outline"
          size="sm"
          disabled={exportCsv.rows.length === 0}
          onClick={() => download(exportCsv.fileName, toCsv(exportCsv.columns, exportCsv.rows))}
        >
          Xuất CSV
        </Button>
      )}
    </div>
  )
}
