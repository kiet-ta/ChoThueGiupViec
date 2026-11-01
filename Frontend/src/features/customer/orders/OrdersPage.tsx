import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CircleAlert, Loader, Plus } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import type { OrderPage } from '../booking/types'
import { dayText, shiftLabel, statusLabel, tierLabel } from '../booking/view'
import { ORDERS_PAGE_SIZE, ordersApi } from './api'
import { pageCount, STATUS_TABS } from './view'

const pill: Record<string, string> = {
  PENDING_PAYMENT: 'bg-amber-100 text-amber-900', PAID: 'bg-emerald-100 text-emerald-900', DISPATCHING: 'bg-amber-100 text-amber-900',
  ASSIGNED: 'bg-emerald-100 text-emerald-900', COMPLETED: 'bg-stone-200 text-stone-800', CANCELLED: 'bg-red-100 text-red-800',
}

export function OrdersPage() {
  const [status, setStatus] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [data, setData] = useState<OrderPage | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setData(null)
    setError(null)
    ordersApi.list(page, status)
      .then((d) => !cancelled && setData(d))
      .catch((e: unknown) => !cancelled && setError(e instanceof ApiError ? e.message : 'Không tải được danh sách đơn.'))
    return () => {
      cancelled = true
    }
  }, [page, status])

  const pages = data ? pageCount(data.total, ORDERS_PAGE_SIZE) : 1
  return <div>
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div><p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Đơn</p><h1 className="mt-1 text-3xl font-semibold">Đơn của tôi</h1></div>
      <div className="flex gap-3"><Link to="/app/disputes" className="inline-flex h-10 items-center rounded-lg border border-stone-300 bg-white px-4 font-semibold">Khiếu nại của tôi</Link>
        <Link to="/app/booking" className="inline-flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white"><Plus size={16} />Đặt ca mới</Link></div>
    </div>
    <div className="mt-6 flex flex-wrap gap-2" role="tablist" aria-label="Lọc theo trạng thái">
      {STATUS_TABS.map((t) => <button key={t.label} type="button" role="tab" aria-selected={status === t.status} onClick={() => { setStatus(t.status); setPage(1) }}
        className={`h-9 rounded-full border px-4 text-sm font-medium ${status === t.status ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-300 bg-white'}`}>{t.label}</button>)}
    </div>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    {!data && !error && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {data && data.items.length === 0 && <p className="mt-6 rounded-xl border border-stone-200 bg-white p-8 text-center text-stone-500">Chưa có đơn nào{status ? ' ở trạng thái này' : ''}.</p>}
    {data && data.items.length > 0 && <div className="mt-6 overflow-x-auto rounded-xl border border-stone-200 bg-white">
      <table className="w-full min-w-[720px] text-left text-sm">
        <thead className="border-b border-stone-200 text-xs uppercase text-stone-500"><tr>
          <th className="px-5 py-3">Mã đơn</th><th className="px-5 py-3">Thời gian</th><th className="px-5 py-3">Gói</th><th className="px-5 py-3">Tổng</th><th className="px-5 py-3">Trạng thái</th><th className="px-5 py-3"><span className="sr-only">Chi tiết</span></th></tr></thead>
        <tbody>{data.items.map((o) => <tr key={o.orderId} className="border-b border-stone-100 last:border-0">
          <td className="px-5 py-4 font-semibold">{o.orderCode}</td>
          <td className="px-5 py-4">{dayText(o.scheduledDate)} · {shiftLabel(o.shiftCode)}</td>
          <td className="px-5 py-4">{tierLabel(o.serviceTier)} · {o.requiredWorkers} thợ</td>
          <td className="px-5 py-4 tabular-nums">{formatVnd(o.totalAmount)}</td>
          <td className="px-5 py-4"><span className={`rounded-full px-3 py-1 text-xs font-semibold ${pill[o.orderStatus] ?? 'bg-stone-200'}`}>{statusLabel(o.orderStatus)}</span></td>
          <td className="px-5 py-4 text-right"><Link to={o.orderStatus === 'PENDING_PAYMENT' ? `/app/payments/${o.orderId}` : `/app/orders/${o.orderId}`} className="font-semibold text-[#1a2f25] underline">{o.orderStatus === 'PENDING_PAYMENT' ? 'Thanh toán' : 'Chi tiết'}</Link></td>
        </tr>)}</tbody>
      </table>
      <div className="flex items-center justify-between border-t border-stone-200 px-5 py-3 text-sm text-stone-500">
        <span>Hiển thị {data.items.length} / {data.total} đơn · {ORDERS_PAGE_SIZE} đơn mỗi trang</span>
        <span className="flex items-center gap-2">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="h-8 rounded-lg border border-stone-300 px-3 disabled:opacity-40">Trước</button>
          <span>Trang {page} / {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="h-8 rounded-lg border border-stone-300 px-3 disabled:opacity-40">Sau</button>
        </span>
      </div>
    </div>}
  </div>
}
