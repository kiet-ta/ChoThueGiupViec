import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { CircleAlert, CircleCheck, Clock, Copy, ExternalLink, FlaskConical, Loader, QrCode, Timer } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatVnd } from '@/lib/format'
import { bookingApi } from '../booking/api'
import type { Order } from '../booking/types'
import { paymentApi } from './api'
import type { Payment } from './types'
import { formatCountdown, paymentState, POLL_INTERVAL_MS, qrImageUrl, qrProblem, secondsLeft, shouldPoll, stateView } from './view'

const icons = { Loader, CircleCheck, Clock, CircleAlert }
const pill: Record<string, string> = { PENDING: 'bg-amber-100 text-amber-900', PAID: 'bg-emerald-100 text-emerald-900', EXPIRED: 'bg-stone-200 text-stone-800', FAILED: 'bg-red-100 text-red-800' }

/** `kind = 'order'` pays `/app/payments/:orderId`; `kind = 'extension'` pays `/app/payments/extension/:extensionId` ("Làm lần 2"). */
export function PaymentPage({ kind = 'order' }: { kind?: 'order' | 'extension' }) {
  const params = useParams()
  const isExtension = kind === 'extension'
  const targetId = Number(isExtension ? params.extensionId : params.orderId)
  const orderId = isExtension ? null : targetId
  const [order, setOrder] = useState<Order | null>(null)
  const [payment, setPayment] = useState<Payment | null>(null)
  const [problem, setProblem] = useState<{ text: string; expired: boolean } | null>(null)
  const [loading, setLoading] = useState(true)
  const [now, setNow] = useState(() => new Date())
  const [copied, setCopied] = useState(false)
  // One request at a time: a double tap on "Thử lại" never fires two QR calls (the server is idempotent too).
  const inFlight = useRef(false)

  const createQr = useCallback(async () => {
    if (inFlight.current || !Number.isInteger(targetId) || targetId <= 0) return
    inFlight.current = true
    setLoading(true)
    setProblem(null)
    try {
      setPayment(await (isExtension ? paymentApi.createExtensionQr(targetId) : paymentApi.createOrderQr(targetId)))
    } catch (e) {
      setProblem(qrProblem(e as ApiError))
    } finally {
      inFlight.current = false
      setLoading(false)
    }
  }, [targetId, isExtension])

  const paymentOrderId = payment?.orderId ?? (isExtension ? null : orderId)
  useEffect(() => {
    void createQr()
  }, [createQr])
  useEffect(() => {
    if (paymentOrderId) bookingApi.order(paymentOrderId).then(setOrder).catch(() => undefined)
  }, [paymentOrderId])

  const paymentId = payment?.paymentId
  const pending = payment ? shouldPoll(payment.txnStatus) : false
  useEffect(() => {
    if (paymentId === undefined || !pending) return
    const timer = setInterval(() => {
      paymentApi.get(paymentId).then(setPayment).catch(() => undefined)
    }, POLL_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [paymentId, pending])

  useEffect(() => {
    if (!pending) return
    const tick = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(tick)
  }, [pending])

  const left = payment ? secondsLeft(payment.expiresAt, now) : 0
  // The server decides EXPIRED; a local countdown at 0 only means "waiting for the server to confirm".
  const state = payment ? paymentState(payment.txnStatus) : problem?.expired ? 'EXPIRED' : 'FAILED'
  const view = stateView(state)
  const StateIcon = icons[view.icon]
  const qrImage = qrImageUrl(payment?.qrPayload)

  async function copy() {
    if (!payment?.qrPayload) return
    try {
      await navigator.clipboard.writeText(payment.qrPayload)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return <div className="mx-auto max-w-3xl">
    <p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Thanh toán</p>
    <h1 className="mt-1 text-3xl font-semibold">{isExtension ? 'Thanh toán phần làm thêm' : 'Thanh toán'} đơn {order?.orderCode ?? (orderId ? `#${orderId}` : '')}</h1>
    {(payment?.sandbox ?? true) && <div className="mt-6 flex gap-3 rounded-xl bg-amber-50 p-4 text-sm text-amber-900"><FlaskConical size={18} className="mt-0.5 shrink-0" /><div><b>SANDBOX</b>. Không dùng tiền thật. Mã QR dưới đây chỉ để thử luồng.</div></div>}
    <section className="mt-4 rounded-xl border border-stone-200 bg-white p-6">
      {loading && !payment && <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tạo mã thanh toán…</p>}
      {problem && !payment && <div role="alert" className="space-y-4">
        <p className="flex gap-2 text-red-700"><CircleAlert size={18} className="mt-0.5 shrink-0" />{problem.text}</p>
        {problem.expired ? <Link to="/app/booking" className="inline-flex h-10 items-center rounded-lg bg-[#1a2f25] px-4 font-semibold text-white">Đặt lại ca</Link>
          : <button type="button" onClick={() => void createQr()} disabled={loading} className="h-10 rounded-lg border border-stone-300 px-4 font-semibold disabled:opacity-50">Thử lại</button>}
      </div>}
      {payment && <div className={qrImage && state === 'PENDING' ? 'grid items-center gap-8 sm:grid-cols-[220px_1fr]' : ''}>
        {qrImage && state === 'PENDING' && <figure className="rounded-xl border border-stone-200 bg-white p-3">
          <img src={qrImage} alt={`Mã QR chuyển khoản ${formatVnd(payment.amount)}`} width={196} height={196} className="h-auto w-full" />
          <figcaption className="mt-2 text-center text-xs text-stone-500">Quét bằng ứng dụng ngân hàng</figcaption></figure>}
        <div>
        <div className="text-sm text-stone-500">{state === 'PAID' ? 'Số tiền đã thanh toán' : 'Số tiền cần thanh toán'}</div>
        <div className="text-4xl font-bold tracking-tight">{formatVnd(payment.amount)}</div>
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <span className={`inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-semibold ${pill[state]}`}>
            <StateIcon size={16} className={state === 'PENDING' ? 'animate-spin' : ''} />{view.label}</span>
          <span className="text-xs text-stone-500">{view.detail}</span>
        </div>
        {state === 'PENDING' && <>
          <p className="mt-4 flex items-center gap-2 text-stone-600"><Timer size={18} />Mã hết hạn sau <b role="timer" className="tabular-nums text-stone-900">{formatCountdown(left)}</b></p>
          {payment.payUrl && <a href={payment.payUrl} target="_blank" rel="noreferrer" className="mt-4 inline-flex h-11 items-center gap-2 rounded-lg bg-[#1a2f25] px-5 font-semibold text-white"><ExternalLink size={16} />Mở trang thanh toán</a>}
          {payment.qrPayload && <div className="mt-4">
            <p className="flex items-center gap-2 text-sm font-semibold"><QrCode size={16} />Liên kết thanh toán</p>
            <div className="mt-2 flex gap-2"><input readOnly aria-label="Liên kết thanh toán" value={payment.qrPayload} className="min-w-0 flex-1 rounded-lg border border-stone-300 bg-stone-50 px-3 py-2 text-sm" onFocus={(e) => e.currentTarget.select()} />
              <button type="button" onClick={() => void copy()} className="flex h-10 items-center gap-2 rounded-lg border border-stone-300 px-3 text-sm font-semibold"><Copy size={15} />{copied ? 'Đã chép' : 'Sao chép'}</button></div>
          </div>}
        </>}
        {state === 'PAID' && payment.paidAt && <p className="mt-4 text-sm text-stone-500">Thanh toán lúc {new Date(payment.paidAt).toLocaleString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' })}.</p>}
        </div>
      </div>}
      <hr className="my-6 border-stone-200" />
      <div className="flex flex-wrap justify-between gap-3">
        <Link to={isExtension && paymentOrderId ? `/app/orders/${paymentOrderId}/extension` : '/app/booking'} className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">{!isExtension && (state === 'EXPIRED' || state === 'FAILED') ? 'Đặt lại ca' : 'Quay lại'}</Link>
        <Link to={paymentOrderId ? `/app/orders/${paymentOrderId}` : '/app/orders'} className="inline-flex h-10 items-center rounded-lg bg-[#1a2f25] px-4 font-semibold text-white">Mở đơn của tôi</Link>
      </div>
    </section>
  </div>
}
