import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { CircleAlert, Loader, Plus } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import type { OrderPage } from '../booking/types'
import { dayText, shiftLabel, statusLabel, tierLabel } from '../booking/view'
import { ORDERS_PAGE_SIZE, ordersApi } from './api'
import { cancelReasonText, pageCount, STATUS_TABS } from './view'
import { statusFromSearch } from '../shell/nav'

const pill: Record<string, string> = {
  PENDING_PAYMENT: 'bg-amber-100 text-amber-900', PAID: 'bg-emerald-100 text-emerald-900', DISPATCHING: 'bg-amber-100 text-amber-900',
  ASSIGNED: 'bg-emerald-100 text-emerald-900', COMPLETED: 'bg-stone-200 text-stone-800', CANCELLED: 'bg-red-100 text-red-800',
}

/** Where a row goes: the payment page while it waits for payment, the order detail otherwise. */
function actionOf(orderId: number, orderStatus: string): { to: string; label: string } {
  return orderStatus === 'PENDING_PAYMENT'
    ? { to: `/app/payments/${orderId}`, label: 'Thanh toán' }
    : { to: `/app/orders/${orderId}`, label: 'Chi tiết' }
}

/** The job's hours (D34) or the shift name. */
function timeOf(o: { startTime?: string | null; endTime?: string | null; shiftCode: string }): string {
  return o.startTime && o.endTime ? `${o.startTime} → ${o.endTime}` : shiftLabel(o.shiftCode)
}

