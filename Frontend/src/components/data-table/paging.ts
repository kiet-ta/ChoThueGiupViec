// Pure paging maths for server-side pagination ({ items, page, pageSize, total }, identity.md section 1).
// No imports: runs under `node --test`.

export function totalPages(total: number, pageSize: number): number {
  if (pageSize <= 0) return 1
  return Math.max(1, Math.ceil(total / pageSize))
}

/** 1-based inclusive range of the rows on the page; { from: 0, to: 0 } when there are none. */
export function pageRange(page: number, pageSize: number, total: number): { from: number; to: number } {
  if (total <= 0 || pageSize <= 0) return { from: 0, to: 0 }
  const from = (page - 1) * pageSize + 1
  if (from > total) return { from: 0, to: 0 }
  return { from, to: Math.min(page * pageSize, total) }
}

export type PageItem = number | 'ellipsis'

/**
 * Page buttons to show, at most 7 slots: first and last page always, a window around the current page,
 * gaps as 'ellipsis'. With 7 pages or fewer every page is listed.
 */
export function pageItems(page: number, pages: number): PageItem[] {
  if (pages <= 7) return Array.from({ length: pages }, (_, i) => i + 1)
  const current = Math.min(Math.max(1, page), pages)
  if (current <= 4) return [1, 2, 3, 4, 5, 'ellipsis', pages]
  if (current >= pages - 3) return [1, 'ellipsis', pages - 4, pages - 3, pages - 2, pages - 1, pages]
  return [1, 'ellipsis', current - 1, current, current + 1, 'ellipsis', pages]
}
