import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { CircleAlert, CircleCheck, Heart, Loader, Star, Timer } from 'lucide-react'
import { ApiError } from '@/services/api'
import { accountApi } from '../account/api'
import { favoriteToggleError, favoriteToggleLabel, isFavorite } from '../account/view'
import { ordersApi } from '../orders/api'
import type { ProgressAssignment } from '../orders/types'
import { afterShiftApi } from './api'
import type { RatingWindow } from './types'
import { afterShiftProblem, COMMENT_MAX, CRITERIA, formatWindow, overallStars, ratingWindowText, secondsUntil, validateRating, type Scores } from './view'

const card = 'rounded-xl border border-stone-200 bg-white p-6'

export function RatingPage() {
  const orderId = Number(useParams().orderId)
  const [orderCode, setOrderCode] = useState('')
  const [worker, setWorker] = useState<ProgressAssignment | null>(null)
  const [window, setWindow] = useState<RatingWindow | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [scores, setScores] = useState<Scores>({})
  const [comment, setComment] = useState('')
  const [errors, setErrors] = useState<{ criteria?: string; comment?: string }>({})
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState(false)
  const [now, setNow] = useState(() => new Date())
  // null = the favorites list could not be read, so the toggle is hidden.
  const [saved, setSaved] = useState<boolean | null>(null)
  const [favBusy, setFavBusy] = useState(false)
  const [favError, setFavError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([ordersApi.get(orderId), ordersApi.progress(orderId)])
      .then(async ([o, p]) => {
        const done = p.assignments.find((a) => a.assignmentStatus === 'COMPLETED')
        if (cancelled) return
        setOrderCode(o.orderCode)
        setWorker(done ?? null)
        if (done) accountApi.favorites().then((list) => !cancelled && setSaved(isFavorite(list, done.workerId))).catch(() => undefined)
        if (done) setWindow(await afterShiftApi.ratingWindow(done.assignmentId))
        else setWindow({ assignmentId: 0, canRate: false, reason: 'NOT_COMPLETED', opensAt: null, closesAt: null, secondsRemaining: 0 })
      })
      .catch((e: unknown) => !cancelled && setError(afterShiftProblem(e as ApiError)))
    return () => {
      cancelled = true
    }
  }, [orderId])

  const open = window?.canRate === true && !sent
  useEffect(() => {
    if (!open) return
    const timer = setInterval(() => setNow(new Date()), 1000)
    return () => clearInterval(timer)
  }, [open])

  if (error && !window) return <p role="alert" className="flex gap-2 rounded-lg bg-red-50 p-4 text-sm text-red-700"><CircleAlert size={16} className="mt-0.5 shrink-0" />{error}</p>
  if (!window) return <p className="flex items-center gap-2 text-stone-500"><Loader className="animate-spin" size={18} />Đang tải…</p>

  const left = secondsUntil(window.closesAt, now)

  async function toggleFavorite() {
    if (!worker || saved === null || favBusy) return
    const adding = !saved
    setSaved(adding)
    setFavBusy(true)
    setFavError(null)
    try {
      if (adding) await accountApi.addFavorite(worker.workerId)
      else await accountApi.removeFavorite(worker.workerId)
    } catch {
      setSaved(!adding)
      setFavError(favoriteToggleError(adding))
    } finally {
      setFavBusy(false)
    }
  }

  async function submit() {
    const problems = validateRating(scores, comment)
    setErrors(problems)
    const stars = overallStars(scores)
    if (problems.criteria || problems.comment || stars === null || !worker || busy) return
    setBusy(true)
    setError(null)
    try {
      await afterShiftApi.rate(worker.assignmentId, {
        stars,
        criteria: { punctuality: scores.punctuality!, cleaningQuality: scores.cleaningQuality!, attitude: scores.attitude! },
        comment: comment.trim() || null,
      })
      setSent(true)
    } catch (e) {
      setError(afterShiftProblem(e as ApiError))
    } finally {
      setBusy(false)
    }
  }

  return <div className="mx-auto max-w-2xl">
    <Link to={`/app/orders/${orderId}`} className="text-sm text-stone-500 underline">← Đơn {orderCode}</Link>
    <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
      <h1 className="text-3xl font-semibold">Đánh giá thợ</h1>
      {open && left !== null && <span className="flex items-center gap-2 rounded-full bg-amber-100 px-3 py-1 text-sm font-semibold text-amber-900"><Timer size={16} />Còn <span role="timer" className="tabular-nums">{formatWindow(left)}</span></span>}
    </div>
    {worker && <div className="mt-2 flex flex-wrap items-center gap-3">
      <p className="text-stone-500">{worker.workerName ?? 'Thợ'} · ca đơn {orderCode}</p>
      {saved !== null && <button type="button" aria-pressed={saved} disabled={favBusy} onClick={() => void toggleFavorite()}
        className={`inline-flex h-9 items-center gap-2 rounded-full border px-3 text-sm font-semibold disabled:opacity-60 ${saved ? 'border-rose-200 bg-rose-50 text-rose-700' : 'border-stone-300 bg-white'}`}>
        <Heart size={16} className={saved ? 'fill-current' : ''} />{favoriteToggleLabel(saved)}</button>}
    </div>}
    {favError && <p role="alert" className="mt-2 text-sm text-red-700">{favError}</p>}
    {error && <p role="alert" className="mt-4 rounded-lg bg-red-50 p-3 text-sm text-red-700">{error}</p>}
    {sent && <p role="status" className="mt-6 flex gap-2 rounded-xl bg-emerald-50 p-4 text-emerald-900"><CircleCheck size={18} className="mt-0.5 shrink-0" />Đã gửi đánh giá. Cảm ơn bạn! <Link to={`/app/orders/${orderId}`} className="underline">Về đơn</Link></p>}
    {!open && !sent && <p role="status" className="mt-6 rounded-xl border border-stone-200 bg-white p-6 text-stone-700">{ratingWindowText(window)} <Link to={`/app/orders/${orderId}`} className="underline">Về đơn</Link></p>}
    {open && <section className={`${card} mt-6`}>
      <div className="space-y-4">{CRITERIA.map((c) => <div key={c.key} className="flex items-center justify-between gap-4" role="group" aria-label={c.label}>
        <span className="font-medium">{c.label}</span>
        <span className="flex gap-1">{[1, 2, 3, 4, 5].map((n) => <button key={n} type="button" aria-label={`${c.label} ${n} sao`} aria-pressed={(scores[c.key] ?? 0) >= n}
          onClick={() => setScores({ ...scores, [c.key]: n })} className="p-1"><Star size={28} className={(scores[c.key] ?? 0) >= n ? 'fill-amber-400 text-amber-500' : 'text-stone-300'} /></button>)}</span>
      </div>)}</div>
      {errors.criteria && <p role="alert" className="mt-2 text-sm text-red-700">{errors.criteria}</p>}
      <label className="mt-6 block text-sm font-medium">Nhận xét (không bắt buộc)
        <textarea value={comment} maxLength={COMMENT_MAX} onChange={(e) => setComment(e.target.value)} className="mt-1 min-h-24 w-full rounded-lg border border-stone-300 p-3 text-sm" /></label>
      <p className="mt-1 text-right text-xs text-stone-500">{comment.length}/{COMMENT_MAX}</p>
      {errors.comment && <p role="alert" className="text-sm text-red-700">{errors.comment}</p>}
      <div className="mt-4 flex justify-end gap-3">
        <Link to={`/app/orders/${orderId}`} className="inline-flex h-10 items-center rounded-lg border border-stone-300 px-4 font-semibold">Để sau</Link>
        <button type="button" disabled={busy} onClick={() => void submit()} className="flex h-10 items-center gap-2 rounded-lg bg-[#1a2f25] px-4 font-semibold text-white disabled:opacity-50">{busy && <Loader size={16} className="animate-spin" />}Gửi đánh giá</button>
      </div>
    </section>}
  </div>
}