export function OrdersPage() {
  // The filter lives in the URL (`?status=`), so the navbar dropdown and the footer open a filter directly (WEB-CUST-UI-01).
  const [params, setParams] = useSearchParams()
  const status = statusFromSearch(params)
  const [paging, setPaging] = useState<{ status: string | null; page: number }>({ status, page: 1 })
  const page = paging.status === status ? paging.page : 1
  const setPage = (next: number) => setPaging({ status, page: next })
  const setStatus = (next: string | null) => {
    setPaging({ status: next, page: 1 })
    setParams(next ? { status: next } : {})
  }
  const [data, setData] = useState<OrderPage | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [reasons, setReasons] = useState<{ page: number; status: string | null; values: Record<number, string> } | null>(null)

  useEffect(() => {
    let cancelled = false
    setData(null)
    setError(null)
    ordersApi.list(page, status)
      .then((d) => {
        if (cancelled) return
        setData(d)
        const cancelledOrders = d.items.filter((order) => order.orderStatus === 'CANCELLED').slice(0, 10)
        void Promise.all(cancelledOrders.map((order) => ordersApi.get(order.orderId)
          .then((detail) => [order.orderId, cancelReasonText(detail)] as const)
          .catch(() => null)))
          .then((results) => {
            if (!cancelled) setReasons({ page, status, values: Object.fromEntries(results.filter((result) => result !== null)) })
          })
      })
      .catch((e: unknown) => !cancelled && setError(e instanceof ApiError ? e.message : 'Không tải được danh sách đơn.'))
    return () => {
      cancelled = true
    }
  }, [page, status])

  const pages = data ? pageCount(data.total, ORDERS_PAGE_SIZE) : 1
  const visibleReasons = reasons?.page === page && reasons.status === status ? reasons.values : {}
  return <div>
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div><p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Đơn</p><h1 className="mt-1 text-3xl font-semibold">Đơn của tôi</h1></div>
      <div className="flex gap-3"><Link to="/app/disputes" className="inline-flex h-10 items-center rounded-lg border border-stone-300 bg-white px-4 font-semibold">Khiếu nại của tôi</Link>
        <Link to="/app/booking" className="inline-flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white"><Plus size={16} />Đặt ca mới</Link></div>
    </div>
    <div className="mt-6 flex flex-wrap gap-2" role="tablist" aria-label="Lọc theo trạng thái">
      {STATUS_TABS.map((t) => <button key={t.label} type="button" role="tab" aria-selected={status === t.status} onClick={() => setStatus(t.status)}
        className={`h-9 rounded-full border px-4 text-sm font-medium ${status === t.status ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-300 bg-white'}`}>{t.label}</button>)}
    </div>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    {!data && !error && <p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>}
    {data && data.items.length === 0 && <p className="mt-6 rounded-xl border border-stone-200 bg-white p-8 text-center text-stone-500">Chưa có đơn nào{status ? ' ở trạng thái này' : ''}.</p>}
    {data && data.items.length > 0 && <>
      {/* Phones: one card per order (WEB-ORDERS-02); a 720 px table pushed the amount, status and link off screen. */}
      <ul className="mt-6 space-y-3 md:hidden" aria-label="Danh sách đơn">
        {data.items.map((o) => {
          const action = actionOf(o.orderId, o.orderStatus)
          return <li key={o.orderId}>
            <Link to={action.to} className="block rounded-xl border border-stone-200 bg-white p-4 active:bg-stone-50">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-sm text-stone-500">{dayText(o.scheduledDate)} · {timeOf(o)}</p>
                  <p className="mt-1 font-semibold">{tierLabel(o.serviceTier)} · {o.requiredWorkers} thợ</p>
                </div>
                <span className={`shrink-0 rounded-full px-3 py-1 text-xs font-semibold ${pill[o.orderStatus] ?? 'bg-stone-200'}`}>{statusLabel(o.orderStatus)}</span>
              </div>
              {o.orderStatus === 'CANCELLED' && visibleReasons[o.orderId] && <p className="mt-2 line-clamp-2 text-xs text-stone-500">{visibleReasons[o.orderId]}</p>}
              <div className="mt-3 flex items-end justify-between gap-3 border-t border-stone-100 pt-3">
                <div className="min-w-0"><p className="truncate text-xs text-stone-500">{o.orderCode}</p><p className="whitespace-nowrap text-lg font-semibold tabular-nums">{formatVnd(o.totalAmount)}</p></div>
                <span className="whitespace-nowrap text-sm font-semibold text-[#1a2f25]">{action.label} →</span>
              </div>
            </Link>
          </li>
        })}
      </ul>
      <div className="mt-6 hidden overflow-x-auto rounded-xl border border-stone-200 bg-white md:block">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-stone-200 text-xs uppercase text-stone-500"><tr>
            <th className="px-5 py-3">Mã đơn</th><th className="px-5 py-3">Thời gian</th><th className="px-5 py-3">Gói</th><th className="px-5 py-3">Tổng</th><th className="px-5 py-3">Trạng thái</th><th className="px-5 py-3"><span className="sr-only">Chi tiết</span></th></tr></thead>
          <tbody>{data.items.map((o) => {
            const action = actionOf(o.orderId, o.orderStatus)
            return <tr key={o.orderId} className="border-b border-stone-100 last:border-0">
              <td className="whitespace-nowrap px-5 py-4 font-semibold">{o.orderCode}</td>
              <td className="px-5 py-4">{dayText(o.scheduledDate)} · {timeOf(o)}</td>
              <td className="px-5 py-4">{tierLabel(o.serviceTier)} · {o.requiredWorkers} thợ</td>
              <td className="whitespace-nowrap px-5 py-4 tabular-nums">{formatVnd(o.totalAmount)}</td>
              <td className="max-w-xs px-5 py-4"><span className={`whitespace-nowrap rounded-full px-3 py-1 text-xs font-semibold ${pill[o.orderStatus] ?? 'bg-stone-200'}`}>{statusLabel(o.orderStatus)}</span>
                {o.orderStatus === 'CANCELLED' && visibleReasons[o.orderId] && <p className="mt-2 line-clamp-2 text-xs text-stone-500" title={visibleReasons[o.orderId]}>{visibleReasons[o.orderId]}</p>}</td>
              <td className="whitespace-nowrap px-5 py-4 text-right"><Link to={action.to} className="font-semibold text-[#1a2f25] underline">{action.label}</Link></td>
            </tr>
          })}</tbody>
        </table>
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm text-stone-500">
        <span>Hiển thị {data.items.length} / {data.total} đơn · {ORDERS_PAGE_SIZE} đơn mỗi trang</span>
        <span className="flex items-center gap-2">
          <button type="button" disabled={page <= 1} onClick={() => setPage(page - 1)} className="h-8 rounded-lg border border-stone-300 bg-white px-3 disabled:opacity-40">Trước</button>
          <span className="whitespace-nowrap">Trang {page} / {pages}</span>
          <button type="button" disabled={page >= pages} onClick={() => setPage(page + 1)} className="h-8 rounded-lg border border-stone-300 bg-white px-3 disabled:opacity-40">Sau</button>
        </span>
      </div>
    </>}
  </div>
}
