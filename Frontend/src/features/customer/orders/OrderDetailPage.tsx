import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { Check, ChevronRight, CircleAlert, Circle, Clock, Flag, Loader, MapPin, MoreHorizontal, Star, X } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import type { Order } from '../booking/types'
import { dayText, shiftLabel, statusLabel, tierLabel } from '../booking/view'
import { ordersApi } from './api'
import type { AddressDetail, OrderProgress } from './types'
import { assignmentLabel, cancelReasonError, CANCEL_REASON_MAX, extensionLabel, hoursText, orderActions, orderProblem, POLL_PROGRESS_MS, timeline, vnTime } from './view'

const card = 'rounded-xl border border-stone-200 bg-white p-6'
const dot = { done: <Check size={14} />, current: <Loader size={14} className="animate-spin" />, todo: <Circle size={14} />, cancelled: <X size={14} /> }
const dotStyle = { done: 'bg-[#1a2f25] text-white', current: 'bg-amber-100 text-amber-900', todo: 'bg-stone-100 text-stone-400', cancelled: 'bg-red-100 text-red-800' }

export function OrderDetailPage() {
  const orderId = Number(useParams().orderId)
  const [order, setOrder] = useState<Order | null>(null)
  const [progress, setProgress] = useState<OrderProgress | null>(null)
  const [address, setAddress] = useState<AddressDetail | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [now, setNow] = useState(() => new Date())
  const [cancelling, setCancelling] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const [o, p] = await Promise.all([ordersApi.get(orderId), ordersApi.progress(orderId)])
      setOrder(o)
      setProgress(p)
      setNow(new Date())
      setError(null)
    } catch (e) {
      setError(orderProblem(e as ApiError))
    }
  }, [orderId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial fetch
    void load()
  }, [load])

  const live = order !== null && !['COMPLETED', 'CANCELLED'].includes(order.orderStatus)
  useEffect(() => {
    if (!live) return
    const timer = setInterval(() => void load(), POLL_PROGRESS_MS)
    return () => clearInterval(timer)
  }, [live, load])

  const addressId = order?.addressId
  useEffect(() => {
    if (addressId === undefined) return
    ordersApi.address(addressId).then(setAddress).catch(() => undefined)
  }, [addressId])

  if (error && !order) return <div role="alert" className="space-y-4"><p className="flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p><Link to="/app/orders" className="underline">Về danh sách đơn</Link></div>
  if (!order || !progress) return <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>

  const actions = orderActions(order.orderStatus, progress.assignments, progress.extension, now)
  const steps = timeline(order.orderStatus, order.orderStatus === 'CANCELLED' && progress.assignments.length > 0)
  const ext = progress.extension

  async function confirmCancel() {
    const problem = cancelReasonError(reason)
    setReasonError(problem)
    if (problem || busy) return
    setBusy(true)
    try {
      setOrder(await ordersApi.cancel(orderId, reason.trim()))
      setCancelling(false)
      setNotice('Đã hủy đơn. Nếu bạn đã thanh toán, khoản tiền sẽ được hoàn lại theo chính sách.')
      void load()
    } catch (e) {
      setReasonError(orderProblem(e as ApiError))
      void load()
    } finally {
      setBusy(false)
    }
  }

  return <div>
    <Link to="/app/orders" className="text-sm text-stone-500 underline">← Đơn của tôi</Link>
    <div className="mt-3 flex flex-wrap items-center justify-between gap-4">
      <div><p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Đơn</p><h1 className="mt-1 text-3xl font-semibold">{order.orderCode}</h1>
        <p className="mt-2 text-stone-500">{dayText(order.scheduledDate)} · {vnTime(order.shiftStartAt)} → {vnTime(order.shiftEndAt)} · {formatVnd(order.totalAmount)} · {tierLabel(order.serviceTier)}</p></div>
      <div className="flex flex-wrap items-center gap-3">
        <span className="rounded-full bg-stone-200 px-3 py-1 text-sm font-semibold">{statusLabel(order.orderStatus)}</span>
        {actions.canPay && <Link to={`/app/payments/${order.orderId}`} className="inline-flex h-10 items-center rounded-lg bg-[#1a2f25] px-4 font-semibold text-white">Thanh toán</Link>}
        {actions.canExtend && <Link to={`/app/orders/${order.orderId}/extension`} className="inline-flex h-10 items-center gap-2 rounded-lg border border-stone-300 bg-white px-4 font-semibold"><Clock size={16} />Làm lần 2</Link>}
      </div>
    </div>
    {notice && <p role="status" className="mt-4 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-900">{notice}</p>}
    {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    <div className="mt-6 grid gap-6 lg:grid-cols-[1.4fr_1fr]">
      <div className="space-y-4">
        <section className={card}>
          <h2 className="text-lg font-semibold">Tiến trình</h2>
          <ol className="mt-4 space-y-4">{steps.map((s) => <li key={s.key} className="flex items-center gap-3">
            <span className={`grid h-7 w-7 place-items-center rounded-full ${dotStyle[s.state]}`}>{dot[s.state]}</span>
            <span className={s.state === 'todo' ? 'text-stone-400' : 'font-medium'}>{s.label}{s.state === 'cancelled' && ' · Đã hủy'}</span></li>)}</ol>
        </section>
        {ext && <section className={card} aria-label="Làm lần 2">
          <h2 className="flex items-center gap-2 text-lg font-semibold"><Clock size={18} />Làm lần 2</h2>
          <p className="mt-2">Thêm {hoursText(ext.extraHours)} · {formatVnd(ext.extraAmount)} · <b>{extensionLabel(ext.extStatus)}</b></p>
          {ext.extStatus === 'PENDING_PAYMENT' && <Link to={`/app/orders/${order.orderId}/extension`} className="mt-3 inline-flex h-10 items-center rounded-lg bg-[#1a2f25] px-4 font-semibold text-white">Thanh toán phần thêm</Link>}
        </section>}
        {(actions.showAcceptance || actions.showRating || actions.showDispute) && <section className={card}>
          <h2 className="text-lg font-semibold">Sau ca</h2>
          <ul className="mt-2 divide-y divide-stone-100">
            {actions.showAcceptance && <li className="flex items-center gap-3 py-3"><MoreHorizontal size={18} /><span className="grow"><b>Xem ảnh và nghiệm thu</b><span className="block text-sm text-stone-500">So sánh trước/sau 5 góc</span></span><ChevronRight size={18} /></li>}
            {actions.showRating && <li className="flex items-center gap-3 py-3"><Star size={18} /><span className="grow"><b>Đánh giá thợ</b><span className="block text-sm text-stone-500">Trong 48 giờ sau ca</span></span><ChevronRight size={18} /></li>}
            {actions.showDispute && <li className="flex items-center gap-3 py-3"><Flag size={18} /><span className="grow"><b>Khiếu nại</b><span className="block text-sm text-stone-500">Trong 24 giờ sau ca</span></span><ChevronRight size={18} /></li>}
          </ul>
          <p className="mt-2 text-xs text-stone-500">Các màn này sẽ mở được ở bản cập nhật tiếp theo.</p>
        </section>}
      </div>
      <div className="space-y-4">
        <section className={card}>
          <h2 className="text-lg font-semibold">Thợ {progress.assignments.length}/{progress.requiredWorkers}</h2>
          {progress.assignments.length === 0 ? <p className="mt-3 text-sm text-stone-500">Thợ được tìm sau khi bạn thanh toán.</p>
            : <ul className="mt-3 space-y-3">{progress.assignments.map((a) => <li key={a.assignmentId} className="flex items-center gap-3">
              <span className="grid h-10 w-10 place-items-center rounded-full bg-stone-200 text-sm font-semibold">{(a.workerName ?? '?').split(' ').map((w) => w[0]).slice(-2).join('')}</span>
              <span className="grow"><b>{a.workerName ?? 'Thợ'}</b><span className="block text-sm text-stone-500"><Star size={12} className="mr-1 inline" />{a.workerRatingAvg.toFixed(1)}</span></span>
              <span className="rounded-full bg-stone-100 px-3 py-1 text-xs font-semibold">{assignmentLabel(a.assignmentStatus)}</span></li>)}</ul>}
        </section>
        <section className={card}>
          <h2 className="text-lg font-semibold">Địa điểm</h2>
          <p className="mt-3 flex gap-2 text-sm"><MapPin size={16} className="mt-0.5 shrink-0" />{address ? <span><b>{address.label}</b><br />{address.addressLine} · {address.district} · {address.totalAreaM2} m²</span> : <span className="text-stone-500">{order.shiftCode && shiftLabel(order.shiftCode)}</span>}</p>
        </section>
        {actions.canCancel && <section className={card}>
          {!cancelling ? <button type="button" onClick={() => { setCancelling(true); setNotice(null) }} className="font-semibold text-red-700">Hủy đơn</button>
            : <div role="group" aria-label="Xác nhận hủy đơn">
              <h2 className="text-lg font-semibold">Hủy đơn {order.orderCode}?</h2>
              <p className="mt-1 text-sm text-stone-500">Đơn đã thanh toán sẽ được hoàn tiền theo chính sách hủy.</p>
              <label className="mt-3 block text-sm font-medium">Lý do hủy
                <textarea value={reason} maxLength={CANCEL_REASON_MAX} onChange={(e) => setReason(e.target.value)} aria-invalid={reasonError !== null} className="mt-1 min-h-20 w-full rounded-lg border border-stone-300 p-3 text-sm" /></label>
              {reasonError && <p role="alert" className="mt-1 text-sm text-red-700">{reasonError}</p>}
              <div className="mt-3 flex gap-2">
                <button type="button" disabled={busy} onClick={() => void confirmCancel()} className="h-10 rounded-lg bg-red-700 px-4 font-semibold text-white disabled:opacity-50">{busy ? 'Đang hủy…' : 'Xác nhận hủy'}</button>
                <button type="button" disabled={busy} onClick={() => { setCancelling(false); setReasonError(null) }} className="h-10 rounded-lg border border-stone-300 px-4 font-semibold">Giữ đơn</button>
              </div>
            </div>}
        </section>}
      </div>
    </div>
  </div>
}
