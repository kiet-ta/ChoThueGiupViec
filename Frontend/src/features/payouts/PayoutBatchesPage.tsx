import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { DataTable, type Column } from '@/components/data-table'
import { StatusBadge } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { ApiError } from '@/services/api'
import { PAGE_SIZE, payoutsApi } from './api'
import { batchRow, buildError, finishedMonths, type BatchRow } from './view'

const COLUMNS: Column<BatchRow>[] = [
  { id: 'month', header: 'Kỳ', cell: (r) => <span className="font-semibold">{r.month}</span> },
  { id: 'status', header: 'Trạng thái', cell: (r) => <StatusBadge tone={r.status.tone}>{r.status.label}</StatusBadge> },
  { id: 'items', header: 'Người nhận', align: 'right', cell: (r) => r.items },
  { id: 'total', header: 'Tổng thực nhận', align: 'right', cell: (r) => <span className="tabular-nums">{r.total}</span> },
  { id: 'confirmed', header: 'Giải ngân lúc', cell: (r) => r.confirmedAt },
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

/** WEB-M6-03 list: the monthly payout batches and the control that builds one (contract payouts.md 2.1-2.2). */
export function PayoutBatchesPage() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [months] = useState(() => finishedMonths(new Date())) // fixed for the life of the page: the finished months do not change while it is open
  const [month, setMonth] = useState(months[0].value)
  const [building, setBuilding] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const inFlight = useRef(false) // a second click while a build runs must not start another one

  const list = useResource(() => payoutsApi.list(page), [page])
  const rows = list.data ? list.data.items.map(batchRow) : []

  async function build() {
    if (inFlight.current) return
    inFlight.current = true
    setBuilding(true)
    setMessage(null)
    try {
      const batch = await payoutsApi.build(month)
      navigate(`/admin/payout-batches/${batch.batchId}`)
    } catch (e) {
      setMessage(e instanceof ApiError ? buildError(e) : 'Đã xảy ra lỗi.')
      list.reload()
    } finally {
      inFlight.current = false
      setBuilding(false)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Payout · Ngày cuối tháng</div>
        <h1 className="text-2xl font-semibold">Kỳ payout hàng tháng</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Tổng hợp thu nhập thợ và Agency của một tháng đã kết thúc, xuất file chuyển khoản, rồi xác nhận giải ngân. Hệ thống không tự chuyển tiền.
        </p>
      </header>

      <section aria-label="Dựng kỳ" className="flex flex-wrap items-end gap-3 rounded-xl border border-border bg-card p-4">
        <div className="flex flex-col gap-1">
          <label htmlFor="period" className="text-sm font-medium">
            Tháng cần dựng
          </label>
          <select id="period" value={month} onChange={(e) => setMonth(e.target.value)} className="h-9 rounded-lg border border-input bg-background px-3 text-sm">
            {months.map((m) => (
              <option key={m.value} value={m.value}>
                {m.label}
              </option>
            ))}
          </select>
        </div>
        <Button onClick={() => void build()} disabled={building}>
          {building ? 'Đang dựng…' : 'Dựng kỳ'}
        </Button>
        <p className="basis-full text-xs text-muted-foreground">
          Chỉ chọn được tháng đã kết thúc. Dựng lại một kỳ nháp sẽ tính lại từ dữ liệu hiện tại; kỳ đã giải ngân không đổi nữa.
        </p>
        {message && (
          <div role="alert" className="basis-full rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm">
            {message}
          </div>
        )}
      </section>

      <DataTable
        columns={COLUMNS}
        rows={rows}
        rowKey={(r) => r.batchId}
        loading={list.loading && !list.data}
        error={list.error}
        onRetry={list.reload}
        emptyMessage="Chưa có kỳ payout nào. Chọn một tháng đã kết thúc và bấm Dựng kỳ."
        pagination={list.data ? { page: list.data.page, pageSize: PAGE_SIZE, total: list.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
