import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { CalendarClock, CircleAlert, Loader, Plus } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import { bookingApi } from '../booking/api'
import type { CustomerAddress, OrderSummary, PriceQuote, ServiceTier } from '../booking/types'
import { dayText, nextOrder, shiftLabel, statusLabel, tierLabel, vnDate, workersPrice } from '../booking/view'

const card = 'rounded-xl border border-stone-200 bg-white p-6'

export function HomePage() {
  const [orders, setOrders] = useState<OrderSummary[] | null>(null)
  const [address, setAddress] = useState<CustomerAddress | null>(null)
  const [quotes, setQuotes] = useState<Partial<Record<ServiceTier, PriceQuote>>>({})
  const [error, setError] = useState<string | null>(null)
  const [today] = useState(() => vnDate(new Date()))

  useEffect(() => {
    let cancelled = false
    bookingApi.orders().then((page) => !cancelled && setOrders(page.items)).catch((e: unknown) => !cancelled && setError(e instanceof ApiError ? e.message : 'Không tải được danh sách đơn.'))
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
  }, [])

  const next = orders ? nextOrder(orders, today) : null
  const sample = quotes.ECONOMY ?? quotes.PREMIUM

  return <div>
    <div className="flex flex-wrap items-end justify-between gap-4">
      <div><h1 className="text-3xl font-semibold">Xin chào</h1><p className="mt-2 text-stone-500">Ca sắp tới và đặt nhanh trong một màn hình.</p></div>
      <Link to="/app/booking" className="inline-flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white"><Plus size={16} />Đặt ca mới</Link>
    </div>
    {error && <p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>}
    <div className="mt-8 grid gap-6 lg:grid-cols-[1.5fr_1fr]">
      <section className="rounded-xl bg-[#1a2f25] p-6 text-white" aria-label="Ca sắp tới">
        {orders === null && !error && <p className="flex items-center gap-2 text-white/70"><Loader className="animate-spin" size={18} />Đang tải…</p>}
        {orders !== null && !next && <div><CalendarClock size={28} className="text-white/70" /><h2 className="mt-3 text-xl font-semibold">Chưa có ca sắp tới</h2><p className="mt-1 text-sm text-white/70">Đặt ca đầu tiên chỉ mất vài phút.</p></div>}
        {next && <div>
          <div className="flex flex-wrap justify-between gap-2"><span className="rounded-full bg-white/15 px-3 py-1 text-sm">Ca sắp tới · {tierLabel(next.serviceTier)}</span><span className="rounded-full bg-emerald-100 px-3 py-1 text-sm font-semibold text-[#1a2f25]">{statusLabel(next.orderStatus)}</span></div>
          <p className="mt-6 text-sm text-white/70">{dayText(next.scheduledDate)}</p>
          <p className="text-3xl font-bold">{shiftLabel(next.shiftCode)}</p>
          <p className="mt-2 text-sm text-white/70">{next.requiredWorkers} thợ · {formatVnd(next.totalAmount)} · Mã {next.orderCode}</p>
          <Link to={next.orderStatus === 'PENDING_PAYMENT' ? `/app/payments/${next.orderId}` : '/app/orders'} className="mt-6 inline-flex h-10 items-center rounded-lg bg-white px-4 font-semibold text-[#1a2f25]">
            {next.orderStatus === 'PENDING_PAYMENT' ? 'Thanh toán ngay' : 'Xem đơn'}</Link>
        </div>}
      </section>
      <section className={card} aria-label="Đặt nhanh">
        <h2 className="text-lg font-semibold">Đặt nhanh</h2>
        {!address && <p className="mt-3 text-sm text-stone-500">Thêm địa chỉ trong ứng dụng TỔ ẤM để xem giá theo diện tích.</p>}
        {address && sample && <p className="mt-1 text-sm text-stone-500">{address.totalAreaM2} m² · {sample.requiredWorkers} thợ · 4 giờ</p>}
        {address && <ul className="mt-4 space-y-3">
          {(['ECONOMY', 'PREMIUM'] as ServiceTier[]).map((t) => <li key={t} className="flex items-center justify-between rounded-xl border border-stone-200 p-4">
            <div><b>{tierLabel(t)}</b><div className="text-sm text-stone-500">mỗi thợ / ca</div></div>
            <b>{quotes[t] ? workersPrice(1, quotes[t].unitPrice) : '—'}</b></li>)}
        </ul>}
        <Link to="/app/booking" className="mt-4 flex h-10 items-center justify-center rounded-lg bg-[#1a2f25] font-semibold text-white">Chọn ngày và giờ</Link>
      </section>
    </div>
  </div>
}
