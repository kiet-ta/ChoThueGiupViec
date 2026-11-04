import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CalendarClock, CircleAlert, Loader, Plus } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import { accountApi } from '../account/api'
import type { FavoriteWorker } from '../account/types'
import { bookingApi } from '../booking/api'
import type { CustomerAddress, OrderSummary, PriceQuote, ServiceTier, ShiftOption } from '../booking/types'
import { dayText, firstName, nextOrder, shiftLabel, shiftRange, statusLabel, tierLabel } from '../booking/view'
import { monthStats, topFavorites } from './view'
import { homeApi } from './api'

const card = 'rounded-xl border border-stone-200 bg-white p-6'

export function HomePage() {
  const [name, setName] = useState('')
  const [orders, setOrders] = useState<OrderSummary[] | null>(null)
  const [address, setAddress] = useState<CustomerAddress | null>(null)
  const [quotes, setQuotes] = useState<Partial<Record<ServiceTier, PriceQuote>>>({})
  const [error, setError] = useState<string | null>(null)
  const [now] = useState(() => new Date())
  const [shifts, setShifts] = useState<ShiftOption[]>([])
  const [stats, setStats] = useState<{ count: number; hours: number; total: number } | null>(null)
  const [statsLoading, setStatsLoading] = useState(true)
  const [favorites, setFavorites] = useState<FavoriteWorker[] | null>(null)
  const [favoritesError, setFavoritesError] = useState(false)

  useEffect(() => {
    let cancelled = false
    accountApi.profile().then((profile) => { if (!cancelled) setName(firstName(profile.fullName)) }).catch(() => undefined)
    accountApi.favorites().then((list) => { if (!cancelled) setFavorites(list) }).catch(() => { if (!cancelled) setFavoritesError(true) })
    bookingApi.orders().then((page) => !cancelled && setOrders(page.items)).catch((e: unknown) => !cancelled && setError(e instanceof ApiError ? e.message : 'Không tải được danh sách đơn.'))
    const options = bookingApi.options()
    options.then((o) => !cancelled && setShifts(o.shifts)).catch(() => undefined)
    Promise.all([homeApi.completedOrders(), options])
      .then(([page, opts]) => { if (!cancelled) setStats({ ...monthStats(page.items, opts.shifts, now), total: page.total }) })
      .catch(() => undefined)
      .finally(() => { if (!cancelled) setStatsLoading(false) })
    bookingApi.addresses().then((list) => {
      const chosen = list.find((a) => a.isDefault) ?? list[0] ?? null
      if (cancelled || !chosen) return
      setAddress(chosen)
      for (const t of ['ECONOMY', 'PREMIUM'] as ServiceTier[]) {
        bookingApi.quote(chosen.addressId, t).then((q) => !cancelled && setQuotes((prev) => ({ ...prev, [t]: q }))).catch(() => undefined)
      }
    }).catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [now])

  const next = orders ? nextOrder(orders, now, shifts) : null
  const nextShift = next ? shifts.find((s) => s.shiftCode === next.shiftCode) : undefined
  const sample = quotes.ECONOMY ?? quotes.PREMIUM

  return <div>
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div><h1 className="text-3xl font-semibold">Xin chào{name && ` ${name}`}</h1><p className="mt-2 text-stone-500">Ca sắp tới và đặt nhanh trong một màn hình.</p></div>
      <Link to="/app/booking" className="inline-flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white"><Plus size={16} />Đặt ca mới</Link>
    </div>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    <div className="mt-8 grid gap-3 sm:grid-cols-3" aria-label="Thống kê của bạn">
      {[
        { label: 'Lần dọn tháng này', value: stats?.count.toString() },
        { label: 'Giờ bạn tiết kiệm tháng này', value: stats ? `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 1 }).format(stats.hours)} giờ` : undefined, hint: 'Chưa tính giờ làm thêm' },
        { label: 'Tổng số lần dọn', value: stats?.total.toString() },
      ].map((item) => <section key={item.label} className={card}><h2 className="text-sm text-stone-500">{item.label}</h2>{statsLoading ? <div className="mt-3 h-8 w-20 animate-pulse rounded bg-stone-200" aria-label="Đang tải" /> : <p className="mt-2 text-2xl font-bold">{item.value ?? '—'}</p>}{item.hint && <p className="mt-1 text-xs text-stone-500">{item.hint}</p>}</section>)}
    </div>
    <div className="mt-8 grid gap-6 lg:grid-cols-[1.5fr_1fr]">
      <section className="rounded-xl bg-[#1a2f25] p-6 text-white" aria-label="Ca sắp tới">
        {orders === null && !error && <p className="flex items-center gap-2 text-white/70"><Loader className="animate-spin" size={18} />Đang tải…</p>}
        {orders !== null && !next && <div><CalendarClock size={28} className="text-white/70" /><h2 className="mt-3 text-xl font-semibold">Chưa có ca sắp tới</h2><p className="mt-1 text-sm text-white/70">Đặt ca đầu tiên chỉ mất vài phút.</p></div>}
        {next && <div>
          <div className="flex flex-wrap justify-between gap-2"><span className="rounded-full bg-white/15 px-3 py-1 text-sm">Ca sắp tới · {tierLabel(next.serviceTier)}</span><span className="rounded-full bg-emerald-100 px-3 py-1 text-sm font-semibold text-[#1a2f25]">{statusLabel(next.orderStatus)}</span></div>
          <p className="mt-6 text-sm text-white/70">{dayText(next.scheduledDate)} · {shiftLabel(next.shiftCode)}</p>
          <p className="text-3xl font-bold tabular-nums">{nextShift ? shiftRange(nextShift) : shiftLabel(next.shiftCode)}</p>
          <p className="mt-2 text-sm text-white/70">{next.requiredWorkers} thợ · {formatVnd(next.totalAmount)} · Mã {next.orderCode}</p>
          <Link to={next.orderStatus === 'PENDING_PAYMENT' ? `/app/payments/${next.orderId}` : `/app/orders/${next.orderId}`} className="mt-6 inline-flex h-10 items-center rounded-lg bg-white px-4 font-semibold text-[#1a2f25]">
            {next.orderStatus === 'PENDING_PAYMENT' ? 'Thanh toán ngay' : 'Xem đơn'}</Link>
        </div>}
      </section>
      <section className={card} aria-label="Đặt nhanh">
        <h2 className="text-lg font-semibold">Đặt nhanh</h2>
        {!address && <p className="mt-3 text-sm text-stone-500">Thêm địa chỉ trong ứng dụng TỔ ẤM để xem giá theo diện tích.</p>}
        {address && sample && <p className="mt-1 text-sm text-stone-500">{address.totalAreaM2} m² · {sample.requiredWorkers} thợ · 4 giờ</p>}
        {address && <ul className="mt-4 space-y-3">
          {(['ECONOMY', 'PREMIUM'] as ServiceTier[]).map((t) => <li key={t}><Link to={`/app/booking?tier=${t}`} className="flex items-center justify-between rounded-xl border border-stone-200 p-4">
            <span><b>{tierLabel(t)}</b><span className="block text-sm text-stone-500">mỗi thợ / ca</span></span>
            <b>{quotes[t] ? formatVnd(quotes[t].unitPrice) : '—'}</b></Link></li>)}
        </ul>}
        <Link to="/app/booking" className="mt-4 flex h-10 items-center justify-center rounded-lg bg-[#1a2f25] font-semibold text-white">Chọn ngày và giờ</Link>
      </section>
    </div>
    <section className={`${card} mt-6`} aria-label="Thợ yêu thích">
      <div className="flex flex-wrap items-center justify-between gap-3"><h2 className="text-lg font-semibold">Thợ yêu thích</h2>{favorites && favorites.length > 3 && <Link to="/app/favorites" className="text-sm font-semibold text-emerald-900">Xem tất cả</Link>}</div>
      {favoritesError ? <p className="mt-4 text-sm text-stone-500">Không tải được danh sách thợ yêu thích.</p> : favorites === null ? <p className="mt-4 text-sm text-stone-500">Đang tải…</p> : favorites.length === 0 ? <p className="mt-4 text-sm text-stone-500">Bạn chưa có thợ yêu thích. <Link to="/app/favorites" className="font-semibold text-emerald-900 underline">Xem thợ yêu thích</Link></p> : <ul className="mt-4 divide-y divide-stone-200">{topFavorites(favorites, 3).map((worker) => <li key={worker.workerId} className="flex flex-wrap items-center justify-between gap-3 py-3"><div><p className="font-semibold">{worker.fullName}</p><p className="text-sm text-stone-500">{worker.ratingAvg.toFixed(1)} ★ · {worker.completedJobs} ca hoàn thành</p></div><Link to={`/app/booking?tier=ECONOMY&worker=${worker.workerId}`} className="rounded-lg border border-stone-300 px-4 py-2 text-sm font-semibold text-emerald-900">Đặt lại</Link></li>)}</ul>}
    </section>
  </div>
}
