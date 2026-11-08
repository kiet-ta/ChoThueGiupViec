import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { CircleAlert, CircleCheck, ImageOff, Loader } from 'lucide-react'
import { ApiError } from '@/services/api'
import { ordersApi } from '../orders/api'
import type { OrderProgress, ProgressAssignment } from '../orders/types'
import { afterShiftApi } from './api'
import type { JobPhoto } from './types'
import { acceptedLine, afterShiftProblem, anglePairs, anglesSummary, redoReasonError, REDO_REASON_MAX, reviewAssignments } from './view'

const card = 'rounded-xl border border-stone-200 bg-white p-6'

/** A photo of the order: the file needs the bearer token, so it is fetched as a blob. Seed photos with an https `photoUrl` fall back to it. */
function Photo({ assignmentId, photo, label }: { assignmentId: number; photo: JobPhoto | null; label: string }) {
  const [src, setSrc] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)
  const photoId = photo?.photoId
  const fallback = photo?.photoUrl.startsWith('https://') ? photo.photoUrl : null
  useEffect(() => {
    if (photoId === undefined) return
    let url: string | null = null
    let cancelled = false
    afterShiftApi.photoFile(assignmentId, photoId)
      .then((blob) => {
        if (cancelled) return
        url = URL.createObjectURL(blob)
        setSrc(url)
      })
      .catch(() => !cancelled && (fallback ? setSrc(fallback) : setFailed(true)))
    return () => {
      cancelled = true
      if (url) URL.revokeObjectURL(url)
    }
  }, [assignmentId, photoId, fallback])
  return <figure className="overflow-hidden rounded-lg border border-stone-200">
    <div className="grid aspect-[4/3] place-items-center bg-stone-100 text-stone-400">
      {src ? <img src={src} alt={label} className="h-full w-full object-cover" /> : failed || !photo ? <span className="flex flex-col items-center gap-1 text-xs"><ImageOff size={18} />Chưa có ảnh</span> : <Loader className="animate-spin" size={18} />}
    </div>
    <figcaption className="px-2 py-1 text-xs text-stone-500">{label}</figcaption>
  </figure>
}

