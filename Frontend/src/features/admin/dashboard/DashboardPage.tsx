import { Link } from 'react-router-dom'
import { DataTable, type Column } from '@/components/data-table'
import { StatCard } from '@/components/status'
import { Button } from '@/components/ui/button'
import { useResource } from '@/hooks/use-resource'
import { dashboardApi } from './api'
import { dashboardCards, nearSlaRows, type NearSlaRow } from './view'

const PLACEHOLDER_CARDS = [
  { title: 'Đơn', figures: [{ value: null, label: 'Hôm nay' }, { value: null, label: 'Tuần này' }] },
  { title: 'Ca làm', figures: [{ value: null, label: 'Đang làm' }, { value: null, label: 'Hoàn tất hôm nay' }] },
  { title: 'Tranh chấp tồn', figures: [{ value: null, label: 'Chưa xử lý' }, { value: null, label: 'Sắp quá SLA' }] },
]

const COLUMNS: Column<NearSlaRow>[] = [
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
    id: 'sla',
    header: 'SLA còn lại',
    cell: (r) => <span className="font-semibold text-destructive tabular-nums">{r.slaText}</span>,
  },
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

/** WEB-M6-04: operations dashboard (Figma 182:2). Three numbers groups and the disputes close to their SLA; no other metric (G-7). */
export function DashboardPage() {
  const dashboard = useResource(() => dashboardApi.get(), [])
  const nearSla = useResource(() => dashboardApi.nearSla(), [])
  const cards = dashboard.data ? dashboardCards(dashboard.data) : PLACEHOLDER_CARDS
  const rows = nearSla.data ? nearSlaRows(nearSla.data.items) : []

  return (
    <div className="flex flex-col gap-8">
      <header>
        <div className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Vận hành</div>
        <h1 className="text-2xl font-semibold">Tổng quan vận hành</h1>
      </header>

      <div className="flex flex-col gap-3">
        {dashboard.error && (
          <div role="alert" className="flex items-center justify-between rounded-lg border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm">
            <span>Không tải được số liệu: {dashboard.error}</span>
            <Button variant="outline" size="sm" onClick={dashboard.reload}>
              Thử lại
            </Button>
          </div>
        )}
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-3">
          {cards.map((c) => (
            <StatCard key={c.title} title={c.title} figures={c.figures} loading={dashboard.loading && !dashboard.data} />
          ))}
        </div>
      </div>

      <section className="flex flex-col gap-3">
        <div className="flex items-end justify-between">
          <div>
            <h2 className="text-lg font-semibold">Tranh chấp sắp quá hạn</h2>
            <p className="text-sm text-muted-foreground">Còn dưới 6 giờ trước hạn SLA 24–48 giờ</p>
          </div>
          <Button variant="outline" render={<Link to="/admin/disputes" />}>
            Xem tất cả khiếu nại →
          </Button>
        </div>
        <DataTable
          columns={COLUMNS}
          rows={rows}
          rowKey={(r) => r.disputeId}
          loading={nearSla.loading && !nearSla.data}
          error={nearSla.error}
          onRetry={nearSla.reload}
          emptyMessage="Không có tranh chấp sắp quá hạn"
        />
      </section>
    </div>
  )
}
