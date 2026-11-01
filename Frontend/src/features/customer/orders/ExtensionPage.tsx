import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { CircleAlert, Clock, Info, Loader } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import type { Order } from '../booking/types'
import { ordersApi } from './api'
import type { OrderProgress } from './types'
import { extensionHourOptions, extensionLabel, hoursText, newEndText, orderActions, orderProblem, POLL_PROGRESS_MS } from './view'

const card = 'rounded-xl border border-stone-200 bg-white p-6'

export function ExtensionPage() {
  const orderId = Number(useParams().orderId)
  const [order, setOrder] = useState<Order | null>(null)
  const [progress, setProgress] = useState<OrderProgress | null>(null)
  const [hours, setHours] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const [o, p] = await Promise.all([ordersApi.get(orderId), ordersApi.progress(orderId)])
      setOrder(o)
      setProgress(p)
    } catch (e) {
      setError(orderProblem(e as ApiError))
    }
  }, [orderId])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- initial fetch
    void load()
  }, [load])

  const ext = progress?.extension ?? null
  // The wait screen refreshes until the worker answered (PAID -> ACCEPTED / DECLINED) or the QR window ended.
  const waiting = ext !== null && ['PENDING_PAYMENT', 'PAID'].includes(ext.extStatus)
  useEffect(() => {
    if (!waiting) return
    const timer = setInterval(() => void load(), POLL_PROGRESS_MS)
    return () => clearInterval(timer)
  }, [waiting, load])

  if (error && !order) return <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>
  if (!order || !progress) return <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>

  const actions = orderActions(order.orderStatus, progress.assignments, ext, new Date())
  const worker = progress.assignments.find((a) => ['IN_PROGRESS', 'AWAITING_ACCEPTANCE'].includes(a.assignmentStatus)) ?? progress.assignments[0]

  async function send() {
    if (hours === null || !worker || busy) return
    setBusy(true)
    setError(null)
    try {
      await ordersApi.requestExtension(orderId, worker.assignmentId, hours)
      await load()
    } catch (e) {
      setError(orderProblem(e as ApiError))
      void load()
    } finally {
      setBusy(false)
    }
  }

  const step = (n: number, label: string, on: boolean) => <li className={`flex items-center gap-2 ${on ? 'font-semibold' : 'text-stone-400'}`}>
    <span className={`grid h-6 w-6 place-items-center rounded-full text-xs ${on ? 'bg-[#1a2f25] text-white' : 'bg-stone-100'}`}>{n}</span>{label}</li>

  return <div className="mx-auto max-w-2xl">
    <Link to={`/app/orders/${orderId}`} className="text-sm text-stone-500 underline">← Đơn {order.orderCode}</Link>
    <h1 className="mt-3 flex items-center gap-2 text-3xl font-semibold"><Clock />Làm lần 2</h1>
    <p className="mt-2 text-stone-500">Đề nghị thợ làm thêm khi ca đang diễn ra.</p>
    <ol className="mt-6 flex flex-wrap gap-6 text-sm" aria-label="Các bước">
      {step(1, 'Gửi yêu cầu', ext === null)}{step(2, 'Thợ đồng ý', ext !== null && ext.extStatus !== 'ACCEPTED')}{step(3, 'Thanh toán', ext?.extStatus === 'ACCEPTED' || ext?.extStatus === 'PENDING_PAYMENT')}
    </ol>
    {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}

    {ext === null && (actions.canExtend && worker
      ? <section className={`${card} mt-6`}>
        <h2 className="text-lg font-semibold">Cần làm thêm bao lâu?</h2>
        <div className="mt-4 flex flex-wrap gap-2" role="group" aria-label="Số giờ làm thêm">
          {extensionHourOptions().map((h) => <button key={h} type="button" aria-pressed={hours === h} onClick={() => setHours(h)}
            className={`h-10 rounded-lg border px-4 text-sm font-medium ${hours === h ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-300 bg-white'}`}>{hoursText(h)}</button>)}
        </div>
        <p className="mt-4 text-sm">Kết thúc mới: <b>{hours === null ? '—' : newEndText(order.shiftEndAt, hours)}</b></p>
        <p className="mt-1 text-sm">Phí thêm: <b>Báo sau khi gửi</b></p>
        <p className="mt-4 flex gap-2 rounded-lg bg-stone-50 p-3 text-sm text-stone-600"><Info size={16} className="mt-0.5 shrink-0" />Giá chưa biết trước. Máy chủ tính sau khi bạn gửi, theo giá đã chốt của đơn. Bạn thanh toán phần phát sinh rồi thợ mới được hỏi.</p>
        <div className="mt-4 flex gap-2">
          <button type="button" disabled={hours === null || busy} onClick={() => void send()} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Gửi đề nghị</button>
          <Link to={`/app/orders/${orderId}`} className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Hủy</Link>
        </div>
      </section>
      : <p className="mt-6 rounded-xl border border-stone-200 bg-white p-6 text-stone-600">Chỉ đề nghị làm thêm được khi thợ đang làm việc tại nhà bạn. <Link to={`/app/orders/${orderId}`} className="underline">Về chi tiết đơn</Link></p>)}

    {ext !== null && <section className={`${card} mt-6`} aria-live="polite">
      <h2 className="text-lg font-semibold">{extensionLabel(ext.extStatus)}</h2>
      <p className="mt-2">Thêm {hoursText(ext.extraHours)} · Phí thêm <b>{formatVnd(ext.extraAmount)}</b> · Kết thúc {newEndText(order.shiftEndAt, ext.extraHours).split('→ ')[1]}</p>
      {ext.extStatus === 'PENDING_PAYMENT' && <>
        <p className="mt-2 text-sm text-stone-500">Thanh toán phần phát sinh để thợ được hỏi có đồng ý làm thêm không.</p>
        <Link to={`/app/payments/extension/${ext.extensionId}`} className="mt-4 inline-flex h-10 items-center rounded-lg bg-[#1a2f25] px-4 font-semibold text-white">Thanh toán {formatVnd(ext.extraAmount)}</Link></>}
      {ext.extStatus === 'PAID' && <p className="mt-2 flex items-center gap-2 text-sm text-stone-500"><Loader size={16} className="animate-spin" />Đang chờ thợ trả lời. Nếu thợ từ chối, phần phí thêm được hoàn lại.</p>}
      {ext.extStatus === 'ACCEPTED' && <p className="mt-2 text-sm text-emerald-800">Thợ đã đồng ý làm thêm. Không cần thao tác gì thêm.</p>}
      {ext.extStatus === 'DECLINED' && <p className="mt-2 text-sm text-stone-600">Thợ không làm thêm được. Phần phí thêm đã được hoàn lại.</p>}
      {ext.extStatus === 'EXPIRED' && <p className="mt-2 text-sm text-stone-600">Đề nghị đã hết hạn thanh toán. Mỗi đơn chỉ có một đề nghị làm thêm.</p>}
      <Link to={`/app/orders/${orderId}`} className="mt-4 inline-block underline">Về chi tiết đơn</Link>
    </section>}
  </div>
}
