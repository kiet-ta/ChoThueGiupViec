import { useState } from 'react'
import { Link } from 'react-router-dom'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { cn } from '@/lib/utils'
import { disputesApi, PAGE_SIZE } from './api'
import { PRIORITY_CHIPS, queueRow, STATUS_CHIPS, type QueueRow } from './view'

const COLUMNS: Column<QueueRow>[] = [
  { id: 'code', header: 'Mã KN', cell: (r) => <span className="font-semibold">{r.code}</span> },
  { id: 'customer', header: 'Khách hàng', cell: (r) => r.customerName },
  {
    id: 'workers',
    header: 'Thợ liên quan',
    cell: (r) => (
      <div className="flex flex-col gap-1">
        {r.workers.map((w) => (
          <div key={`${w.name}-${w.kind}`}>
            <div>{w.name}</div>
            <div className="text-xs text-muted-foreground">{w.kind}</div>
          </div>
        ))}
      </div>
    ),
  },
  {
    id: 'category',
    header: 'Phân loại',
    cell: (r) => (
      <div className="flex flex-col items-start gap-1">
        <StatusBadge tone="info">{r.category}</StatusBadge>
        {r.autoTag && <StatusBadge tone="warning">Auto-Cancelled Dispute</StatusBadge>}
      </div>
    ),
  },
  { id: 'priority', header: 'Ưu tiên', cell: (r) => <StatusBadge tone={r.priority.tone}>{r.priority.label}</StatusBadge> },
  {
    id: 'sla',
    header: 'SLA còn lại',
    cell: (r) => <span className={cn('tabular-nums', r.overdue && 'font-semibold text-destructive')}>{r.slaText}</span>,
  },
  { id: 'status', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
  {
    id: 'action',
    header: 'Hành động',
    cell: (r) => (
      <Link to={r.detailPath} className="font-medium underline-offset-4 hover:underline">
        Xem chi tiết →
      </Link>
    ),
  },
]

function Chips({ label, options, value, onChange }: { label: string; options: { value: string; label: string }[]; value: string; onChange: (v: string) => void }) {
  return (
    <div role="group" aria-label={label} className="flex gap-2">
      {options.map((o) => (
        <Button key={o.value} size="sm" aria-pressed={value === o.value} variant={value === o.value ? 'default' : 'outline'} onClick={() => onChange(o.value)}>
          {o.label}
        </Button>
      ))}
    </div>
  )
}

/** WEB-M6-01 queue (Figma 66:2, top): unresolved disputes by SLA, filtered by priority; contract disputes.md 2.2. */
export function DisputeQueuePage() {
  const [status, setStatus] = useState(STATUS_CHIPS[0].value)
  const [priority, setPriority] = useState('')
  const [page, setPage] = useState(1)
  const unresolved = status === STATUS_CHIPS[0].value

  const queue = useResource(() => disputesApi.queue({ status, priority: unresolved ? priority : '', page }), [status, priority, page])
  const rows = queue.data ? queue.data.items.map(queueRow) : []

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">SLA 24–48H · BR-32</div>
        <h1 className="text-2xl font-semibold">Xử lý khiếu nại tranh chấp dịch vụ</h1>
        <p className="mt-1 text-sm text-muted-foreground">Hàng đợi sắp theo hạn SLA; ưu tiên tính từ thời gian còn lại (dưới 6 giờ là cao, dưới 24 giờ là trung bình).</p>
      </header>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <Chips label="Trạng thái" options={STATUS_CHIPS} value={status} onChange={(v) => { setStatus(v); setPage(1) }} />
        {unresolved && <Chips label="Mức ưu tiên" options={PRIORITY_CHIPS} value={priority} onChange={(v) => { setPriority(v); setPage(1) }} />}
      </div>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.disputeId}
        loading={queue.loading && !queue.data}
        error={queue.error}
        onRetry={queue.reload}
        emptyMessage={unresolved ? 'Không có khiếu nại nào đang mở' : 'Chưa có khiếu nại nào đã xử lý'}
        pagination={queue.data ? { page: queue.data.page, pageSize: PAGE_SIZE, total: queue.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
