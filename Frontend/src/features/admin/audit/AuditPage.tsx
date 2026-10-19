import { useState } from 'react'
import { Link } from 'react-router-dom'
import { DataTable, type Column } from '@/components/data-table'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { auditApi } from './api'
import type { AuditFilter } from './types'
import { auditRow, EMPTY_FILTER, ENTITY_TYPES, PAGE_SIZE, rangeError, type AuditRow } from './view'

const COLUMNS: Column<AuditRow>[] = [
  { id: 'time', header: 'Thời điểm', cell: (r) => <span className="tabular-nums">{r.time}</span> },
  { id: 'actor', header: 'Người thực hiện', cell: (r) => r.actor },
  {
    id: 'entity',
    header: 'Đối tượng',
    cell: (r) =>
      r.link ? (
        <Link to={r.link} className="underline-offset-4 hover:underline">
          {r.entity} #{r.entityId}
        </Link>
      ) : (
        <span>
          {r.entity} #{r.entityId}
        </span>
      ),
  },
  { id: 'field', header: 'Trường', cell: (r) => <span className="font-mono text-xs">{r.field}</span> },
  { id: 'change', header: 'Thay đổi', cell: (r) => <span className="tabular-nums">{r.change}</span> },
  { id: 'reason', header: 'Lý do', cell: (r) => r.reason },
]

const INPUT = 'rounded-lg border border-border bg-background p-2 text-sm'

/** The audit trail of the Admin actions, read only (contract admin.md 2.4, PRD: every Admin change is logged). */
export function AuditPage() {
  const [draft, setDraft] = useState<AuditFilter>(EMPTY_FILTER)
  const [applied, setApplied] = useState<AuditFilter>(EMPTY_FILTER)
  const [page, setPage] = useState(1)

  const error = rangeError(draft)
  const list = useResource(() => auditApi.list(applied, page), [applied, page])
  const rows = list.data ? list.data.items.map(auditRow) : []

  function apply() {
    if (error) return
    setPage(1)
    setApplied(draft)
  }

  function clear() {
    setDraft(EMPTY_FILTER)
    setApplied(EMPTY_FILTER)
    setPage(1)
  }

  const field = (id: string, label: string, control: React.ReactNode) => (
    <div className="flex flex-col gap-1 text-sm">
      <label htmlFor={id} className="text-xs font-medium text-muted-foreground">
        {label}
      </label>
      {control}
    </div>
  )

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Hệ thống · Nhật ký kiểm toán</div>
        <h1 className="text-2xl font-semibold">Nhật ký kiểm toán</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Mọi thay đổi của Admin và của hệ thống (phán quyết, khoá thợ, duyệt vắng mặt, kỳ payout, giá, gói) được ghi một lần và không sửa hay xoá được.
          Mới nhất ở trên.
        </p>
      </header>

      <form
        className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4"
        onSubmit={(e) => {
          e.preventDefault()
          apply()
        }}
      >
        {field(
          'audit-entity',
          'Đối tượng',
          <select id="audit-entity" className={INPUT} value={draft.entityType} onChange={(e) => setDraft({ ...draft, entityType: e.target.value })}>
            <option value="">Tất cả</option>
            {ENTITY_TYPES.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>,
        )}
        {field(
          'audit-id',
          'Mã đối tượng',
          <input id="audit-id" className={`${INPUT} w-32`} inputMode="numeric" value={draft.entityId} onChange={(e) => setDraft({ ...draft, entityId: e.target.value })} />,
        )}
        {field(
          'audit-actor',
          'Người thực hiện',
          <select id="audit-actor" className={INPUT} value={draft.actorType} onChange={(e) => setDraft({ ...draft, actorType: e.target.value })}>
            <option value="">Tất cả</option>
            <option value="ADMIN">Admin</option>
            <option value="SYSTEM">Hệ thống</option>
          </select>,
        )}
        {field('audit-from', 'Từ ngày', <input id="audit-from" type="date" className={INPUT} value={draft.from} onChange={(e) => setDraft({ ...draft, from: e.target.value })} />)}
        {field('audit-to', 'Đến ngày', <input id="audit-to" type="date" className={INPUT} value={draft.to} onChange={(e) => setDraft({ ...draft, to: e.target.value })} />)}
        <Button type="submit" disabled={Boolean(error)}>
          Lọc
        </Button>
        <Button type="button" variant="ghost" onClick={clear}>
          Xoá bộ lọc
        </Button>
        {error && (
          <div role="alert" className="w-full text-sm text-destructive">
            {error}
          </div>
        )}
      </form>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.id}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Không có bản ghi nào khớp với bộ lọc."
        pagination={list.data ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