export function AcceptancePage() {
  const orderId = Number(useParams().orderId)
  const [progress, setProgress] = useState<OrderProgress | null>(null)
  const [orderCode, setOrderCode] = useState('')
  const [loaded, setLoaded] = useState<{ assignmentId: number; photos: JobPhoto[] } | null>(null)
  const [picked, setPicked] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [redo, setRedo] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [done, setDone] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([ordersApi.get(orderId), ordersApi.progress(orderId)])
      .then(([o, p]) => {
        if (cancelled) return
        setOrderCode(o.orderCode)
        setProgress(p)
      })
      .catch((e: unknown) => !cancelled && setError(afterShiftProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [orderId])

  // While a worker awaits acceptance the page is the acceptance; otherwise it reviews the COMPLETED assignments (one at a time).
  const reviews = reviewAssignments(progress?.assignments ?? [])
  const target: ProgressAssignment | undefined = progress?.assignments.find((a) => a.assignmentStatus === 'AWAITING_ACCEPTANCE')
    ?? reviews.find((a) => a.assignmentId === picked) ?? reviews[0]
  const review = reviews.length > 0
  const assignmentId = target?.assignmentId
  useEffect(() => {
    if (assignmentId === undefined) return
    let cancelled = false
    afterShiftApi.photos(assignmentId)
      .then((photos) => !cancelled && setLoaded({ assignmentId, photos }))
      .catch((e: unknown) => !cancelled && setError(afterShiftProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [assignmentId])
  const photos = loaded && loaded.assignmentId === assignmentId ? loaded.photos : null

  if (error && !progress) return <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>
  if (!progress) return <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>

  const waiting = target?.assignmentStatus === 'AWAITING_ACCEPTANCE'
  const pairs = anglePairs(photos ?? [])

  async function act(kind: 'accept' | 'redo') {
    if (!target || busy) return
    if (kind === 'redo') {
      const problem = redoReasonError(reason)
      setReasonError(problem)
      if (problem) return
    }
    setBusy(true)
    setError(null)
    try {
      if (kind === 'accept') await afterShiftApi.accept(target.assignmentId, null)
      else await afterShiftApi.requestRedo(target.assignmentId, reason.trim())
      setDone(kind === 'accept' ? 'Cảm ơn bạn. Ca đã được nghiệm thu, bạn có thể đánh giá thợ.' : 'Đã gửi yêu cầu dọn lại. Thợ sẽ dọn thêm 15 đến 30 phút.')
    } catch (e) {
      setError(afterShiftProblem(e as ApiError))
    } finally {
      setBusy(false)
    }
  }

  return <div>
    <Link to={`/app/orders/${orderId}`} className="text-sm text-stone-500 underline">← Đơn {orderCode}</Link>
    <h1 className="mt-3 text-3xl font-semibold">{review ? 'Kết quả nghiệm thu' : 'Nghiệm thu ca dọn'}</h1>
    {review && target && <p className="mt-2 flex gap-2 text-emerald-900"><CircleCheck size={18} className="mt-0.5 shrink-0" />{acceptedLine(target.completedAt)}</p>}
    <p className="mt-2 text-stone-500">{photos ? anglesSummary(pairs) : 'Đang tải ảnh…'}{!review && ' Bạn chỉ chốt ca khi đồng ý với kết quả.'}</p>
    {reviews.length > 1 && <div role="group" aria-label="Chọn thợ" className="mt-4 flex flex-wrap gap-2">
      {reviews.map((a) => <button key={a.assignmentId} type="button" aria-pressed={a.assignmentId === target?.assignmentId} onClick={() => setPicked(a.assignmentId)}
        className={`h-9 rounded-full border px-4 text-sm font-semibold ${a.assignmentId === target?.assignmentId ? 'border-[#1a2f25] bg-[#1a2f25] text-white' : 'border-stone-300'}`}>Thợ {a.workerName ?? `#${a.workerId}`}</button>)}
    </div>}
    {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {done && <p role="status" className="mt-4 flex gap-2 rounded-lg bg-emerald-50 p-3 text-sm text-emerald-900"><CircleCheck size={16} className="mt-0.5 shrink-0" />{done} <Link to={`/app/orders/${orderId}`} className="underline">Về đơn</Link></p>}
    {!target && <p className="mt-6 rounded-xl border border-stone-200 bg-white p-6 text-stone-600">Chưa có ca nào chờ nghiệm thu.</p>}
    {target && <div className="mt-6 space-y-4">
      {pairs.map((p) => <section key={p.angleNo} className={card} aria-label={`Góc ${p.angleNo}`}>
        <h2 className="font-semibold">Góc {p.angleNo}</h2>
        <div className="mt-3 grid grid-cols-2 gap-4">
          <Photo assignmentId={target.assignmentId} photo={p.before} label="Trước" />
          <Photo assignmentId={target.assignmentId} photo={p.after} label="Sau" />
        </div>
      </section>)}
      {waiting && !done && <section className={card}>
        <p className="text-sm text-stone-500">Máy chủ chưa gửi hạn nghiệm thu cho ca này, nên trang không hiển thị đồng hồ đếm ngược.</p>
        {redo && <div className="mt-4" role="group" aria-label="Yêu cầu dọn lại">
          <label className="block text-sm font-medium">Cần dọn lại điều gì?
            <textarea value={reason} maxLength={REDO_REASON_MAX} onChange={(e) => setReason(e.target.value)} aria-invalid={reasonError !== null} className="mt-1 min-h-20 w-full rounded-lg border border-stone-300 p-3 text-sm" /></label>
          {reasonError && <p role="alert" className="mt-1 text-sm text-red-700">{reasonError}</p>}
        </div>}
        <div className="mt-4 flex flex-wrap justify-end gap-3">
          {!redo ? <button type="button" onClick={() => setRedo(true)} className="h-10 rounded-lg border border-stone-300 px-4 font-semibold">Yêu cầu dọn lại</button>
            : <button type="button" disabled={busy} onClick={() => void act('redo')} className="h-10 rounded-lg border border-stone-300 px-4 font-semibold disabled:opacity-50">Gửi yêu cầu dọn lại</button>}
          <button type="button" disabled={busy} onClick={() => void act('accept')} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Nghiệm thu ca này</button>
        </div>
      </section>}
    </div>}
  </div>
}
