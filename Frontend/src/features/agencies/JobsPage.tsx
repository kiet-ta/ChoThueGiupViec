import { useState } from 'react'
import { DataTable, type Column } from '@/components/data-table'
import { useResource } from '@/hooks/use-resource'
import { formatVnd } from '../../lib/format.ts'
import { agenciesApi, PAGE_SIZE } from './api'
import { Alert, Metric, PageHeader, Panel } from './shared'
import type { WorkerPerformance } from './types'
import { jobRow, jobStatusText, percent } from './view'

type Row = ReturnType<typeof jobRow>

const STATUSES = ['ASSIGNED', 'CHECKED_IN', 'IN_PROGRESS', 'AWAITING_ACCEPTANCE', 'COMPLETED', 'CANCELLED_BY_WORKER', 'ABSENT', 'INCIDENT']

/** WEB-M5-05: the company's jobs and the Pro performance report (contract agencies.md 8, decisions Q08, D14). */
export function JobsPage() {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState('')
  const jobs = useResource(() => agenciesApi.jobs(page, status || undefined), [page, status])
  const report = useResource(() => agenciesApi.performance(), [])

  const columns: Column<Row>[] = [
    { id: 'id', header: 'Ca', cell: (r) => `#${r.id}` },
    { id: 'order', header: 'Đơn', cell: (r) => `#${r.orderId}` },
    { id: 'worker', header: 'Thợ', cell: (r) => r.worker },
    { id: 'status', header: 'Trạng thái', cell: (r) => r.statusText },
    { id: 'gross', header: 'Giá ca', align: 'right', cell: (r) => <span className="tabular-nums">{r.gross}</span> },
    { id: 'payout', header: 'Thực nhận', align: 'right', cell: (r) => <span className="tabular-nums">{r.payout}</span> },
    { id: 'created', header: 'Tạo lúc', cell: (r) => r.created },
  ]

  const perfColumns: Column<WorkerPerformance>[] = [
    { id: 'worker', header: 'Thợ', cell: (w) => w.workerName || `#${w.workerId}` },
    { id: 'assigned', header: 'Nhận', align: 'right', cell: (w) => w.assigned },
    { id: 'completed', header: 'Hoàn thành', align: 'right', cell: (w) => w.completed },
    { id: 'rate', header: 'Tỷ lệ', align: 'right', cell: (w) => percent(w.completionRate) },
    { id: 'cancelled', header: 'Huỷ/chuyển', align: 'right', cell: (w) => w.cancelled },
    { id: 'issues', header: 'Vắng/sự cố', align: 'right', cell: (w) => w.absent + w.incident },
    { id: 'payout', header: 'Thực nhận', align: 'right', cell: (w) => <span className="tabular-nums">{formatVnd(w.payoutTotal)}</span> },
  ]

  return (
    <div className="flex flex-col gap-6">
      <PageHeader eyebrow="Đối tác · Đơn hàng" title="Đơn của công ty và hiệu suất nhân viên">
        Báo cáo hiệu suất là tính năng của gói Pro (30 ngày gần nhất).
      </PageHeader>

      <Panel title="Hiệu suất nhân viên (30 ngày)">
        {report.error && <Alert>{report.error}</Alert>}
        {report.data && (
          <>
            <div className="mb-3 grid gap-4 md:grid-cols-2">
              <Metric label="Tổng số ca" value={String(report.data.totalJobs)} />
              <Metric label="Thực nhận (ca hoàn thành)" value={formatVnd(report.data.payoutTotal)} />
            </div>
            <DataTable columns={perfColumns} rows={report.data.workers} rowKey={(w) => w.workerId} emptyMessage="Chưa có ca nào trong kỳ." />
          </>
        )}
      </Panel>

      <DataTable
        columns={columns}
        rows={jobs.data ? jobs.data.items.map(jobRow) : []}
        rowKey={(r) => r.id}
        loading={jobs.loading && !jobs.data}
        error={jobs.error}
        onRetry={jobs.reload}
        emptyMessage="Chưa có ca nào."
        toolbar={
          <label className="flex items-center gap-2 text-sm">
            Trạng thái
            <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }} className="rounded-lg border border-border bg-background p-2">
              <option value="">Tất cả</option>
              {STATUSES.map((s) => (
                <option key={s} value={s}>{jobStatusText(s)}</option>
              ))}
            </select>
          </label>
        }
        pagination={jobs.data ? { page: jobs.data.page, pageSize: PAGE_SIZE, total: jobs.data.total, onPageChange: setPage } : undefined}
      />
    </div>
  )
}
