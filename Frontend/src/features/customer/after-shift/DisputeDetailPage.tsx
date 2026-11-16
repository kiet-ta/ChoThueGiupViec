import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, CircleAlert, Loader } from 'lucide-react'
import { ApiError } from '@/services/api'
import { formatDateTime } from '@/lib/format'
import { bookingApi } from '../booking/api'
import { afterShiftApi } from './api'
import type { Dispute } from './types'
import { categoryLabel, disputeDetailProblem, disputeOutcomeLines, disputeStatusLabel, disputeSteps } from './view'

const tone: Record<string, string> = { OPEN: 'bg-amber-100 text-amber-900', IN_REVIEW: 'bg-amber-100 text-amber-900', RESOLVED: 'bg-emerald-100 text-emerald-900' }

/** One dispute of the customer (DISPUTE-DETAIL-01): `GET /customers/me/disputes/{id}`, disputes.md 2.1. */
export function DisputeDetailPage() {
  const { disputeId } = useParams()
  const id = Number(disputeId)
  const [dispute, setDispute] = useState<Dispute | null>(null)
  const [orderCode, setOrderCode] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [photo, setPhoto] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    afterShiftApi.dispute(id)
      .then((d) => {
        if (cancelled) return
        setDispute(d)
        bookingApi.orders(100).then((page) => !cancelled && setOrderCode(page.items.find((o) => o.orderId === d.orderId)?.orderCode ?? null)).catch(() => undefined)
      })
      .catch((e: unknown) => !cancelled && setError(disputeDetailProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [id])

  const back = <Link to="/app/disputes" className="inline-flex items-center gap-1 text-sm text-stone-600 underline"><ArrowLeft size={14} />Khiếu nại của tôi</Link>
  if (error) return <div>{back}<p role="alert" className="mt-6 flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p></div>
  if (!dispute) return <div>{back}<p className="mt-6 flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p></div>

  const resolved = dispute.disputeStatus === 'RESOLVED'
  return <div>
    {back}
    <div className="mt-3 flex flex-wrap items-start justify-between gap-3">
      <div><p className="text-xs font-semibold uppercase tracking-wide text-stone-500">Khiếu nại</p><h1 className="mt-1 text-3xl font-semibold">#D-{dispute.disputeId}</h1>
        <p className="mt-1 text-stone-500">{categoryLabel(dispute.category)} · gửi lúc {formatDateTime(dispute.createdAt)}</p></div>
      <span className={`rounded-full px-3 py-1 text-sm font-semibold ${tone[dispute.disputeStatus] ?? 'bg-stone-200'}`}>{disputeStatusLabel(dispute.disputeStatus)}</span>
    </div>

    <div className="mt-6 grid gap-4 lg:grid-cols-[1fr_360px]">
      <div className="space-y-4">
        <section className="rounded-xl border border-stone-200 bg-white p-6">
          <h2 className="text-lg font-semibold">Nội dung</h2>
          <p className="mt-3 whitespace-pre-line text-stone-700">{dispute.description}</p>
        </section>
        <section className="rounded-xl border border-stone-200 bg-white p-6">
          <h2 className="text-lg font-semibold">Ảnh bằng chứng <span className="text-sm font-normal text-stone-500">· {dispute.evidenceUrls.length} ảnh</span></h2>
          {dispute.evidenceUrls.length === 0 ? <p className="mt-3 text-sm text-stone-500">Không có ảnh.</p>
            : <div className="mt-3 grid grid-cols-3 gap-3 sm:grid-cols-4">{dispute.evidenceUrls.map((url, i) => <button key={url} type="button" onClick={() => setPhoto(url)} className="aspect-square overflow-hidden rounded-lg border border-stone-200 bg-stone-100" aria-label={`Xem ảnh bằng chứng ${i + 1}`}>
              <img src={url} alt={`Ảnh bằng chứng ${i + 1}`} className="h-full w-full object-cover" /></button>)}</div>}
        </section>
      </div>
      <div className="space-y-4">
        <section className="rounded-xl border border-stone-200 bg-white p-6">
          <h2 className="text-lg font-semibold">Tiến trình</h2>
          <ol className="mt-4 space-y-3">{disputeSteps(dispute.disputeStatus).map((s) => <li key={s.label} className="flex items-center gap-3">
            <span className={`grid h-6 w-6 place-items-center rounded-full text-xs font-bold ${s.state === 'done' ? 'bg-[#1a2f25] text-white' : s.state === 'current' ? 'border-2 border-[#1a2f25] bg-emerald-50 text-[#1a2f25]' : 'border border-stone-300 text-stone-400'}`}>{s.state === 'done' ? '✓' : ''}</span>
            <span className={s.state === 'todo' ? 'text-stone-400' : s.state === 'current' ? 'font-semibold' : ''}>{s.label}</span></li>)}</ol>
          <div className="mt-4 space-y-1 text-sm text-stone-600">{disputeOutcomeLines(dispute).map((line) => <p key={line}>{line}</p>)}</div>
        </section>
        {resolved && <section className="rounded-xl border border-emerald-200 bg-emerald-50 p-6">
          <h2 className="text-lg font-semibold text-emerald-900">Kết quả</h2>
          <p className="mt-2 text-emerald-900">{dispute.compensationAmount ? <>Bồi hoàn <b className="whitespace-nowrap">{dispute.compensationAmount.toLocaleString('vi-VN')} đ</b></> : 'Không có khoản bồi hoàn.'}</p>
        </section>}
        <section className="rounded-xl border border-stone-200 bg-white p-6">
          <h2 className="text-lg font-semibold">Đơn liên quan</h2>
          <Link to={`/app/orders/${dispute.orderId}`} className="mt-2 inline-block font-semibold text-[#1a2f25] underline">{orderCode ?? `Đơn #${dispute.orderId}`}</Link>
        </section>
      </div>
    </div>

    {photo && <div role="dialog" aria-label="Ảnh bằng chứng" className="fixed inset-0 z-50 grid place-items-center bg-black/80 p-4" onClick={() => setPhoto(null)}>
      <img src={photo} alt="Ảnh bằng chứng" className="max-h-full max-w-full rounded-lg" />
      <button type="button" className="absolute right-4 top-4 rounded-full bg-white/90 px-3 py-1 text-sm font-semibold" onClick={() => setPhoto(null)}>Đóng</button>
    </div>}
  </div>
}
