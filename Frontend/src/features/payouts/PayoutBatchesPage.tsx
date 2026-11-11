import { useState } from 'react'
import { Link } from 'react-router-dom'
import { DataTable, type Column, type SortState } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { useResource } from '@/hooks/use-resource'
import { PAGE_SIZE, payoutsApi } from './api'
import { BATCH_DEFAULT_SORT, BATCH_SORT, batchRow, type BatchRow } from './view'

const COLUMNS: Column<BatchRow>[] = [
  { id: 'month', header: 'Kỳ', sortKey: BATCH_SORT.period, sortLabel: 'kỳ', cell: (r) => <span className="font-semibold">{r.month}</span> },
  { id: 'status', header: 'Trạng thái', sortKey: BATCH_SORT.status, sortLabel: 'trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
  { id: 'items', header: 'Người nhận', align: 'right', sortKey: BATCH_SORT.items, sortLabel: 'số người nhận', cell: (r) => r.items },
  { id: 'total', header: 'Tổng đã tất toán', align: 'right', sortKey: BATCH_SORT.total, sortLabel: 'tổng đã tất toán', cell: (r) => <span className="tabular-nums">{r.total}</span> },
  { id: 'closed', header: 'Tất toán', sortKey: BATCH_SORT.closed, sortLabel: 'ngày tất toán', cell: (r) => r.closedBy },
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

/** The monthly payout batches (contract payouts.md 2.2): read only, the system settles each month from the ledger (decision D28). */
export function PayoutBatchesPage() {
  const [page, setPage] = useState(1)
  const [sort, setSort] = useState<SortState | null>(BATCH_DEFAULT_SORT)
  const list = useResource(() => payoutsApi.list(page, sort), [page, sort])
  const rows = list.data ? list.data.items.map(batchRow) : []

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Payout · Đầu mỗi tháng</div>
        <h1 className="text-2xl font-semibold">Kỳ payout hàng tháng</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Đầu mỗi tháng hệ thống tự tất toán tháng trước từ sổ cái: trừ tiền đã ứng, khoản khấu trừ và số dư kỳ trước. Admin chỉ xem và xuất file đối chiếu; hệ thống không chuyển tiền thật.
        </p>
      </header>

      <DataTable
        columns={COLUMNS}
        sort={sort}
        onSortChange={(next) => {
          setPage(1)
          setSort(next)
        }}
        rows={rows}
        rowKey={(r) => r.batchId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Chưa có kỳ payout nào. Kỳ đầu tiên được hệ thống tự chốt vào đầu tháng sau."
        pagination={list.data ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
